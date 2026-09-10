"""Decode the FNTF bitmap-font layout observed in the three local font records.

This is a data-derived reader, not a recovered original font renderer. It checks
redundant table offsets/counts, atlas dimensions/reciprocals and every glyph rect.
ASCII keys map directly; non-ASCII original character keys retain their numeric
values without an invented Windows/code-page or Unicode conversion.
"""

from __future__ import annotations

import json
import math
import struct
from pathlib import Path

from archive_decode import sha256
from audit_capture import checkerboard, read_generated_png
from decode_frontend import ASSETS, PROJECT, TOOL, TOOLS, guarded, immutable_write, json_bytes, preserve_or_create_meta, publish_owned_manifest
from texture_decode import Image, png_bytes, validate_dimensions


class FontError(ValueError):
    """The supplied font does not match the independently checked local layout."""


def parse_font(data: bytes, atlas: Image, texture_resource: str) -> tuple[dict, dict]:
    start = 512
    if len(data) < 640 or data[start:start + 4] != b"FNTF":
        raise FontError("Expected the 512-byte name wrapper and FNTF header")
    name = data[:256].split(b"\0", 1)[0].decode("ascii")
    texture_name = data[256:512].split(b"\0", 1)[0].decode("ascii").upper()
    if not name or texture_name != name.upper():
        raise FontError("Font and texture name wrappers disagree")
    version, count = struct.unpack_from("<HH", data, start + 8)
    glyph_offset, kerning_offset, end_offset = struct.unpack_from("<III", data, start + 20)
    descriptor_offset = struct.unpack_from("<I", data, start + 48)[0]
    baseline, line_gap = data[start + 18], data[start + 19]
    line_height = struct.unpack_from("<I", data, start + 36)[0]
    inverse_width, inverse_height = struct.unpack_from("<ff", data, start + 64)
    if version != 414 or not 1 <= count <= 4096:
        raise FontError("Unsupported FNTF version or glyph count")
    if glyph_offset != 128 or kerning_offset != glyph_offset + 16 * count:
        raise FontError("Glyph table extent/count disagree")
    if not kerning_offset + 4 <= descriptor_offset or end_offset != descriptor_offset + 32 or len(data) != start + end_offset:
        raise FontError("FNTF table offsets/end do not match supplied bytes")
    width, height = struct.unpack_from("<II", data, start + descriptor_offset + 24)
    validate_dimensions(width, height)
    if (width, height) != (atlas.width, atlas.height):
        raise FontError("Font descriptor and recovered texture dimensions disagree")
    if not math.isclose(inverse_width, 1 / width, rel_tol=1e-7) or not math.isclose(inverse_height, 1 / height, rel_tol=1e-7):
        raise FontError("FNTF reciprocal dimensions disagree with recovered texture")
    if not 1 <= baseline <= 255 or line_height != baseline + line_gap:
        raise FontError("FNTF baseline/line gap disagree with stored line height")
    font = {"name": texture_name, "textureResourcePath": texture_resource,
            "atlasWidth": width, "atlasHeight": height, "lineHeight": line_height,
            "baseline": baseline, "lineGap": line_gap,
            "coordinateSystem": "atlas top-left, x right, y down; metrics in native atlas pixels",
            "characterEncoding": "original 16-bit character keys; ASCII verified; non-ASCII code-page conversion not performed",
            "glyphs": [], "kerning": []}
    evidence = {"fontName": name, "fontPayloadSha256": sha256(data), "fntfVersion": version,
                "fntfOffset": start, "glyphCount": count, "glyphOffset": glyph_offset,
                "kerningOffset": kerning_offset, "descriptorOffset": descriptor_offset,
                "endOffset": end_offset, "headerHex": data[start:start + 128].hex(),
                "glyphRecords": [], "validation": []}
    previous = -1
    for index in range(count):
        offset = start + glyph_offset + 16 * index
        key, glyph_width, glyph_height, x, y, page, left, top, pair_count, pair_index, advance = struct.unpack_from("<HBBHHBbBBHH", data, offset)
        if key <= previous or 0xd800 <= key <= 0xdfff or page != 0:
            raise FontError("Unordered/duplicate character keys, surrogate key or unsupported atlas page")
        previous = key
        if x + glyph_width > width or y + glyph_height > height:
            raise FontError(f"Character key {key} has an out-of-atlas rectangle")
        if advance > width or top > line_height or top + glyph_height > line_height + 2:
            raise FontError(f"Character key {key} has unsupported advance/top metrics")
        glyph = {"codepoint": key, "x": x, "y": y, "width": glyph_width, "height": glyph_height,
                 "advance": advance, "bearingX": left, "bearingY": baseline - top, "offsetY": top}
        font["glyphs"].append(glyph)
        evidence["glyphRecords"].append({"characterKey": key, "sourceOffset": offset,
                                         "bytesHex": data[offset:offset + 16].hex(),
                                         "page": page, "pairCount": pair_count, "pairIndex": pair_index})
    pair_total = struct.unpack_from("<I", data, start + kerning_offset)[0]
    pair_start = start + kerning_offset + 4
    if pair_total > 65535 or pair_start + pair_total * 4 > start + descriptor_offset:
        raise FontError("Kerning pair table exceeds its declared extent")
    used_pairs: set[int] = set()
    for glyph_record in evidence["glyphRecords"]:
        left = glyph_record["characterKey"]
        begin, count = glyph_record["pairIndex"], glyph_record["pairCount"]
        if begin + count > pair_total:
            raise FontError("Glyph kerning range exceeds pair table")
        for index in range(begin, begin + count):
            right, delta, left_check = struct.unpack_from("<HbB", data, pair_start + index * 4)
            if index in used_pairs or left_check != left & 255:
                raise FontError("Kerning ownership/left-character check does not match glyph table")
            used_pairs.add(index)
            font["kerning"].append({"leftCodepoint": left, "rightCodepoint": right, "advanceAdjustment": delta})
    if len(used_pairs) != pair_total:
        raise FontError("Kerning pair table contains unclaimed records")
    evidence.update(kerningPairCount=pair_total,
                    kerningBytesHex=data[start + kerning_offset:start + descriptor_offset].hex(),
                    descriptorBytesHex=data[start + descriptor_offset:start + end_offset].hex())
    evidence["validation"] = ["Glyph count and 16-byte table end agree", "Descriptor end equals complete font length",
                              "Atlas size and reciprocal dimensions match texture", "Every glyph rectangle is in bounds",
                              "Character keys are strictly ordered", "Kerning ranges claim each record exactly once",
                              "Every kerning record left key matches its owning glyph"]
    return font, evidence


def render_sample(font: dict, atlas: Image, lines: list[str]) -> Image:
    glyphs = {g["codepoint"]: g for g in font["glyphs"]}
    kerning = {(p["leftCodepoint"], p["rightCodepoint"]): p["advanceAdjustment"] for p in font["kerning"]}
    width = max(sum(glyphs[ord(c)]["advance"] for c in line) for line in lines) + 48
    height = font["lineHeight"] * len(lines) + 32
    pixels = bytearray(width * height * 4)
    for row, line in enumerate(lines):
        cursor, prior = 16, None
        for character in line:
            key = ord(character)
            glyph = glyphs[key]
            if prior is not None:
                cursor += kerning.get((prior, key), 0)
            left = cursor + glyph["bearingX"]
            top = 16 + row * font["lineHeight"] + glyph["offsetY"]
            for y in range(glyph["height"]):
                for x in range(glyph["width"]):
                    source = ((glyph["y"] + y) * atlas.width + glyph["x"] + x) * 4
                    target_x, target_y = left + x, top + y
                    if not 0 <= target_x < width or not 0 <= target_y < height:
                        raise FontError("Sample glyph lies outside the sample canvas")
                    target = (target_y * width + target_x) * 4
                    # Glyphs can overlap by kerning. Straight-alpha source-over.
                    alpha, old_alpha = atlas.rgba[source + 3], pixels[target + 3]
                    combined = alpha * 255 + old_alpha * (255 - alpha)
                    if combined:
                        for channel in range(3):
                            numerator = atlas.rgba[source + channel] * alpha * 255 + pixels[target + channel] * old_alpha * (255 - alpha)
                            pixels[target + channel] = (numerator + combined // 2) // combined
                        pixels[target + 3] = (combined + 127) // 255
            cursor += glyph["advance"]
            prior = key
    return checkerboard(Image(width, height, bytes(pixels)))


def main() -> None:
    manifest = json.loads(guarded(ASSETS / "Catalog.json", ASSETS).read_text())
    report_path = guarded(PROJECT / manifest["evidence"], TOOLS)
    report = json.loads(report_path.read_text())
    catalog = {"schemaVersion": 1, "generator": TOOL, "fonts": []}
    analysis = {"schemaVersion": 1, "generator": TOOL, "readerSha256": sha256(guarded(TOOLS / "font_metrics.py", TOOLS).read_bytes()),
                "captureEvidence": manifest["evidence"], "fonts": []}
    for source in report["frontendRecords"]:
        if source["kind"] != "bitmap-font-record" or source["status"] != "opaque-font-metrics":
            continue
        path = guarded(report_path.parent / "OpaqueFonts" / Path(source["evidenceFile"]).name, TOOLS)
        data = path.read_bytes()
        if sha256(data) != source["recoveredSha256"]:
            raise FontError("Font evidence bytes differ from capture hash")
        texture_name = data[256:512].split(b"\0", 1)[0].decode("ascii").upper()
        matches = {t["resourcePath"]: t for t in manifest["textures"] if t.get("name") == texture_name}
        if len(matches) != 1:
            raise FontError("Expected a unique decoded atlas for " + texture_name)
        texture = next(iter(matches.values()))
        encoded = guarded(PROJECT / texture["assetPath"], ASSETS).read_bytes()
        if sha256(encoded) != texture["pngSha256"]:
            raise FontError("Font atlas hash differs from capture")
        atlas = read_generated_png(encoded)
        font, evidence = parse_font(data, atlas, texture["resourcePath"])
        catalog["fonts"].append(font)
        evidence.update(sourceArchive=source["source"], sourceChunkOffset=source["chunkOffset"],
                        sourceEvidenceFile=path.relative_to(PROJECT).as_posix(), atlasPngSha256=texture["pngSha256"],
                        textureResourcePath=texture["resourcePath"])
        analysis["fonts"].append(evidence)
        sample = ["0123456789", "98765-43210"] if texture_name.endswith("NUMBERS") else ["Safe House", "Options", "Milestones", "BLACKLIST 15"]
        preview_path = report_path.parent / "Previews" / (texture_name + ".sample.png")
        immutable_write(preview_path, png_bytes(render_sample(font, atlas, sample)), TOOLS)
        print("FONT", texture_name, "glyphs", len(font["glyphs"]), "kerning", len(font["kerning"]),
              "height/baseline", font["lineHeight"], font["baseline"], "preview", preview_path.relative_to(PROJECT))
    if len(catalog["fonts"]) != 3:
        raise FontError("Expected all three explicitly captured frontend bitmap fonts")
    raw_evidence = json_bytes(analysis)
    evidence_name = "font-metrics-" + sha256(raw_evidence)[:16] + ".json"
    immutable_write(report_path.parent / evidence_name, raw_evidence, TOOLS)
    catalog["evidence"] = (report_path.parent / evidence_name).relative_to(PROJECT).as_posix()
    publish_owned_manifest("Fonts/FontMetrics.json", json_bytes(catalog))
    guid = "ed56230fbe6be5f9aa38d60a418442b9"
    meta = (f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n"
            f"  userData: {TOOL}\n  assetBundleName: \n  assetBundleVariant: \n").encode()
    preserve_or_create_meta(ASSETS / "Fonts/FontMetrics.json.meta", meta)
    print("FONT_MANIFEST MostWantedUI/Fonts/FontMetrics", (report_path.parent / evidence_name).relative_to(PROJECT))


if __name__ == "__main__":
    main()
