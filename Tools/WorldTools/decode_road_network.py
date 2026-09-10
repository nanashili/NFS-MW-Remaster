"""Decode Most Wanted's original CARP road graph without modifying game files.

The observed PC format is documented by MWSDK and MWEncyclopedia.  This reader
keeps the bounded bchunk traversal used by the map reader, discovers the
ordinal lattices inside the CARP payload, and rejects inconsistent topology.
"""
from __future__ import annotations

import collections
import hashlib
import json
import math
import mmap
import struct
from pathlib import Path

from inventory_game import GAME, ROOT, chunks


SOURCE = GAME / "TRACKS" / "L2RA.BUN"
SOURCE_LABEL = "TRACKS/L2RA.BUN"
INVENTORY = ROOT / "Art/RockportBuildings/Source/game-inventory.json"
ASSET = ROOT / "Assets/NfsMw/Content/World/Maps/Rockport/Navigation/rockport-road-network.json"
REPORT = ROOT / "Artifacts/RockportStreaming/road-network-source.json"


def find_ordinal_lattice(data: mmap.mmap, start: int, stop: int, stride: int,
                         ordinal_offset: int, minimum: int) -> tuple[int, int]:
    best_start, best_count = -1, 0
    for candidate in range(start, stop - ordinal_offset - 2, 2):
        if struct.unpack_from("<H", data, candidate + ordinal_offset)[0] != 0:
            continue
        count = 1
        while candidate + count * stride + ordinal_offset + 2 <= stop:
            ordinal = struct.unpack_from("<H", data, candidate + count * stride + ordinal_offset)[0]
            if ordinal != count:
                break
            count += 1
        if count > best_count:
            best_start, best_count = candidate, count
    if best_count < minimum:
        raise ValueError(f"CARP ordinal lattice was not found: stride={stride}, best={best_count}")
    return best_start, best_count


def finite(values) -> bool:
    return all(math.isfinite(value) for value in values)


def connected_components(node_count: int, segments: list[dict]) -> list[list[int]]:
    adjacency = [[] for _ in range(node_count)]
    for segment in segments:
        a, b = segment["nodeA"], segment["nodeB"]
        adjacency[a].append(b)
        adjacency[b].append(a)
    seen = [False] * node_count
    result = []
    for root in range(node_count):
        if seen[root]:
            continue
        stack, component = [root], []
        seen[root] = True
        while stack:
            node = stack.pop()
            component.append(node)
            for other in adjacency[node]:
                if not seen[other]:
                    seen[other] = True
                    stack.append(other)
        result.append(component)
    return sorted(result, key=len, reverse=True)


def run() -> None:
    expected = json.loads(INVENTORY.read_text())
    expected_archive = next(item for item in expected["archives"] if item["source"] == SOURCE_LABEL)
    expected_digest = expected_archive["sha256"]

    with SOURCE.open("rb") as handle, mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ) as data:
        source_digest = hashlib.sha256(data).hexdigest()
        if source_digest != expected_digest:
            raise ValueError("L2RA.BUN no longer matches the source used for the decoded map")
        carps = [(offset, size) for kind, offset, size, _ in chunks(data) if kind == 0x0003B800]
        if len(carps) != 1:
            raise ValueError(f"Expected one CARP chunk, found {len(carps)}")
        chunk_offset, chunk_size = carps[0]
        payload_start, payload_stop = chunk_offset + 8, chunk_offset + 8 + chunk_size
        magic_at = data.find(b"PRAC", payload_start, min(payload_start + 64, payload_stop))
        if magic_at < 0:
            raise ValueError("CARP magic was not found")
        version = struct.unpack_from("<I", data, magic_at + 4)[0]

        node_start, node_count = find_ordinal_lattice(data, payload_start, payload_stop, 32, 12, 1024)
        segment_start, segment_count = find_ordinal_lattice(data, payload_start, payload_stop, 22, 8, 1024)
        if node_start + node_count * 32 > payload_stop or segment_start + segment_count * 22 > payload_stop:
            raise ValueError("CARP lattice extends outside its chunk")

        nodes = []
        for index in range(node_count):
            position = node_start + index * 32
            game_x, game_height, game_y = struct.unpack_from("<3f", data, position)
            ordinal, road_index = struct.unpack_from("<HH", data, position + 12)
            degree = data[position + 16]
            padding = data[position + 17]
            segment_indices = list(struct.unpack_from("<7H", data, position + 18))
            if ordinal != index or not finite((game_x, game_height, game_y)) or degree > 7:
                raise ValueError(f"Invalid CARP node {index}")
            if any(item >= segment_count for item in segment_indices[:degree]):
                raise ValueError(f"CARP node {index} references an invalid segment")
            # The game's CARP horizontal axes differ from scenery records.
            # This transform was independently checked against the recovered map
            # heightfield: game (x, height, y) -> Unity (-y, height, x).
            unity = [-game_y, game_height, game_x]
            nodes.append({
                "id": index,
                "position": unity,
                "gamePosition": [game_x, game_height, game_y],
                "roadIndex": road_index,
                "degree": degree,
                "padding": padding,
                "segments": segment_indices[:degree],
            })

        segments = []
        incidence = [[] for _ in range(node_count)]
        for index in range(segment_count):
            position = segment_start + index * 22
            node_a, node_b = struct.unpack_from("<HH", data, position)
            arc_q10_6, optional, ordinal, flags = struct.unpack_from("<HhHH", data, position + 4)
            if ordinal != index or node_a >= node_count or node_b >= node_count or node_a == node_b:
                raise ValueError(f"Invalid CARP segment {index}")
            a, b = nodes[node_a]["position"], nodes[node_b]["position"]
            chord = math.dist(a, b)
            if not math.isfinite(chord) or chord < 0.001:
                raise ValueError(f"Degenerate CARP segment {index}")
            incidence[node_a].append(index)
            incidence[node_b].append(index)
            segments.append({
                "id": index,
                "nodeA": node_a,
                "nodeB": node_b,
                "arcLengthQ10_6": arc_q10_6,
                "arcLength": arc_q10_6 / 64.0,
                "chordLength": chord,
                "optional": optional,
                "flags": flags,
                "tail": data[position + 12:position + 22].hex(),
            })

        for index, node in enumerate(nodes):
            if sorted(node["segments"]) != sorted(incidence[index]) or node["degree"] != len(incidence[index]):
                raise ValueError(f"CARP node {index} incidence does not match its segment table")

        if hashlib.sha256(data).hexdigest() != source_digest:
            raise ValueError("Source archive changed while reading")

    components = connected_components(node_count, segments)
    bounds_min = [min(node["position"][axis] for node in nodes) for axis in range(3)]
    bounds_max = [max(node["position"][axis] for node in nodes) for axis in range(3)]
    fingerprint = hashlib.sha256(json.dumps({"nodes": nodes, "segments": segments}, separators=(",", ":")).encode()).hexdigest()
    degree_counts = dict(sorted(collections.Counter(node["degree"] for node in nodes).items()))
    serialized_nodes = []
    for node in nodes:
        serialized = dict(node)
        serialized["position"] = {"x": node["position"][0], "y": node["position"][1], "z": node["position"][2]}
        serialized_nodes.append(serialized)
    asset = {
        "schemaVersion": 1,
        "source": SOURCE_LABEL,
        "sourceSha256": source_digest,
        "sourceChunkOffset": chunk_offset,
        "sourceChunkSize": chunk_size,
        "carpVersion": version,
        "coordinateTransform": "game CARP (x,height,y) -> Unity (-y,height,x)",
        "fingerprint": fingerprint,
        "boundsMin": bounds_min,
        "boundsMax": bounds_max,
        "nodes": serialized_nodes,
        "segments": segments,
    }
    report = {
        "status": "PASS",
        "source": SOURCE_LABEL,
        "sourceSha256": source_digest,
        "sourceUnchanged": True,
        "carpVersion": version,
        "nodeCount": node_count,
        "segmentCount": segment_count,
        "directedLaneCount": segment_count * 2,
        "degreeCounts": {str(key): value for key, value in degree_counts.items()},
        "degreeSum": sum(node["degree"] for node in nodes),
        "expectedDegreeSum": segment_count * 2,
        "componentCount": len(components),
        "componentSizes": [len(component) for component in components],
        "boundsMin": bounds_min,
        "boundsMax": bounds_max,
        "fingerprint": fingerprint,
        "references": [
            "https://github.com/TsyVM/MWSDK/blob/main/src/paths.cpp",
            "https://github.com/TsyVM/MWEncyclopedia/tree/main/C18-Road-Network-CARP",
        ],
    }
    if report["degreeSum"] != report["expectedDegreeSum"]:
        raise ValueError("CARP graph degree sum is inconsistent")
    ASSET.parent.mkdir(parents=True, exist_ok=True)
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    ASSET.write_text(json.dumps(asset, separators=(",", ":")) + "\n")
    REPORT.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    run()
