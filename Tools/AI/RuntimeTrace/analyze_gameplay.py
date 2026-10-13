#!/usr/bin/env python3
"""Derive a conservative gameplay census from an NFSMWAITrace JSONL decode.

This analyzer only assigns names to object fields that were independently
confirmed in the retail executable. All counts describe sampled function-entry
events, not function returns or successful operations.
"""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import json
import math
from pathlib import Path
import struct
from typing import Any


GOAL_VTABLES = {
    0x00892B30: "AIGoalPursuit",
    0x00892BF0: "AIGoalTraffic",
    0x00892B40: "AIGoalStaticRoadBlock",
    0x00892D00: "AIGoalFleePursuit",
    0x00892D10: "AIGoalHeliPursuit",
    0x00892CD8: "AIGoalHeadOnRam",
    0x00892C64: "AIGoalStopShort",
    0x00892C88: "AIGoalRam",
    0x00892D30: "AIGoalRacer",
    0x00892C14: "AIGoalPatrol",
    0x00892D20: "AIGoalHeliExit",
}

GOAL_CONSTRUCTORS = {
    0x00892BF0: 0x0043B670,
    0x00892C14: 0x0043B730,
    0x00892B30: 0x0043B800,
    0x00892C64: 0x0043B8C0,
    0x00892C88: 0x0043B9B0,
    0x00892CD8: 0x0043BD10,
    0x00892B40: 0x0043BE50,
    0x00892D00: 0x0043BED0,
    0x00892D10: 0x0043BFC0,
    0x00892D20: 0x0043C090,
    0x00892D30: 0x0043C160,
}

VEHICLE_VTABLES = {
    0x00891CF8: "AIVehicleTraffic",
    0x00892560: "AIVehicleCopCar",
    0x00892E28: "AIVehicleEmpty",
    0x00892AD0: "AIVehicleHuman",
    0x008920D8: "AIVehicleHelicopter",
}

TASK_FIELDS = {
    "AICopManager::OnTask": {"simulate_task": 0x80},
    "AITrafficManager::OnTask": {"task": 0x5C},
    "AIPursuit::OnTask": {
        "simulate_task": 0x54,
        "busted_timer_task": 0x58,
    },
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("jsonl", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expect-target-sha256")
    return parser.parse_args()


def pointer_sort_key(value: str) -> int:
    try:
        return int(value, 16)
    except (TypeError, ValueError):
        return -1


def read_bytes(event: dict[str, Any], object_offset: int, size: int) -> bytes | None:
    for region in event.get("regions", []):
        region_offset = region.get("offset")
        raw_hex = region.get("raw_hex")
        if not isinstance(region_offset, int) or not isinstance(raw_hex, str):
            continue
        raw = bytes.fromhex(raw_hex)
        local_offset = object_offset - region_offset
        if local_offset >= 0 and local_offset + size <= len(raw):
            return raw[local_offset : local_offset + size]
    return None


def read_u32(event: dict[str, Any], object_offset: int) -> int | None:
    raw = read_bytes(event, object_offset, 4)
    return None if raw is None else struct.unpack("<I", raw)[0]


def read_f32(event: dict[str, Any], object_offset: int) -> float | None:
    raw = read_bytes(event, object_offset, 4)
    if raw is None:
        return None
    value = struct.unpack("<f", raw)[0]
    return value if math.isfinite(value) else None


def census_rows(
    values: dict[str, dict[str, Any]],
) -> list[dict[str, Any]]:
    rows = []
    for name, item in values.items():
        rows.append(
            {
                "name": name,
                "entry_samples": item["count"],
                "unique_object_addresses": len(item["objects"]),
                "first_seconds_after_trace_start": round(item["first"], 6),
                "last_seconds_after_trace_start": round(item["last"], 6),
            }
        )
    return sorted(rows, key=lambda row: (-row["entry_samples"], row["name"]))


def counter_rows(counter: Counter[str]) -> list[dict[str, Any]]:
    return [
        {"value": value, "count": count}
        for value, count in sorted(
            counter.items(), key=lambda item: (-item[1], pointer_sort_key(item[0]))
        )
    ]


def main() -> int:
    args = parse_args()
    header: dict[str, Any] | None = None
    record_types: Counter[str] = Counter()
    event_kinds: Counter[str] = Counter()
    hook_stats: dict[str, dict[str, Any]] = {}
    goal_census: dict[str, dict[str, Any]] = {}
    vehicle_census: dict[str, dict[str, Any]] = {}
    task_observations: dict[str, Counter[tuple[int, tuple[int | None, ...]]]] = {
        hook: Counter() for hook in TASK_FIELDS
    }
    task_missing: Counter[str] = Counter()
    pursuit_pointers: Counter[str] = Counter()
    vehicle_pointers: Counter[str] = Counter()
    heli_fuel: dict[str, list[dict[str, Any]]] = defaultdict(list)

    with args.jsonl.open("r", encoding="utf-8") as source:
        for line_number, line in enumerate(source, 1):
            record = json.loads(line)
            record_type = str(record.get("type", "unknown"))
            record_types[record_type] += 1
            if record_type == "header":
                if header is not None:
                    raise ValueError(f"duplicate header at line {line_number}")
                header = record
                continue
            if record_type != "event":
                continue
            if header is None:
                raise ValueError("event encountered before header")

            event_kind = str(record.get("kind", "unknown"))
            event_kinds[event_kind] += 1
            if event_kind != "entry":
                continue

            hook = record["hook"]
            elapsed = (record["qpc"] - header["started_qpc"]) / header["qpc_frequency"]
            stats = hook_stats.setdefault(
                hook,
                {
                    "entry_samples": 0,
                    "first": elapsed,
                    "last": elapsed,
                    "threads": set(),
                    "objects": set(),
                    "max_call_index": 0,
                },
            )
            stats["entry_samples"] += 1
            stats["first"] = min(stats["first"], elapsed)
            stats["last"] = max(stats["last"], elapsed)
            stats["threads"].add(record.get("thread_id"))
            if record.get("object_base_ptr"):
                stats["objects"].add(record["object_base_ptr"])
            stats["max_call_index"] = max(stats["max_call_index"], record.get("call_index", 0))

            if hook == "AIGoal::ChooseAction":
                vtable = read_u32(record, 0)
                name = GOAL_VTABLES.get(vtable, f"unmapped_{vtable:#010x}" if vtable is not None else "unreadable")
                item = goal_census.setdefault(
                    name, {"count": 0, "objects": set(), "first": elapsed, "last": elapsed}
                )
                item["count"] += 1
                item["objects"].add(record.get("object_base_ptr"))
                item["first"] = min(item["first"], elapsed)
                item["last"] = max(item["last"], elapsed)

            if hook == "AIVehicle::OnTaskSimulate":
                vtable = read_u32(record, 0)
                name = VEHICLE_VTABLES.get(vtable, f"unmapped_{vtable:#010x}" if vtable is not None else "unreadable")
                item = vehicle_census.setdefault(
                    name, {"count": 0, "objects": set(), "first": elapsed, "last": elapsed}
                )
                item["count"] += 1
                item["objects"].add(record.get("object_base_ptr"))
                item["first"] = min(item["first"], elapsed)
                item["last"] = max(item["last"], elapsed)

            if hook in TASK_FIELDS:
                task = record.get("args", {}).get("task_handle")
                values = tuple(read_u32(record, offset) for offset in TASK_FIELDS[hook].values())
                if isinstance(task, int):
                    task_observations[hook][(task, values)] += 1
                else:
                    task_missing[hook] += 1

            if hook == "AICopManager::SpawnPursuitIVehicle":
                spawn_args = record.get("args", {})
                if spawn_args.get("pursuit_ptr"):
                    pursuit_pointers[str(spawn_args["pursuit_ptr"])] += 1
                if spawn_args.get("vehicle_ptr"):
                    vehicle_pointers[str(spawn_args["vehicle_ptr"])] += 1

            if hook == "AIVehicleHelicopter::Update":
                fuel = read_f32(record, 0x7D8)
                object_pointer = record.get("object_base_ptr")
                if fuel is not None and object_pointer:
                    heli_fuel[object_pointer].append(
                        {
                            "seconds_after_trace_start": round(elapsed, 6),
                            "fuel_seconds": round(fuel, 6),
                        }
                    )

    if header is None:
        raise ValueError("missing header")
    if args.expect_target_sha256 and header.get("target_sha256") != args.expect_target_sha256:
        raise ValueError(
            f"target sha256 mismatch: {header.get('target_sha256')} != {args.expect_target_sha256}"
        )

    hooks = []
    for name, stats in hook_stats.items():
        hooks.append(
            {
                "hook": name,
                "entry_samples": stats["entry_samples"],
                "first_seconds_after_trace_start": round(stats["first"], 6),
                "last_seconds_after_trace_start": round(stats["last"], 6),
                "thread_ids": sorted(stats["threads"]),
                "unique_object_addresses": len(stats["objects"]),
                "max_call_index": stats["max_call_index"],
            }
        )
    hooks.sort(key=lambda row: (-row["entry_samples"], row["hook"]))

    task_validation: dict[str, Any] = {}
    for hook, observations in task_observations.items():
        field_names = list(TASK_FIELDS[hook])
        rows = []
        for (argument, values), count in sorted(observations.items()):
            matching_fields = [
                field_names[index]
                for index, value in enumerate(values)
                if value == argument
            ]
            rows.append(
                {
                    "argument": argument,
                    "field_values": dict(zip(field_names, values)),
                    "matching_fields": matching_fields,
                    "entry_samples": count,
                }
            )
        task_validation[hook] = {
            "field_offsets": {
                name: f"0x{offset:x}" for name, offset in TASK_FIELDS[hook].items()
            },
            "observations": rows,
            "missing_argument_samples": task_missing[hook],
        }

    fuel_rows = []
    for object_pointer, samples in sorted(heli_fuel.items(), key=lambda item: pointer_sort_key(item[0])):
        values = [sample["fuel_seconds"] for sample in samples]
        deltas = [right - left for left, right in zip(values, values[1:])]
        fuel_rows.append(
            {
                "object_pointer": object_pointer,
                "entry_samples": len(samples),
                "first_fuel_seconds": values[0],
                "last_fuel_seconds": values[-1],
                "minimum_fuel_seconds": min(values),
                "maximum_fuel_seconds": max(values),
                "nonincreasing_steps": sum(delta <= 0.000001 for delta in deltas),
                "increasing_steps": sum(delta > 0.000001 for delta in deltas),
                "samples": samples,
            }
        )

    result = {
        "interpretation_boundary": {
            "event_kind": "sampled function-entry observations",
            "does_not_prove": [
                "function return values",
                "successful spawns",
                "exact state transitions between samples",
                "unique lifetime instances when allocator addresses are reused",
            ],
        },
        "capture_identity": {
            "capture_id": header.get("capture_id"),
            "target_name": header.get("target_name"),
            "target_sha256": header.get("target_sha256"),
            "catalogue_sha256": header.get("catalogue_sha256"),
            "catalogue_matches_local": header.get("catalogue_matches_local"),
            "schema_version": header.get("schema_version"),
            "activation_state": header.get("activation_state"),
            "flags": header.get("flag_names"),
            "record_types": dict(sorted(record_types.items())),
            "event_kinds": dict(sorted(event_kinds.items())),
        },
        "hook_census": hooks,
        "task_field_validation": task_validation,
        "pursuit_spawn_entries": {
            "entry_samples": hook_stats.get("AICopManager::SpawnPursuitIVehicle", {}).get("entry_samples", 0),
            "pursuit_pointers": counter_rows(pursuit_pointers),
            "vehicle_pointers": counter_rows(vehicle_pointers),
        },
        "goal_vtable_mapping": [
            {
                "vtable": f"0x{vtable:08x}",
                "name": name,
                "constructor_va": f"0x{GOAL_CONSTRUCTORS[vtable]:08x}",
                "confidence": "high",
                "basis": (
                    "named retail symbol plus exact-matched constructor"
                    if name == "AIGoalTraffic"
                    else "retail constructor vtable assignment and action-string relocations"
                ),
            }
            for vtable, name in sorted(GOAL_VTABLES.items(), key=lambda item: GOAL_CONSTRUCTORS[item[0]])
        ],
        "goal_choose_action_census": census_rows(goal_census),
        "vehicle_vtable_mapping": [
            {
                "vtable": f"0x{vtable:08x}",
                "name": name,
                "confidence": "high",
                "basis": "named retail symbol table entry",
            }
            for vtable, name in sorted(VEHICLE_VTABLES.items())
        ],
        "vehicle_simulation_census": census_rows(vehicle_census),
        "helicopter_fuel_entry_samples": {
            "retail_object_offset": "0x7d8",
            "objects": fuel_rows,
        },
    }
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
