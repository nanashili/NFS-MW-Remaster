#!/usr/bin/env python3
"""Migrate the Unity project to the canonical content/module layout.

The project contains a large decoded asset set, so this script deliberately
moves files in place instead of copying them. Unity ``.meta`` sidecars move
with their asset and therefore keep the existing GUIDs. It is safe to run the
script again: already-migrated paths are treated as complete and conflicting
destinations are rejected.

Run from the project root:

    python3 Tools/ArchitectureValidation/migrate_unity_layout.py --apply

The migration leaves ``Art`` source caches untouched. Unity recovery snapshots,
when present, remain outside the authored runtime layout.
"""
from __future__ import annotations

import argparse
import hashlib
import shutil
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Assets"
NFS = ASSETS / "NfsMw"


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def sidecar(path: Path) -> Path:
    return path.with_name(path.name + ".meta")


def move_sidecar(source: Path, destination: Path) -> None:
    old_meta, new_meta = sidecar(source), sidecar(destination)
    if not old_meta.exists():
        return
    new_meta.parent.mkdir(parents=True, exist_ok=True)
    if new_meta.exists():
        if old_meta.read_bytes() != new_meta.read_bytes():
            raise RuntimeError(f"refusing to overwrite different sidecar: {new_meta}")
        old_meta.unlink()
        return
    shutil.move(str(old_meta), str(new_meta))


def remove_empty_directory(path: Path) -> None:
    """Remove an empty legacy folder and its now-unused sidecar."""
    if not path.is_dir() or any(path.iterdir()):
        return
    path.rmdir()
    old_meta = sidecar(path)
    if old_meta.exists():
        old_meta.unlink()


def move_path(source: Path, destination: Path) -> None:
    """Move one asset/folder and its sibling sidecar without overwriting."""
    if not source.exists():
        if destination.exists():
            return
        raise FileNotFoundError(source)

    if destination.exists():
        if source.is_dir() and destination.is_dir() and not any(source.iterdir()):
            move_sidecar(source, destination)
            remove_empty_directory(source)
            return
        if source.is_file() and destination.is_file() and digest(source) == digest(destination):
            source.unlink()
            move_sidecar(source, destination)
            return
        raise RuntimeError(f"refusing to overwrite existing destination: {destination}")

    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(source), str(destination))
    move_sidecar(source, destination)


def move_children(source: Path, destination: Path) -> None:
    """Move all children of a folder while retaining the folder's GUID."""
    if not source.exists():
        if destination.exists():
            return
        raise FileNotFoundError(source)
    destination.mkdir(parents=True, exist_ok=True)
    for child in sorted(source.iterdir(), key=lambda item: item.name):
        move_path(child, destination / child.name)
    move_sidecar(source, destination)
    remove_empty_directory(source)


def deterministic_folder_meta(path: Path) -> None:
    """Create a stable Unity folder sidecar for newly introduced boundaries."""
    if not path.is_dir() or sidecar(path).exists():
        return
    relative = path.relative_to(ROOT).as_posix()
    guid = uuid.uuid5(uuid.NAMESPACE_URL, f"nfs-mw-layout:{relative}").hex
    sidecar(path).write_text(
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


# These replacements cover both authored code and generated catalogs/reports.
# More specific prefixes must precede their parent prefix.
PATH_REPLACEMENTS: tuple[tuple[str, str], ...] = (
    ("Assets/Models/Cars/Police/", "Assets/NfsMw/Content/Vehicles/Police/"),
    ("Assets/Models/Cars/Traffic/", "Assets/NfsMw/Content/Vehicles/Traffic/"),
    ("Assets/Models/Cars/", "Assets/NfsMw/Content/Vehicles/Street/"),
    ("Assets/Models/Cars", "Assets/NfsMw/Content/Vehicles"),
    ("Assets/Models/Frontend", "Assets/NfsMw/Content/Frontend/Models/Frontend"),
    ("Assets/Models/Buildings", "Assets/NfsMw/Content/World/Models/Buildings"),
    ("Assets/Models/Rockport", "Assets/NfsMw/Content/World/Models/Rockport"),
    ("Assets/Models/WeatherDemoCircuit", "Assets/NfsMw/Content/World/Models/WeatherDemoCircuit"),
    ("Assets/Models/", "Assets/NfsMw/Content/World/Models/"),
    ("Assets/Models", "Assets/NfsMw/Content/World/Models"),
    ("Assets/CarRemaster/Customization", "Assets/NfsMw/Content/Customization"),
    ("Assets/CarRemaster/RemasteredCarShowcase.unity", "Assets/NfsMw/Scenes/Showcase/RemasteredCarShowcase.unity"),
    ("Assets/CarRemaster/Editor", "Assets/NfsMw/Modules/ContentTools/CarRemaster/Editor"),
    ("Assets/CarRemaster/", "Assets/NfsMw/Modules/ContentTools/CarRemaster/"),
    ("Assets/CarRemaster", "Assets/NfsMw/Modules/ContentTools/CarRemaster"),
    ("Assets/Driving/", "Assets/NfsMw/Modules/Driving/"),
    ("Assets/Driving", "Assets/NfsMw/Modules/Driving"),
    ("Assets/Audio/", "Assets/NfsMw/Content/Audio/"),
    ("Assets/Audio", "Assets/NfsMw/Content/Audio"),
    ("Assets/Maps/", "Assets/NfsMw/Content/World/Maps/"),
    ("Assets/Maps", "Assets/NfsMw/Content/World/Maps"),
    ("Assets/Scenes/FrontendTests/", "Assets/NfsMw/Content/Frontend/UI/Tests/Scenes/"),
    ("Assets/Scenes/FrontendTests", "Assets/NfsMw/Content/Frontend/UI/Tests/Scenes"),
    ("Assets/Scenes/RockportStreaming/", "Assets/NfsMw/Scenes/World/Streaming/"),
    ("Assets/Scenes/RockportStreaming", "Assets/NfsMw/Scenes/World/Streaming"),
    ("Assets/Scenes/Traffic/", "Assets/NfsMw/Scenes/Tests/Traffic/"),
    ("Assets/Scenes/Traffic", "Assets/NfsMw/Scenes/Tests/Traffic"),
    ("Assets/Scenes/Boot.unity", "Assets/NfsMw/Content/Frontend/UI/Scenes/Boot.unity"),
    ("Assets/Scenes/DrivingDemo.unity", "Assets/NfsMw/Scenes/Game/DrivingDemo.unity"),
    ("Assets/Scenes/RockportBuildings.unity", "Assets/NfsMw/Scenes/World/RockportBuildings.unity"),
    ("Assets/Scenes/RockportMap.unity", "Assets/NfsMw/Scenes/World/RockportMap.unity"),
    ("Assets/Scenes/WeatherDemo.unity", "Assets/NfsMw/Scenes/Showcase/WeatherDemo.unity"),
    ("Assets/Scenes/SampleScene.unity", "Assets/NfsMw/Scenes/Game/SampleScene.unity"),
    ("Assets/Scenes/", "Assets/NfsMw/Scenes/Tests/"),
    ("Assets/Scenes", "Assets/NfsMw/Scenes"),
    ("Assets/Settings/HDRP/", "Assets/NfsMw/Settings/Rendering/HDRP/"),
    ("Assets/Settings/HDRP", "Assets/NfsMw/Settings/Rendering/HDRP"),
    ("Assets/Settings/", "Assets/NfsMw/Settings/Rendering/"),
    ("Assets/Settings", "Assets/NfsMw/Settings/Rendering"),
    ("Assets/Adaptive Performance/", "Assets/NfsMw/Settings/AdaptivePerformance/"),
    ("Assets/Adaptive Performance", "Assets/NfsMw/Settings/AdaptivePerformance"),
    ("Assets/HDRPDefaultResources/", "Assets/NfsMw/Settings/Rendering/HDRPDefaultResources/"),
    ("Assets/HDRPDefaultResources", "Assets/NfsMw/Settings/Rendering/HDRPDefaultResources"),
    ("Assets/InputSystem_Actions.inputactions", "Assets/NfsMw/Settings/Input/InputSystem_Actions.inputactions"),
    ("Assets/DefaultVolumeProfile.asset", "Assets/NfsMw/Settings/Rendering/VolumeProfiles/DefaultVolumeProfile.asset"),
    ("Assets/RacingLine.asset", "Assets/NfsMw/Content/World/Navigation/RacingLine.asset"),
)


TEXT_SKIP = {
    Path(__file__).resolve(),
    ROOT / "Tools/CarRemaster/reorganize_unity_assets.py",
}


def update_text_references() -> int:
    changed = 0
    excluded_parts = {".git", "Library", "Temp", "Logs", "obj", "Build", "Builds"}
    binary_suffixes = {
        ".assetbundle", ".bin", ".blend", ".dds", ".fbx", ".glb", ".gltf",
        ".jpg", ".jpeg", ".mp3", ".mp4", ".ogg", ".png", ".tif", ".tiff",
        ".wav", ".webm", ".zip",
    }
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.resolve() in TEXT_SKIP:
            continue
        if any(part in excluded_parts for part in path.relative_to(ROOT).parts):
            continue
        if path.suffix.lower() in binary_suffixes:
            continue
        try:
            original = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        updated = original
        for old, new in PATH_REPLACEMENTS:
            updated = updated.replace(old, new)
        if updated != original:
            path.write_text(updated, encoding="utf-8")
            changed += 1
    return changed


def migrate_models() -> None:
    models = ASSETS / "Models"
    cars = models / "Cars"
    vehicles = NFS / "Content/Vehicles"
    for child in (sorted(cars.iterdir(), key=lambda item: item.name) if cars.is_dir() else []):
        # Folder sidecars live alongside the folder inside the parent.
        if child.name.endswith(".meta"):
            continue
        if child.name == "Police":
            destination = vehicles / "Police"
        elif child.name == "Traffic":
            destination = vehicles / "Traffic"
        else:
            destination = vehicles / "Street" / child.name
        move_path(child, destination)
    if cars.exists():
        move_sidecar(cars, vehicles)
        remove_empty_directory(cars)

    frontend_root = NFS / "Content/Frontend/Models"
    world_root = NFS / "Content/World/Models"
    for child in (sorted(models.iterdir(), key=lambda item: item.name) if models.is_dir() else []):
        if not child.is_dir() or child.name.endswith(".meta"):
            continue
        if child.name == "Cars":
            continue
        destination = frontend_root / child.name if child.name.startswith("Frontend") else world_root / child.name
        move_path(child, destination)
    if models.exists():
        move_sidecar(models, world_root)
        remove_empty_directory(models)


def migrate_scenes() -> None:
    scenes = ASSETS / "Scenes"
    target = NFS / "Scenes"
    subdirectories = {
        "FrontendTests": target / "Frontend/Tests",
        "RockportStreaming": target / "World/Streaming",
        "Traffic": target / "Tests/Traffic",
    }
    for name, destination in subdirectories.items():
        move_path(scenes / name, destination)

    direct = {
        "Boot.unity": target / "Game/Boot.unity",
        "DrivingDemo.unity": target / "Game/DrivingDemo.unity",
        "SampleScene.unity": target / "Game/SampleScene.unity",
        "RockportBuildings.unity": target / "World/RockportBuildings.unity",
        "RockportMap.unity": target / "World/RockportMap.unity",
        "WeatherDemo.unity": target / "Showcase/WeatherDemo.unity",
        "AudioZoneTest.unity": target / "Tests/AudioZoneTest.unity",
        "BountyTest.unity": target / "Tests/BountyTest.unity",
        "PursuitTest.unity": target / "Tests/PursuitTest.unity",
        "SensoryTest.unity": target / "Tests/SensoryTest.unity",
        "ShopTest.unity": target / "Tests/ShopTest.unity",
    }
    for name, destination in direct.items():
        move_path(scenes / name, destination)
    move_sidecar(scenes, target)
    remove_empty_directory(scenes)


def migrate() -> dict[str, int]:
    # Existing cohesive modules/content roots move as a unit, keeping all
    # internal ownership boundaries intact.
    move_path(ASSETS / "Driving", NFS / "Modules/Driving")
    move_path(ASSETS / "Audio", NFS / "Content/Audio")
    move_path(ASSETS / "Maps", NFS / "Content/World/Maps")
    move_path(ASSETS / "Settings", NFS / "Settings/Rendering")
    move_path(ASSETS / "Adaptive Performance", NFS / "Settings/AdaptivePerformance")
    move_path(ASSETS / "HDRPDefaultResources", NFS / "Settings/Rendering/HDRPDefaultResources")
    move_path(ASSETS / "InputSystem_Actions.inputactions", NFS / "Settings/Input/InputSystem_Actions.inputactions")
    move_path(ASSETS / "DefaultVolumeProfile.asset", NFS / "Settings/Rendering/VolumeProfiles/DefaultVolumeProfile.asset")
    move_path(ASSETS / "RacingLine.asset", NFS / "Content/World/Navigation/RacingLine.asset")

    move_path(ASSETS / "CarRemaster/Customization", NFS / "Content/Customization")
    move_path(ASSETS / "CarRemaster/Editor", NFS / "Modules/ContentTools/CarRemaster/Editor")
    move_path(ASSETS / "CarRemaster/RemasteredCarShowcase.unity", NFS / "Scenes/Showcase/RemasteredCarShowcase.unity")
    car_remaster = ASSETS / "CarRemaster"
    if car_remaster.is_dir():
        move_sidecar(car_remaster, NFS / "Modules/ContentTools/CarRemaster")
        remove_empty_directory(car_remaster)

    migrate_models()
    migrate_scenes()

    # New grouping boundaries receive folder metas. Moved folders retain their
    # original sidecars and are skipped by this function.
    grouping_dirs = [
        NFS,
        NFS / "Content",
        NFS / "Content/Audio",
        NFS / "Content/Customization",
        NFS / "Content/Frontend",
        NFS / "Content/Frontend/Models",
        NFS / "Content/Vehicles",
        NFS / "Content/Vehicles/Street",
        NFS / "Content/Vehicles/Police",
        NFS / "Content/Vehicles/Traffic",
        NFS / "Content/World",
        NFS / "Content/World/Models",
        NFS / "Content/World/Navigation",
        NFS / "Modules",
        NFS / "Modules/ContentTools",
        NFS / "Modules/ContentTools/CarRemaster",
        NFS / "Scenes",
        NFS / "Scenes/Game",
        NFS / "Content/Frontend/UI/Tests",
        NFS / "Content/Frontend/UI/Tests/Scenes",
        NFS / "Scenes/Showcase",
        NFS / "Scenes/Tests",
        NFS / "Scenes/World",
        NFS / "Scenes/World/Streaming",
        NFS / "Settings",
        NFS / "Settings/Input",
        NFS / "Settings/Rendering",
        NFS / "Settings/Rendering/VolumeProfiles",
    ]
    for directory in grouping_dirs:
        directory.mkdir(parents=True, exist_ok=True)
        deterministic_folder_meta(directory)

    changed = update_text_references()
    return {"textFilesUpdated": changed}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="perform the migration")
    args = parser.parse_args()
    if args.apply:
        print(migrate())
    else:
        print("dry run: pass --apply to move the Unity assets")


if __name__ == "__main__":
    main()
