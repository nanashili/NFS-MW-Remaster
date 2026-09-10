"""Read the PC texture serialization actually observed in the staged archives.

The on-disk platform entry is 32 bytes with D3D format at +20. This is NOT the
60-byte platform runtime class in the cached reconstruction. Index, info, platform,
data-header and image extents are cross-checked; no table-stride guessing occurs.
"""

from __future__ import annotations

import struct
from collections import defaultdict
from dataclasses import dataclass
from typing import Iterator

from archive_decode import ArchiveError, Chunk, MAX_ARCHIVE_BYTES, sha256, unwrap
from texture_decode import DecodeError, Image, block_size, decode_blocks, decode_masked, validate_dimensions

PACK, INFO, DATA = 0xB3300000, 0xB3310000, 0xB3320000
HEADER, KEYS, STREAMS, TEXTURES, PLATFORMS = range(0x33310001, 0x33310006)
DATA_HEADER, DATA_ARRAY = 0x33320001, 0x33320002
INFO_SIZE, PLATFORM_SIZE, STREAM_SIZE = 124, 32, 24
MAX_TEXTURES = 4096


def u32(data: bytes, offset: int) -> int:
    if offset < 0 or offset + 4 > len(data):
        raise ArchiveError("Integer lies outside payload")
    return struct.unpack_from("<I", data, offset)[0]


def cstring(data: bytes) -> str:
    return data.split(b"\0", 1)[0].decode("ascii", errors="replace")


@dataclass
class TextureRecord:
    metadata: dict
    payload: bytes = b""
    error: str = ""
    palette: bytes = b""


def parse_info(raw: bytes, platform: bytes, expected_key: int) -> dict:
    if len(raw) != INFO_SIZE or len(platform) != PLATFORM_SIZE:
        raise ArchiveError("Unsupported serialized PC texture/platform record size")
    key = u32(raw, 0x24)
    if key != expected_key:
        raise ArchiveError("Texture record hash differs from its index entry")
    width, height = struct.unpack_from("<hh", raw, 0x44)
    validate_dimensions(width, height)
    placement, palette_placement, image_size, palette_size, base_size = struct.unpack_from("<5i", raw, 0x30)
    if placement < 0 or image_size <= 0 or base_size <= 0 or base_size > image_size:
        raise ArchiveError("Invalid image placement or base/total image size")
    name = cstring(raw[12:36])
    if not name or any(ord(c) < 32 or ord(c) > 126 for c in name):
        raise ArchiveError("Texture debug name is not supported printable ASCII")
    return {
        "name": name, "nameHash": f"{key:08x}", "nameBytesHex": raw[12:36].hex(),
        "debugNameMayBeTruncated": b"\0" not in raw[12:36],
        "width": width, "height": height, "imagePlacement": placement,
        "imageSize": image_size, "baseImageSize": base_size,
        "palettePlacement": palette_placement, "paletteSize": palette_size,
        "imageParentHash": f"{u32(raw, 0x2c):08x}",
        "classNameHash": f"{u32(raw, 0x28):08x}",
        "imageCompressionType": raw[0x4a], "paletteCompressionType": raw[0x4b],
        "paletteEntries": struct.unpack_from("<h", raw, 0x4c)[0],
        "sourceMipCount": raw[0x4e], "exportedMip": 0,
        "alphaUsage": raw[0x55], "alphaBlend": raw[0x56],
        "platformFormat": u32(platform, 20), "platformRecordBytesHex": platform.hex(),
        "textureRecordSha256": sha256(raw), "textureRecordBytesHex": raw.hex(),
        "serializedLayout": "PC-v5-info124-platform32-format20",
    }


def decode_texture(record: TextureRecord) -> Image:
    if record.error:
        raise DecodeError(record.error)
    m = record.metadata
    if m["imageParentHash"] != "00000000":
        raise DecodeError("Image-parent aliases require a separate verified resolver")
    width, height, format_number = m["width"], m["height"], m["platformFormat"]
    if format_number == 41:
        # The observed UI P8 palettes are monochrome. R/B channel order cannot
        # change them; coloured palette layouts remain explicitly unsupported.
        if (m["imageCompressionType"] not in (8, 129) or m["paletteCompressionType"] not in (0, 32)
                or m["paletteEntries"] != 256 or m["paletteSize"] != 1024
                or len(record.palette) != 1024 or m["baseImageSize"] != width * height
                or len(record.payload) < width * height):
            raise DecodeError("Unsupported P8 palette/texel serialization")
        entries = [record.palette[i:i + 4] for i in range(0, 1024, 4)]
        if any(colour[0] != colour[1] or colour[1] != colour[2] for colour in entries):
            raise DecodeError("Coloured P8 palette channel ordering is not verified")
        m["format"] = "P8-monochrome-RGBA-palette"
        return Image(width, height, b"".join(entries[index] for index in record.payload[:width * height]))
    if m["paletteSize"] or m["paletteEntries"]:
        raise DecodeError("Non-P8 texture declares an unexpected palette")
    code = struct.pack("<I", format_number)
    if code in (b"DXT1", b"DXT3", b"DXT5"):
        fourcc = code.decode("ascii")
        required = block_size(width, height, fourcc)
        expected_compression = {"DXT1": 34, "DXT3": 36, "DXT5": 38}[fourcc]
        if m["imageCompressionType"] != expected_compression:
            raise DecodeError("Texture compression and platform FourCC disagree")
        if required != m["baseImageSize"]:
            raise DecodeError("Declared base image size does not match the DXT top mip")
        m["format"] = fourcc
        return decode_blocks(record.payload, width, height, fourcc)
    if format_number in (21, 22):
        if m["imageCompressionType"] != 32 or width * height * 4 != m["baseImageSize"]:
            raise DecodeError("Unsupported packing for declared 32-bit PC RGB texture")
        m["format"] = "A8R8G8B8" if format_number == 21 else "X8R8G8B8"
        return decode_masked(record.payload, width, height, 32,
                             (0x00ff0000, 0x0000ff00, 0x000000ff, 0xff000000 if format_number == 21 else 0))
    raise DecodeError(f"Unsupported PC pixel format 0x{format_number:08x}")


def iter_textures(data: bytes, chunks: list[Chunk]) -> Iterator[TextureRecord]:
    children: dict[int | None, list[Chunk]] = defaultdict(list)
    for chunk in chunks:
        children[chunk.parent].append(chunk)

    def one(parent: int, kind: int) -> Chunk:
        found = [c for c in children[parent] if c.kind == kind]
        if len(found) != 1:
            raise ArchiveError(f"Expected exactly one 0x{kind:08x} chunk, found {len(found)}")
        return found[0]

    for pack in (c for c in chunks if c.kind == PACK):
        common = {"packChunkOffset": pack.offset, "coordinateSpace": "uncompressed-archive"}
        try:
            info, storage = one(pack.offset, INFO), one(pack.offset, DATA)
            header, index = one(info.offset, HEADER), one(info.offset, KEYS)
            data_header, pixels = one(storage.offset, DATA_HEADER), one(storage.offset, DATA_ARRAY)
            if header.size != 124 or data_header.size != 24 or index.size % 8:
                raise ArchiveError("Unsupported pack/header/index serialization")
            version = u32(data, header.data_offset)
            if version != 5:
                raise ArchiveError(f"Only observed PC texture pack version 5 is supported, found {version}")
            filename_hash = u32(data, header.data_offset + 0x60)
            if filename_hash != u32(data, data_header.data_offset + 12):
                raise ArchiveError("Pack header and VRAM data filename hashes disagree")
            count = index.size // 8
            if not 0 < count <= MAX_TEXTURES:
                raise ArchiveError("Texture index count exceeds bounds")
            common.update(packName=cstring(data[header.data_offset + 4:header.data_offset + 32]),
                          packFilename=cstring(data[header.data_offset + 32:header.data_offset + 96]),
                          packFilenameHash=f"{filename_hash:08x}", packVersion=version)
            pixel_start = pixels.aligned_offset(128)
            keys = [u32(data, index.data_offset + i * 8) for i in range(count)]
            if keys != sorted(keys) or len(set(keys)) != len(keys):
                raise ArchiveError("Texture index keys are not strictly ordered and unique")
            has_streams = any(c.kind == STREAMS for c in children[info.offset])
            if has_streams:
                stream = one(info.offset, STREAMS)
                if stream.size != count * STREAM_SIZE:
                    raise ArchiveError("Streaming table length does not match index")
                for number, key in enumerate(keys):
                    metadata = dict(common, recordIndex=number, nameHash=f"{key:08x}",
                                    streamRecordOffset=stream.data_offset + number * STREAM_SIZE)
                    try:
                        at = metadata["streamRecordOffset"]
                        actual, offset, size, unpacked, user_flags, flags, refs, pointer = struct.unpack_from("<IIiiBBHI", data, at)
                        metadata.update(streamFileOffset=offset, streamStoredSize=size,
                                        streamUncompressedSize=unpacked, streamFlags=flags,
                                        streamUserFlags=user_flags, streamReferenceCount=refs)
                        if actual != key or size < 16 or unpacked < INFO_SIZE + PLATFORM_SIZE:
                            raise ArchiveError("Invalid streamed texture index or size")
                        if size > MAX_ARCHIVE_BYTES or unpacked > MAX_ARCHIVE_BYTES:
                            raise ArchiveError("Streamed texture exceeds size limit")
                        if offset < pixel_start or offset + size > pixels.end:
                            raise ArchiveError("Streamed texture range lies outside its declared data chunk")
                        if flags not in (0, 1) or refs or pointer:
                            raise ArchiveError("Unsupported serialized streaming flags/pointers")
                        stored = data[offset:offset + size]
                        payload, compression = unwrap(stored) if flags & 1 else (stored, {"codec": "none"})
                        if len(payload) != unpacked:
                            raise ArchiveError("Streamed texture decompressed size differs from index")
                        metadata.update(streamSha256=sha256(stored), streamCompression=compression,
                                        textureRecordOffset=unpacked - INFO_SIZE - PLATFORM_SIZE,
                                        textureRecordCoordinateSpace="decompressed-stream")
                        metadata.update(parse_info(payload[-156:-32], payload[-32:], key))
                        length = metadata["imageSize"]
                        if length > len(payload) - 156:
                            raise ArchiveError("Streamed image overlaps metadata")
                        # The source callback assigns stream payload byte zero to ImagePlacement.
                        image = payload[:length]
                        metadata.update(imageDataOffset=0, imageDataCoordinateSpace="decompressed-stream",
                                        sourceImageSha256=sha256(image))
                        palette = b""
                        if metadata["paletteSize"]:
                            palette_start = metadata["palettePlacement"] - metadata["imagePlacement"]
                            palette_end = palette_start + metadata["paletteSize"]
                            if palette_start < 0 or palette_end > len(payload) - 156:
                                raise ArchiveError("Streamed palette lies outside its texture data")
                            palette = payload[palette_start:palette_end]
                            metadata.update(paletteDataOffset=palette_start, sourcePaletteSha256=sha256(palette))
                        yield TextureRecord(metadata, image, palette=palette)
                    except (ArchiveError, DecodeError) as error:
                        yield TextureRecord(metadata, error=str(error))
            else:
                table, platforms = one(info.offset, TEXTURES), one(info.offset, PLATFORMS)
                if table.size != count * INFO_SIZE or platforms.size != count * PLATFORM_SIZE:
                    raise ArchiveError("Index/info/platform table sizes do not match the observed PC layout")
                for number, key in enumerate(keys):
                    at = table.data_offset + number * INFO_SIZE
                    pat = platforms.data_offset + number * PLATFORM_SIZE
                    metadata = dict(common, recordIndex=number, nameHash=f"{key:08x}",
                                    textureRecordOffset=at, textureRecordCoordinateSpace="uncompressed-archive",
                                    platformRecordOffset=pat)
                    try:
                        metadata.update(parse_info(data[at:at + INFO_SIZE], data[pat:pat + PLATFORM_SIZE], key))
                        start = pixel_start + metadata["imagePlacement"]
                        end = start + metadata["imageSize"]
                        if start < pixel_start or end > pixels.end:
                            raise ArchiveError("Image range lies outside its VRAM data chunk")
                        image = data[start:end]
                        metadata.update(imageDataOffset=start, imageDataCoordinateSpace="uncompressed-archive",
                                        sourceImageSha256=sha256(image))
                        palette = b""
                        if metadata["paletteSize"]:
                            palette_start = pixel_start + metadata["palettePlacement"]
                            palette_end = palette_start + metadata["paletteSize"]
                            if palette_start < pixel_start or palette_end > pixels.end:
                                raise ArchiveError("Palette lies outside its VRAM data chunk")
                            palette = data[palette_start:palette_end]
                            metadata.update(paletteDataOffset=palette_start, sourcePaletteSha256=sha256(palette))
                        yield TextureRecord(metadata, image, palette=palette)
                    except (ArchiveError, DecodeError) as error:
                        yield TextureRecord(metadata, error=str(error))
        except (ArchiveError, DecodeError) as error:
            yield TextureRecord(common, error=str(error))
