"""Shared paths for the source vinyl cache and Unity's canonical library."""
from __future__ import annotations

import copy
import json
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
UNITY_VINYL_ROOT = ROOT / "Assets/NfsMw/Content/Customization/Vinyls"


def canonical_vinyl_path(archive: dict, texture: dict) -> Path:
    """Return the Unity path for one decoded texture without changing its name."""
    source = str(archive["source"]).upper()
    if source.endswith("/PREVINYL.BIN"):
        folder = "PreVinyl"
    elif texture.get("role") == "mask":
        folder = "Masks"
    else:
        folder = "Artwork"
    archive_name = Path(archive["source"]).parent.name
    filename = Path(texture.get("sourceAsset", texture["asset"])).name
    return UNITY_VINYL_ROOT / folder / archive_name / filename


def unity_catalog(report: dict, copy_assets: bool = True) -> dict:
    """Create the Unity-facing catalog and optionally copy source outputs into it."""
    catalog = copy.deepcopy(report)
    catalog["layout"] = "content-first-v2"
    for archive in catalog.get("archives", []):
        source_kind = "PREVINYL" if str(archive["source"]).upper().endswith("/PREVINYL.BIN") else "VINYLS"
        for texture in archive.get("textures", []):
            source_asset = texture.get("sourceAsset", texture["asset"])
            source = ROOT / texture["asset"]
            destination = canonical_vinyl_path(archive, texture)
            if copy_assets:
                if not source.is_file():
                    raise FileNotFoundError(source)
                destination.parent.mkdir(parents=True, exist_ok=True)
                if source != destination:
                    shutil.copy2(source, destination)
            texture["asset"] = str(destination.relative_to(ROOT))
            texture["sourceAsset"] = source_asset
            texture["sourceKind"] = source_kind
    return catalog


def write_unity_catalog(report: dict, path: Path | None = None, copy_assets: bool = True) -> Path:
    destination = path or UNITY_VINYL_ROOT / "catalog.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(unity_catalog(report, copy_assets=copy_assets), indent=2) + "\n")
    return destination
