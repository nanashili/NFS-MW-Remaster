"""Validate a published local decoding and produce non-runtime alpha previews.

The PNG reader intentionally accepts only the RGBA8/filter-zero PNGs emitted by
this tool. It is not a general image-file importer. Preview compositing never
changes an decoded asset; diagnostic checkerboards live under Tools only.
"""

from __future__ import annotations

import collections
import json
import re
import struct
import zlib
from pathlib import Path

from archive_decode import sha256
from decode_frontend import ASSETS, PROJECT, SOURCE, TOOLS, guarded, immutable_write, json_bytes, read_source, safe_name
from texture_decode import Image, png_bytes, validate_dimensions


def read_generated_png(data: bytes) -> Image:
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("Not a PNG")
    width = height = 0
    compressed, position = bytearray(), 8
    kinds = []
    while position < len(data):
        if position + 12 > len(data):
            raise ValueError("Truncated PNG chunk")
        length = struct.unpack_from(">I", data, position)[0]
        end = position + 12 + length
        if end > len(data):
            raise ValueError("PNG chunk exceeds file")
        kind, payload = data[position + 4:position + 8], data[position + 8:position + 8 + length]
        if zlib.crc32(kind + payload) & 0xffffffff != struct.unpack_from(">I", data, position + 8 + length)[0]:
            raise ValueError("PNG CRC mismatch")
        kinds.append(kind)
        if kind == b"IHDR":
            if len(kinds) != 1 or length != 13:
                raise ValueError("Unexpected PNG header")
            width, height, depth, colour, compression, filtering, interlace = struct.unpack(">IIBBBBB", payload)
            validate_dimensions(width, height)
            if (depth, colour, compression, filtering, interlace) != (8, 6, 0, 0, 0):
                raise ValueError("Expected generated non-interlaced RGBA8 PNG")
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind != b"IEND":
            raise ValueError("Unexpected chunk in generated PNG")
        position = end
    if not width or kinds != [b"IHDR", b"IDAT", b"IEND"]:
        raise ValueError("Unexpected generated PNG structure")
    expected = (width * 4 + 1) * height
    inflater = zlib.decompressobj()
    decoded = inflater.decompress(bytes(compressed), expected + 1)
    if len(decoded) != expected or not inflater.eof or inflater.unused_data or inflater.unconsumed_tail:
        raise ValueError("PNG decompressed length differs from its dimensions")
    stride = width * 4 + 1
    if any(decoded[row] != 0 for row in range(0, expected, stride)):
        raise ValueError("Expected filter-zero generated PNG")
    return Image(width, height, b"".join(decoded[row + 1:row + stride] for row in range(0, expected, stride)))


def checkerboard(image: Image) -> Image:
    result = bytearray(image.rgba)
    for y in range(image.height):
        for x in range(image.width):
            offset = (y * image.width + x) * 4
            alpha = image.rgba[offset + 3]
            background = 64 if (x // 16 + y // 16) % 2 else 96
            for channel in range(3):
                result[offset + channel] = (image.rgba[offset + channel] * alpha + background * (255 - alpha) + 127) // 255
            result[offset + 3] = 255
    return Image(image.width, image.height, bytes(result))


def main() -> None:
    manifest = json.loads(guarded(ASSETS / "Catalog.json", ASSETS).read_text())
    evidence_path = guarded(PROJECT / manifest["evidence"], TOOLS)
    evidence = json.loads(evidence_path.read_text())
    # Capture evidence remains immutable. Resolve its images by content identity
    # because the library now separates UI, world and vehicle textures.
    lookup = {(t["nameHash"], t["pngSha256"]): t for t in evidence["textures"] if t["status"] == "decoded"}
    result = {"captureEvidence": manifest["evidence"], "verifiedSourceHashes": [], "verifiedTextures": [],
              "previews": [], "errors": []}
    preview_names = {"FONT_MW_TITLE", "FONT_MW_BODY", "FONT_MW_CUSTOM_NUMBERS", "MW_LOGO", "SPLATTER_01",
                     "MAIN_ICON_CAREER", "MAIN_ICON_TOP_15", "OPTIONS_ICON_AUDIO", "LOAD_SPLATTER",
                     "MAIN_ICON_LAN", "MODE_ICON_CIRCUIT", "TRACKMAP_MASK", "LOADING_GRIT",
                     "RIVAL_01", "RIVAL_01_IG", "MW_GRIT_01", "MW_GRIT_02", "GRIT_VINETTE_01",
                     "GRIT_VINETTE_02", "GRIT_UNDERLAY", "GRIT_LONG"}
    rival_previews = 0
    for source in evidence["sources"]:
        actual = sha256(read_source(SOURCE / source["name"]))
        if actual != source["sha256"]:
            raise ValueError("Staged source hash changed: " + source["name"])
        result["verifiedSourceHashes"].append({"name": source["name"], "sha256": actual})
    from image_library import locate_texture
    captured = []
    for record in lookup.values():
        location = locate_texture(record["nameHash"], record["pngSha256"])
        texture = dict(record, resourcePath=location["resourcePath"], assetPath=location["assetPath"])
        captured.append(texture)
        path = guarded(PROJECT / texture["assetPath"], PROJECT / "Assets/NfsMw/Content")
        encoded = path.read_bytes()
        if sha256(encoded) != record["pngSha256"] or texture["pngSha256"] != record["pngSha256"]:
            raise ValueError("Texture PNG hash mismatch: " + texture["name"])
        image = read_generated_png(encoded)
        if sha256(image.rgba) != record["rgbaSha256"] or (image.width, image.height) != (texture["width"], texture["height"]):
            raise ValueError("Texture pixels/dimensions differ from manifest: " + texture["name"])
        alpha = image.rgba[3::4]
        result["verifiedTextures"].append({"name": texture["name"], "assetPath": texture["assetPath"], "resourcePath": texture["resourcePath"],
                                           "pngSha256": record["pngSha256"], "alphaMinimum": min(alpha), "alphaMaximum": max(alpha)})
        if texture["name"] in preview_names or (texture["source"] == "GLOBAL/RIVALS.BIN" and rival_previews < 2):
            if texture["source"] == "GLOBAL/RIVALS.BIN":
                rival_previews += 1
            preview = evidence_path.parent / "Previews" / (safe_name(texture["name"]) + "_" + texture["nameHash"] + ".checkerboard.png")
            immutable_write(preview, png_bytes(checkerboard(image)), TOOLS)
            result["previews"].append({"name": texture["name"], "sourceAsset": path.relative_to(PROJECT).as_posix(),
                                        "previewPath": preview.relative_to(PROJECT).as_posix(),
                                        "meaning": "Diagnostic checkerboard composite; never a runtime asset"})
            print("PREVIEW", texture["name"], image.width, image.height, "alpha", min(alpha), max(alpha), preview.relative_to(PROJECT))
    for record in evidence["textures"]:
        if record.get("reason") == "Palettized texture decoding is not supported":
            print("PALETTE_CASE", {key: record.get(key) for key in ("name", "source", "platformFormat", "imageCompressionType", "paletteSize", "paletteEntries", "palettePlacement", "width", "height")})
    print("RIVAL_NAMES", [t["name"] for t in captured if t["source"] == "GLOBAL/RIVALS.BIN"])
    print("DYNAMIC_NAMES", [t["name"] for t in captured if t["source"] == "GLOBAL/DYNTEX.BIN"])
    print("GRUNGE_FRAME_NAMES", [t["name"] for t in captured if re.search(r"GRUN|GRIT|FRAME|SPLAT|BRUSH|INK|SKETCH|SCRIB|STREAK|VIGN|VINET|BACKING|FE_BACK|CORNER|BLOT|PAINT|BOARD|STRIPE|LINE_|_BAR", t["name"])])
    result.update(verifiedTextureCount=len(result["verifiedTextures"]),
                  duplicateResourcePaths=[name for name, n in collections.Counter(t["resourcePath"] for t in manifest["textures"]).items() if n > 1])
    if result["duplicateResourcePaths"]:
        raise ValueError("Duplicate resource paths in runtime manifest")
    payload = json_bytes(result)
    output = evidence_path.parent / ("validation-" + sha256(payload)[:16] + ".json")
    immutable_write(output, payload, TOOLS)
    print("VALIDATED", len(captured), "PNG hashes/pixels/CRCs/dimensions and", len(evidence["sources"]), "source hashes", output.relative_to(PROJECT))


if __name__ == "__main__":
    main()
