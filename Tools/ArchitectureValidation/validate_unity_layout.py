#!/usr/bin/env python3
"""Validate the canonical Unity content and module layout.

This is intentionally filesystem-based: it can run before Unity imports a
large decoded asset set and catches path/catalog mistakes that a successful
script compilation would not see.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Assets"
NFS = ASSETS / "NfsMw"

REQUIRED_DIRECTORIES = (
    "Content/Audio",
    "Content/Customization/Parts",
    "Content/Customization/Sources",
    "Content/Customization/Vinyls",
    "Content/Frontend/Models",
    "Content/Vehicles/Street",
    "Content/Vehicles/Police",
    "Content/Vehicles/Traffic",
    "Content/World/Maps",
    "Content/World/Models",
    "Content/World/Navigation",
    "Modules/ContentTools/CarRemaster/Editor",
    "Modules/Driving",
    "Scenes/Game",
    "Content/Frontend/UI/Runtime",
    "Content/Frontend/UI/Editor",
    "Content/Frontend/UI/Resources",
    "Content/Frontend/UI/Tests/Scenes",
    "Scenes/Showcase",
    "Scenes/Tests",
    "Scenes/World/Streaming",
    "Settings/Input",
    "Settings/Rendering",
)

LEGACY_ROOTS = (
    "Models",
    "CarRemaster",
    "Driving",
    "Audio",
    "Maps",
    "Scenes",
    "Settings",
    "Adaptive Performance",
    "HDRPDefaultResources",
    "TutorialInfo",
)

PART_TYPES = {
    "BodyKit",
    "Brake",
    "DamageState",
    "DecalMesh",
    "Glass",
    "Hood",
    "Interior",
    "Light",
    "Mirror",
    "OtherSourcePart",
    "Plate",
    "Rim",
    "RoofScoop",
    "Spoiler",
    "Tyre",
    "WheelAndTyre",
}


def fail(errors: list[str], message: str) -> None:
    errors.append(message)


def check_catalogs(errors: list[str]) -> tuple[int, int, int]:
    sources = NFS / "Content/Customization/Sources"
    source_count = part_count = 0
    for catalog_path in sorted(sources.glob("*/Metadata/catalog.json")):
        source_count += 1
        try:
            catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            fail(errors, f"invalid catalog {catalog_path}: {exc}")
            continue
        asset = catalog.get("asset")
        if not asset or not (ROOT / asset).is_file():
            fail(errors, f"missing source asset for {catalog_path}: {asset}")
        for entry in catalog.get("entries", []):
            part_count += 1
            prefab = entry.get("prefab")
            if not prefab or not (ROOT / prefab).is_file():
                fail(errors, f"missing part prefab for {catalog_path}: {prefab}")
            if prefab and "/Parts/" in prefab:
                category = prefab.split("/Parts/", 1)[1].split("/", 1)[0]
                if category not in PART_TYPES:
                    fail(errors, f"unknown part folder {category} in {catalog_path}")

    vinyl_path = NFS / "Content/Customization/Vinyls/catalog.json"
    vinyl_count = 0
    if not vinyl_path.is_file():
        fail(errors, f"missing vinyl catalog: {vinyl_path}")
    else:
        try:
            catalog = json.loads(vinyl_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            fail(errors, f"invalid vinyl catalog {vinyl_path}: {exc}")
        else:
            for archive in catalog.get("archives", []):
                for texture in archive.get("textures", []):
                    vinyl_count += 1
                    asset = texture.get("asset")
                    if not asset or not (ROOT / asset).is_file():
                        fail(errors, f"missing vinyl asset: {asset}")
                    if asset and not asset.startswith("Assets/NfsMw/Content/Customization/Vinyls/"):
                        fail(errors, f"vinyl outside canonical root: {asset}")
    return source_count, part_count, vinyl_count


def check_vehicles(errors: list[str]) -> tuple[int, int, int]:
    root = NFS / "Content/Vehicles"
    counts: list[int] = []
    for kind in ("Street", "Police", "Traffic"):
        kind_root = root / kind
        count = 0
        for child in sorted(kind_root.iterdir() if kind_root.is_dir() else []):
            if child.name.endswith(".meta") or not child.is_dir():
                continue
            if kind == "Street":
                models = [entry for entry in child.iterdir() if entry.is_dir() and not entry.name.endswith(".meta")]
                count += len(models)
                if not models:
                    fail(errors, f"street make has no model folders: {child}")
            else:
                count += 1
        counts.append(count)
    return tuple(counts)  # type: ignore[return-value]


def check_text_references(errors: list[str]) -> int:
    legacy_tokens = tuple(f"Assets/{name}" for name in LEGACY_ROOTS)
    excluded = {".git", "Library", "Temp", "Logs", "obj", "Build", "Builds"}
    binary_suffixes = {
        ".assetbundle", ".bin", ".blend", ".dds", ".fbx", ".glb", ".gltf",
        ".jpg", ".jpeg", ".mp3", ".mp4", ".ogg", ".png", ".tif", ".tiff",
        ".wav", ".webm", ".zip",
    }
    excluded_paths = {
        Path(__file__).resolve(),
        ROOT / "Tools/ArchitectureValidation/migrate_unity_layout.py",
        ROOT / "Tools/CarRemaster/reorganize_unity_assets.py",
    }
    hits = 0
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.resolve() in excluded_paths or path.suffix.lower() in binary_suffixes:
            continue
        if any(part in excluded for part in path.relative_to(ROOT).parts):
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        for token in legacy_tokens:
            if token in text:
                hits += 1
                fail(errors, f"legacy path reference {token} in {path}")
                break
    return hits


def validate() -> dict[str, int]:
    errors: list[str] = []
    if not NFS.is_dir():
        fail(errors, f"missing canonical root: {NFS}")
    for relative in REQUIRED_DIRECTORIES:
        path = NFS / relative
        if not path.is_dir():
            fail(errors, f"missing required directory: {path}")
        elif not path.with_name(path.name + ".meta").is_file():
            fail(errors, f"missing folder sidecar: {path}.meta")
    for name in LEGACY_ROOTS:
        path = ASSETS / name
        if path.exists():
            fail(errors, f"legacy Assets root remains: {path}")

    source_count, part_count, vinyl_count = check_catalogs(errors)
    street_count, police_count, traffic_count = check_vehicles(errors)
    legacy_reference_count = check_text_references(errors)
    return {
        "streetVehicles": street_count,
        "policeVehicles": police_count,
        "trafficVehicles": traffic_count,
        "customizationSources": source_count,
        "customizationParts": part_count,
        "vinylTextures": vinyl_count,
        "legacyReferenceFiles": legacy_reference_count,
        "errors": len(errors),
    }, errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true", help="print only the summary JSON")
    args = parser.parse_args()
    summary, errors = validate()
    print(json.dumps(summary, sort_keys=True))
    if errors and not args.json:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
