"""Decode individual original UI images from explicit in-project archive copies.

Default operation is read-only. --export creates immutable PNGs at curated paths,
evidence, and a Unity Resources import manifest. No original installation lookup,
network request, executable/plugin loading, Unity invocation or source write occurs.
"""

from __future__ import annotations

import argparse
import collections
import json
import os
import re
import struct
import sys
from pathlib import Path

from archive_decode import ArchiveError, MAX_ARCHIVE_BYTES, read_chunks, sha256, unwrap
from texture_archive import TextureRecord, cstring, decode_texture, iter_textures, u32
from texture_decode import DecodeError, png_bytes
from image_library import locate_texture

TOOL = "nfsmw-frontend-decoding/v1"
TOOLS = Path(__file__).resolve().parent
PROJECT = TOOLS.parents[1]
SOURCE = TOOLS / "Source"
RESOURCE_KEY = "MostWantedUI"
ASSETS = PROJECT / "Assets/NfsMw/Content/Frontend/UI/Resources" / RESOURCE_KEY
SOURCES = (
    "FRONTEND/FrontB.lzc", "GLOBAL/GLOBALA.BUN", "GLOBAL/GLOBALB.BUN",
    "GLOBAL/RIVALS.BIN", "GLOBAL/DYNTEX.BIN", "GLOBAL/FE_ATTRIB.bin",
)
MAX_TOTAL_PIXELS = 192 * 1024 * 1024
REFERENCE_ROOT = PROJECT / "Library/DrivingMechanicsResearch/nfsmw"
REFERENCES = (
    "src/Speed/Indep/bWare/Inc/bChunk.hpp",
    "src/Speed/Indep/Src/Misc/SpeedChunks.hpp",
    "src/Speed/Indep/Src/Misc/LZCompress.hpp",
    "src/Speed/Indep/Src/Misc/LZCompress.cpp",
    "src/Speed/Indep/Src/Ecstasy/Texture.hpp",
    "src/Speed/Indep/Src/Ecstasy/Texture.cpp",
    "src/Speed/Indep/Src/Ecstasy/eStreamingPack.hpp",
    "src/Speed/PC/Src/Ecstasy/TextureInfoPlat.hpp",
)


def guarded(path: Path, boundary: Path) -> Path:
    """Reject traversal and symlinks, including symlinked ancestor directories."""
    path, boundary = Path(os.path.abspath(path)), Path(os.path.abspath(boundary))
    if not path.is_relative_to(boundary):
        raise ValueError(f"Path is outside its owned boundary: {path}")
    # Check ancestors all the way to the project, not just the output directory.
    project = Path(os.path.abspath(PROJECT))
    if not path.is_relative_to(project):
        raise ValueError("Path is outside the approved project")
    current = project
    if current.is_symlink():
        raise ValueError("Project path must not be a symbolic link")
    for part in path.relative_to(project).parts:
        current /= part
        if current.is_symlink():
            raise ValueError(f"Symbolic-link path is not permitted: {current}")
    return path


def read_source(path: Path) -> bytes:
    path = guarded(path, SOURCE)
    if not path.is_file() or path.stat().st_size > MAX_ARCHIVE_BYTES:
        raise ValueError("Source is missing, not an ordinary file, or exceeds the size limit")
    with path.open("rb") as stream:
        data = stream.read(MAX_ARCHIVE_BYTES + 1)
    if len(data) > MAX_ARCHIVE_BYTES:
        raise ValueError("Source grew beyond the size limit")
    return data


def immutable_write(path: Path, data: bytes, boundary: Path) -> str:
    """Idempotent for identical bytes. Never overwrite an authored/differing file."""
    path = guarded(path, boundary)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if not path.is_file() or path.stat().st_size != len(data) or path.read_bytes() != data:
            raise FileExistsError(f"Refusing to overwrite differing output: {path}")
        return "reused"
    with path.open("xb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    return "created"


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=True, allow_nan=False) + "\n").encode("utf-8")


def safe_name(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9_-]", "_", value)[:96] or "unnamed"


def texture_meta(resource_path: str, guid: str | None = None) -> bytes:
    guid = guid or sha256((TOOL + ":" + resource_path).encode())[:32]
    # Default Texture2D, preserving RGBA; suitable for UI Toolkit backgroundImage.
    # The runtime owns blend-mode handling. No source alpha is artistically edited.
    return (f"fileFormatVersion: 2\nguid: {guid}\nTextureImporter:\n"
            "  serializedVersion: 13\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 0\n"
            "    sRGBTexture: 1\n  isReadable: 0\n  streamingMipmaps: 0\n"
            "  textureFormat: 1\n  maxTextureSize: 4096\n  textureSettings:\n"
            "    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n"
            "    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n  nPOTScale: 0\n"
            "  lightmap: 0\n  compressionQuality: 100\n  spriteMode: 0\n"
            "  alphaUsage: 1\n  alphaIsTransparency: 0\n  textureType: 0\n"
            "  textureShape: 1\n  singleChannelComponent: 0\n"
            "  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n"
            "    maxTextureSize: 4096\n    resizeAlgorithm: 0\n    textureFormat: -1\n"
            "    textureCompression: 0\n    compressionQuality: 100\n    crunchedCompression: 0\n"
            "    allowsAlphaSplitting: 0\n    overridden: 0\n"
            f"  userData: {TOOL}\n  assetBundleName: \n  assetBundleVariant: \n").encode()


def manifest_meta() -> bytes:
    # Serialized Unity identity stays fixed across tool and folder renames.
    guid = "888d4f44cad2a71e57bb33431beecb02"
    return (f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n"
            f"  externalObjects: {{}}\n  userData: {TOOL}\n  assetBundleName: \n  assetBundleVariant: \n").encode()


def preserve_or_create_meta(path: Path, expected: bytes, boundary: Path | None = None) -> None:
    """Preserve Unity/user importer serialization when its stable GUID matches."""
    boundary = boundary or ASSETS
    path = guarded(path, boundary)
    if path.exists():
        actual = path.read_bytes()
        guid = re.search(rb"(?m)^guid: ([a-f0-9]{32})$", expected)
        existing = re.search(rb"(?m)^guid: ([a-f0-9]{32})$", actual)
        if not guid or not existing or existing.group(1) != guid.group(1):
            raise FileExistsError(f"Refusing to replace an existing importer GUID: {path}")
        return
    immutable_write(path, expected, boundary)


def inspect_fe(data: bytes, chunks: list, source_name: str, evidence: Path, export: bool) -> list[dict]:
    records: list[dict] = []
    for c in chunks:
        if c.kind not in (0x30201, 0x30203, 0x30210):
            continue
        payload = data[c.data_offset:c.end]
        record = {"source": source_name, "chunkId": f"{c.kind:08x}", "chunkOffset": c.offset,
                  "payloadSize": c.size, "payloadSha256": sha256(payload),
                  "coordinateSpace": "uncompressed-archive", "layoutDecoded": False}
        try:
            if c.kind == 0x30201:
                record.update(kind="bitmap-font-record", name=cstring(payload[:64]),
                              status="opaque-font-metrics", fontConversionPerformed=False)
                recovered = payload
                suffix = "font.bin"
            else:
                record["kind"] = "frontend-package"
                if c.kind == 0x30210:
                    if len(payload) < 20:
                        raise ArchiveError("Compressed FE package header is truncated")
                    record["packageNameHash"] = f"{u32(payload, 0):08x}"
                    length = u32(payload, 16)
                    if length < 16 or 4 + length > len(payload):
                        raise ArchiveError("Compressed FE package extent exceeds its chunk")
                    padding = payload[4 + length:]
                    if len(padding) > 3 or any(padding):
                        raise ArchiveError("Unexpected FE chunk alignment padding")
                    recovered, codec = unwrap(payload[4:4 + length])
                    record.update(compression=codec, outerPaddingHex=padding.hex())
                else:
                    recovered = payload
                    record["compression"] = {"codec": "none"}
                if len(recovered) < 44 or recovered[:4] != b"FE\x6e\xe7" or u32(recovered, 4) != len(recovered) - 8:
                    raise ArchiveError("Decoded FE package does not match its envelope signature/length")
                candidate = cstring(recovered[40:296])
                record["nameCandidate"] = candidate if re.fullmatch(r"[A-Za-z0-9_ .-]+\.fng", candidate, re.IGNORECASE) else None
                record["nameEvidence"] = "ASCII candidate at package +40; object layout and animation semantics not interpreted"
                record["status"] = "decompressed-package-not-interpreted"
                suffix = "fng"
            record["recoveredSha256"] = sha256(recovered)
            record["recoveredSize"] = len(recovered)
            label = record.get("name") or record.get("nameCandidate") or f"chunk_{c.offset:08x}"
            name = f"{safe_name(label)}_{record['recoveredSha256'][:12]}.{suffix}"
            relative = Path("OpaqueFonts" if c.kind == 0x30201 else "Packages") / name
            record["evidenceFile"] = (evidence / relative).relative_to(PROJECT).as_posix()
            if export:
                immutable_write(evidence / relative, recovered, TOOLS)
        except ArchiveError as error:
            record.update(status="unsupported", reason=str(error))
        records.append(record)
    return records


def capture(export: bool, capture_id: str) -> dict:
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_-]{0,63}", capture_id):
        raise ValueError("Capture ID must be a simple alphanumeric identifier")
    evidence = guarded(TOOLS / "Evidence" / capture_id, TOOLS)
    report = {
        "schemaVersion": 1, "generator": TOOL, "captureId": capture_id,
        "sources": [], "textures": [], "frontendRecords": [], "unsupported": [],
        "formatReferences": [], "toolSources": [],
        "boundaries": [
            "Only explicit ordinary copies under Tools/FrontendAssets/Source were read.",
            "Original installation hashes/access were not independently verified by this worker.",
            "Source identity proves local bytes, not edition, language, patch, or absence of mods.",
            "Top mip only; source row order, colour and alpha preserved. No whole-screen screenshot backgrounds.",
            "Original bitmap font records are evidence, not TTF/OTF or a Unity text/font implementation.",
            "Decompressed FE packages retain bytes; screen object layout and animation timing are not decoded.",
            "Game asset redistribution rights are not inferred from possession or successful decoding.",
        ],
    }
    pixels = 0
    for filename in SOURCES:
        path = SOURCE / filename
        if not path.exists():
            report["unsupported"].append({"source": filename, "reason": "Explicit source file is missing"})
            continue
        raw = read_source(path)
        source = {"name": filename, "stagedPath": path.relative_to(PROJECT).as_posix(),
                  "size": len(raw), "sha256": sha256(raw), "stagedHashUnchanged": False,
                  "origin": "Local installation copy staged by prime; not independently authenticated as an unmodified retail build"}
        report["sources"].append(source)
        try:
            if raw[:4] == b"VPAK":
                raise ArchiveError("VPAK attribute database: retained as source evidence, not a texture or FE layout archive")
            data, source["compression"] = unwrap(raw)
            chunks = read_chunks(data)
            source["chunks"] = [{"id": f"{c.kind:08x}", "offset": c.offset, "size": c.size,
                                 "depth": c.depth, "parentOffset": c.parent,
                                 "payloadSha256": sha256(memoryview(data)[c.data_offset:c.end])} for c in chunks]
            source["chunkCount"] = len(chunks)
            for record in iter_textures(data, chunks):
                metadata = record.metadata
                metadata.update(source=filename, sourceSha256=source["sha256"],
                                uncompressedArchiveSha256=source["compression"]["uncompressedSha256"])
                try:
                    count = metadata.get("width", 0) * metadata.get("height", 0)
                    if pixels + count > MAX_TOTAL_PIXELS:
                        raise DecodeError("Aggregate decoded pixel budget exceeded")
                    image = decode_texture(record)
                    pixels += count
                    encoded = png_bytes(image)
                    metadata.update(status="decoded", pngSha256=sha256(encoded), rgbaSha256=sha256(image.rgba),
                                    pngSize=len(encoded), exportedMip=0)
                    location = locate_texture(metadata["nameHash"], metadata["pngSha256"])
                    metadata.update(resourcePath=location["resourcePath"], assetPath=location["assetPath"])
                    if export:
                        path = PROJECT / location["assetPath"]
                        boundary = PROJECT / "Assets/NfsMw/Content"
                        immutable_write(path, encoded, boundary)
                        preserve_or_create_meta(path.with_suffix(".png.meta"),
                                                texture_meta(location["assetPath"], location["guid"]), boundary)
                except (ArchiveError, DecodeError) as error:
                    metadata.update(status="unsupported", reason=str(error))
                report["textures"].append(metadata)
            report["frontendRecords"].extend(inspect_fe(data, chunks, filename, evidence, export))
        except ArchiveError as error:
            report["unsupported"].append({"source": filename, "reason": str(error)})
        source["sha256After"] = sha256(read_source(path))
        source["stagedHashUnchanged"] = source["sha256After"] == source["sha256"]
        if not source["stagedHashUnchanged"]:
            raise RuntimeError(f"Source changed during decoding: {filename}; manifest will not be published")
        local = [t for t in report["textures"] if t["source"] == filename]
        print(filename, "decoded", sum(t["status"] == "decoded" for t in local),
              "unsupported", sum(t["status"] != "decoded" for t in local), flush=True)
    for name in REFERENCES:
        path = guarded(REFERENCE_ROOT / name, PROJECT)
        if path.is_file():
            report["formatReferences"].append({"path": path.relative_to(PROJECT).as_posix(),
                                               "sha256": sha256(path.read_bytes()),
                                               "revision": "13189413c4c6e447c2225052c66b55d76863b985",
                                               "executed": False})
    for name in ("archive_decode.py", "texture_decode.py", "texture_archive.py", "decode_frontend.py"):
        path = guarded(TOOLS / name, TOOLS)
        report["toolSources"].append({"path": path.relative_to(PROJECT).as_posix(), "sha256": sha256(path.read_bytes())})
    decoded = [t for t in report["textures"] if t["status"] == "decoded"]
    names = collections.Counter(t["name"] for t in decoded)
    report["duplicateNames"] = {name: count for name, count in sorted(names.items()) if count > 1}
    report["summary"] = {"sources": len(report["sources"]), "decodedTextures": len(decoded),
                         "uniqueTextureAssets": len({t["assetPath"] for t in decoded}),
                         "unsupportedTextures": len(report["textures"]) - len(decoded),
                         "frontendRecords": len(report["frontendRecords"]), "decodedPixels": pixels,
                         "unsupportedFrontendRecords": sum(r["status"] == "unsupported" for r in report["frontendRecords"]),
                         "sourceHashesUnchanged": all(s["stagedHashUnchanged"] for s in report["sources"])}
    if export:
        # Hash-addressed evidence is immutable. Publish the small runtime manifest LAST.
        report_bytes = json_bytes(report)
        report_name = f"report-{sha256(report_bytes)[:16]}.json"
        immutable_write(evidence / report_name, report_bytes, TOOLS)
        runtime_fields = ("name", "nameHash", "resourcePath", "assetPath", "width", "height", "pngSha256", "alphaUsage", "alphaBlend", "source", "packFilename")
        unique_textures = {}
        for texture in decoded:
            if texture["resourcePath"]:
                unique_textures.setdefault(texture["resourcePath"], texture)
        manifest = {"schemaVersion": 1, "generator": TOOL,
                    "evidence": (evidence / report_name).relative_to(PROJECT).as_posix(),
                    "sourceHashesUnchanged": report["summary"]["sourceHashesUnchanged"],
                    "textures": [{key: t[key] for key in runtime_fields} for t in unique_textures.values()]}
        publish_manifest(json_bytes(manifest))
        preserve_or_create_meta(ASSETS / "Catalog.json.meta", manifest_meta())
        print("EVIDENCE", (evidence / report_name).relative_to(PROJECT), flush=True)
    print("SUMMARY", json.dumps(report["summary"], sort_keys=True), flush=True)
    print("UNSUPPORTED_REASONS", dict(collections.Counter(t.get("reason") for t in report["textures"] if t["status"] != "decoded")))
    print("USEFUL_NAMES", [t["name"] for t in decoded if re.search(r"FONT|GRUNGE|SPLAT|MAP|ICON|LOGO|BLACK|BORDER|CROWN", t["name"], re.IGNORECASE)])
    return report


def publish_manifest(payload: bytes) -> None:
    """Atomically replace only this tool's own manifest, preserving its old bytes."""
    manifest = json.loads(payload)
    path = ASSETS / "Catalog.json"
    if path.exists():
        previous = json.loads(guarded(path, ASSETS).read_text())
        textures = {t["assetPath"]: t for t in previous.get("textures", [])}
        textures.update({t["assetPath"]: t for t in manifest.get("textures", [])})
        manifest["textures"] = list(textures.values())
    publish_owned_manifest("Catalog.json", json_bytes(manifest))


def publish_owned_manifest(filename: str, payload: bytes) -> None:
    if filename not in ("Catalog.json", "Fonts/FontMetrics.json"):
        raise ValueError("Only the two explicit tool-owned resource manifests are replaceable")
    path = guarded(ASSETS / filename, ASSETS)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        old = path.read_bytes()
        if old == payload:
            return
        previous = json.loads(old)
        if not isinstance(previous, dict) or previous.get("generator") != TOOL or previous.get("schemaVersion") != 1:
            raise FileExistsError("Refusing to replace an unowned runtime manifest")
        immutable_write(TOOLS / "Evidence/PreviousManifests" / (Path(filename).name + "-" + sha256(old) + ".json"), old, TOOLS)
    temporary = guarded(ASSETS / (".manifest-" + sha256(payload)[:16] + ".tmp"), ASSETS)
    immutable_write(temporary, payload, ASSETS)
    os.replace(temporary, path)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--export", action="store_true", help="Write decoded assets/evidence; without this flag all operations are read-only")
    parser.add_argument("--capture-id", default="local-20260908", help="Safe subdirectory name under Tools/FrontendAssets/Evidence")
    args = parser.parse_args()
    try:
        capture(args.export, args.capture_id)
    except (ArchiveError, DecodeError, OSError, ValueError, RuntimeError) as error:
        print(f"DECODING_FAILED: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
