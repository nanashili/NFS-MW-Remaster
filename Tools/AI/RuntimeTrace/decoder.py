#!/usr/bin/env python3
from __future__ import annotations

import argparse
import configparser
import csv
import hashlib
import json
import math
import os
import statistics
import struct
import sys
import tempfile
from collections import Counter
from pathlib import Path
from typing import Any, BinaryIO, Iterable, Iterator, Mapping

from generate_catalogue import CatalogueError, load_catalogue

HEADER = struct.Struct("<8sIII32sIIIIIIQII32s32s100s")
EVENT_PREFIX = struct.Struct("<IHBB" + "I" * 10)
EVENT_SIZE = 600
HEADER_SIZE = 256
EVENT_MAGIC = 0x31545645
SCHEMA3_HEADER_CORE = struct.Struct("<8s4I32s32s32s32s4I3Q16s4IQ7I32s32s32s64s32s572s")
SCHEMA3_MANIFEST_PREFIX = struct.Struct("<7Ii8I16s8s72s48s16s")
SCHEMA3_MANIFEST_REGION = struct.Struct("<IiII16s")
SCHEMA3_EVENT_PREFIX = struct.Struct("<IHBB4Q11I8I3I3I3H3H2I8Q2I16s8s")
SCHEMA3_HEADER_SIZE = 16384
SCHEMA4_HEADER_SIZE = 32768
SCHEMA3_HEADER_CORE_SIZE = 1024
SCHEMA3_MANIFEST_SIZE = 320
SCHEMA3_EVENT_SIZE = 1024
SCHEMA3_EVENT_MAGIC = 0x33545645
SCHEMA3_REGION_COUNT = 3
SCHEMA3_REGION_BYTES = 256
SCHEMA3_HEADER_DIGEST_OFFSET = struct.calcsize("<8s4I32s32s32s32s4I3Q16s4IQ7I32s32s32s64s")
SCHEMA3_EVENT_DIGEST_OFFSET = struct.calcsize("<IHBB4Q11I8I3I3I3H3H2I8Q2I")
EXPECTED_TARGET_SHA256 = "80774c2e5d619b4f120b48d4462896fd504c263399d203a238769cffde1d253c"
CURRENT_SCHEMA_VERSION = 4
SUPPORTED_SCHEMAS = frozenset({1, 2, 3, 4})

SCHEMA3_EVENT_KINDS = {1: "entry", 2: "checkpoint", 3: "terminal"}
SCHEMA3_TERMINAL_REASONS = {
    0: "none",
    1: "limit",
    2: "process_detach",
    3: "write_failure",
    4: "call_index_wrap",
    5: "stop_request",
}
SCHEMA3_ACTIVATION_STATES = {
    1: "starting",
    2: "validated",
    3: "patching",
    4: "active",
    5: "stopping",
    6: "closed",
    7: "failed",
}
SCHEMA3_REGION_SOURCES = {
    0: "none",
    1: "normalized_this",
    2: "raw_this",
    **{16 + index: f"arg{index}" for index in range(8)},
    32: "absolute",
}
SCHEMA3_ARG_TYPES = {0: "integer", 1: "float", 2: "pointer", 3: "handle"}
SCHEMA3_EVENT_FLAG_NAMES = {
    1: "return_address_valid",
    2: "normalized_this_valid",
    4: "region_partial",
    8: "read_budget_exceeded",
}
SCHEMA3_EVENT_FLAG_MASK = sum(SCHEMA3_EVENT_FLAG_NAMES)
SCHEMA4_EVENT_FLAG_NAMES = {
    **SCHEMA3_EVENT_FLAG_NAMES,
    16: "synthetic_probe",
    32: "probe_assisted",
}
SCHEMA4_EVENT_FLAG_MASK = sum(SCHEMA4_EVENT_FLAG_NAMES)
SCHEMA3_HEADER_FLAG_MASK = 0xF
SCHEMA3_HOOK_FLAG_MASK = 0x1
SCHEMA3_RECOVERY_CLASSES = frozenset({"existing", "exact", "near", "partial"})
SCHEMA3_PROFILE_NAMES = {
    1: "survey",
    2: "core",
    3: "racer",
    4: "traffic",
    5: "pursuit",
    6: "helicopter",
    7: "navigation",
    8: "all",
}
SCHEMA4_PROFILE_NAMES = {
    1: "survey",
    2: "core",
    3: "racer",
    4: "traffic",
    5: "pursuit",
    6: "helicopter",
    7: "navigation",
    8: "zbware",
    9: "all",
}
STREAM_INDEX_TRACKING_LIMIT = 4096
STREAM_EXAMPLE_LIMIT = 12
STREAM_THREAD_TRACKING_LIMIT = 4096

HOOKS = {
    1: ("AIVehicle::OnTaskSimulate", ("dT",)),
    2: ("AIVehicle::OnTask", ("task_handle", "dT")),
    3: ("AIGoal::ChooseAction", ("dT",)),
    4: ("AISpawnManager::CheckSpawnPosition", ("check_pos_ptr", "check_lane", "lane", "node", "check_dist")),
    5: ("AISpawnManager::GetSpawnLocation", ("segment_ptr", "lane_ptr", "time_step_ptr")),
    6: ("AICopManager::SpawnPursuitIVehicle", ("pursuit_ptr", "vehicle_ptr")),
    7: ("AIPursuit::UpdateFormation", ("dT",)),
    8: ("AITrafficManager::SpawnTraffic", ()),
    9: ("AIPursuit::OnTask", ("task_handle", "dT")),
}

HOOK_SAMPLE_CONFIG_KEYS = {
    1: "sample_AIVehicle_OnTaskSimulate",
    2: "sample_AIVehicle_OnTask",
    3: "sample_AIGoal_ChooseAction",
    4: "sample_AISpawnManager_CheckSpawnPosition",
    5: "sample_AISpawnManager_GetSpawnLocation",
    6: "sample_AICopManager_SpawnPursuitIVehicle",
    7: "sample_AIPursuit_UpdateFormation",
    8: "sample_AITrafficManager_SpawnTraffic",
    9: "sample_AIPursuit_OnTask",
}

DEFAULT_SAMPLE_DIVISORS = {
    1: 128,
    2: 64,
    3: 1,
    4: 1,
    5: 1,
    6: 1,
    7: 8,
    8: 1,
    9: 16,
}

# The two task callbacks are methods of a secondary Sim::IActivity subobject.
HOOK_THIS_TO_COMPLETE_BASE = {2: -8, 9: -8}

VEHICLE_VALUES = (
    "drive_speed",
    "think_task_handle",
    "last_spawn_time",
    "current_goal_ptr",
    "pursuit_ptr",
    "goal_crc",
    "drive_flags",
    "top_speed",
    "race_throttle_brake_raw",
)
GOAL_VALUES = ("current_action_ptr", "action_list_0", "action_list_1", "owner_ptr")
SPAWN_VALUES = ("min_spawn_distance", "max_spawn_distance")
PURSUIT_VALUES = (
    "busted_timer_task_handle",
    "target_ptr",
    "formation_ptr",
    "pursuit_level",
    "base_heat",
    "maximum_heat",
    "heat_scale",
    "in_formation_timer",
    "breaker_timer",
    "total_pursuit_time",
    "formation_attempt_count",
    "active_formation",
    "active_formation_time",
    "busted_timer",
    "cooldown_time_remaining",
    "pursuit_meter",
)

FLOAT_ARGS = {1: {0}, 2: {1}, 3: {0}, 7: {0}, 9: {1}}
BOOL_RESULTS = {2, 4, 5, 6, 8, 9}
FLOAT_VEHICLE = {0, 2, 7, 8}
FLOAT_SPAWN = {0, 1}
FLOAT_PURSUIT = {4, 5, 6, 7, 8, 9, 12, 13, 14, 15}

VEHICLE_OBJECT_FIELDS = (
    ("drive_speed", 0x84, "float"),
    ("think_task_handle", 0xAC, "handle"),
    ("last_spawn_time", 0xB0, "float"),
    ("current_goal_ptr", 0xB8, "pointer"),
    ("pursuit_ptr", 0xBC, "pointer"),
    ("goal_crc", 0xC4, "crc"),
    ("drive_flags", 0xCC, "integer"),
)

SPAWN_OBJECT_FIELDS = (
    ("min_spawn_distance", 0x04, "float"),
    ("max_spawn_distance", 0x08, "float"),
)

PURSUIT_OBJECT_FIELDS = (
    ("busted_timer_task_handle", 0x58, "handle"),
    ("target_ptr", 0xBC, "pointer"),
    ("formation_ptr", 0xC8, "pointer"),
    ("pursuit_level", 0xE0, "integer"),
    ("base_heat", 0xE4, "float"),
    ("maximum_heat", 0xE8, "float"),
    ("heat_scale", 0xEC, "float"),
    ("in_formation_timer", 0xF4, "float"),
    ("breaker_timer", 0xF8, "float"),
    ("total_pursuit_time", 0xFC, "float"),
    ("formation_attempt_count", 0x104, "integer"),
    ("active_formation", 0x108, "integer"),
    ("active_formation_time", 0x10C, "float"),
    ("busted_timer", 0x124, "float"),
    ("cooldown_time_remaining", 0x148, "float"),
    ("pursuit_meter", 0x158, "float"),
)


class DecodeError(RuntimeError):
    pass


def u32_float(value: int) -> float | None:
    result = struct.unpack("<f", struct.pack("<I", value))[0]
    return result if math.isfinite(result) else None


def decode_c_string(raw: bytes) -> str:
    return raw.split(b"\0", 1)[0].decode("utf-8", errors="replace")


def decode_schema3_text(raw: bytes, *, field: str, required: bool = True) -> str:
    try:
        terminator = raw.index(0)
    except ValueError as exc:
        raise DecodeError(f"{field}: missing NUL terminator") from exc
    if any(raw[terminator + 1 :]):
        raise DecodeError(f"{field}: nonzero bytes after NUL terminator")
    value_raw = raw[:terminator]
    if required and not value_raw:
        raise DecodeError(f"{field}: missing text")
    if any(byte < 0x20 or byte > 0x7E for byte in value_raw):
        raise DecodeError(f"{field}: text is not printable ASCII")
    return value_raw.decode("ascii")


def read_exact(stream: BinaryIO, size: int, *, label: str) -> bytes:
    raw = stream.read(size)
    if len(raw) != size:
        raise DecodeError(f"truncated {label}: expected {size}, got {len(raw)}")
    return raw


def read_legacy_header(raw: bytes) -> dict[str, object]:
    unpacked = HEADER.unpack(raw)
    magic, schema, header_size, record_size = unpacked[:4]
    if magic != b"NMWAITR1":
        raise DecodeError(f"bad capture magic {magic!r}")
    if schema not in {1, 2} or header_size != HEADER_SIZE or record_size != EVENT_SIZE:
        raise DecodeError(
            f"unsupported format schema/header/record {schema}/{header_size}/{record_size}"
        )
    filetime = unpacked[9] | (unpacked[10] << 32)
    return {
        "schema_version": schema,
        "header_size": header_size,
        "record_size": record_size,
        "target_sha256": unpacked[4].hex(),
        "preferred_base": f"0x{unpacked[5]:08x}",
        "image_base": f"0x{unpacked[6]:08x}",
        "image_size": unpacked[7],
        "process_id": unpacked[8],
        "started_filetime_utc": filetime,
        "qpc_frequency": unpacked[11],
        "hook_count": unpacked[12],
        "flags": unpacked[13],
        "tracer_version": decode_c_string(unpacked[14]),
        "target_name": decode_c_string(unpacked[15]),
    }


def schema3_argument_names(catalogue: dict[str, object] | None, hook_id: int, arg_count: int) -> list[str]:
    if catalogue is not None:
        hooks = catalogue.get("hooks")
        if isinstance(hooks, list) and 0 < hook_id <= len(hooks):
            hook = hooks[hook_id - 1]
            if isinstance(hook, dict) and int(hook.get("_id", 0)) == hook_id:
                args = hook.get("args")
                if isinstance(args, list) and len(args) == arg_count:
                    names = [arg.get("name") for arg in args if isinstance(arg, dict)]
                    if len(names) == arg_count and all(isinstance(name, str) and name for name in names):
                        return [str(name) for name in names]
    return [f"arg{index}" for index in range(arg_count)]


def decode_schema3_manifest_entry(
    raw: bytes,
    *,
    slot: int,
    preferred_base: int,
    local_catalogue: dict[str, object] | None,
) -> dict[str, object]:
    fields = SCHEMA3_MANIFEST_PREFIX.unpack_from(raw)
    (
        hook_id,
        address,
        rva,
        profile_mask,
        enabled,
        divisor,
        arg_count,
        this_adjust,
        flags,
        return_kind,
        arg_type_bits,
        relocation_kind,
        relocation_offset,
        relocation_target,
        stolen_len,
        instruction_count,
        stolen_raw,
        instruction_lengths_raw,
        name_raw,
        config_key_raw,
        recovery_raw,
    ) = fields
    if hook_id != slot + 1:
        raise DecodeError(f"manifest slot {slot}: hook id {hook_id} is not {slot + 1}")
    if address != (preferred_base + rva) & 0xFFFFFFFF:
        raise DecodeError(f"manifest hook {hook_id}: address/RVA disagree")
    if (
        not preferred_base <= address
        or address % 8 != 0
        or address + max(stolen_len, 8) > preferred_base + 0x00678E4E
    ):
        raise DecodeError(f"manifest hook {hook_id}: anchor or atomic patch is outside the exact target contract")
    if enabled not in {0, 1} or (enabled == 1 and divisor == 0) or (enabled == 0 and divisor != 0):
        raise DecodeError(f"manifest hook {hook_id}: enabled/divisor state is inconsistent")
    if divisor > 1_000_000_000:
        raise DecodeError(f"manifest hook {hook_id}: sample divisor is outside the supported range")
    if (
        arg_count > 8
        or stolen_len < 5
        or stolen_len > 16
        or instruction_count == 0
        or instruction_count > 8
        or flags & ~SCHEMA3_HOOK_FLAG_MASK
        or return_kind not in {0, 1, 2, 3, 4}
        or profile_mask == 0
    ):
        raise DecodeError(f"manifest hook {hook_id}: invalid argument or instruction bounds")
    if arg_type_bits >> (arg_count * 2):
        raise DecodeError(f"manifest hook {hook_id}: argument type bits exceed declared arguments")
    if flags & 1 and this_adjust != 0:
        raise DecodeError(f"manifest hook {hook_id}: static hook has a this adjustment")
    stolen = stolen_raw[:stolen_len]
    instruction_lengths = list(instruction_lengths_raw[:instruction_count])
    if any(length == 0 or length > 15 for length in instruction_lengths) or sum(instruction_lengths) != stolen_len:
        raise DecodeError(f"manifest hook {hook_id}: stolen bytes do not match instruction boundaries")
    if any(stolen_raw[stolen_len:]) or any(instruction_lengths_raw[instruction_count:]):
        raise DecodeError(f"manifest hook {hook_id}: nonzero unused instruction storage")
    if relocation_kind == 0:
        if relocation_offset != 0 or relocation_target != 0:
            raise DecodeError(f"manifest hook {hook_id}: unexpected relocation payload")
        relocation: dict[str, object] | None = None
    elif relocation_kind == 1:
        boundaries: set[int] = set()
        cursor = 0
        for length in instruction_lengths:
            boundaries.add(cursor)
            cursor += length
        if (
            relocation_offset not in boundaries
            or relocation_offset + 5 > stolen_len
            or stolen[relocation_offset] != 0xE8
        ):
            raise DecodeError(f"manifest hook {hook_id}: invalid call_rel32 relocation")
        displacement = struct.unpack_from("<i", stolen, relocation_offset + 1)[0]
        resolved_target = (address + relocation_offset + 5 + displacement) & 0xFFFFFFFF
        if resolved_target != relocation_target:
            raise DecodeError(f"manifest hook {hook_id}: relocation target does not match stolen call")
        if not preferred_base <= relocation_target < preferred_base + 0x00678E4E:
            raise DecodeError(f"manifest hook {hook_id}: relocation target is outside the exact target image")
        relocation = {
            "kind": "call_rel32",
            "offset": relocation_offset,
            "target": f"0x{relocation_target:08x}",
        }
    else:
        raise DecodeError(f"manifest hook {hook_id}: unsupported relocation kind {relocation_kind}")
    relative_instructions: list[tuple[int, str]] = []
    cursor = 0
    for length in instruction_lengths:
        opcode = stolen[cursor]
        if opcode in {0xE8, 0xE9}:
            relative_instructions.append((cursor, "call_rel32" if opcode == 0xE8 else "unsupported"))
        elif opcode == 0xEB or 0x70 <= opcode <= 0x7F or 0xE0 <= opcode <= 0xE3:
            relative_instructions.append((cursor, "unsupported"))
        elif opcode == 0x0F and length >= 2 and 0x80 <= stolen[cursor + 1] <= 0x8F:
            relative_instructions.append((cursor, "unsupported"))
        cursor += length
    declared_relative = [] if relocation is None else [(int(relocation["offset"]), str(relocation["kind"]))]
    if relative_instructions != declared_relative:
        raise DecodeError(f"manifest hook {hook_id}: relative instruction relocation coverage is incomplete")
    regions: list[dict[str, object]] = []
    at = SCHEMA3_MANIFEST_PREFIX.size
    for region_index in range(SCHEMA3_REGION_COUNT):
        source, offset, length, phase_mask, label_raw = SCHEMA3_MANIFEST_REGION.unpack_from(raw, at)
        at += SCHEMA3_MANIFEST_REGION.size
        if source not in SCHEMA3_REGION_SOURCES or length > SCHEMA3_REGION_BYTES or phase_mask not in {0, 1}:
            raise DecodeError(f"manifest hook {hook_id} region {region_index}: invalid bounds or source")
        if (length == 0) != (source == 0) or phase_mask != (1 if length else 0):
            raise DecodeError(f"manifest hook {hook_id} region {region_index}: empty/phase state is inconsistent")
        label = decode_schema3_text(
            label_raw,
            field=f"manifest hook {hook_id} region {region_index} label",
            required=bool(length),
        )
        if not length and (offset != 0 or label):
            raise DecodeError(f"manifest hook {hook_id} region {region_index}: empty region has payload")
        if 16 <= source <= 23 and source - 16 >= arg_count:
            raise DecodeError(f"manifest hook {hook_id} region {region_index}: source exceeds declared arguments")
        if source == 1 and flags & 1:
            raise DecodeError(f"manifest hook {hook_id} region {region_index}: static hook uses normalized this")
        if source == 32:
            absolute = offset & 0xFFFFFFFF
            if not preferred_base <= absolute or absolute + length > preferred_base + 0x00678E4E:
                raise DecodeError(f"manifest hook {hook_id} region {region_index}: absolute source is outside image")
        regions.append(
            {
                "index": region_index,
                "source_code": source,
                "source": SCHEMA3_REGION_SOURCES[source],
                "offset": offset,
                "length": length,
                "phase_mask": phase_mask,
                "label": label,
            }
        )
    name = decode_schema3_text(name_raw, field=f"manifest hook {hook_id} name")
    config_key = decode_schema3_text(config_key_raw, field=f"manifest hook {hook_id} config key")
    recovery = decode_schema3_text(recovery_raw, field=f"manifest hook {hook_id} recovery")
    if recovery not in SCHEMA3_RECOVERY_CLASSES:
        raise DecodeError(f"manifest hook {hook_id}: unsupported recovery class {recovery!r}")
    arg_names = schema3_argument_names(local_catalogue, hook_id, arg_count)
    arguments = [
        {
            "index": index,
            "name": arg_names[index],
            "type": SCHEMA3_ARG_TYPES[(arg_type_bits >> (index * 2)) & 0x3],
        }
        for index in range(arg_count)
    ]
    return {
        "id": hook_id,
        "name": name,
        "address": f"0x{address:08x}",
        "address_value": address,
        "rva": f"0x{rva:08x}",
        "rva_value": rva,
        "profile_mask": f"0x{profile_mask:08x}",
        "enabled": bool(enabled),
        "divisor": divisor,
        "arguments": arguments,
        "arg_count": arg_count,
        "this_adjust": this_adjust,
        "flags": flags,
        "static": bool(flags & 1),
        "return_kind": return_kind,
        "arg_type_bits": f"0x{arg_type_bits:08x}",
        "relocation": relocation,
        "stolen": stolen.hex(),
        "instruction_lengths": instruction_lengths,
        "config_key": config_key,
        "recovery": recovery,
        "regions": regions,
    }


def validate_schema3_manifest_against_catalogue(
    manifest: list[dict[str, object]], catalogue: Mapping[str, object]
) -> None:
    hooks = catalogue.get("hooks")
    if not isinstance(hooks, list) or len(hooks) != len(manifest):
        raise DecodeError("matching local catalogue has an incompatible hook count")
    for actual, expected in zip(manifest, hooks, strict=True):
        if not isinstance(expected, dict):
            raise DecodeError("matching local catalogue contains an invalid hook")
        expected_fields = {
            "id": int(expected["_id"]),
            "name": str(expected["_name"]),
            "address_value": int(expected["_address"]),
            "rva_value": int(expected["_rva"]),
            "profile_mask": f"0x{int(expected['_profile_mask']):08x}",
            "arg_count": len(expected["args"]),
            "this_adjust": int(expected["_this_adjust"]),
            "flags": int(expected["_flags"]),
            "return_kind": int(expected["_return_code"]),
            "arg_type_bits": f"0x{int(expected['_arg_type_bits']):08x}",
            "stolen": bytes(expected["_stolen"]).hex(),
            "instruction_lengths": list(expected["_instruction_lengths"]),
            "config_key": str(expected["_config_key"]),
            "recovery": str(expected["_recovery"]),
        }
        for field, expected_value in expected_fields.items():
            if actual[field] != expected_value:
                raise DecodeError(
                    f"manifest hook {actual['id']}: {field} disagrees with its matching local catalogue"
                )
        expected_relocation: dict[str, object] | None = None
        if int(expected["_relocation_code"]) == 1:
            expected_relocation = {
                "kind": "call_rel32",
                "offset": int(expected["_relocation_offset"]),
                "target": f"0x{int(expected['_relocation_target']):08x}",
            }
        if actual["relocation"] != expected_relocation:
            raise DecodeError(f"manifest hook {actual['id']}: relocation disagrees with its matching local catalogue")
        expected_regions = list(expected["_regions"])
        while len(expected_regions) < SCHEMA3_REGION_COUNT:
            expected_regions.append({"source_code": 0, "offset": 0, "length": 0, "label": ""})
        for actual_region, expected_region in zip(actual["regions"], expected_regions, strict=True):
            expected_region_fields = {
                "source_code": int(expected_region["source_code"]),
                "offset": int(expected_region["offset"]),
                "length": int(expected_region["length"]),
                "label": str(expected_region["label"]),
            }
            for field, expected_value in expected_region_fields.items():
                if actual_region[field] != expected_value:
                    raise DecodeError(
                        f"manifest hook {actual['id']} region {actual_region['index']}: {field} disagrees with its matching local catalogue"
                    )


def _read_manifest_header(
    raw: bytes,
    *,
    expected_magic: bytes,
    expected_schema: int,
    expected_header_size: int,
    expected_hook_count: int,
    profile_names: Mapping[int, str],
) -> dict[str, object]:
    schema_label = f"schema-{expected_schema}"
    core = SCHEMA3_HEADER_CORE.unpack_from(raw)
    magic, schema, header_size, record_size, manifest_entry_size = core[:5]
    if (
        magic != expected_magic
        or schema != expected_schema
        or header_size != expected_header_size
        or record_size != SCHEMA3_EVENT_SIZE
        or manifest_entry_size != SCHEMA3_MANIFEST_SIZE
        or len(raw) != expected_header_size
    ):
        raise DecodeError(
            f"unsupported {schema_label} format {magic!r}/{schema}/{header_size}/{record_size}/{manifest_entry_size}"
        )
    header_core_sha256 = core[33]
    digest_input = bytearray(raw[:SCHEMA3_HEADER_CORE_SIZE])
    digest_input[SCHEMA3_HEADER_DIGEST_OFFSET : SCHEMA3_HEADER_DIGEST_OFFSET + 32] = bytes(32)
    actual_header_core_sha256 = hashlib.sha256(digest_input).digest()
    if actual_header_core_sha256 != header_core_sha256:
        raise DecodeError(
            f"{schema_label} header-core SHA-256 {actual_header_core_sha256.hex()} != header {header_core_sha256.hex()}"
        )
    target_sha256 = core[5].hex()
    if target_sha256 != EXPECTED_TARGET_SHA256:
        raise DecodeError(f"{schema_label} capture target SHA-256 {target_sha256} is not the supported PC 1.3 target")
    hook_count = int(core[17])
    enabled_hook_count = int(core[18])
    profile_id = int(core[19])
    flags = int(core[20])
    state = int(core[28])
    if hook_count != expected_hook_count or enabled_hook_count == 0 or enabled_hook_count > hook_count:
        raise DecodeError(f"{schema_label} manifest count {hook_count}/{enabled_hook_count} is invalid")
    if state not in SCHEMA3_ACTIVATION_STATES:
        raise DecodeError(f"{schema_label} activation state {state} is invalid")
    if flags != SCHEMA3_HEADER_FLAG_MASK:
        raise DecodeError(f"{schema_label} header flags 0x{flags:08x} do not match the entry-only contract")
    preferred_base = int(core[9])
    image_base = int(core[10])
    image_size = int(core[11])
    if preferred_base != 0x00400000 or image_size != 0x00678E4E:
        raise DecodeError(f"{schema_label} target image identity is inconsistent")
    if image_base == 0 or image_base + image_size > 0x1_0000_0000:
        raise DecodeError(f"{schema_label} runtime image range is outside the 32-bit address space")
    if profile_id == 0 or int(core[21]) == 0 or int(core[22]) == 0 or int(core[25]) == 0:
        raise DecodeError(f"{schema_label} profile or runtime budgets are zero")
    if int(core[26]) == 0 or int(core[27]) == 0:
        raise DecodeError(f"{schema_label} checkpoint or flush interval is zero")
    if int(core[23]) != SCHEMA3_REGION_COUNT or int(core[24]) != SCHEMA3_REGION_BYTES:
        raise DecodeError(f"{schema_label} region dimensions are unsupported")
    if not all(any(digest) for digest in (core[6], core[7], core[8])) or not any(core[16]):
        raise DecodeError(f"{schema_label} identity hashes or capture id are zero")
    if any(core[34]):
        raise DecodeError(f"{schema_label} header reserved bytes are nonzero")
    tracer_version = decode_schema3_text(core[29], field=f"{schema_label} tracer version")
    target_name = decode_schema3_text(core[30], field=f"{schema_label} target name")
    profile_name = decode_schema3_text(core[31], field=f"{schema_label} profile name")
    config_name = decode_schema3_text(core[32], field=f"{schema_label} config name")
    if profile_names.get(profile_id) != profile_name:
        raise DecodeError(f"{schema_label} profile identity {profile_id}/{profile_name!r} is unsupported")
    manifest_raw = raw[SCHEMA3_HEADER_CORE_SIZE:]
    actual_manifest_sha256 = hashlib.sha256(manifest_raw).digest()
    if actual_manifest_sha256 != core[8]:
        raise DecodeError(
            f"{schema_label} manifest SHA-256 {actual_manifest_sha256.hex()} != header {core[8].hex()}"
        )
    local_catalogue: dict[str, object] | None = None
    local_catalogue_error: str | None = None
    try:
        candidate = load_catalogue()
        if bytes(candidate["_raw_sha256"]) == core[7]:
            local_catalogue = candidate
    except (CatalogueError, OSError, UnicodeError) as exc:
        local_catalogue_error = str(exc)
    manifest: list[dict[str, object]] = []
    for slot in range(hook_count):
        at = SCHEMA3_HEADER_CORE_SIZE + slot * SCHEMA3_MANIFEST_SIZE
        manifest.append(
            decode_schema3_manifest_entry(
                raw[at : at + SCHEMA3_MANIFEST_SIZE],
                slot=slot,
                preferred_base=preferred_base,
                local_catalogue=local_catalogue,
            )
        )
    padding_at = SCHEMA3_HEADER_CORE_SIZE + hook_count * SCHEMA3_MANIFEST_SIZE
    if any(raw[padding_at:]):
        raise DecodeError(f"{schema_label} manifest padding is nonzero")
    if sum(1 for hook in manifest if hook["enabled"]) != enabled_hook_count:
        raise DecodeError(f"{schema_label} enabled hook count disagrees with the manifest")
    if local_catalogue is not None:
        validate_schema3_manifest_against_catalogue(manifest, local_catalogue)
        profiles = local_catalogue.get("profiles")
        matching_profiles = [
            profile
            for profile in profiles if isinstance(profile, dict) and int(profile.get("_id", 0)) == profile_id
        ] if isinstance(profiles, list) else []
        if len(matching_profiles) != 1 or matching_profiles[0].get("name") != profile_name:
            raise DecodeError(f"{schema_label} profile identity disagrees with its matching local catalogue")
    flag_names = [name for bit, name in {1: "entry_only", 2: "fxsave", 4: "profile_preset", 8: "ordered_fifo"}.items() if flags & bit]
    header: dict[str, object] = {
        "schema_version": schema,
        "header_size": header_size,
        "record_size": record_size,
        "manifest_entry_size": manifest_entry_size,
        "target_sha256": target_sha256,
        "config_sha256": core[6].hex(),
        "catalogue_sha256": core[7].hex(),
        "manifest_sha256": core[8].hex(),
        "header_core_sha256": header_core_sha256.hex(),
        "preferred_base": f"0x{preferred_base:08x}",
        "image_base": f"0x{image_base:08x}",
        "image_size": image_size,
        "process_id": int(core[12]),
        "started_filetime_utc": int(core[13]),
        "qpc_frequency": int(core[14]),
        "started_qpc": int(core[15]),
        "capture_id": core[16].hex(),
        "hook_count": hook_count,
        "enabled_hook_count": enabled_hook_count,
        "profile_id": profile_id,
        "flags": flags,
        "flag_names": flag_names,
        "max_data_records": int(core[21]),
        "queue_capacity": int(core[22]),
        "region_count": int(core[23]),
        "region_bytes": int(core[24]),
        "read_call_budget": int(core[25]),
        "checkpoint_interval": int(core[26]),
        "flush_interval_ms": int(core[27]),
        "activation_state": SCHEMA3_ACTIVATION_STATES[state],
        "activation_state_code": state,
        "tracer_version": tracer_version,
        "target_name": target_name,
        "profile_name": profile_name,
        "config_name": config_name,
        "catalogue_matches_local": local_catalogue is not None,
        "argument_labels_source": "matching local catalogue" if local_catalogue is not None else "ordinal manifest slots",
        "manifest": manifest,
    }
    if local_catalogue_error is not None:
        header["local_catalogue_error"] = local_catalogue_error
    return header


def read_schema3_header(raw: bytes) -> dict[str, object]:
    return _read_manifest_header(
        raw,
        expected_magic=b"NMWAITR3",
        expected_schema=3,
        expected_header_size=SCHEMA3_HEADER_SIZE,
        expected_hook_count=48,
        profile_names=SCHEMA3_PROFILE_NAMES,
    )


def read_schema4_header(raw: bytes) -> dict[str, object]:
    return _read_manifest_header(
        raw,
        expected_magic=b"NMWAITR4",
        expected_schema=4,
        expected_header_size=SCHEMA4_HEADER_SIZE,
        expected_hook_count=64,
        profile_names=SCHEMA4_PROFILE_NAMES,
    )


def read_header(stream: BinaryIO) -> dict[str, object]:
    magic = read_exact(stream, 8, label="header magic")
    if magic == b"NMWAITR1":
        return read_legacy_header(magic + read_exact(stream, HEADER_SIZE - 8, label="legacy header"))
    if magic == b"NMWAITR3":
        return read_schema3_header(magic + read_exact(stream, SCHEMA3_HEADER_SIZE - 8, label="schema-3 header"))
    if magic == b"NMWAITR4":
        return read_schema4_header(magic + read_exact(stream, SCHEMA4_HEADER_SIZE - 8, label="schema-4 header"))
    raise DecodeError(f"bad capture magic {magic!r}")


def named_args(hook_id: int, args: tuple[int, ...]) -> dict[str, object]:
    labels = HOOKS.get(hook_id, ("unknown", ()))[1]
    output: dict[str, object] = {}
    for index, label in enumerate(labels):
        value = args[index]
        if index in FLOAT_ARGS.get(hook_id, set()):
            output[label] = u32_float(value)
            output[f"{label}_bits"] = f"0x{value:08x}"
        elif "ptr" in label:
            output[label] = f"0x{value:08x}"
        else:
            output[label] = value
    return output


def format_typed_value(label: str, value: int, kind: str) -> dict[str, object]:
    if kind == "float":
        return {label: u32_float(value), f"{label}_bits": f"0x{value:08x}"}
    if kind == "pointer":
        return {label: f"0x{value:08x}"}
    if kind == "crc":
        return {label: f"0x{value:08x}"}
    return {label: value}


def named_values_v2(hook_id: int, values: tuple[int, ...]) -> dict[str, object]:
    if hook_id in {1, 2}:
        labels, float_indices = VEHICLE_VALUES, FLOAT_VEHICLE
    elif hook_id == 3:
        labels, float_indices = GOAL_VALUES, set()
    elif hook_id in {4, 5}:
        labels, float_indices = SPAWN_VALUES, FLOAT_SPAWN
    elif hook_id in {7, 9}:
        labels, float_indices = PURSUIT_VALUES, FLOAT_PURSUIT
    else:
        labels, float_indices = (), set()
    output: dict[str, object] = {}
    for index, label in enumerate(labels):
        value = values[index]
        if index in float_indices:
            output.update(format_typed_value(label, value, "float"))
        elif label.endswith("_ptr"):
            output.update(format_typed_value(label, value, "pointer"))
        elif label.endswith("crc"):
            output.update(format_typed_value(label, value, "crc"))
        else:
            output.update(format_typed_value(label, value, "integer"))
    return output


def object_field_values(
    raw: bytes,
    object_size: int,
    fields: tuple[tuple[str, int, str], ...],
    *,
    snapshot_complete_base_offset: int,
) -> dict[str, object]:
    output: dict[str, object] = {}
    readable = min(object_size, len(raw))
    for label, complete_offset, kind in fields:
        snapshot_offset = complete_offset - snapshot_complete_base_offset
        if snapshot_offset < 0 or snapshot_offset + 4 > readable:
            continue
        value = struct.unpack_from("<I", raw, snapshot_offset)[0]
        output.update(format_typed_value(label, value, kind))
    return output


def validated_schema1_values(
    hook_id: int,
    values: tuple[int, ...],
    object_bytes: bytes,
    object_size: int,
) -> dict[str, object]:
    # Schema 1 captured raw task-hook `this` snapshots. For hooks 2 and 9 the
    # snapshot therefore begins eight bytes into the complete object.
    snapshot_complete_base_offset = 8 if hook_id in HOOK_THIS_TO_COMPLETE_BASE else 0
    if hook_id in {1, 2}:
        return object_field_values(
            object_bytes,
            object_size,
            VEHICLE_OBJECT_FIELDS,
            snapshot_complete_base_offset=snapshot_complete_base_offset,
        )
    if hook_id == 3:
        return named_values_v2(hook_id, values)
    if hook_id in {4, 5}:
        return object_field_values(
            object_bytes,
            object_size,
            SPAWN_OBJECT_FIELDS,
            snapshot_complete_base_offset=snapshot_complete_base_offset,
        )
    if hook_id in {7, 9}:
        return object_field_values(
            object_bytes,
            object_size,
            PURSUIT_OBJECT_FIELDS,
            snapshot_complete_base_offset=snapshot_complete_base_offset,
        )
    return {}


def decode_pointees(hook_id: int, sizes: tuple[int, ...], raw: tuple[bytes, ...]) -> dict[str, object]:
    output: dict[str, object] = {}
    if hook_id == 4 and sizes[0] >= 12:
        output["check_position"] = list(struct.unpack_from("<fff", raw[0]))
    elif hook_id == 5:
        if sizes[0] >= 2:
            output["segment"] = struct.unpack_from("<h", raw[0])[0]
        if sizes[1] >= 1:
            output["lane"] = struct.unpack_from("<b", raw[1])[0]
        if sizes[2] >= 4:
            output["time_step"] = struct.unpack_from("<f", raw[2])[0]
    elif hook_id == 6:
        output["pursuit_head"] = raw[0][: sizes[0]].hex()
        output["vehicle_head"] = raw[1][: sizes[1]].hex()
    return output


def schema3_add_offset(base: int, offset: int) -> int | None:
    value = base + offset
    return value if 0 < value <= 0xFFFFFFFF else None


def schema3_expected_region_address(
    region: Mapping[str, object],
    *,
    raw_this: int,
    normalized_this: int,
    normalized_valid: bool,
    args: tuple[int, ...],
    arg_valid_mask: int,
    image_base: int,
    preferred_base: int,
) -> int | None:
    source = int(region["source_code"])
    offset = int(region["offset"])
    if source == 1:
        return schema3_add_offset(normalized_this, offset) if normalized_valid else None
    if source == 2:
        return schema3_add_offset(raw_this, offset)
    if 16 <= source <= 23:
        argument_index = source - 16
        if not arg_valid_mask & (1 << argument_index):
            return None
        return schema3_add_offset(args[argument_index], offset)
    if source == 32:
        preferred_address = offset & 0xFFFFFFFF
        if not preferred_base <= preferred_address < preferred_base + 0x00678E4E:
            return None
        return image_base + (preferred_address - preferred_base)
    return None


def schema3_argument_value(value: int, arg_type: str) -> object:
    if arg_type == "float":
        return u32_float(value)
    if arg_type == "pointer":
        return f"0x{value:08x}"
    return value


def decode_schema3_event(
    raw: bytes,
    *,
    schema_version: int = 3,
    manifest: list[dict[str, object]] | None,
    image_base: int,
    preferred_base: int,
    read_call_budget: int,
    queue_capacity: int = 0xFFFFFFFF,
    max_data_records: int = 0xFFFFFFFFFFFFFFFF,
    include_region_data: bool = True,
) -> dict[str, object]:
    expected_hook_count = {3: 48, 4: 64}.get(schema_version)
    if expected_hook_count is None:
        raise DecodeError(f"unsupported manifest event schema {schema_version}")
    if len(raw) != SCHEMA3_EVENT_SIZE:
        raise DecodeError(f"truncated schema-3 event: expected {SCHEMA3_EVENT_SIZE}, got {len(raw)}")
    if manifest is None or len(manifest) != expected_hook_count:
        raise DecodeError(
            f"schema-{schema_version} event decoding requires its validated {expected_hook_count}-hook manifest"
        )
    fields = SCHEMA3_EVENT_PREFIX.unpack_from(raw)
    magic, size, schema, kind_code = fields[:4]
    if magic != SCHEMA3_EVENT_MAGIC or size != SCHEMA3_EVENT_SIZE or schema != schema_version or kind_code not in SCHEMA3_EVENT_KINDS:
        raise DecodeError(f"invalid schema-3 event marker/size/schema/kind at sequence candidate {fields[4:5]}")
    sequence, qpc, call_index, sample_index = (int(value) for value in fields[4:8])
    (
        tick_ms,
        thread_id,
        hook_id,
        image_address,
        raw_this,
        normalized_this,
        return_address,
        eax,
        edx,
        eflags,
        arg_valid_mask,
    ) = (int(value) for value in fields[8:19])
    args_raw = tuple(int(value) for value in fields[19:27])
    region_addresses = tuple(int(value) for value in fields[27:30])
    region_valid_masks = tuple(int(value) for value in fields[30:33])
    region_requested = tuple(int(value) for value in fields[33:36])
    region_captured = tuple(int(value) for value in fields[36:39])
    event_flags = int(fields[39])
    read_calls_used = int(fields[40])
    counter_values = tuple(int(value) for value in fields[41:49])
    queue_high_water = int(fields[49])
    terminal_reason_code = int(fields[50])
    record_sha256_128 = fields[51]
    reserved = fields[52]
    digest_input = bytearray(raw)
    digest_input[SCHEMA3_EVENT_DIGEST_OFFSET : SCHEMA3_EVENT_DIGEST_OFFSET + 16] = bytes(16)
    actual_record_sha256_128 = hashlib.sha256(digest_input).digest()[:16]
    if actual_record_sha256_128 != record_sha256_128:
        raise DecodeError(
            f"schema-3 event sequence {sequence}: record SHA-256/128 "
            f"{actual_record_sha256_128.hex()} != stored {record_sha256_128.hex()}"
        )
    if sequence == 0 or any(reserved):
        raise DecodeError(f"schema-3 event sequence {sequence}: zero sequence or nonzero reserved bytes")
    event_flag_names = SCHEMA4_EVENT_FLAG_NAMES if schema_version == 4 else SCHEMA3_EVENT_FLAG_NAMES
    event_flag_mask = SCHEMA4_EVENT_FLAG_MASK if schema_version == 4 else SCHEMA3_EVENT_FLAG_MASK
    if event_flags & ~event_flag_mask:
        raise DecodeError(f"schema-3 event sequence {sequence}: unknown event flags 0x{event_flags:08x}")
    if read_calls_used > read_call_budget:
        raise DecodeError(f"schema-3 event sequence {sequence}: read-call budget exceeded without enforcement")
    if event_flags & 8 and read_calls_used != read_call_budget:
        raise DecodeError(f"schema-3 event sequence {sequence}: read-budget flag lacks a saturated budget")
    kind = SCHEMA3_EVENT_KINDS[kind_code]
    if terminal_reason_code not in SCHEMA3_TERMINAL_REASONS:
        raise DecodeError(f"schema-3 event sequence {sequence}: unknown terminal reason {terminal_reason_code}")
    if kind == "terminal":
        if terminal_reason_code == 0:
            raise DecodeError(f"schema-3 terminal sequence {sequence}: missing terminal reason")
    elif terminal_reason_code != 0:
        raise DecodeError(f"schema-3 nonterminal sequence {sequence}: unexpected terminal reason")
    hook: dict[str, object] | None = None
    if kind == "entry":
        if not 0 < hook_id <= len(manifest):
            raise DecodeError(f"schema-3 entry sequence {sequence}: invalid hook id {hook_id}")
        hook = manifest[hook_id - 1]
        if not hook["enabled"]:
            raise DecodeError(f"schema-3 entry sequence {sequence}: disabled hook {hook_id} emitted an event")
        if image_address != int(hook["address_value"]):
            raise DecodeError(f"schema-3 entry sequence {sequence}: image address disagrees with manifest")
        divisor = int(hook["divisor"])
        if call_index == 0 or sample_index == 0 or ((call_index - 1) % divisor) != 0:
            raise DecodeError(f"schema-3 entry sequence {sequence}: invalid call/sample index")
        expected_sample_index = ((call_index - 1) // divisor) + 1
        if sample_index != expected_sample_index:
            raise DecodeError(f"schema-3 entry sequence {sequence}: sample index {sample_index} != {expected_sample_index}")
        arg_count = int(hook["arg_count"])
        valid_arg_bits = (1 << arg_count) - 1 if arg_count else 0
        if arg_valid_mask & ~valid_arg_bits or any(args_raw[index] for index in range(arg_count, 8)):
            raise DecodeError(f"schema-3 entry sequence {sequence}: argument payload exceeds declared ABI")
        if any(args_raw[index] for index in range(arg_count) if not arg_valid_mask & (1 << index)):
            raise DecodeError(f"schema-3 entry sequence {sequence}: invalid argument slot is nonzero")
        if eflags & 2 == 0:
            raise DecodeError(f"schema-3 entry sequence {sequence}: EFLAGS reserved bit is clear")
    else:
        if any((hook_id, image_address, raw_this, normalized_this, return_address, eax, edx, eflags, arg_valid_mask)):
            raise DecodeError(f"schema-3 control sequence {sequence}: hook payload is nonzero")
        if call_index != 0 or sample_index != 0 or any(args_raw):
            raise DecodeError(f"schema-3 control sequence {sequence}: call payload is nonzero")
        if event_flags != 0 or read_calls_used != 0:
            raise DecodeError(f"schema-3 control sequence {sequence}: event flags or read count are nonzero")
    normalized_valid = bool(event_flags & 2)
    return_address_valid = bool(event_flags & 1)
    if return_address_valid != (return_address != 0):
        raise DecodeError(f"schema-3 event sequence {sequence}: return-address validity is inconsistent")
    if hook is None:
        expected_normalized = None
    elif bool(hook["static"]):
        expected_normalized = None
    else:
        expected_normalized = schema3_add_offset(raw_this, int(hook["this_adjust"]))
    if normalized_valid != (expected_normalized is not None) or (
        expected_normalized is not None and normalized_this != expected_normalized
    ) or (expected_normalized is None and normalized_this != 0):
        raise DecodeError(f"schema-3 event sequence {sequence}: normalized pointer is inconsistent")
    argument_values: dict[str, object] = {}
    argument_bits: dict[str, str] = {}
    invalid_arguments: list[str] = []
    if hook is not None:
        for argument in hook["arguments"]:
            index = int(argument["index"])
            name = str(argument["name"])
            argument_bits[name] = f"0x{args_raw[index]:08x}"
            if arg_valid_mask & (1 << index):
                argument_values[name] = schema3_argument_value(args_raw[index], str(argument["type"]))
            else:
                invalid_arguments.append(name)
    decoded_regions: list[dict[str, object]] = []
    any_region_partial = False
    for region_index in range(SCHEMA3_REGION_COUNT):
        manifest_region: Mapping[str, object]
        if hook is None:
            manifest_region = {
                "source_code": 0,
                "source": "none",
                "offset": 0,
                "length": 0,
                "label": "",
            }
        else:
            manifest_region = hook["regions"][region_index]
        requested = region_requested[region_index]
        captured = region_captured[region_index]
        valid_mask = region_valid_masks[region_index]
        expected_requested = int(manifest_region["length"])
        if requested != expected_requested or captured > requested:
            raise DecodeError(f"schema-3 event sequence {sequence} region {region_index}: invalid byte counts")
        block_count = (requested + 15) // 16
        allowed_valid_mask = (1 << block_count) - 1 if block_count else 0
        if valid_mask & ~allowed_valid_mask or (captured == 0 and valid_mask != 0):
            raise DecodeError(f"schema-3 event sequence {sequence} region {region_index}: invalid validity mask")
        valid_byte_count = sum(
            min(16, requested - block * 16)
            for block in range(block_count)
            if valid_mask & (1 << block)
        )
        if captured < valid_byte_count:
            raise DecodeError(
                f"schema-3 event sequence {sequence} region {region_index}: captured count is smaller than valid blocks"
            )
        expected_address = schema3_expected_region_address(
            manifest_region,
            raw_this=raw_this,
            normalized_this=normalized_this,
            normalized_valid=normalized_valid,
            args=args_raw,
            arg_valid_mask=arg_valid_mask,
            image_base=image_base,
            preferred_base=preferred_base,
        )
        actual_address = region_addresses[region_index]
        if expected_address is None:
            if actual_address != 0 or captured != 0 or valid_mask != 0:
                raise DecodeError(f"schema-3 event sequence {sequence} region {region_index}: unresolved source has data")
        elif actual_address != expected_address:
            raise DecodeError(f"schema-3 event sequence {sequence} region {region_index}: address disagrees with source")
        region_raw = raw[
            SCHEMA3_EVENT_PREFIX.size + region_index * SCHEMA3_REGION_BYTES :
            SCHEMA3_EVENT_PREFIX.size + (region_index + 1) * SCHEMA3_REGION_BYTES
        ]
        if any(region_raw[requested:]):
            raise DecodeError(f"schema-3 event sequence {sequence} region {region_index}: bytes beyond request are nonzero")
        full_valid_mask = allowed_valid_mask
        if captured == requested and valid_mask != full_valid_mask:
            raise DecodeError(f"schema-3 event sequence {sequence} region {region_index}: complete capture lacks valid blocks")
        partial = captured != requested
        any_region_partial |= partial
        valid_blocks = [index for index in range(block_count) if valid_mask & (1 << index)]
        decoded_region: dict[str, object] = {
            "index": region_index,
            "label": str(manifest_region["label"]),
            "source": str(manifest_region["source"]),
            "offset": int(manifest_region["offset"]),
            "address": f"0x{actual_address:08x}" if actual_address else None,
            "requested": requested,
            "captured": captured,
            "complete": not partial,
            "valid_block_mask": f"0x{valid_mask:08x}",
            "valid_blocks": valid_blocks,
            "valid_bytes": valid_byte_count,
        }
        if include_region_data and requested:
            decoded_region["raw_hex"] = region_raw[:requested].hex()
            decoded_region["raw_sha256"] = hashlib.sha256(region_raw[:requested]).hexdigest()
        decoded_regions.append(decoded_region)
    if bool(event_flags & 4) != any_region_partial:
        raise DecodeError(f"schema-3 event sequence {sequence}: region-partial flag disagrees with regions")
    counter_names = (
        "admitted_records",
        "enqueued_records",
        "queue_full_losses",
        "budget_rejections",
        "read_faults",
        "read_budget_exceeded",
        "write_failures",
        "written_records",
    )
    counters = dict(zip(counter_names, counter_values, strict=True))
    counters["queue_high_water"] = queue_high_water
    if (
        counters["admitted_records"] > max_data_records
        or counters["enqueued_records"] + counters["queue_full_losses"] > counters["admitted_records"]
        or queue_high_water > queue_capacity
    ):
        raise DecodeError(f"schema-3 event sequence {sequence}: integrity counters exceed runtime bounds")
    return {
        "schema_version": schema_version,
        "sequence": sequence,
        "phase": kind,
        "kind": kind,
        "tick_ms": tick_ms,
        "qpc": qpc,
        "thread_id": thread_id,
        "hook_id": hook_id,
        "hook": str(hook["name"]) if hook is not None else f"<{kind}>",
        "image_address": f"0x{image_address:08x}",
        "this_ptr": f"0x{raw_this:08x}",
        "object_base_ptr": f"0x{normalized_this:08x}" if normalized_valid else None,
        "object_snapshot_ptr": None,
        "return_address": f"0x{return_address:08x}" if event_flags & 1 else None,
        "eax": f"0x{eax:08x}",
        "edx": f"0x{edx:08x}",
        "eflags": f"0x{eflags:08x}",
        "call_index": call_index,
        "sample_index": sample_index,
        "arg_valid_mask": f"0x{arg_valid_mask:08x}",
        "args": argument_values,
        "arg_bits": argument_bits,
        "invalid_arguments": invalid_arguments,
        "regions": decoded_regions,
        "event_flags": event_flags,
        "event_flag_names": [name for bit, name in event_flag_names.items() if event_flags & bit],
        "read_calls_used": read_calls_used,
        "counters": counters,
        "terminal_reason": SCHEMA3_TERMINAL_REASONS[terminal_reason_code],
        "terminal_reason_code": terminal_reason_code,
        "record_sha256_128": record_sha256_128.hex(),
        "values": {},
        "pointees": {},
        "action": None,
        "dropped_before": counters["queue_full_losses"],
    }


def decode_event(
    raw: bytes,
    *,
    expected_schema: int | None = None,
    manifest: list[dict[str, object]] | None = None,
    image_base: int = 0x00400000,
    preferred_base: int = 0x00400000,
    read_call_budget: int = 12,
    queue_capacity: int = 0xFFFFFFFF,
    max_data_records: int = 0xFFFFFFFFFFFFFFFF,
    include_region_data: bool = True,
) -> dict[str, object]:
    manifest_schema = expected_schema
    if manifest_schema is None and len(raw) == SCHEMA3_EVENT_SIZE and len(raw) > 6:
        manifest_schema = int(raw[6])
    if manifest_schema in {3, 4}:
        return decode_schema3_event(
            raw,
            schema_version=manifest_schema,
            manifest=manifest,
            image_base=image_base,
            preferred_base=preferred_base,
            read_call_budget=read_call_budget,
            queue_capacity=queue_capacity,
            max_data_records=max_data_records,
            include_region_data=include_region_data,
        )
    if expected_schema not in {None, 1, 2}:
        raise DecodeError(f"unsupported expected schema {expected_schema}")
    if len(raw) != EVENT_SIZE:
        raise DecodeError(f"truncated event: expected {EVENT_SIZE}, got {len(raw)}")
    prefix = EVENT_PREFIX.unpack_from(raw, 0)
    magic, size, schema, phase = prefix[:4]
    if (
        magic != EVENT_MAGIC
        or size != EVENT_SIZE
        or schema not in SUPPORTED_SCHEMAS
        or (expected_schema is not None and schema != expected_schema)
        or phase not in {1, 2}
    ):
        raise DecodeError(f"invalid event marker/size/schema/phase at sequence candidate {prefix[4:5]}")
    (
        sequence,
        tick_ms,
        thread_id,
        hook_id,
        image_address,
        this_ptr,
        return_address,
        eax,
        edx,
        eflags,
    ) = prefix[4:]
    at = EVENT_PREFIX.size
    args = struct.unpack_from("<8I", raw, at)
    at += 32
    values = struct.unpack_from("<16I", raw, at)
    at += 64
    object_size = struct.unpack_from("<I", raw, at)[0]
    at += 4
    object_bytes = raw[at : at + 256]
    at += 256
    aux_ptr = struct.unpack_from("<I", raw, at)[0]
    at += 4
    aux_bytes = raw[at : at + 72]
    at += 72
    aux_name = decode_c_string(raw[at : at + 48])
    at += 48
    pointee_sizes = struct.unpack_from("<3I", raw, at)
    at += 12
    pointees = tuple(raw[at + index * 16 : at + (index + 1) * 16] for index in range(3))
    at += 48
    dropped_before, qpc_low, qpc_high = struct.unpack_from("<III", raw, at)
    action: dict[str, object] | None = None
    action_layout_trusted = schema >= 2 or hook_id == 3
    if aux_ptr and action_layout_trusted:
        name_ptr, action_crc, vehicle_ptr, ai_ptr, score_bits = struct.unpack_from("<IIIII", aux_bytes, 0x34)
        action = {
            "ptr": f"0x{aux_ptr:08x}",
            "name": aux_name,
            "name_ptr": f"0x{name_ptr:08x}",
            "crc": f"0x{action_crc:08x}",
            "vehicle_ptr": f"0x{vehicle_ptr:08x}",
            "ai_ptr": f"0x{ai_ptr:08x}",
            "score": u32_float(score_bits),
            "score_bits": f"0x{score_bits:08x}",
        }
    hook_name = HOOKS.get(hook_id, (f"unknown:{hook_id}", ()))[0]
    complete_base = this_ptr + HOOK_THIS_TO_COMPLETE_BASE.get(hook_id, 0)
    object_snapshot_ptr = complete_base if schema >= 2 else this_ptr
    decoded_values = (
        named_values_v2(hook_id, values)
        if schema >= 2
        else validated_schema1_values(hook_id, values, object_bytes, object_size)
    )
    event: dict[str, object] = {
        "schema_version": schema,
        "sequence": sequence,
        "phase": "entry" if phase == 1 else "exit",
        "tick_ms": tick_ms,
        "qpc": qpc_low | (qpc_high << 32),
        "thread_id": thread_id,
        "hook_id": hook_id,
        "hook": hook_name,
        "image_address": f"0x{image_address:08x}",
        "this_ptr": f"0x{this_ptr:08x}",
        "object_base_ptr": f"0x{complete_base:08x}",
        "object_snapshot_ptr": f"0x{object_snapshot_ptr:08x}",
        "return_address": f"0x{return_address:08x}",
        "eax": f"0x{eax:08x}",
        "edx": f"0x{edx:08x}",
        "eflags": f"0x{eflags:08x}",
        "args": named_args(hook_id, args),
        "values": decoded_values,
        "action": action,
        "pointees": decode_pointees(hook_id, pointee_sizes, pointees),
        "object_size": object_size,
        "object_sha256": hashlib.sha256(object_bytes[:object_size]).hexdigest() if object_size else None,
        "dropped_before": dropped_before,
    }
    if schema == 1 and hook_id in {1, 2, 4, 5, 7, 9}:
        event["legacy_probe_values"] = [f"0x{value:08x}" for value in values]
    if phase == 2 and hook_id in BOOL_RESULTS:
        event["result"] = bool(eax & 0xFF)
    return event


def iter_events(
    stream: BinaryIO,
    *,
    expected_schema: int | None = None,
    manifest: list[dict[str, object]] | None = None,
    image_base: int = 0x00400000,
    preferred_base: int = 0x00400000,
    read_call_budget: int = 12,
    queue_capacity: int = 0xFFFFFFFF,
    max_data_records: int = 0xFFFFFFFFFFFFFFFF,
    include_region_data: bool = True,
    tolerate_truncated_tail: bool = False,
    record_limit: int | None = None,
) -> Iterator[dict[str, object]]:
    record_size = SCHEMA3_EVENT_SIZE if expected_schema in {3, 4} else EVENT_SIZE
    physical_index = 0
    while True:
        if record_limit is not None and physical_index >= record_limit:
            return
        raw = stream.read(record_size)
        if not raw:
            return
        if len(raw) != record_size and tolerate_truncated_tail:
            return
        event = decode_event(
            raw,
            expected_schema=expected_schema,
            manifest=manifest,
            image_base=image_base,
            preferred_base=preferred_base,
            read_call_budget=read_call_budget,
            queue_capacity=queue_capacity,
            max_data_records=max_data_records,
            include_region_data=include_region_data,
        )
        event["physical_record_index"] = physical_index
        physical_index += 1
        yield event


def attach_file_layout(path: Path, header: dict[str, object]) -> dict[str, int]:
    file_size = path.stat().st_size
    payload_size = max(0, file_size - int(header["header_size"]))
    complete_records, truncated_tail_bytes = divmod(payload_size, int(header["record_size"]))
    layout = {
        "size_bytes": file_size,
        "complete_record_count": complete_records,
        "truncated_tail_bytes": truncated_tail_bytes,
    }
    header["file_layout"] = layout
    return layout


def read_capture(path: Path, *, tolerate_truncated_tail: bool = False) -> tuple[dict[str, object], list[dict[str, object]]]:
    with path.open("rb") as stream:
        header = read_header(stream)
        schema = int(header["schema_version"])
        attach_file_layout(path, header)
        events = list(
            iter_events(
                stream,
                expected_schema=schema,
                manifest=header.get("manifest") if schema in {3, 4} else None,
                image_base=int(str(header["image_base"]), 16),
                preferred_base=int(str(header["preferred_base"]), 16),
                read_call_budget=int(header.get("read_call_budget", 12)),
                queue_capacity=int(header.get("queue_capacity", 0xFFFFFFFF)),
                max_data_records=int(header.get("max_data_records", 0xFFFFFFFFFFFFFFFF)),
                tolerate_truncated_tail=tolerate_truncated_tail,
            )
        )
    if schema < 3:
        events.sort(key=lambda event: int(event["sequence"]))
    return header, events


def emit_jsonl(header: dict[str, object], events: Iterable[dict[str, object]], stream: object) -> None:
    print(json.dumps({"type": "header", **header}, sort_keys=True), file=stream)
    for event in events:
        print(json.dumps({"type": "event", **event}, sort_keys=True), file=stream)


def emit_csv(events: Iterable[dict[str, object]], stream: object) -> None:
    fields = [
        "sequence", "phase", "tick_ms", "qpc", "thread_id", "hook", "this_ptr", "return_address",
        "call_index", "sample_index", "result", "action_name", "action_score", "args", "arg_bits",
        "values", "pointees", "regions", "counters", "terminal_reason", "dropped_before",
    ]
    writer = csv.DictWriter(stream, fieldnames=fields)
    writer.writeheader()
    for event in events:
        action = event.get("action") or {}
        writer.writerow(
            {
                "sequence": event["sequence"],
                "phase": event["phase"],
                "tick_ms": event["tick_ms"],
                "qpc": event["qpc"],
                "thread_id": event["thread_id"],
                "hook": event["hook"],
                "this_ptr": event["this_ptr"],
                "return_address": event["return_address"],
                "call_index": event.get("call_index", ""),
                "sample_index": event.get("sample_index", ""),
                "result": event.get("result", ""),
                "action_name": action.get("name", ""),
                "action_score": action.get("score", ""),
                "args": json.dumps(event["args"], sort_keys=True),
                "arg_bits": json.dumps(event.get("arg_bits", {}), sort_keys=True),
                "values": json.dumps(event["values"], sort_keys=True),
                "pointees": json.dumps(event["pointees"], sort_keys=True),
                "regions": json.dumps(event.get("regions", []), sort_keys=True),
                "counters": json.dumps(event.get("counters", {}), sort_keys=True),
                "terminal_reason": event.get("terminal_reason", ""),
                "dropped_before": event["dropped_before"],
            }
        )


def pair_events(
    events: list[dict[str, object]],
) -> tuple[list[tuple[dict[str, object], dict[str, object]]], dict[str, int]]:
    stacks: dict[int, list[dict[str, object]]] = {}
    pairs: list[tuple[dict[str, object], dict[str, object]]] = []
    unmatched_exits = 0
    hook_mismatches = 0
    this_mismatches = 0
    return_address_mismatches = 0
    for event in events:
        thread_id = int(event["thread_id"])
        stack = stacks.setdefault(thread_id, [])
        if event["phase"] == "entry":
            stack.append(event)
            continue
        if not stack:
            unmatched_exits += 1
            continue
        entry = stack.pop()
        if entry["hook_id"] != event["hook_id"]:
            hook_mismatches += 1
        if entry["this_ptr"] != event["this_ptr"]:
            this_mismatches += 1
        if entry["return_address"] != event["return_address"]:
            return_address_mismatches += 1
        pairs.append((entry, event))
    unmatched_entries = sum(len(stack) for stack in stacks.values())
    mismatch_count = hook_mismatches + this_mismatches + return_address_mismatches
    return pairs, {
        "pair_count": len(pairs),
        "unmatched_entry_count": unmatched_entries,
        "unmatched_exit_count": unmatched_exits,
        "hook_mismatch_count": hook_mismatches,
        "this_mismatch_count": this_mismatches,
        "return_address_mismatch_count": return_address_mismatches,
        "pair_mismatch_count": mismatch_count,
    }


def sequence_integrity(events: list[dict[str, object]]) -> dict[str, object]:
    sequences = [int(event["sequence"]) for event in events]
    unique = sorted(set(sequences))
    minimum = unique[0] if unique else None
    maximum = unique[-1] if unique else None
    expected_count = (maximum - minimum + 1) if minimum is not None and maximum is not None else 0
    return {
        "sequence_min": minimum,
        "sequence_max": maximum,
        "starts_at_one": minimum == 1 if minimum is not None else False,
        "duplicate_sequence_count": len(sequences) - len(unique),
        "missing_sequence_count": expected_count - len(unique),
        "contiguous": bool(unique) and len(unique) == expected_count,
        "on_disk_strictly_increasing": all(right > left for left, right in zip(sequences, sequences[1:])),
        "on_disk_contiguous": bool(sequences) and all(right == left + 1 for left, right in zip(sequences, sequences[1:])),
    }


def schema3_counter_integrity(
    header: Mapping[str, object], events: list[dict[str, object]]
) -> dict[str, object]:
    counter_names = (
        "admitted_records",
        "enqueued_records",
        "queue_full_losses",
        "budget_rejections",
        "read_faults",
        "read_budget_exceeded",
        "write_failures",
        "written_records",
        "queue_high_water",
    )
    monotonic_failures: dict[str, int] = {}
    for name in counter_names:
        values = [int(dict(event["counters"])[name]) for event in events]
        failures = sum(right < left for left, right in zip(values, values[1:]))
        if failures:
            monotonic_failures[name] = failures
    terminal_events = [event for event in events if event["kind"] == "terminal"]
    terminal = terminal_events[-1] if terminal_events else None
    terminal_is_last = bool(events) and terminal is events[-1]
    final_counters = dict(terminal["counters"] if terminal is not None else events[-1]["counters"] if events else {})
    entry_count = sum(event["kind"] == "entry" for event in events)
    equations: dict[str, bool | None] = {
        "admitted_equals_enqueued_plus_queue_losses": None,
        "written_entries_equal_enqueued": None,
        "terminal_written_prefix_equals_prior_records": None,
        "limit_terminal_reaches_max_data_records": None,
    }
    if final_counters:
        equations["admitted_equals_enqueued_plus_queue_losses"] = (
            int(final_counters["admitted_records"])
            == int(final_counters["enqueued_records"]) + int(final_counters["queue_full_losses"])
        )
        equations["written_entries_equal_enqueued"] = entry_count == int(final_counters["enqueued_records"])
        if terminal_is_last:
            equations["terminal_written_prefix_equals_prior_records"] = (
                int(final_counters["written_records"]) == len(events) - 1
            )
            if terminal["terminal_reason"] == "limit":
                equations["limit_terminal_reaches_max_data_records"] = (
                    int(final_counters["admitted_records"]) == int(header["max_data_records"])
                )
    high_water_valid = not final_counters or int(final_counters["queue_high_water"]) <= int(header["queue_capacity"])
    no_write_failures = not final_counters or int(final_counters["write_failures"]) == 0
    sequence = sequence_integrity(events)
    file_layout = header.get("file_layout", {})
    truncated_tail_bytes = int(file_layout.get("truncated_tail_bytes", 0)) if isinstance(file_layout, Mapping) else 0
    structurally_complete = (
        header["activation_state"] == "closed"
        and len(terminal_events) == 1
        and terminal_is_last
        and sequence["starts_at_one"]
        and sequence["on_disk_contiguous"]
        and sequence["on_disk_strictly_increasing"]
        and int(sequence["duplicate_sequence_count"]) == 0
        and int(sequence["missing_sequence_count"]) == 0
        and truncated_tail_bytes == 0
        and not monotonic_failures
        and high_water_valid
        and no_write_failures
        and all(value is not False for value in equations.values())
    )
    lossless_admitted = bool(
        structurally_complete
        and final_counters
        and int(final_counters["queue_full_losses"]) == 0
        and equations["admitted_equals_enqueued_plus_queue_losses"] is True
        and equations["written_entries_equal_enqueued"] is True
    )
    return {
        "terminal_count": len(terminal_events),
        "terminal_is_last": terminal_is_last,
        "terminal_reason": terminal["terminal_reason"] if terminal is not None else None,
        "counter_monotonicity_failures": monotonic_failures,
        "final_counters": final_counters,
        "queue_high_water_within_capacity": high_water_valid,
        "no_reported_write_failures": no_write_failures,
        "counter_equations": equations,
        "truncated_tail_bytes": truncated_tail_bytes,
        "structurally_complete_closed_capture": structurally_complete,
        "lossless_admitted_capture": lossless_admitted,
        "complete_closed_capture": structurally_complete,
    }


def schema3_hook_reachability(
    header: Mapping[str, object], events: list[dict[str, object]]
) -> dict[str, dict[str, object]]:
    by_hook: dict[int, list[dict[str, object]]] = {}
    for event in events:
        if event["kind"] == "entry":
            by_hook.setdefault(int(event["hook_id"]), []).append(event)
    output: dict[str, dict[str, object]] = {}
    for hook in header["manifest"]:
        hook_id = int(hook["id"])
        observed = by_hook.get(hook_id, [])
        call_indices = [int(event["call_index"]) for event in observed]
        sample_indices = [int(event["sample_index"]) for event in observed]
        unique_calls = set(call_indices)
        unique_samples = set(sample_indices)
        first_sample = min(unique_samples) if unique_samples else None
        last_sample = max(unique_samples) if unique_samples else None
        sample_span = (last_sample - first_sample + 1) if first_sample is not None and last_sample is not None else 0
        output[str(hook["name"])] = {
            "hook_id": hook_id,
            "enabled": bool(hook["enabled"]),
            "sample_divisor": int(hook["divisor"]),
            "reached": bool(observed),
            "observed_samples": len(observed),
            "call_index_min": min(call_indices) if call_indices else None,
            "call_index_max": max(call_indices) if call_indices else None,
            "sample_index_min": first_sample,
            "sample_index_max": last_sample,
            "unobserved_sample_slots_before_first": (first_sample - 1) if first_sample is not None else None,
            "unobserved_sample_slots_within_observed_span": sample_span - len(unique_samples),
            "duplicate_call_index_count": len(call_indices) - len(unique_calls),
            "duplicate_sample_index_count": len(sample_indices) - len(unique_samples),
            "qpc_first": min((int(event["qpc"]) for event in observed), default=None),
            "qpc_last": max((int(event["qpc"]) for event in observed), default=None),
            "thread_ids": sorted({int(event["thread_id"]) for event in observed}),
        }
    return output


def schema3_region_coverage(events: list[dict[str, object]]) -> dict[str, dict[str, object]]:
    counters: dict[str, Counter[str]] = {}
    byte_totals: Counter[str] = Counter()
    for event in events:
        if event["kind"] != "entry":
            continue
        for region in event["regions"]:
            if not region["label"]:
                continue
            key = f"{event['hook']}::{region['label']}"
            state = counters.setdefault(key, Counter())
            state["events"] += 1
            state["complete" if region["complete"] else "partial"] += 1
            if region["address"] is None:
                state["unresolved"] += 1
            byte_totals[key] += int(region["captured"])
    return {
        key: {
            "events": state["events"],
            "complete_events": state["complete"],
            "partial_events": state["partial"],
            "unresolved_events": state["unresolved"],
            "captured_bytes": byte_totals[key],
        }
        for key, state in sorted(counters.items())
    }


def build_schema3_summary(
    header: dict[str, object],
    events: list[dict[str, object]],
    *,
    sample_config_sha256: str | None = None,
) -> dict[str, object]:
    kinds = Counter(str(event["kind"]) for event in events)
    hook_entries = Counter(str(event["hook"]) for event in events if event["kind"] == "entry")
    qpcs = [int(event["qpc"]) for event in events if int(event["qpc"]) != 0]
    qpc_frequency = int(header["qpc_frequency"])
    integrity = {
        **sequence_integrity(events),
        **schema3_counter_integrity(header, events),
    }
    config_matches = sample_config_sha256 == header["config_sha256"] if sample_config_sha256 is not None else None
    return {
        "header": header,
        "event_count": len(events),
        "event_kind_counts": dict(sorted(kinds.items())),
        "thread_ids": sorted({int(event["thread_id"]) for event in events if event["kind"] == "entry"}),
        "qpc_span_seconds": ((max(qpcs) - min(qpcs)) / qpc_frequency) if qpcs and qpc_frequency > 0 else None,
        "integrity": integrity,
        "hook_phase_counts": {f"{hook} [entry]": count for hook, count in sorted(hook_entries.items())},
        "hook_reachability": schema3_hook_reachability(header, events),
        "region_coverage": schema3_region_coverage(events),
        "config_integrity": {
            "embedded_sha256": header["config_sha256"],
            "provided_sha256": sample_config_sha256,
            "matches": config_matches,
            "resolved_manifest_is_authoritative": True,
        },
        "boolean_results": {},
        "action_transitions": {},
        "sampling_bounds": {},
        "instrumented_duration_ms": {},
    }


class Schema3StreamAccumulator:
    """Bounded-memory schema-3 summary state for large capture files."""

    COUNTER_NAMES = (
        "admitted_records",
        "enqueued_records",
        "queue_full_losses",
        "budget_rejections",
        "read_faults",
        "read_budget_exceeded",
        "write_failures",
        "written_records",
        "queue_high_water",
    )

    def __init__(self, header: Mapping[str, object]) -> None:
        self.header = header
        self.event_count = 0
        self.entry_count = 0
        self.kinds: Counter[str] = Counter()
        self.hook_entries: Counter[str] = Counter()
        self.thread_ids: set[int] = set()
        self.thread_ids_truncated = False
        self.qpc_min: int | None = None
        self.qpc_max: int | None = None
        self.sequence_min: int | None = None
        self.sequence_max: int | None = None
        self.previous_sequence: int | None = None
        self.sequence_first: int | None = None
        self.sequence_position_mismatches = 0
        self.on_disk_strictly_increasing = True
        self.on_disk_contiguous = True
        self.sequence_seen: set[int] = set()
        self.sequence_tracking_complete = True
        self.sequence_duplicate_lower_bound = 0
        self.previous_counters: dict[str, int] | None = None
        self.counter_monotonicity_failures: Counter[str] = Counter()
        self.last_event: dict[str, object] | None = None
        self.last_terminal: dict[str, object] | None = None
        self.terminal_count = 0
        self.reachability: dict[int, dict[str, Any]] = {
            int(hook["id"]): {
                "count": 0,
                "call_min": None,
                "call_max": None,
                "sample_min": None,
                "sample_max": None,
                "qpc_min": None,
                "qpc_max": None,
                "threads": set(),
                "threads_truncated": False,
                "calls": set(),
                "samples": set(),
                "index_tracking_complete": True,
                "call_duplicate_lower_bound": 0,
                "sample_duplicate_lower_bound": 0,
            }
            for hook in header["manifest"]
        }
        self.region_states: dict[str, Counter[str]] = {}
        self.region_bytes: Counter[str] = Counter()
        self.argument_states: dict[str, dict[str, dict[str, Any]]] = {}

    @staticmethod
    def _track_thread(state: set[int], value: int) -> bool:
        if value in state:
            return False
        if len(state) < STREAM_THREAD_TRACKING_LIMIT:
            state.add(value)
            return False
        return True

    @staticmethod
    def _update_range(state: dict[str, Any], prefix: str, value: int) -> None:
        minimum = f"{prefix}_min"
        maximum = f"{prefix}_max"
        state[minimum] = value if state[minimum] is None else min(int(state[minimum]), value)
        state[maximum] = value if state[maximum] is None else max(int(state[maximum]), value)

    @staticmethod
    def _observe_index(state: dict[str, Any], field: str, value: int) -> None:
        values: set[int] = state[field]
        duplicate_key = "call_duplicate_lower_bound" if field == "calls" else "sample_duplicate_lower_bound"
        if value in values:
            state[duplicate_key] += 1
            return
        if state["index_tracking_complete"] and len(values) < STREAM_INDEX_TRACKING_LIMIT:
            values.add(value)
            return
        state["index_tracking_complete"] = False

    @staticmethod
    def _observe_argument(state: dict[str, Any], value: object) -> None:
        state["count"] += 1
        numeric = isinstance(value, (int, float)) and not isinstance(value, bool)
        if isinstance(value, float) and not math.isfinite(value):
            numeric = False
        if not numeric:
            state["numeric"] = False
        elif state["numeric"]:
            state["min"] = value if state["min"] is None else min(state["min"], value)
            state["max"] = value if state["max"] is None else max(state["max"], value)
        key = (type(value).__name__, repr(value))
        examples: dict[tuple[str, str], object] = state["examples"]
        if key in examples:
            return
        if len(examples) < STREAM_EXAMPLE_LIMIT:
            examples[key] = value
        else:
            state["distinct_overflow"] = True

    def add(self, event: dict[str, object]) -> None:
        self.event_count += 1
        self.last_event = event
        kind = str(event["kind"])
        self.kinds[kind] += 1
        sequence = int(event["sequence"])
        if self.sequence_first is None:
            self.sequence_first = sequence
        self.sequence_min = sequence if self.sequence_min is None else min(self.sequence_min, sequence)
        self.sequence_max = sequence if self.sequence_max is None else max(self.sequence_max, sequence)
        if sequence != self.event_count:
            self.sequence_position_mismatches += 1
        if self.previous_sequence is not None:
            if sequence <= self.previous_sequence:
                self.on_disk_strictly_increasing = False
            if sequence != self.previous_sequence + 1:
                self.on_disk_contiguous = False
        self.previous_sequence = sequence
        if sequence in self.sequence_seen:
            self.sequence_duplicate_lower_bound += 1
        elif self.sequence_tracking_complete and len(self.sequence_seen) < STREAM_INDEX_TRACKING_LIMIT:
            self.sequence_seen.add(sequence)
        else:
            self.sequence_tracking_complete = False

        counters = {name: int(dict(event["counters"])[name]) for name in self.COUNTER_NAMES}
        if self.previous_counters is not None:
            for name, value in counters.items():
                if value < self.previous_counters[name]:
                    self.counter_monotonicity_failures[name] += 1
        self.previous_counters = counters

        qpc = int(event["qpc"])
        if qpc:
            self.qpc_min = qpc if self.qpc_min is None else min(self.qpc_min, qpc)
            self.qpc_max = qpc if self.qpc_max is None else max(self.qpc_max, qpc)
        if kind == "terminal":
            self.terminal_count += 1
            self.last_terminal = event
            return
        if kind != "entry":
            return

        self.entry_count += 1
        hook_name = str(event["hook"])
        self.hook_entries[hook_name] += 1
        thread_id = int(event["thread_id"])
        if self._track_thread(self.thread_ids, thread_id):
            self.thread_ids_truncated = True
        hook_id = int(event["hook_id"])
        reach = self.reachability[hook_id]
        reach["count"] += 1
        call_index = int(event["call_index"])
        sample_index = int(event["sample_index"])
        self._update_range(reach, "call", call_index)
        self._update_range(reach, "sample", sample_index)
        self._update_range(reach, "qpc", qpc)
        if self._track_thread(reach["threads"], thread_id):
            reach["threads_truncated"] = True
        self._observe_index(reach, "calls", call_index)
        self._observe_index(reach, "samples", sample_index)

        hook_fields = self.argument_states.setdefault(hook_name, {})
        for field, value in dict(event["args"]).items():
            if field.endswith("_bits") or value is None:
                continue
            field_state = hook_fields.setdefault(
                field,
                {
                    "count": 0,
                    "numeric": True,
                    "min": None,
                    "max": None,
                    "examples": {},
                    "distinct_overflow": False,
                },
            )
            self._observe_argument(field_state, value)

        for region in event["regions"]:
            if not region["label"]:
                continue
            key = f"{hook_name}::{region['label']}"
            state = self.region_states.setdefault(key, Counter())
            state["events"] += 1
            state["complete" if region["complete"] else "partial"] += 1
            if region["address"] is None:
                state["unresolved"] += 1
            self.region_bytes[key] += int(region["captured"])

    def _sequence_integrity(self) -> dict[str, object]:
        clean_position_sequence = self.event_count > 0 and self.sequence_position_mismatches == 0
        if clean_position_sequence:
            duplicate_count: int | None = 0
            missing_count: int | None = 0
        elif self.sequence_tracking_complete:
            duplicate_count = self.event_count - len(self.sequence_seen)
            if self.sequence_min is None or self.sequence_max is None:
                missing_count = 0
            else:
                missing_count = self.sequence_max - self.sequence_min + 1 - len(self.sequence_seen)
        else:
            duplicate_count = None
            missing_count = None
        return {
            "sequence_min": self.sequence_min,
            "sequence_max": self.sequence_max,
            "starts_at_one": self.sequence_first == 1,
            "duplicate_sequence_count": duplicate_count,
            "duplicate_sequence_count_lower_bound": self.sequence_duplicate_lower_bound,
            "missing_sequence_count": missing_count,
            "contiguous": clean_position_sequence,
            "on_disk_strictly_increasing": self.event_count > 0 and self.on_disk_strictly_increasing,
            "on_disk_contiguous": self.event_count > 0 and self.on_disk_contiguous,
            "sequence_position_mismatch_count": self.sequence_position_mismatches,
            "index_tracking_complete": self.sequence_tracking_complete,
        }

    def _counter_integrity(self, sequence: Mapping[str, object]) -> dict[str, object]:
        terminal_is_last = self.last_event is not None and self.last_event.get("kind") == "terminal"
        final_event = self.last_terminal or self.last_event
        final_counters = dict(final_event["counters"]) if final_event is not None else {}
        equations: dict[str, bool | None] = {
            "admitted_equals_enqueued_plus_queue_losses": None,
            "written_entries_equal_enqueued": None,
            "terminal_written_prefix_equals_prior_records": None,
            "limit_terminal_reaches_max_data_records": None,
        }
        if final_counters:
            equations["admitted_equals_enqueued_plus_queue_losses"] = (
                int(final_counters["admitted_records"])
                == int(final_counters["enqueued_records"]) + int(final_counters["queue_full_losses"])
            )
            equations["written_entries_equal_enqueued"] = (
                self.entry_count == int(final_counters["enqueued_records"])
            )
            if terminal_is_last:
                equations["terminal_written_prefix_equals_prior_records"] = (
                    int(final_counters["written_records"]) == self.event_count - 1
                )
                if self.last_terminal is not None and self.last_terminal["terminal_reason"] == "limit":
                    equations["limit_terminal_reaches_max_data_records"] = (
                        int(final_counters["admitted_records"]) == int(self.header["max_data_records"])
                    )
        file_layout = self.header.get("file_layout", {})
        truncated_tail_bytes = int(file_layout.get("truncated_tail_bytes", 0)) if isinstance(file_layout, Mapping) else 0
        complete_record_count = int(file_layout.get("complete_record_count", self.event_count)) if isinstance(file_layout, Mapping) else self.event_count
        high_water_valid = not final_counters or int(final_counters["queue_high_water"]) <= int(self.header["queue_capacity"])
        no_write_failures = not final_counters or int(final_counters["write_failures"]) == 0
        core_equations_hold = all(
            equations[name] is True
            for name in (
                "admitted_equals_enqueued_plus_queue_losses",
                "written_entries_equal_enqueued",
                "terminal_written_prefix_equals_prior_records",
            )
        ) and equations["limit_terminal_reaches_max_data_records"] is not False
        structurally_complete = bool(
            self.header["activation_state"] == "closed"
            and self.terminal_count == 1
            and terminal_is_last
            and sequence["starts_at_one"]
            and sequence["on_disk_contiguous"]
            and sequence["on_disk_strictly_increasing"]
            and sequence["contiguous"]
            and complete_record_count == self.event_count
            and truncated_tail_bytes == 0
            and not self.counter_monotonicity_failures
            and high_water_valid
            and no_write_failures
            and core_equations_hold
        )
        lossless_admitted = bool(
            structurally_complete
            and final_counters
            and int(final_counters["queue_full_losses"]) == 0
        )
        return {
            "terminal_count": self.terminal_count,
            "terminal_is_last": terminal_is_last,
            "terminal_reason": self.last_terminal["terminal_reason"] if self.last_terminal is not None else None,
            "counter_monotonicity_failures": dict(self.counter_monotonicity_failures),
            "final_counters": final_counters,
            "queue_high_water_within_capacity": high_water_valid,
            "no_reported_write_failures": no_write_failures,
            "counter_equations": equations,
            "truncated_tail_bytes": truncated_tail_bytes,
            "complete_record_count_matches_decode": complete_record_count == self.event_count,
            "structurally_complete_closed_capture": structurally_complete,
            "lossless_admitted_capture": lossless_admitted,
            "complete_closed_capture": structurally_complete,
        }

    def _hook_reachability(self) -> dict[str, dict[str, object]]:
        output: dict[str, dict[str, object]] = {}
        for hook in self.header["manifest"]:
            hook_id = int(hook["id"])
            state = self.reachability[hook_id]
            observed = int(state["count"])
            sample_min = state["sample_min"]
            sample_max = state["sample_max"]
            tracking_complete = bool(state["index_tracking_complete"])
            if observed == 0:
                missing_slots: int | None = 0
                call_duplicates: int | None = 0
                sample_duplicates: int | None = 0
            elif tracking_complete:
                sample_span = int(sample_max) - int(sample_min) + 1
                missing_slots = sample_span - len(state["samples"])
                call_duplicates = observed - len(state["calls"])
                sample_duplicates = observed - len(state["samples"])
            else:
                missing_slots = None
                call_duplicates = None
                sample_duplicates = None
            output[str(hook["name"])] = {
                "hook_id": hook_id,
                "enabled": bool(hook["enabled"]),
                "sample_divisor": int(hook["divisor"]),
                "reached": observed != 0,
                "observed_samples": observed,
                "call_index_min": state["call_min"],
                "call_index_max": state["call_max"],
                "sample_index_min": sample_min,
                "sample_index_max": sample_max,
                "unobserved_sample_slots_before_first": int(sample_min) - 1 if sample_min is not None else None,
                "unobserved_sample_slots_within_observed_span": missing_slots,
                "duplicate_call_index_count": call_duplicates,
                "duplicate_sample_index_count": sample_duplicates,
                "duplicate_call_index_count_lower_bound": state["call_duplicate_lower_bound"],
                "duplicate_sample_index_count_lower_bound": state["sample_duplicate_lower_bound"],
                "index_tracking_complete": tracking_complete,
                "qpc_first": state["qpc_min"],
                "qpc_last": state["qpc_max"],
                "thread_ids": sorted(state["threads"]),
                "thread_ids_truncated": bool(state["threads_truncated"]),
            }
        return output

    def _region_coverage(self) -> dict[str, dict[str, object]]:
        return {
            key: {
                "events": state["events"],
                "complete_events": state["complete"],
                "partial_events": state["partial"],
                "unresolved_events": state["unresolved"],
                "captured_bytes": self.region_bytes[key],
            }
            for key, state in sorted(self.region_states.items())
        }

    def argument_observations(self) -> dict[str, dict[str, dict[str, object]]]:
        output: dict[str, dict[str, dict[str, object]]] = {}
        for hook, fields in sorted(self.argument_states.items()):
            output[hook] = {}
            for field, state in sorted(fields.items()):
                examples = list(state["examples"].values())
                if state["numeric"]:
                    examples.sort()
                else:
                    examples.sort(key=lambda value: (type(value).__name__, str(value)))
                overflow = bool(state["distinct_overflow"])
                item: dict[str, object] = {
                    "count": int(state["count"]),
                    "distinct_count": None if overflow else len(examples),
                    "distinct_count_exact": not overflow,
                }
                if overflow:
                    item["distinct_count_lower_bound"] = STREAM_EXAMPLE_LIMIT + 1
                    item["examples"] = examples
                else:
                    item["values"] = examples
                if state["numeric"] and state["min"] is not None:
                    item["min"] = state["min"]
                    item["max"] = state["max"]
                output[hook][field] = item
        return output

    def summary(self, *, sample_config_sha256: str | None = None) -> dict[str, object]:
        sequence = self._sequence_integrity()
        integrity = {**sequence, **self._counter_integrity(sequence)}
        qpc_frequency = int(self.header["qpc_frequency"])
        config_matches = (
            sample_config_sha256 == self.header["config_sha256"]
            if sample_config_sha256 is not None
            else None
        )
        return {
            "header": self.header,
            "event_count": self.event_count,
            "event_kind_counts": dict(sorted(self.kinds.items())),
            "thread_ids": sorted(self.thread_ids),
            "thread_ids_truncated": self.thread_ids_truncated,
            "qpc_span_seconds": (
                (self.qpc_max - self.qpc_min) / qpc_frequency
                if self.qpc_min is not None and self.qpc_max is not None and qpc_frequency > 0
                else None
            ),
            "integrity": integrity,
            "hook_phase_counts": {
                f"{hook} [entry]": count for hook, count in sorted(self.hook_entries.items())
            },
            "hook_reachability": self._hook_reachability(),
            "region_coverage": self._region_coverage(),
            "config_integrity": {
                "embedded_sha256": self.header["config_sha256"],
                "provided_sha256": sample_config_sha256,
                "matches": config_matches,
                "resolved_manifest_is_authoritative": True,
            },
            "boolean_results": {},
            "action_transitions": {},
            "sampling_bounds": {},
            "instrumented_duration_ms": {},
        }


def build_schema3_stream_summary(
    header: dict[str, object],
    events: Iterable[dict[str, object]],
    *,
    sample_config_sha256: str | None = None,
) -> tuple[dict[str, object], dict[str, dict[str, dict[str, object]]]]:
    accumulator = Schema3StreamAccumulator(header)
    for event in events:
        accumulator.add(event)
    return accumulator.summary(sample_config_sha256=sample_config_sha256), accumulator.argument_observations()


def percentile_nearest_rank(values: list[float], percentile: float) -> float:
    if not values:
        raise ValueError("percentile requires at least one value")
    ordered = sorted(values)
    index = max(0, math.ceil(percentile * len(ordered)) - 1)
    return ordered[index]


def duration_summary(
    pairs: list[tuple[dict[str, object], dict[str, object]]], qpc_frequency: int
) -> dict[str, dict[str, object]]:
    by_hook: dict[str, list[float]] = {}
    if qpc_frequency <= 0:
        return {}
    for entry, exit_event in pairs:
        delta = int(exit_event["qpc"]) - int(entry["qpc"])
        if delta < 0:
            continue
        milliseconds = delta * 1000.0 / qpc_frequency
        by_hook.setdefault(str(entry["hook"]), []).append(milliseconds)
    output: dict[str, dict[str, object]] = {}
    for hook, values in sorted(by_hook.items()):
        output[hook] = {
            "count": len(values),
            "min": min(values),
            "median": statistics.median(values),
            "p95_nearest_rank": percentile_nearest_rank(values, 0.95),
            "max": max(values),
        }
    return output


def load_sample_divisors(path: Path) -> tuple[dict[int, int], str]:
    parser = configparser.ConfigParser()
    parser.optionxform = str
    with path.open("r", encoding="utf-8-sig") as stream:
        parser.read_file(stream)
    if not parser.has_section("trace"):
        raise DecodeError(f"sample config has no [trace] section: {path}")
    divisors: dict[int, int] = {}
    for hook_id, key in HOOK_SAMPLE_CONFIG_KEYS.items():
        raw = parser.get("trace", key, fallback=str(DEFAULT_SAMPLE_DIVISORS[hook_id]))
        if not raw or any(character < "0" or character > "9" for character in raw):
            raise DecodeError(f"invalid sample divisor {key}={raw!r}")
        value = int(raw, 10)
        if value > 1_000_000_000:
            raise DecodeError(f"invalid sample divisor {key}={raw!r}")
        divisors[hook_id] = value
    return divisors, hashlib.sha256(path.read_bytes()).hexdigest()


def hash_path_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def hash_path_prefix_sha256(path: Path, size: int) -> str:
    digest = hashlib.sha256()
    remaining = size
    with path.open("rb") as stream:
        while remaining:
            chunk = stream.read(min(1024 * 1024, remaining))
            if not chunk:
                raise DecodeError(f"capture became shorter while hashing: expected {size} bytes")
            digest.update(chunk)
            remaining -= len(chunk)
    return digest.hexdigest()


def sampling_bounds(
    events: list[dict[str, object]], sample_divisors: Mapping[int, int] | None
) -> dict[str, dict[str, object]]:
    if sample_divisors is None:
        return {}
    entries = Counter(int(event["hook_id"]) for event in events if event["phase"] == "entry")
    output: dict[str, dict[str, object]] = {}
    for hook_id, (hook_name, _) in sorted(HOOKS.items()):
        divisor = int(sample_divisors.get(hook_id, 0))
        observed = entries[hook_id]
        item: dict[str, object] = {
            "sample_divisor": divisor,
            "observed_samples": observed,
        }
        if divisor > 0 and observed > 0:
            minimum = 1 + (observed - 1) * divisor
            item["call_count_min"] = minimum
            item["call_count_max"] = minimum + divisor - 1
            item["exact"] = divisor == 1
        output[hook_name] = item
    return output


def action_observations(
    pairs: list[tuple[dict[str, object], dict[str, object]]]
) -> tuple[dict[str, int], dict[str, int]]:
    transitions: Counter[tuple[str, str]] = Counter()
    exit_states: Counter[tuple[str, object]] = Counter()
    for entry, exit_event in pairs:
        before = entry.get("action") or {}
        after = exit_event.get("action") or {}
        before_name = str(before.get("name") or "<none>")
        after_name = str(after.get("name") or "<none>")
        if before_name != after_name:
            transitions[(before_name, after_name)] += 1
        if after:
            exit_states[(after_name, after.get("score"))] += 1
    return (
        {f"{before} -> {after}": count for (before, after), count in transitions.most_common()},
        {
            f"{name} [score={score}]": count
            for (name, score), count in sorted(exit_states.items(), key=lambda item: (-item[1], str(item[0])))
        },
    )


def summarize_scalar_values(values: list[object]) -> dict[str, object]:
    numeric = bool(values) and all(
        isinstance(value, (int, float)) and not isinstance(value, bool) for value in values
    )
    unique = sorted(set(values)) if numeric else sorted(
        set(values), key=lambda value: (type(value).__name__, str(value))
    )
    output: dict[str, object] = {"count": len(values), "distinct_count": len(unique)}
    if numeric:
        output["min"] = min(values)
        output["max"] = max(values)
    if len(unique) <= 12:
        output["values"] = unique
    else:
        output["examples"] = unique[:12]
    return output


def field_observations(
    events: list[dict[str, object]], hook_ids: set[int]
) -> dict[str, dict[str, object]]:
    by_field: dict[str, list[object]] = {}
    for event in events:
        if event["phase"] != "exit" or int(event["hook_id"]) not in hook_ids:
            continue
        for field, value in dict(event["values"]).items():
            if field.endswith("_bits") or value is None:
                continue
            by_field.setdefault(field, []).append(value)
    return {field: summarize_scalar_values(values) for field, values in sorted(by_field.items())}


def argument_observations(events: list[dict[str, object]]) -> dict[str, dict[str, dict[str, object]]]:
    by_hook: dict[str, dict[str, list[object]]] = {}
    for event in events:
        if event["phase"] != "entry":
            continue
        hook_fields = by_hook.setdefault(str(event["hook"]), {})
        for field, value in dict(event["args"]).items():
            if field.endswith("_bits") or value is None:
                continue
            hook_fields.setdefault(field, []).append(value)
    return {
        hook: {
            field: summarize_scalar_values(values)
            for field, values in sorted(fields.items())
        }
        for hook, fields in sorted(by_hook.items())
    }


def successful_call_observations(
    pairs: list[tuple[dict[str, object], dict[str, object]]], hook_id: int
) -> list[dict[str, object]]:
    output: list[dict[str, object]] = []
    for entry, exit_event in pairs:
        if int(entry["hook_id"]) != hook_id or not exit_event.get("result", False):
            continue
        output.append(
            {
                "entry_sequence": entry["sequence"],
                "exit_sequence": exit_event["sequence"],
                "args": entry["args"],
                "pointees_at_entry": entry["pointees"],
                "pointees_at_exit": exit_event["pointees"],
            }
        )
    return output


def build_summary(
    header: dict[str, object],
    events: list[dict[str, object]],
    *,
    sample_divisors: Mapping[int, int] | None = None,
    sample_config_sha256: str | None = None,
) -> dict[str, object]:
    if int(header["schema_version"]) == 3:
        return build_schema3_summary(header, events, sample_config_sha256=sample_config_sha256)
    hook_phase = Counter((str(event["hook"]), str(event["phase"])) for event in events)
    results = Counter((str(event["hook"]), bool(event["result"])) for event in events if "result" in event)
    pairs, pair_integrity = pair_events(events)
    transitions, _ = action_observations(pairs)
    qpc_frequency = int(header["qpc_frequency"])
    qpcs = [int(event["qpc"]) for event in events]
    integrity = {
        **sequence_integrity(events),
        **pair_integrity,
        "dropped_reported": max((int(event["dropped_before"]) for event in events), default=0),
    }
    return {
        "header": header,
        "event_count": len(events),
        "thread_ids": sorted({int(event["thread_id"]) for event in events}),
        "qpc_span_seconds": ((max(qpcs) - min(qpcs)) / qpc_frequency) if qpcs and qpc_frequency > 0 else None,
        "integrity": integrity,
        "hook_phase_counts": {f"{hook} [{phase}]": count for (hook, phase), count in sorted(hook_phase.items())},
        "boolean_results": {f"{hook}={result}": count for (hook, result), count in sorted(results.items())},
        "action_transitions": transitions,
        "sampling_bounds": sampling_bounds(events, sample_divisors),
        "instrumented_duration_ms": duration_summary(pairs, qpc_frequency),
    }


def build_analysis(
    header: dict[str, object],
    events: list[dict[str, object]],
    *,
    capture_sha256: str,
    capture_size: int,
    sample_divisors: Mapping[int, int] | None = None,
    sample_config_sha256: str | None = None,
) -> dict[str, object]:
    schema = int(header["schema_version"])
    if schema in {3, 4}:
        summary = build_schema3_summary(header, events, sample_config_sha256=sample_config_sha256)
        return build_schema3_analysis_from_summary(
            header,
            summary,
            argument_observations(events),
            capture_sha256=capture_sha256,
            capture_size=capture_size,
            sample_config_sha256=sample_config_sha256,
        )
    summary = build_summary(header, events, sample_divisors=sample_divisors)
    pairs, _ = pair_events(events)
    _, action_exit_states = action_observations(pairs)
    validation: dict[str, object]
    warnings = [
        "Instrumented durations include probe, snapshot, and ReadProcessMemory overhead and are compatibility timings, not uninstrumented performance benchmarks."
    ]
    if schema == 1:
        validation = {
            "values_source": "retail-layout re-decode of each bounded raw object snapshot",
            "task_hook_snapshot_adjustment": "schema 1 hooks 2 and 9 begin at complete object + 0x08",
            "hsimtask_representation": "opaque monotonically assigned integer handle; the C++ typedef is pointer-shaped but the value is not an object address",
            "legacy_specialized_slots": "preserved only as untrusted hexadecimal slots in per-event JSONL",
            "unavailable_from_capture": [
                "AIVehicle.top_speed",
                "AIVehicle.race_throttle_brake_raw",
                "late AIPursuit fields when outside a hook's 256-byte snapshot",
                "AIVehicle action snapshots outside AIGoal::ChooseAction",
            ],
        }
        warnings.append(
            "Schema 1 specialized field labels came from non-retail header offsets; this analysis ignores those labels and re-decodes only binary-validated fields from raw snapshots."
        )
    else:
        validation = {
            "values_source": "schema 2 normalized complete-object snapshot and binary-validated specialized probes",
            "task_hook_snapshot_adjustment": "hooks 2 and 9 normalized from this + 0x08 to the complete object before capture",
            "hsimtask_representation": "opaque monotonically assigned integer handle; the C++ typedef is pointer-shaped but the value is not an object address",
            "legacy_specialized_slots": None,
            "unavailable_from_capture": [],
        }
    pursuit_attempts: list[dict[str, object]] = []
    for entry, exit_event in pairs:
        if int(entry["hook_id"]) != 6:
            continue
        pursuit_attempts.append(
            {
                "entry_sequence": entry["sequence"],
                "exit_sequence": exit_event["sequence"],
                "pursuit_ptr": dict(entry["args"]).get("pursuit_ptr"),
                "vehicle_ptr": dict(entry["args"]).get("vehicle_ptr"),
                "result": exit_event.get("result"),
            }
        )
    return {
        "analysis_schema": 2,
        "capture": {
            "sha256": capture_sha256,
            "size_bytes": capture_size,
            "sample_config_sha256": sample_config_sha256,
        },
        **summary,
        "field_validation": validation,
        "runtime_values": {
            "hook_entry_arguments": argument_observations(events),
            "vehicle_exit_fields": field_observations(events, {1, 2}),
            "spawn_manager_exit_fields": field_observations(events, {4, 5}),
            "pursuit_exit_fields": field_observations(events, {7, 9}),
            "action_exit_states": action_exit_states,
            "successful_check_spawn_position": successful_call_observations(pairs, 4),
            "successful_get_spawn_location": successful_call_observations(pairs, 5),
            "pursuit_vehicle_attempts": pursuit_attempts,
        },
        "warnings": warnings,
    }


def build_schema3_analysis_from_summary(
    header: Mapping[str, object],
    summary: Mapping[str, object],
    arguments: Mapping[str, object],
    *,
    capture_sha256: str,
    capture_size: int,
    sample_config_sha256: str | None,
) -> dict[str, object]:
    schema_version = int(header["schema_version"])
    return {
        "analysis_schema": schema_version,
        "capture": {
            "sha256": capture_sha256,
            "size_bytes": capture_size,
            "sample_config_sha256": sample_config_sha256,
        },
        **summary,
        "field_validation": {
            "values_source": "catalogue-defined entry-time raw regions with per-region addresses, byte counts, and 16-byte validity blocks",
            "return_values": f"not captured; schema {schema_version} is deliberately entry-only",
            "argument_labels_source": header["argument_labels_source"],
            "semantic_field_names": "not asserted for raw region bytes; decode them only after a separate binary/runtime layout proof",
            "unavailable_from_capture": [
                "function return values",
                "entry/exit durations",
                "implicit action transitions",
                "bytes in invalid or unresolved region spans",
            ],
        },
        "runtime_values": {
            "hook_entry_arguments": arguments,
            "hook_reachability": summary["hook_reachability"],
            "raw_region_coverage": summary["region_coverage"],
        },
        "warnings": [
            f"Schema {schema_version} is entry-only and makes no return-value, duration, or action-transition claim.",
            "Region offsets are observation coordinates from the versioned hook catalogue, not automatically validated C++ field identities.",
            "A reached hook proves only that sampled entries were observed in this scenario; an unreached hook is not proof that the game never calls it.",
            "Large-capture CLI analysis bounds distinct-index and argument-example tracking; fields explicitly say when a distinct count is a lower bound.",
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Decode NFSMWAITrace binary captures.")
    parser.add_argument("capture", type=Path)
    parser.add_argument("--format", choices=("summary", "analysis", "jsonl", "csv"), default="summary")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--config", type=Path, help="INI used for exact sample-divisor call-count bounds")
    parser.add_argument("--tolerate-truncated-tail", action="store_true")
    args = parser.parse_args()
    destination = None
    temporary_output: Path | None = None
    try:
        with args.capture.open("rb") as capture_stream:
            header = read_header(capture_stream)
            schema = int(header["schema_version"])
            layout = attach_file_layout(args.capture, header)
            if layout["truncated_tail_bytes"] and not args.tolerate_truncated_tail:
                raise DecodeError(
                    f"truncated event tail: {layout['truncated_tail_bytes']} bytes after "
                    f"{layout['complete_record_count']} complete records"
                )
            sample_divisors: dict[int, int] | None = None
            sample_config_sha256: str | None = None
            if args.config:
                sample_config_sha256 = hash_path_sha256(args.config)
                if schema < 3:
                    sample_divisors, _ = load_sample_divisors(args.config)
            if args.output:
                temporary = tempfile.NamedTemporaryFile(
                    mode="w",
                    encoding="utf-8",
                    newline="",
                    dir=args.output.parent,
                    prefix=f".{args.output.name}.",
                    suffix=".tmp",
                    delete=False,
                )
                destination = temporary
                temporary_output = Path(temporary.name)
            else:
                destination = sys.stdout
            include_region_data = args.format in {"jsonl", "csv"}
            events = iter_events(
                capture_stream,
                expected_schema=schema,
                manifest=header.get("manifest") if schema in {3, 4} else None,
                image_base=int(str(header["image_base"]), 16),
                preferred_base=int(str(header["preferred_base"]), 16),
                read_call_budget=int(header.get("read_call_budget", 12)),
                queue_capacity=int(header.get("queue_capacity", 0xFFFFFFFF)),
                max_data_records=int(header.get("max_data_records", 0xFFFFFFFFFFFFFFFF)),
                include_region_data=include_region_data,
                tolerate_truncated_tail=args.tolerate_truncated_tail,
                record_limit=layout["complete_record_count"],
            )
            if schema in {3, 4} and args.format in {"summary", "analysis"}:
                summary, arguments = build_schema3_stream_summary(
                    header,
                    events,
                    sample_config_sha256=sample_config_sha256,
                )
                output: Mapping[str, object]
                if args.format == "analysis":
                    output = build_schema3_analysis_from_summary(
                        header,
                        summary,
                        arguments,
                        capture_sha256=hash_path_prefix_sha256(args.capture, layout["size_bytes"]),
                        capture_size=layout["size_bytes"],
                        sample_config_sha256=sample_config_sha256,
                    )
                else:
                    output = summary
                print(json.dumps(output, indent=2, sort_keys=True), file=destination)
            elif schema in {3, 4} and args.format == "jsonl":
                emit_jsonl(header, events, destination)
            elif schema in {3, 4}:
                emit_csv(events, destination)
            else:
                legacy_events = list(events)
                legacy_events.sort(key=lambda event: int(event["sequence"]))
                if args.format == "summary":
                    output = build_summary(
                        header,
                        legacy_events,
                        sample_divisors=sample_divisors,
                        sample_config_sha256=sample_config_sha256,
                    )
                    print(json.dumps(output, indent=2, sort_keys=True), file=destination)
                elif args.format == "analysis":
                    output = build_analysis(
                        header,
                        legacy_events,
                        capture_sha256=hash_path_prefix_sha256(args.capture, layout["size_bytes"]),
                        capture_size=layout["size_bytes"],
                        sample_divisors=sample_divisors,
                        sample_config_sha256=sample_config_sha256,
                    )
                    print(json.dumps(output, indent=2, sort_keys=True), file=destination)
                elif args.format == "jsonl":
                    emit_jsonl(header, legacy_events, destination)
                else:
                    emit_csv(legacy_events, destination)
            if args.output:
                destination.flush()
                os.fsync(destination.fileno())
                destination.close()
                destination = None
                os.replace(temporary_output, args.output)
                temporary_output = None
    except (OSError, DecodeError, configparser.Error, struct.error, ValueError) as exc:
        parser.exit(1, f"decode failed: {exc}\n")
    finally:
        if args.output and destination is not None:
            destination.close()
        if temporary_output is not None:
            try:
                temporary_output.unlink()
            except FileNotFoundError:
                pass
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
