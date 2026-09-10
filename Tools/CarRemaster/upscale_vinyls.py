"""Create 2K copies of every recovered vinyl texture.

The indexed PNG output keeps the source palette/alpha compact enough for the
full library while retaining 2K pixels. By default decoded PNGs are copied to
Vinyls/2K/; --in-place can replace them when disk space is constrained.
Masks are interpolated without sharpening.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from PIL import Image, ImageFilter
from vinyl_paths import write_unity_catalog

ROOT = Path(__file__).resolve().parents[2]
VINYL_ROOT = ROOT / "Art/Cars/Modifications/Vinyls"
UPSCALED_ROOT = VINYL_ROOT / "2K"
REPORT_PATH = ROOT / "Art/Cars/Modifications/vinyl-decoding-report.json"
REPORT_COPY = VINYL_ROOT / "catalog.json"
TARGET = 2048


def upscale_one(source: Path, destination: Path, role: str, force: bool) -> dict:
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists() and not force:
        with Image.open(destination) as image:
            if image.size == (TARGET, TARGET):
                return {"width": TARGET, "height": TARGET,
                        "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
                        "skipped": True}
    temporary = destination.with_name(destination.name + ".tmp")
    try:
        with Image.open(source) as original:
            original.load()
            source_width, source_height = original.size
            image = original.convert("RGBA").resize((TARGET, TARGET), Image.Resampling.LANCZOS)
            if role != "mask":
                image = image.filter(ImageFilter.UnsharpMask(radius=0.55, percent=18, threshold=2))
            # Game vinyls are palette artwork/masks.  An adaptive indexed PNG
            # preserves the RGBA result closely and avoids multi-gigabyte
            # uncompressed RGBA assets for the 10,983-texture library.
            image = image.quantize(colors=256, method=Image.Quantize.FASTOCTREE,
                                   dither=Image.Dither.NONE)
            image.save(temporary, format="PNG", optimize=True, compress_level=9)
        os.replace(temporary, destination)
        return {"sourceWidth": source_width, "sourceHeight": source_height,
                "width": TARGET, "height": TARGET,
                "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
                "skipped": False}
    finally:
        temporary.unlink(missing_ok=True)


def run(force: bool = False, workers: int = 4, in_place: bool = False) -> dict:
    report = json.loads(REPORT_PATH.read_text())
    textures = [texture for archive in report["archives"] for texture in archive["textures"]]
    if len(textures) != 10983:
        raise ValueError(f"expected 10983 catalog textures, found {len(textures)}")
    jobs = []
    for texture in textures:
        source_asset = texture.get("sourceAsset", texture["asset"])
        source = ROOT / source_asset
        if not source.is_file():
            raise FileNotFoundError(source)
        destination = source if in_place else UPSCALED_ROOT / source.relative_to(VINYL_ROOT)
        texture.setdefault("sourceAsset", source_asset)
        texture.setdefault("sourceSha256", hashlib.sha256(source.read_bytes()).hexdigest())
        jobs.append((texture, source, destination))
    # Persist the source manifest before starting writes. If the process is
    # interrupted during an in-place run, the original dimensions and hashes
    # are still known when the resumable pass is started again.
    report["upscale"] = {"target": TARGET, "textures": len(textures),
        "status": "running", "inPlace": in_place, "errors": []}
    REPORT_PATH.write_text(json.dumps(report, indent=2) + "\n")
    completed = skipped = 0
    errors = []
    with ThreadPoolExecutor(max_workers=max(1, workers)) as pool:
        futures = {pool.submit(upscale_one, source, dest, texture["role"], force): texture
                   for texture, source, dest in jobs}
        for future in as_completed(futures):
            texture = futures[future]
            try:
                result = future.result()
                skipped += int(result["skipped"])
                completed += int(not result["skipped"])
                texture.setdefault("sourceAsset", texture["asset"])
                texture.setdefault("sourceWidth", texture["width"])
                texture.setdefault("sourceHeight", texture["height"])
                if not in_place:
                    texture["asset"] = str((UPSCALED_ROOT / (ROOT / texture["sourceAsset"]).relative_to(VINYL_ROOT)).relative_to(ROOT))
                texture["width"] = result["width"]
                texture["height"] = result["height"]
                texture["sha256"] = result["sha256"]
                texture["upscale"] = {"target": TARGET,
                    "method": "RGBA Lanczos 4x, adaptive 256-color PNG, light artwork unsharp mask",
                    "maskMethod": "RGBA Lanczos 4x, adaptive 256-color PNG"}
            except Exception as error:
                errors.append({"asset": texture.get("asset"), "error": repr(error)})
    report["upscale"] = {"target": TARGET, "textures": len(textures),
        "completed": completed, "skipped": skipped,
        "sourceRoot": str(VINYL_ROOT.relative_to(ROOT)),
        "outputRoot": str(VINYL_ROOT.relative_to(ROOT) if in_place else UPSCALED_ROOT.relative_to(ROOT)),
        "inPlace": in_place,
        "method": "RGBA Lanczos 4x, adaptive 256-color PNG, light artwork unsharp mask",
        "maskMethod": "RGBA Lanczos 4x, adaptive 256-color PNG", "errors": errors}
    encoded = json.dumps(report, indent=2) + "\n"
    REPORT_PATH.write_text(encoded)
    REPORT_COPY.write_text(encoded)
    write_unity_catalog(report)
    if errors:
        raise RuntimeError(json.dumps(errors[:8], indent=2))
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--workers", type=int, default=min(4, os.cpu_count() or 1))
    parser.add_argument("--in-place", action="store_true",
                        help="replace decoded PNGs in place (useful on a full disk)")
    args = parser.parse_args()
    result = run(args.force, args.workers, args.in_place)
    print("2K vinyls:", result["upscale"]["textures"], "textures;",
          result["upscale"]["completed"], "written;",
          result["upscale"]["skipped"], "reused; 0 errors")


if __name__ == "__main__":
    main()
