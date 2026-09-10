"""Move recovered customization assets into the canonical Unity layout.

The decoding folders under ``Art/Cars`` remain the immutable source cache.
This migration only moves the Unity-facing GLBs, catalogs, prefabs, and the
decoded vinyl images into ``Assets/NfsMw/Content/Customization``. File sidecars
are moved with their assets so Unity GUIDs survive the layout change.

Run once with ``--apply`` from the project root. A second run is harmless and
only verifies that the legacy roots are already gone.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CUSTOMIZATION = ROOT / "Assets/NfsMw/Content/Customization"
SOURCES = CUSTOMIZATION / "Sources"
PARTS = CUSTOMIZATION / "Parts"
VINYLS = CUSTOMIZATION / "Vinyls"
SOURCE_VINYL_ROOT = ROOT / "Art/Cars/Modifications/Vinyls"
LEGACY_CAR_ROOT = ROOT / "Assets" / "CarRemaster"
OLD_MODIFICATIONS = LEGACY_CAR_ROOT / "Modifications"
OLD_VINYLS = LEGACY_CAR_ROOT / "Vinyls"

CATEGORY_FOLDERS = {
    "Brake": "Brake",
    "Body kit": "BodyKit",
    "Damage state": "DamageState",
    "Decal mesh": "DecalMesh",
    "Glass": "Glass",
    "Hood": "Hood",
    "Interior": "Interior",
    "Light": "Light",
    "Mirror": "Mirror",
    "Other source part": "OtherSourcePart",
    "Plate": "Plate",
    "Rim": "Rim",
    "Roof scoop": "RoofScoop",
    "Spoiler": "Spoiler",
    "Tyre": "Tyre",
    "Wheel and tyre": "WheelAndTyre",
}


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def sidecar(path: Path) -> Path:
    return Path(str(path) + ".meta")


def move_file(source: Path, destination: Path) -> None:
    """Move a file and its Unity sidecar, tolerating an already-migrated file."""
    if source == destination:
        return
    if not source.exists():
        if destination.exists():
            return
        raise FileNotFoundError(source)
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        if digest(source) != digest(destination):
            raise RuntimeError(f"refusing to overwrite different file: {destination}")
        source.unlink()
    else:
        shutil.move(str(source), str(destination))
    old_meta, new_meta = sidecar(source), sidecar(destination)
    if old_meta.exists():
        if new_meta.exists():
            if old_meta.read_bytes() != new_meta.read_bytes():
                raise RuntimeError(f"refusing to overwrite different sidecar: {new_meta}")
            old_meta.unlink()
        else:
            new_meta.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(old_meta), str(new_meta))


def move_sidecar(source: Path, destination: Path) -> None:
    """Move a directory sidecar without moving the directory itself."""
    old_meta, new_meta = sidecar(source), sidecar(destination)
    if not old_meta.exists():
        return
    new_meta.parent.mkdir(parents=True, exist_ok=True)
    if new_meta.exists():
        if old_meta.read_bytes() != new_meta.read_bytes():
            raise RuntimeError(f"refusing to overwrite different sidecar: {new_meta}")
        old_meta.unlink()
    else:
        shutil.move(str(old_meta), str(new_meta))


def remove_empty(path: Path) -> None:
    if not path.exists() or not path.is_dir():
        return
    for child in sorted(path.iterdir(), reverse=True):
        if child.is_dir():
            remove_empty(child)
        elif child.name.endswith(".meta") and not child.with_name(child.name[:-5]).exists():
            # A sidecar whose asset or folder was removed is disposable.
            child.unlink()
    if not any(path.iterdir()):
        path.rmdir()


def canonical_vinyl_path(archive: dict, texture: dict) -> str:
    source_kind = "PreVinyl" if archive["source"].upper().endswith("/PREVINYL.BIN") else None
    if source_kind:
        folder = source_kind
    else:
        folder = "Masks" if texture.get("role") == "mask" else "Artwork"
    filename = Path(texture.get("sourceAsset", texture["asset"])).name
    return f"Assets/NfsMw/Content/Customization/Vinyls/{folder}/{Path(archive['source']).parent.name}/{filename}"


def migrate_modifications() -> tuple[int, int]:
    catalogs = sorted(OLD_MODIFICATIONS.glob("*/catalog.json"))
    if not catalogs:
        return 0, 0
    moved_parts = 0
    moved_archives = 0
    CUSTOMIZATION.mkdir(parents=True, exist_ok=True)
    # Keep the old root GUID attached to the new content root.
    move_sidecar(OLD_MODIFICATIONS, CUSTOMIZATION)
    for catalog_path in catalogs:
        catalog = json.loads(catalog_path.read_text())
        archive = catalog["archive"]
        source_archive = OLD_MODIFICATIONS / archive
        source_asset = ROOT / catalog["asset"]
        source_glb = SOURCES / archive / "Geometry" / "Parts.glb"
        source_catalog = SOURCES / archive / "Metadata" / "catalog.json"
        move_file(source_asset, source_glb)
        for entry in catalog.get("entries", []):
            category = entry.get("category")
            if category not in CATEGORY_FOLDERS:
                raise ValueError(f"unknown modification category {category!r} in {archive}")
            destination = PARTS / CATEGORY_FOLDERS[category] / archive / f"{entry['id']}.prefab"
            move_file(source_archive / "Prefabs" / f"{entry['id']}.prefab", destination)
            entry["prefab"] = str(destination.relative_to(ROOT))
            moved_parts += 1
        catalog["layout"] = "content-first-v2"
        catalog["asset"] = str(source_glb.relative_to(ROOT))
        catalog["sourceCatalog"] = str(source_catalog.relative_to(ROOT))
        source_catalog.parent.mkdir(parents=True, exist_ok=True)
        source_catalog.write_text(json.dumps(catalog, indent=2) + "\n")
        move_sidecar(catalog_path, source_catalog)
        if catalog_path.exists():
            catalog_path.unlink()
        # Archive folder GUIDs remain meaningful on the source archive folders.
        move_sidecar(source_archive, SOURCES / archive)
        moved_archives += 1
    for old in sorted(OLD_MODIFICATIONS.glob("*/Prefabs")):
        remove_empty(old)
    for old in sorted(OLD_MODIFICATIONS.glob("*")):
        if old.is_dir():
            remove_empty(old)
    remove_empty(OLD_MODIFICATIONS)
    return moved_archives, moved_parts


def migrate_vinyls() -> int:
    catalog_path = OLD_VINYLS / "catalog.json"
    if not catalog_path.exists():
        return 0
    catalog = json.loads(catalog_path.read_text())
    moved = 0
    imported = OLD_VINYLS / "Imported"
    for archive in catalog.get("archives", []):
        archive_name = Path(archive["source"]).parent.name
        for texture in archive.get("textures", []):
            source_asset = texture.setdefault("sourceAsset", texture["asset"])
            source = ROOT / source_asset
            destination = ROOT / canonical_vinyl_path(archive, texture)
            if not source.is_file():
                raise FileNotFoundError(source)
            destination.parent.mkdir(parents=True, exist_ok=True)
            # Existing sample imports may have importer settings worth keeping.
            old_sample = next(imported.rglob(destination.name), None) if imported.exists() else None
            shutil.copy2(source, destination)
            if old_sample is not None and sidecar(old_sample).exists() and not sidecar(destination).exists():
                shutil.copy2(sidecar(old_sample), sidecar(destination))
            texture["asset"] = str(destination.relative_to(ROOT))
            texture["sourceAsset"] = source_asset
            texture["sourceKind"] = "PREVINYL" if archive["source"].upper().endswith("/PREVINYL.BIN") else "VINYLS"
            moved += 1
    VINYLS.mkdir(parents=True, exist_ok=True)
    catalog["layout"] = "content-first-v2"
    (VINYLS / "catalog.json").write_text(json.dumps(catalog, indent=2) + "\n")
    move_sidecar(catalog_path, VINYLS / "catalog.json")
    if catalog_path.exists():
        catalog_path.unlink()
    # Preserve the existing Vinyls folder GUID on the new canonical folder.
    move_sidecar(OLD_VINYLS, VINYLS)
    if imported.exists():
        shutil.rmtree(imported)
    remove_empty(OLD_VINYLS)
    return moved


def verify() -> dict:
    catalogs = sorted(SOURCES.glob("*/Metadata/catalog.json"))
    entries = []
    for path in catalogs:
        catalog = json.loads(path.read_text())
        assert catalog.get("layout") == "content-first-v2", path
        assert (ROOT / catalog["asset"]).is_file(), catalog["asset"]
        for entry in catalog.get("entries", []):
            target = ROOT / entry["prefab"]
            assert target.is_file(), target
            assert target.parent.parent.name == CATEGORY_FOLDERS[entry["category"]], target
            entries.append(entry)
    vinyl_catalog = VINYLS / "catalog.json"
    vinyls = json.loads(vinyl_catalog.read_text()) if vinyl_catalog.exists() else {"archives": []}
    textures = [t for a in vinyls.get("archives", []) for t in a.get("textures", [])]
    for texture in textures:
        assert (ROOT / texture["asset"]).is_file(), texture["asset"]
        assert (ROOT / texture["sourceAsset"]).is_file(), texture["sourceAsset"]
    assert not OLD_MODIFICATIONS.exists(), OLD_MODIFICATIONS
    assert not OLD_VINYLS.exists(), OLD_VINYLS
    return {"archives": len(catalogs), "parts": len(entries), "vinyls": len(textures)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="perform the migration")
    args = parser.parse_args()
    if args.apply:
        archives, parts = migrate_modifications()
        vinyls = migrate_vinyls()
        print(f"migrated {archives} source archives, {parts} prefabs, {vinyls} vinyl textures")
    print(json.dumps(verify(), sort_keys=True))


if __name__ == "__main__":
    main()
