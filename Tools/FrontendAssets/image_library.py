"""Resolve source texture identities to the curated Unity image library."""

import json
from functools import cache
from pathlib import Path


@cache
def locations() -> dict:
    records = json.loads(Path(__file__).with_name("image-library.json").read_text())["textures"]
    result = {}
    for record in records:
        # Historical copies keep their GUIDs; prefer the original import when
        # the same source image was also imported by the supplemental decoder.
        result.setdefault((record["nameHash"], record["pngSha256"]), record)
    return result


def locate_texture(name_hash: str, png_sha256: str) -> dict:
    try:
        return locations()[(name_hash, png_sha256)]
    except KeyError:
        raise ValueError(
            f"Texture {name_hash} ({png_sha256}) needs a descriptive destination in image-library.json"
        ) from None
