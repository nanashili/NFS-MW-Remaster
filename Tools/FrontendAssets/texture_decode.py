"""Small, bounded, dependency-free texture decoder for local frontend recovery.

Only the top mip is exported. No DLL, executable, external process or plugin is
loaded. The image result is raw RGBA8 in source row order; no artistic edits,
upscaling, screenshot segmentation, recolouring or alpha premultiplication occurs.
"""

from __future__ import annotations

import struct
import zlib
from dataclasses import dataclass

MAX_DIMENSION = 4096
MAX_PIXELS = 16_777_216


class DecodeError(ValueError):
    """The input is malformed or outside the explicitly supported formats."""


@dataclass(frozen=True)
class Image:
    width: int
    height: int
    rgba: bytes


def validate_dimensions(width: int, height: int) -> None:
    if not (1 <= width <= MAX_DIMENSION and 1 <= height <= MAX_DIMENSION):
        raise DecodeError(f"Texture dimensions {width}x{height} exceed bounds")
    if width * height > MAX_PIXELS:
        raise DecodeError("Texture pixel count exceeds bounds")


def block_size(width: int, height: int, fourcc: str) -> int:
    validate_dimensions(width, height)
    if fourcc not in ("DXT1", "DXT3", "DXT5"):
        raise DecodeError(f"Unsupported block compression: {fourcc!r}")
    return ((width + 3) // 4) * ((height + 3) // 4) * (8 if fourcc == "DXT1" else 16)


def _rgb565(value: int) -> tuple[int, int, int, int]:
    red, green, blue = (value >> 11) & 31, (value >> 5) & 63, value & 31
    return (red << 3 | red >> 2, green << 2 | green >> 4, blue << 3 | blue >> 2, 255)


def decode_blocks(data: bytes, width: int, height: int, fourcc: str) -> Image:
    required = block_size(width, height, fourcc)
    if len(data) < required:
        raise DecodeError(f"Truncated {fourcc} top mip: need {required}, have {len(data)}")
    result = bytearray(width * height * 4)
    stride = 8 if fourcc == "DXT1" else 16
    position = 0
    for block_y in range(0, height, 4):
        for block_x in range(0, width, 4):
            block = data[position:position + stride]
            position += stride
            colour_offset = 0 if fourcc == "DXT1" else 8
            c0, c1, indices = struct.unpack_from("<HHI", block, colour_offset)
            first, second = _rgb565(c0), _rgb565(c1)
            if c0 > c1 or fourcc != "DXT1":
                colours = (first, second,
                           tuple((2 * first[i] + second[i]) // 3 for i in range(3)) + (255,),
                           tuple((first[i] + 2 * second[i]) // 3 for i in range(3)) + (255,))
            else:
                colours = (first, second,
                           tuple((first[i] + second[i]) // 2 for i in range(3)) + (255,),
                           (0, 0, 0, 0))
            alpha_indices = 0
            alphas: tuple[int, ...] = ()
            if fourcc == "DXT3":
                alpha_indices = int.from_bytes(block[:8], "little")
            elif fourcc == "DXT5":
                a0, a1 = block[0], block[1]
                if a0 > a1:
                    alphas = (a0, a1) + tuple(((7 - i) * a0 + i * a1) // 7 for i in range(1, 7))
                else:
                    alphas = (a0, a1) + tuple(((5 - i) * a0 + i * a1) // 5 for i in range(1, 5)) + (0, 255)
                alpha_indices = int.from_bytes(block[2:8], "little")
            for pixel in range(16):
                x, y = block_x + pixel % 4, block_y + pixel // 4
                if x >= width or y >= height:
                    continue
                red, green, blue, alpha = colours[(indices >> (2 * pixel)) & 3]
                if fourcc == "DXT3":
                    alpha = ((alpha_indices >> (4 * pixel)) & 15) * 17
                elif fourcc == "DXT5":
                    alpha = alphas[(alpha_indices >> (3 * pixel)) & 7]
                offset = (y * width + x) * 4
                result[offset:offset + 4] = bytes((red, green, blue, alpha))
    return Image(width, height, bytes(result))


def decode_masked(data: bytes, width: int, height: int, bits: int,
                  masks: tuple[int, int, int, int], pitch: int | None = None) -> Image:
    validate_dimensions(width, height)
    if bits not in (16, 24, 32):
        raise DecodeError(f"Unsupported RGB bit depth {bits}")
    if any(mask == 0 for mask in masks[:3]):
        raise DecodeError("RGB channel masks must be present")
    channels: list[tuple[int, int] | None] = []
    seen = 0
    for mask in masks:
        if mask < 0 or mask >= (1 << bits) or seen & mask:
            raise DecodeError("Overlapping or out-of-range channel mask")
        seen |= mask
        if not mask:
            channels.append(None)
            continue
        shift = (mask & -mask).bit_length() - 1
        maximum = mask >> shift
        if maximum & (maximum + 1):
            raise DecodeError("Non-contiguous channel mask")
        channels.append((shift, maximum))
    bytes_per_pixel = bits // 8
    packed_pitch = width * bytes_per_pixel
    pitch = packed_pitch if pitch is None else pitch
    if pitch < packed_pitch or pitch > packed_pitch + 4096 or len(data) < pitch * height:
        raise DecodeError("Invalid pitch or truncated RGB texture")
    result = bytearray(width * height * 4)
    for y in range(height):
        for x in range(width):
            offset = y * pitch + x * bytes_per_pixel
            value = int.from_bytes(data[offset:offset + bytes_per_pixel], "little")
            output = (y * width + x) * 4
            for channel, spec in enumerate(channels):
                result[output + channel] = 255 if spec is None else ((value >> spec[0]) & spec[1]) * 255 // spec[1]
    return Image(width, height, bytes(result))


def decode_dds(data: bytes) -> tuple[Image, dict]:
    if len(data) < 128 or data[:4] != b"DDS ":
        raise DecodeError("Missing or truncated DDS header")
    if struct.unpack_from("<I", data, 4)[0] != 124 or struct.unpack_from("<I", data, 76)[0] != 32:
        raise DecodeError("Unsupported DDS header layout")
    flags, height, width, pitch, depth, mip_count = struct.unpack_from("<6I", data, 8)
    validate_dimensions(width, height)
    caps2 = struct.unpack_from("<I", data, 112)[0]
    if depth > 1 or caps2 & (0xFE00 | 0x200000):
        raise DecodeError("Cubemaps and volume DDS textures are not frontend 2D assets")
    pixel_flags = struct.unpack_from("<I", data, 80)[0]
    metadata = {"width": width, "height": height, "sourceMipCount": max(1, mip_count), "exportedMip": 0}
    if pixel_flags & 4:
        fourcc = data[84:88].decode("ascii", errors="replace")
        metadata["format"] = fourcc
        return decode_blocks(data[128:], width, height, fourcc), metadata
    if not pixel_flags & 0x40:
        raise DecodeError("Only declared RGB or DXT DDS pixel formats are supported")
    bits, red, green, blue, alpha = struct.unpack_from("<5I", data, 88)
    if not pixel_flags & 1:
        alpha = 0
    metadata["format"] = f"RGB{bits}"
    metadata["channelMasks"] = [red, green, blue, alpha]
    image = decode_masked(data[128:], width, height, bits, (red, green, blue, alpha), pitch if flags & 8 else None)
    return image, metadata


def png_bytes(image: Image) -> bytes:
    validate_dimensions(image.width, image.height)
    if len(image.rgba) != image.width * image.height * 4:
        raise DecodeError("RGBA payload length does not match image dimensions")

    def chunk(kind: bytes, payload: bytes) -> bytes:
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)

    stride = image.width * 4
    compressor = zlib.compressobj(level=9)
    pieces = [compressor.compress(b"\0" + image.rgba[row:row + stride]) for row in range(0, len(image.rgba), stride)]
    pieces.append(compressor.flush())
    header = struct.pack(">IIBBBBB", image.width, image.height, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", b"".join(pieces)) + chunk(b"IEND", b"")
