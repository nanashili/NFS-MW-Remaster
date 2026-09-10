"""Synthetic tests. Fixtures are self-authored, not game data or screenshots."""

from __future__ import annotations

import contextlib
import io
import json
import struct
import tempfile
import unittest
import zlib
from pathlib import Path
from unittest.mock import patch

import archive_decode
import decode_frontend
from archive_decode import ArchiveError, Chunk, decompress_jdlz, read_chunks, unwrap
from texture_archive import TextureRecord, decode_texture, iter_textures, parse_info
from texture_decode import DecodeError, Image, block_size, decode_blocks, decode_dds, decode_masked, png_bytes
from font_metrics import FontError, parse_font, render_sample
from audit_capture import read_generated_png


def jdlz(payload: bytes, expected: int, control: int = 0, run_type: int = 0) -> bytes:
    return struct.pack("<4sBBHII", b"JDLZ", 2, 16, 0, expected, len(payload) + 18) + bytes((control, run_type)) + payload


def chunk(kind: int, payload: bytes) -> bytes:
    return struct.pack("<II", kind, len(payload)) + payload


def make_pack(*, stream: bool = False, mismatched_key: bool = False) -> bytes:
    key, file_hash = 0x1000, 0x2000
    info = bytearray(124)
    info[12:17] = b"TEST\0"
    struct.pack_into("<I", info, 0x24, key + int(mismatched_key))
    struct.pack_into("<5i", info, 0x30, 0, -1, 8, 0, 8)
    struct.pack_into("<hh", info, 0x44, 4, 4)
    info[0x4a], info[0x4e] = 34, 1
    platform = bytearray(32)
    platform[20:24] = b"DXT1"
    header = bytearray(124)
    struct.pack_into("<I", header, 0, 5)
    header[4:9] = b"TEST\0"
    header[32:41] = b"test.tpk\0"
    struct.pack_into("<I", header, 0x60, file_hash)
    common = chunk(0x33310001, bytes(header)) + chunk(0x33310002, struct.pack("<II", key, 0))
    texture = struct.pack("<HHI", 0xf800, 0x07e0, 0)
    pixel = texture + bytes(info) + bytes(platform) if stream else texture
    table = chunk(0x33310003, bytes(24)) if stream else chunk(0x33310004, bytes(info)) + chunk(0x33310005, bytes(platform))
    information = chunk(0xb3310000, common + table)
    storage_header = bytearray(24)
    struct.pack_into("<I", storage_header, 12, file_hash)
    # pack8 + information + storage8 + storage_header32 + pixels_header8
    unaligned = 8 + len(information) + 8 + 32 + 8
    pad = (-unaligned) % 128
    start = unaligned + pad
    if stream:
        table = chunk(0x33310003, struct.pack("<IIiiBBHI", key, start, len(pixel), len(pixel), 0, 0, 0, 0))
        information = chunk(0xb3310000, common + table)
    storage = chunk(0xb3320000, chunk(0x33320001, bytes(storage_header)) + chunk(0x33320002, bytes([17]) * pad + pixel))
    return chunk(0xb3300000, information + storage)


def make_font() -> tuple[bytes, Image]:
    data = bytearray(720)
    data[:10], data[256:266] = b"font_test\0", b"font_test\0"
    data[512:516] = b"FNTF"
    struct.pack_into("<HH", data, 520, 414, 2)
    data[530], data[531] = 6, 2
    struct.pack_into("<III", data, 532, 128, 160, 208)
    struct.pack_into("<I", data, 548, 8)
    struct.pack_into("<I", data, 560, 176)
    struct.pack_into("<ff", data, 576, 1 / 8, 1 / 8)
    struct.pack_into("<HBBHHBbBBHH", data, 640, 32, 0, 0, 1, 1, 0, 0, 6, 0, 0, 2)
    struct.pack_into("<HBBHHBbBBHH", data, 656, 65, 2, 3, 2, 2, 0, -1, 2, 1, 0, 3)
    struct.pack_into("<IHbB", data, 672, 1, 65, -1, 65)
    struct.pack_into("<II", data, 712, 8, 8)
    return bytes(data), Image(8, 8, bytes((255, 255, 255, 255)) * 64)


class ArchiveTests(unittest.TestCase):
    def test_jdlz_literal_and_terminal_padding(self):
        decoded, metadata = decompress_jdlz(jdlz(b"ABC\0", 3))
        self.assertEqual(b"ABC", decoded)
        self.assertEqual("00", metadata["terminalBytesHex"])

    def test_jdlz_short_overlapping_copy(self):
        decoded, _ = decompress_jdlz(jdlz(b"A\0\x02", 6, control=2, run_type=1))
        self.assertEqual(b"AAAAAA", decoded)

    def test_jdlz_long_distance_and_control_reload(self):
        literals = bytes(range(17))
        payload = literals[:8] + b"\0" + literals[8:16] + b"\x02" + literals[16:] + b"\0\0"
        decoded, _ = decompress_jdlz(jdlz(payload, 20))
        self.assertEqual(literals + literals[:3], decoded)

    def test_jdlz_rejects_before_start_reference(self):
        with self.assertRaises(ArchiveError):
            decompress_jdlz(jdlz(b"\0\0", 3, 1, 1))

    def test_jdlz_rejects_overrun_reference(self):
        with self.assertRaises(ArchiveError):
            decompress_jdlz(jdlz(b"A\0\xff", 4, 2, 1))

    def test_jdlz_rejects_truncated_token(self):
        with self.assertRaises(ArchiveError):
            decompress_jdlz(jdlz(b"A\0", 4, 2, 1))

    def test_jdlz_rejects_header_variants_and_sizes(self):
        original = jdlz(b"A", 1)
        for offset, value in ((4, 1), (5, 12), (6, 1), (8, 0), (12, 1)):
            data = bytearray(original)
            data[offset] = value
            with self.subTest(offset=offset), self.assertRaises(ArchiveError):
                decompress_jdlz(bytes(data))
        huge = bytearray(original)
        struct.pack_into("<I", huge, 8, archive_decode.MAX_ARCHIVE_BYTES + 1)
        with self.assertRaises(ArchiveError):
            decompress_jdlz(bytes(huge))

    def test_jdlz_rejects_unexplained_tail(self):
        with self.assertRaises(ArchiveError):
            decompress_jdlz(jdlz(b"A1234", 1))

    def test_unsupported_codecs_are_not_treated_as_chunks(self):
        for magic in (b"HUFF", b"COMP", b"RAWW", b"\x22\x11\x44\x55"):
            with self.subTest(magic=magic), self.assertRaises(ArchiveError):
                unwrap(magic + bytes(32))

    def test_chunk_tree_exact_offsets(self):
        data = chunk(0x80000100, chunk(2, b"ABCD") + chunk(0, b""))
        chunks = read_chunks(data)
        self.assertEqual([(0, None, 0), (8, 0, 1), (20, 0, 1)], [(c.offset, c.parent, c.depth) for c in chunks])

    def test_chunk_overflow_truncation_negative_and_depth(self):
        for data in (b"A", struct.pack("<Ii", 1, -1), chunk(1, b"") + b"A", struct.pack("<II", 1, 20)):
            with self.subTest(data=data), self.assertRaises(ArchiveError):
                read_chunks(data)
        data = b""
        for _ in range(34):
            data = chunk(0x80000001, data)
        with self.assertRaises(ArchiveError):
            read_chunks(data)

    def test_alignment_is_absolute_and_bounded(self):
        self.assertEqual(128, Chunk(120, 1, 32, 0, None).aligned_offset(128))
        with self.assertRaises(ArchiveError):
            Chunk(128, 1, 8, 0, None).aligned_offset(128)
        with self.assertRaises(ArchiveError):
            Chunk(0, 1, 8, 0, None).aligned_offset(3)


class PixelTests(unittest.TestCase):
    def test_bc1_red_and_transparency(self):
        red = decode_blocks(struct.pack("<HHI", 0xf800, 0x07e0, 0), 4, 4, "DXT1")
        self.assertEqual(bytes((255, 0, 0, 255)) * 16, red.rgba)
        clear = decode_blocks(struct.pack("<HHI", 0, 0xffff, 0xffffffff), 4, 4, "DXT1")
        self.assertEqual(bytes(64), clear.rgba)

    def test_bc2_always_four_colours_and_explicit_alpha(self):
        image = decode_blocks(bytes([0x8f]) * 8 + struct.pack("<HHI", 0, 0xffff, 0xffffffff), 4, 4, "DXT3")
        self.assertEqual(bytes((170, 170, 170, 255, 170, 170, 170, 136)), image.rgba[:8])

    def test_bc3_both_alpha_interpolation_modes(self):
        selectors = sum((n % 8) << (n * 3) for n in range(16)).to_bytes(6, "little")
        colours = struct.pack("<HHI", 0xf800, 0, 0)
        for endpoints, expected in (((255, 0), [255, 0, 218, 182, 145, 109, 72, 36]),
                                    ((0, 255), [0, 255, 51, 102, 153, 204, 0, 255])):
            image = decode_blocks(bytes(endpoints) + selectors + colours, 4, 4, "DXT5")
            self.assertEqual(expected, list(image.rgba[3:32:4]))

    def test_clipped_blocks_and_truncation(self):
        block = struct.pack("<HHI", 0xf800, 0, 0)
        self.assertEqual(5 * 3 * 4, len(decode_blocks(block * 2, 5, 3, "DXT1").rgba))
        with self.assertRaises(DecodeError):
            decode_blocks(block, 5, 3, "DXT1")
        for width, height in ((0, 4), (-1, 4), (4097, 1)):
            with self.assertRaises(DecodeError):
                block_size(width, height, "DXT1")

    def test_bgra_channels_and_pitch(self):
        masks = (0xff0000, 0xff00, 0xff, 0xff000000)
        image = decode_masked(bytes((1, 2, 3, 4, 0, 0, 0, 0, 5, 6, 7, 8, 0, 0, 0, 0)), 1, 2, 32, masks, 8)
        self.assertEqual(bytes((3, 2, 1, 4, 7, 6, 5, 8)), image.rgba)
        image = decode_masked(bytes((1, 2, 3, 4)), 1, 1, 32, masks[:3] + (0,))
        self.assertEqual(255, image.rgba[-1])

    def test_bad_masks_and_pitch(self):
        for masks in ((1, 1, 2, 0), (0, 1, 2, 0), (5, 8, 16, 0)):
            with self.assertRaises(DecodeError):
                decode_masked(bytes(4), 1, 1, 32, masks)
        with self.assertRaises(DecodeError):
            decode_masked(bytes(4), 1, 2, 32, (0xff0000, 0xff00, 0xff, 0xff000000))

    def test_png_crc_and_raw_rows(self):
        raw = bytes((12, 34, 56, 78)) * 6
        encoded = png_bytes(Image(3, 2, raw))
        self.assertEqual(b"\x89PNG\r\n\x1a\n", encoded[:8])
        pos, compressed = 8, b""
        while pos < len(encoded):
            size = struct.unpack_from(">I", encoded, pos)[0]
            kind, payload = encoded[pos + 4:pos + 8], encoded[pos + 8:pos + 8 + size]
            self.assertEqual(zlib.crc32(kind + payload) & 0xffffffff, struct.unpack_from(">I", encoded, pos + 8 + size)[0])
            if kind == b"IDAT":
                compressed += payload
            pos += 12 + size
        self.assertEqual(b"\0" + raw[:12] + b"\0" + raw[12:], zlib.decompress(compressed))

    def test_dds_top_mip_and_cube_rejection(self):
        header = bytearray(128)
        header[:4] = b"DDS "
        struct.pack_into("<I", header, 4, 124)
        struct.pack_into("<6I", header, 8, 0x81007, 4, 4, 8, 0, 1)
        struct.pack_into("<II4s", header, 76, 32, 4, b"DXT1")
        image, _ = decode_dds(bytes(header) + struct.pack("<HHI", 0xf800, 0, 0))
        self.assertEqual(255, image.rgba[0])
        struct.pack_into("<I", header, 112, 0x200)
        with self.assertRaises(DecodeError):
            decode_dds(bytes(header) + bytes(8))


class TexturePackTests(unittest.TestCase):
    def test_monochrome_p8_preserves_alpha_without_channel_order_guess(self):
        metadata = {"width": 2, "height": 1, "imageParentHash": "00000000", "platformFormat": 41,
                    "imageCompressionType": 129, "paletteCompressionType": 32, "paletteEntries": 256,
                    "paletteSize": 1024, "baseImageSize": 2}
        palette = bytes((5, 5, 5, 0, 255, 255, 255, 128)) + bytes(1016)
        record = TextureRecord(metadata, bytes((0, 1)), palette=palette)
        self.assertEqual(palette[:8], decode_texture(record).rgba)
        record.metadata["paletteCompressionType"] = 0
        self.assertEqual(palette[:8], decode_texture(record).rgba)
        record.palette = bytes((1, 2, 3, 4)) + palette[4:]
        with self.assertRaises(DecodeError):
            decode_texture(record)

    def test_inline_pack_joins_verified_index_and_metadata(self):
        data = make_pack()
        records = list(iter_textures(data, read_chunks(data)))
        self.assertEqual(1, len(records))
        self.assertFalse(records[0].error)
        image = decode_texture(records[0])
        self.assertEqual("TEST", records[0].metadata["name"])
        self.assertEqual(bytes((255, 0, 0, 255)) * 16, image.rgba)

    def test_streamed_uncompressed_pack_keeps_origin(self):
        data = make_pack(stream=True)
        records = list(iter_textures(data, read_chunks(data)))
        self.assertFalse(records[0].error)
        self.assertEqual("decompressed-stream", records[0].metadata["imageDataCoordinateSpace"])
        self.assertEqual(255, decode_texture(records[0]).rgba[0])

    def test_mismatched_key_is_retained_as_unsupported(self):
        data = make_pack(mismatched_key=True)
        records = list(iter_textures(data, read_chunks(data)))
        self.assertIn("hash differs", records[0].error)

    def test_out_of_bounds_image_and_format_mismatch(self):
        data = bytearray(make_pack())
        chunks = read_chunks(bytes(data))
        info = next(c for c in chunks if c.kind == 0x33310004)
        struct.pack_into("<i", data, info.data_offset + 0x30, 0x7fffffff)
        self.assertIn("outside", list(iter_textures(bytes(data), chunks))[0].error)
        data = bytearray(make_pack())
        data[info.data_offset + 0x4a] = 36
        record = list(iter_textures(bytes(data), chunks))[0]
        with self.assertRaises(DecodeError):
            decode_texture(record)


class PublicationTests(unittest.TestCase):
    def setUp(self):
        # Keep even synthetic temporary files inside this worker's assigned directory.
        self.temp = tempfile.TemporaryDirectory(prefix="test-", dir=decode_frontend.TOOLS)
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_immutable_reuse_and_no_overwrite(self):
        path = self.root / "test.bin"
        self.assertEqual("created", decode_frontend.immutable_write(path, b"original", self.root))
        self.assertEqual("reused", decode_frontend.immutable_write(path, b"original", self.root))
        with self.assertRaises(FileExistsError):
            decode_frontend.immutable_write(path, b"changed", self.root)
        self.assertEqual(b"original", path.read_bytes())

    def test_traversal_and_symlink_are_rejected(self):
        with self.assertRaises(ValueError):
            decode_frontend.guarded(self.root / "../escape.bin", self.root)
        target = self.root / "target"
        target.mkdir()
        link = self.root / "link"
        link.symlink_to(target, target_is_directory=True)
        with self.assertRaises(ValueError):
            decode_frontend.immutable_write(link / "file", b"x", self.root)
        self.assertFalse((target / "file").exists())

    def test_existing_unowned_manifest_is_preserved(self):
        assets = self.root / "Assets"
        assets.mkdir()
        target = assets / "Catalog.json"
        target.write_text('{"generator":"someone-else"}')
        with patch.object(decode_frontend, "ASSETS", assets), self.assertRaises(FileExistsError):
            decode_frontend.publish_manifest(b'{"generator":"new"}')
        self.assertEqual('{"generator":"someone-else"}', target.read_text())

    def test_unity_importer_changes_with_stable_guid_are_preserved(self):
        path = self.root / "test.png.meta"
        expected = decode_frontend.texture_meta("synthetic/test")
        authored = expected + b"# importer settings changed by Unity or the user\n"
        path.write_bytes(authored)
        with patch.object(decode_frontend, "ASSETS", self.root):
            decode_frontend.preserve_or_create_meta(path, expected)
        self.assertEqual(authored, path.read_bytes())

    def test_manifest_name_cannot_escape_owned_allowlist(self):
        with self.assertRaises(ValueError):
            decode_frontend.publish_owned_manifest("../other.json", b"{}")

    def test_font_evidence_is_never_a_font_conversion(self):
        raw = chunk(0x30201, b"font_test\0" + bytes(64))
        records = decode_frontend.inspect_fe(raw, read_chunks(raw), "synthetic", self.root, False)
        self.assertEqual("opaque-font-metrics", records[0]["status"])
        self.assertFalse(records[0]["fontConversionPerformed"])
        self.assertEqual([], list(self.root.iterdir()))

    def test_unsupported_fe_codec_keeps_provenance(self):
        codec = struct.pack("<4sBBHII", b"HUFF", 1, 16, 0, 20, 16)
        raw = chunk(0x30210, bytes(4) + codec)
        records = decode_frontend.inspect_fe(raw, read_chunks(raw), "synthetic", self.root, False)
        self.assertEqual("unsupported", records[0]["status"])
        self.assertEqual(64, len(records[0]["payloadSha256"]))


class FontTests(unittest.TestCase):
    def test_exact_rect_advance_bearings_and_kerning(self):
        data, atlas = make_font()
        font, evidence = parse_font(data, atlas, "synthetic/atlas")
        self.assertEqual(2, evidence["glyphCount"])
        self.assertEqual(8, font["lineHeight"])
        self.assertEqual({"codepoint": 65, "x": 2, "y": 2, "width": 2, "height": 3,
                          "advance": 3, "bearingX": -1, "bearingY": 4, "offsetY": 2}, font["glyphs"][1])
        self.assertEqual([{"leftCodepoint": 65, "rightCodepoint": 65, "advanceAdjustment": -1}], font["kerning"])
        self.assertEqual(255, render_sample(font, atlas, ["A A", "AA"]).rgba[-1])

    def test_rejects_wrong_atlas_and_inverse_dimensions(self):
        data, atlas = make_font()
        with self.assertRaises(FontError):
            parse_font(data, Image(16, 8, bytes(512)), "synthetic")
        changed = bytearray(data)
        struct.pack_into("<f", changed, 576, float("nan"))
        with self.assertRaises(FontError):
            parse_font(bytes(changed), atlas, "synthetic")

    def test_rejects_font_truncation_counts_rectangles_and_kerning_ownership(self):
        original, atlas = make_font()
        for offset, value in ((522, 3), (532, 0), (656, 32), (658, 255), (679, 66)):
            changed = bytearray(original)
            changed[offset] = value
            with self.subTest(offset=offset), self.assertRaises(FontError):
                parse_font(bytes(changed), atlas, "synthetic")
        with self.assertRaises(FontError):
            parse_font(original[:-1], atlas, "synthetic")

    def test_png_validation_rejects_crc_corruption_and_decompression_tail(self):
        image = Image(2, 2, bytes(16))
        encoded = png_bytes(image)
        self.assertEqual(image, read_generated_png(encoded))
        corrupted = bytearray(encoded)
        corrupted[-5] ^= 1
        with self.assertRaises(ValueError):
            read_generated_png(bytes(corrupted))


if __name__ == "__main__":
    unittest.main(verbosity=2)
