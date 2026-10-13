#!/usr/bin/env python3
"""Build a deterministic, address-bearing ledger for the NFSMW PC 1.3 zAI unit."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable


UNIT_NAME = "main/Speed/Indep/SourceLists/zAI"
UNIT_SOURCE = "src/Speed/Indep/SourceLists/zAI.cpp"
SPLIT_KEY = "Speed/Indep/SourceLists/zAI.obj"
REPORT_PATH = "build/SPEED_EXE_1_3/report.json"
SYMBOLS_PATH = "config/SPEED_EXE_1_3/symbols.json"
SPLITS_PATH = "config/SPEED_EXE_1_3/splits.json"
CORPUS_ORACLE_PATH = "orig/SPEED_EXE_1_3/speed.exe"
AI_ROOT = "src/Speed/Indep/Src/AI"

GENERIC_SYMBOL = re.compile(r"^(?:sub_[0-9A-Fa-f]+|nullsub_[0-9]+|j_.+)$")
QUALIFIED_CALLABLE = re.compile(
    r"([A-Za-z_~][A-Za-z0-9_~]*(?:::[A-Za-z_~][A-Za-z0-9_~]*)+)\s*\("
)
RECOVERY_MARKER = re.compile(r"\b(?:TODO|FIXME|UNSOLVED|STRIPPED)\b")


SUBSYSTEM_OWNERS: dict[str, tuple[str, ...]] = {
    "core_vehicle_runtime": (
        "AIVehicle",
        "AIVehicleEmpty",
        "AIVehicleHuman",
        "AIPerpVehicle",
        "Behavior",
        "VehicleParams",
    ),
    "goal_action_framework": (
        "AIAction",
        "AIGoal",
        "AIGoalNone",
        "AIActionNone",
        "AIActionTooDamaged",
        "AIActionGetUnstuck",
        "AIActionAirborne",
        "AIActionSpline",
        "AIActionStrafe",
    ),
    "racer_ai": (
        "AIVehicleRacecar",
        "AIActionRace",
        "GRaceStatus",
        "IRacer",
    ),
    "traffic_ai": (
        "AITrafficManager",
        "AITrafficManager::PatternMap",
        "AIVehicleTraffic",
        "AIActionTraffic",
        "AIGoalTraffic",
        "AvoidableManager",
        "AIAvoidable",
        "ITrafficMgr",
        "ITrafficAI",
        "MSetTrafficSpeed",
    ),
    "police_and_pursuit": (
        "AICopManager",
        "AIPursuit",
        "AIVehicleCopCar",
        "AIVehiclePursuit",
        "AIRoadBlock",
        "AIActionPursuitOffRoad",
        "AIActionHeadOnRam",
        "AIActionJackKnife",
        "AIActionRam",
        "AIActionStopShort",
        "AIActionStaticRoadBlock",
        "PursuitFormation",
        "BoxInFormation",
        "FollowFormation",
        "PitFormation",
        "RollingBlockFormation",
        "HerdFormation",
        "StaggerFollowFormation",
        "GroundSupportRequest",
        "IPursuit",
        "IPursuitAI",
        "ICopMgr",
        "IRoadBlock",
        "MForcePursuitStart",
        "MReqRoadBlock",
        "MReqBackup",
        "MUnspawnCop",
        "MPerpBusted",
        "MPerpEscaped",
    ),
    "helicopter_ai": (
        "AIVehicleHelicopter",
        "AIActionHeliPursuit",
        "AIActionHeliExit",
        "ISimpleChopper",
        "IAIHelicopter",
    ),
    "spawn_admission": ("AISpawnManager",),
    "targeting_and_road_navigation": (
        "AITarget",
        "Gps",
        "WRoadNav",
        "WRoadNavWithCookies",
        "WRoadProfile",
        "WRoadSegment",
        "road_walker",
        "path_spot",
        "MControlPathfinder",
    ),
    "steering_and_control": (
        "AISteer",
        "AIVehiclePid",
        "PidError",
        "AdaptivePIDControllerBase",
        "AdaptivePIDControllerSimple",
        "AdaptivePIDControllerComplicated",
        "performance_limiter",
    ),
    "ai_math": ("AI::Math",),
}

OWNER_TO_SUBSYSTEM = {
    owner: subsystem
    for subsystem, owners in SUBSYSTEM_OWNERS.items()
    for owner in owners
}

SOURCE_STEM_SUBSYSTEM: dict[str, str] = {
    "AIAction": "goal_action_framework",
    "AIActionNone": "goal_action_framework",
    "AIActionTooDamaged": "goal_action_framework",
    "AIActionGetUnstuck": "goal_action_framework",
    "AIActionAirborne": "goal_action_framework",
    "AIActionSpline": "goal_action_framework",
    "AIActionStrafe": "goal_action_framework",
    "AIGoal": "goal_action_framework",
    "AIVehicle": "core_vehicle_runtime",
    "AIVehicleRacecar": "racer_ai",
    "AIActionRace": "racer_ai",
    "AITrafficManager": "traffic_ai",
    "AIVehicleTraffic": "traffic_ai",
    "AIActionTraffic": "traffic_ai",
    "AvoidableManager": "traffic_ai",
    "AIAvoidable": "traffic_ai",
    "AICopManager": "police_and_pursuit",
    "AIPursuit": "police_and_pursuit",
    "AIVehicleCopCar": "police_and_pursuit",
    "AIVehiclePursuit": "police_and_pursuit",
    "AIRoadBlock": "police_and_pursuit",
    "AIRoadBlockSetups": "police_and_pursuit",
    "AIActionPursuitOffRoad": "police_and_pursuit",
    "AIActionHeadOnRam": "police_and_pursuit",
    "AIActionJackKnife": "police_and_pursuit",
    "AIActionRam": "police_and_pursuit",
    "AIActionStopShort": "police_and_pursuit",
    "AIActionStaticRoadBlock": "police_and_pursuit",
    "AIVehicleHelicopter": "helicopter_ai",
    "AIActionHeliPursuit": "helicopter_ai",
    "AIActionHeliExit": "helicopter_ai",
    "AISpawnManager": "spawn_admission",
    "AITarget": "targeting_and_road_navigation",
    "Gps": "targeting_and_road_navigation",
    "AISteer": "steering_and_control",
    "AdaptivePIDController": "steering_and_control",
    "AIMath": "ai_math",
}

ANCHORS: tuple[str, ...] = (
    "AIVehicle::Construct",
    "AIVehicle::OnTask",
    "AIVehicle::Update",
    "AIVehicle::ResetInternals",
    "AIVehicle::OnTaskSimulate",
    "AIVehicle::SetGoal",
    "AIVehicle::UpdateRoadNavInfo",
    "AIVehicle::UpdateRoads",
    "AIAction::AIAction",
    "AIGoal::ChooseAction",
    "AIGoal::AddAction",
    "AIVehicleRacecar::Update",
    "AIActionRace::CanBeAttempted",
    "AIActionRace::ComputePotentials",
    "AIActionRace::Update",
    "AITrafficManager::OnTask",
    "AITrafficManager::SpawnTraffic",
    "AIVehicleTraffic::StartDriving",
    "AIActionTraffic::Update",
    "AIAvoidable::UpdateAllAvoidables",
    "AICopManager::OnTask",
    "AICopManager::UpdatePursuits",
    "AICopManager::SpawnPursuitIVehicle",
    "AIPursuit::OnTask",
    "AIPursuit::UpdateFormation",
    "AIVehiclePursuit::Update",
    "AIVehicleCopCar::WatchForPerps",
    "AIRoadBlock::Construct",
    "AIVehicleHelicopter::Update",
    "AIActionHeliPursuit::Update",
    "AISpawnManager::GetSpawnLocation",
    "AISpawnManager::CheckSpawnPosition",
    "AITarget::TrackAll",
    "AITarget::Aquire",
    "AISteer::Separation",
    "AISteer::Ram",
    "Gps::Update",
    "Gps::Engage",
)


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def file_record(root: Path, relative: str) -> dict[str, Any]:
    data = (root / relative).read_bytes()
    return {"path": relative, "size": len(data), "sha256": sha256_bytes(data)}


def aggregate_digest(records: Iterable[dict[str, Any]]) -> str:
    digest = hashlib.sha256()
    for record in sorted(records, key=lambda item: item["path"]):
        digest.update(record["path"].encode())
        digest.update(b"\0")
        digest.update(str(record["size"]).encode())
        digest.update(b"\0")
        digest.update(record["sha256"].encode())
        digest.update(b"\n")
    return digest.hexdigest()


def recovery_class(percent: float) -> str:
    if percent == 100.0:
        return "exact_binary_match"
    if percent >= 95.0:
        return "near_match"
    if percent > 0.0:
        return "partial_match"
    return "unmatched"


def qualified_callable(demangled: str) -> str | None:
    matches = QUALIFIED_CALLABLE.findall(demangled)
    return matches[-1] if matches else None


def owner_for(demangled: str, qualified: str | None) -> str | None:
    known = [owner for owner in OWNER_TO_SUBSYSTEM if f"{owner}::" in demangled]
    if known:
        return max(known, key=len)
    if qualified is None:
        return None
    return qualified.rsplit("::", 1)[0]


def subsystem_for(
    name: str, demangled: str, owner: str | None
) -> tuple[str, str]:
    if not demangled:
        if GENERIC_SYMBOL.match(name):
            return "unidentified_original_code", "unidentified_generic_gap"
        return "unidentified_original_code", "unidentified_named_gap"
    if owner in OWNER_TO_SUBSYSTEM:
        return OWNER_TO_SUBSYSTEM[owner], "identified_zai_or_adjacent_contract"
    if owner and owner.startswith("Attrib::Gen::"):
        return "attribute_contracts", "identified_engine_contract"
    if owner and owner.startswith("std::"):
        return "compiler_and_standard_library", "identified_dependency"
    if owner and owner.startswith("Concurrency::"):
        return "compiler_and_standard_library", "identified_dependency"
    if owner and owner.startswith("I"):
        return "engine_interfaces", "identified_engine_contract"
    if owner and owner.startswith("M"):
        return "messaging_contracts", "identified_engine_contract"
    if owner in {"UMath", "bVector3", "bMatrix4", "SimRandom"}:
        return "math_and_sim_dependencies", "identified_dependency"
    if owner and owner.startswith("W"):
        return "world_and_road_dependencies", "identified_engine_contract"
    if owner and owner.startswith("Sim::"):
        return "simulation_dependencies", "identified_engine_contract"
    if owner is None:
        return "identified_free_or_inlined_code", "identified_dependency"
    return "other_identified_dependencies", "identified_dependency"


def counter_dict(values: Iterable[str]) -> dict[str, int]:
    return dict(sorted(Counter(values).items()))


def weighted_percent(functions: list[dict[str, Any]]) -> float:
    code_bytes = sum(item["size"] for item in functions)
    if code_bytes == 0:
        return 0.0
    score = sum(item["size"] * item["fuzzy_match_percent"] for item in functions)
    return round(score / code_bytes, 6)


def source_manifest(root: Path, included_paths: set[str]) -> list[dict[str, Any]]:
    ai_root = root / AI_ROOT
    records: list[dict[str, Any]] = []
    for path in sorted(ai_root.rglob("*")):
        if not path.is_file() or path.suffix not in {".cpp", ".h", ".hpp"}:
            continue
        relative = path.relative_to(root).as_posix()
        record = file_record(root, relative)
        record["role"] = (
            "translation_unit_include" if relative in included_paths else "supporting_header"
        )
        record["subsystem"] = SOURCE_STEM_SUBSYSTEM.get(
            path.stem, "shared_or_engine_contract"
        )
        records.append(record)
    return records


def source_candidates(
    qualified: str | None,
    source_lines: dict[str, list[str]],
) -> list[dict[str, Any]]:
    if qualified is None:
        return []
    owner = qualified.rsplit("::", 1)[0]
    owner_leaf = owner.rsplit("::", 1)[-1]
    candidates: list[dict[str, Any]] = []
    for path, lines in source_lines.items():
        for line_number, line in enumerate(lines, 1):
            position = line.find(qualified)
            if position < 0 or line.lstrip().startswith("//"):
                continue
            prefix = line[:position]
            score = 0
            if "->" not in prefix and not prefix.rstrip().endswith((".", "&")):
                score += 4
            if "{" in line[position:]:
                score += 2
            if Path(path).stem == owner_leaf:
                score += 2
            if f"&{qualified}" in line:
                score -= 6
            candidates.append(
                {
                    "path": path,
                    "line": line_number,
                    "score": score,
                    "excerpt": line.strip()[:240],
                }
            )
    candidates.sort(key=lambda item: (-item["score"], item["path"], item["line"]))
    return candidates[:3]


def normalized_measures(raw: dict[str, Any]) -> dict[str, int | float]:
    integer_fields = {
        "total_code",
        "matched_code",
        "total_data",
        "total_functions",
        "matched_functions",
        "total_units",
    }
    return {
        key: int(value) if key in integer_fields else float(value)
        for key, value in raw.items()
    }


def build(root: Path, oracle: Path | None) -> dict[str, Any]:
    report = json.loads((root / REPORT_PATH).read_text())
    symbols = json.loads((root / SYMBOLS_PATH).read_text())
    splits = json.loads((root / SPLITS_PATH).read_text())

    unit = next((item for item in report["units"] if item["name"] == UNIT_NAME), None)
    if unit is None:
        raise ValueError(f"report unit not found: {UNIT_NAME}")
    ranges = splits[SPLIT_KEY]["function_ranges"]
    if len(ranges) != 1:
        raise ValueError(f"expected one zAI function range, got {ranges!r}")
    range_start, range_end = (int(value) for value in ranges[0])
    split_symbols = sorted(
        (
            symbol
            for symbol in symbols["functions"]
            if range_start <= int(symbol["start"]) < range_end
        ),
        key=lambda symbol: int(symbol["start"]),
    )
    report_functions = unit["functions"]
    if len(split_symbols) != len(report_functions):
        raise ValueError(
            f"zAI symbol/report count mismatch: {len(split_symbols)} != "
            f"{len(report_functions)}"
        )

    mismatches: list[dict[str, Any]] = []
    for index, (function, symbol) in enumerate(zip(report_functions, split_symbols)):
        report_size = int(function["size"])
        symbol_size = int(symbol["end"]) - int(symbol["start"])
        if function["name"] != symbol["name"] or report_size != symbol_size:
            mismatches.append(
                {
                    "index": index,
                    "report_name": function["name"],
                    "symbol_name": symbol["name"],
                    "report_size": report_size,
                    "symbol_size": symbol_size,
                }
            )
    if mismatches:
        raise ValueError(f"zAI ordered mapping invariant failed: {mismatches[:5]!r}")

    source_list_text = (root / UNIT_SOURCE).read_text()
    include_paths = {
        f"src/{match}"
        for match in re.findall(r'^#include\s+"([^"]+\.cpp)"', source_list_text, re.M)
    }
    if len(include_paths) != 36:
        raise ValueError(f"expected 36 zAI translation-unit includes, got {len(include_paths)}")
    missing_includes = sorted(path for path in include_paths if not (root / path).is_file())
    if missing_includes:
        raise ValueError(f"missing zAI includes: {missing_includes!r}")

    sources = source_manifest(root, include_paths)
    if len(sources) != 53:
        raise ValueError(f"expected 53 AI source/header files, got {len(sources)}")
    source_lines = {
        record["path"]: (root / record["path"]).read_text(errors="replace").splitlines()
        for record in sources
    }
    candidate_cache: dict[str, list[dict[str, Any]]] = {}

    functions: list[dict[str, Any]] = []
    for index, (raw, symbol) in enumerate(zip(report_functions, split_symbols)):
        demangled = raw.get("metadata", {}).get("demangled_name", "")
        qualified = qualified_callable(demangled)
        owner = owner_for(demangled, qualified)
        subsystem, identification_class = subsystem_for(raw["name"], demangled, owner)
        percent = float(raw.get("fuzzy_match_percent", 0.0))
        if qualified and qualified not in candidate_cache:
            candidate_cache[qualified] = source_candidates(qualified, source_lines)
        functions.append(
            {
                "index": index,
                "mangled_name": raw["name"],
                "demangled_name": demangled or None,
                "qualified_callable": qualified,
                "owner": owner,
                "subsystem": subsystem,
                "identification_class": identification_class,
                "size": int(raw["size"]),
                "fuzzy_match_percent": percent,
                "recovery_class": recovery_class(percent),
                "va_start": f"0x{int(symbol['start']):08x}",
                "va_end_exclusive": f"0x{int(symbol['end']):08x}",
                "report_unit_offset": f"0x{int(raw['address']):08x}",
                "source_candidates": candidate_cache.get(qualified, []),
            }
        )

    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for function in functions:
        grouped[function["subsystem"]].append(function)
    subsystem_stats = []
    for name in sorted(grouped):
        selected = grouped[name]
        subsystem_stats.append(
            {
                "name": name,
                "function_count": len(selected),
                "code_bytes": sum(item["size"] for item in selected),
                "weighted_fuzzy_match_percent": weighted_percent(selected),
                "recovery_classes": counter_dict(
                    item["recovery_class"] for item in selected
                ),
                "identification_classes": counter_dict(
                    item["identification_class"] for item in selected
                ),
            }
        )

    owner_groups: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for function in functions:
        if function["owner"]:
            owner_groups[function["owner"]].append(function)
    owner_stats = []
    for owner, selected in owner_groups.items():
        owner_stats.append(
            {
                "owner": owner,
                "subsystem": selected[0]["subsystem"],
                "function_count": len(selected),
                "code_bytes": sum(item["size"] for item in selected),
                "weighted_fuzzy_match_percent": weighted_percent(selected),
                "recovery_classes": counter_dict(
                    item["recovery_class"] for item in selected
                ),
            }
        )
    owner_stats.sort(key=lambda item: (-item["code_bytes"], item["owner"]))

    anchor_records = []
    for anchor in ANCHORS:
        matches = [item for item in functions if item["qualified_callable"] == anchor]
        anchor_records.append({"name": anchor, "matches": matches})

    recovery_markers = []
    for path, lines in source_lines.items():
        for line_number, line in enumerate(lines, 1):
            if RECOVERY_MARKER.search(line):
                recovery_markers.append(
                    {
                        "path": path,
                        "line": line_number,
                        "excerpt": line.strip()[:240],
                    }
                )

    unidentified = [
        item
        for item in functions
        if item["subsystem"] == "unidentified_original_code"
    ]
    identified_unmatched = [
        item
        for item in functions
        if item["demangled_name"] and item["recovery_class"] == "unmatched"
    ]
    largest_key = lambda item: (-item["size"], item["va_start"])

    corpus_oracle = file_record(root, CORPUS_ORACLE_PATH)
    binary_identity: dict[str, Any] = {
        "corpus_oracle": corpus_oracle,
        "installed_oracle": None,
        "byte_identity_verified": None,
    }
    if oracle is not None:
        installed_data = oracle.read_bytes()
        installed = {
            "size": len(installed_data),
            "sha256": sha256_bytes(installed_data),
        }
        binary_identity["installed_oracle"] = installed
        binary_identity["byte_identity_verified"] = (
            installed["size"] == corpus_oracle["size"]
            and installed["sha256"] == corpus_oracle["sha256"]
            and installed_data == (root / CORPUS_ORACLE_PATH).read_bytes()
        )

    fixed_inputs = [
        file_record(root, REPORT_PATH),
        file_record(root, SYMBOLS_PATH),
        file_record(root, SPLITS_PATH),
        file_record(root, UNIT_SOURCE),
    ]
    measures = normalized_measures(unit["measures"])

    return {
        "schema_version": 1,
        "target": "Need for Speed Most Wanted PC 1.3 complete zAI object unit",
        "claim_boundary": {
            "full_retail_equivalence_supported": False,
            "reason": (
                "The complete unit is inventoried, but the upstream report marks it "
                "incomplete and contains unmatched and unidentified original functions."
            ),
        },
        "address_semantics": {
            "va_start": "absolute preferred image virtual address from symbols.json",
            "va_end_exclusive": "exclusive absolute image virtual address from symbols.json",
            "report_unit_offset": (
                "objdiff unit-relative packed code offset; alignment means it is not "
                "an executable virtual-address delta"
            ),
        },
        "recovery_classes": {
            "exact_binary_match": "objdiff fuzzy_match_percent is exactly 100",
            "near_match": (
                "objdiff fuzzy_match_percent is at least 95 but below 100; inspect "
                "differences before exact behavioral claims"
            ),
            "partial_match": "objdiff fuzzy_match_percent is above 0 but below 95",
            "unmatched": "no reported fuzzy match or a reported value of 0",
        },
        "binary_identity": binary_identity,
        "inputs": fixed_inputs,
        "input_manifest_sha256": aggregate_digest(fixed_inputs),
        "source_manifest": sources,
        "source_manifest_sha256": aggregate_digest(sources),
        "translation_unit_includes": sorted(include_paths),
        "unit": {
            "name": UNIT_NAME,
            "source_path": unit.get("metadata", {}).get("source_path"),
            "complete": bool(unit.get("metadata", {}).get("complete", False)),
            "function_range": {
                "start": f"0x{range_start:08x}",
                "end_exclusive": f"0x{range_end:08x}",
            },
            "measures": measures,
            "sections": unit.get("sections", []),
        },
        "mapping_invariant": {
            "report_function_count": len(report_functions),
            "split_symbol_count": len(split_symbols),
            "name_and_size_pair_count": len(report_functions),
            "mismatch_count": 0,
            "method": (
                "Pair report functions in report order with symbols in ascending VA "
                "order, then require every mangled name and byte size to agree."
            ),
        },
        "inventory_summary": {
            "function_count": len(functions),
            "code_bytes": sum(item["size"] for item in functions),
            "demangled_function_count": sum(
                item["demangled_name"] is not None for item in functions
            ),
            "recovery_classes": counter_dict(
                item["recovery_class"] for item in functions
            ),
            "identification_classes": counter_dict(
                item["identification_class"] for item in functions
            ),
        },
        "gap_summary": {
            "unidentified_function_count": len(unidentified),
            "unidentified_code_bytes": sum(item["size"] for item in unidentified),
            "unidentified_classes": counter_dict(
                item["identification_class"] for item in unidentified
            ),
            "identified_unmatched_function_count": len(identified_unmatched),
            "identified_unmatched_code_bytes": sum(
                item["size"] for item in identified_unmatched
            ),
            "largest_unidentified": sorted(unidentified, key=largest_key)[:50],
            "largest_identified_unmatched": sorted(
                identified_unmatched, key=largest_key
            )[:50],
        },
        "source_recovery_markers": recovery_markers,
        "subsystems": subsystem_stats,
        "owners": owner_stats,
        "anchors": anchor_records,
        "functions": functions,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("decomp_root", type=Path)
    parser.add_argument("--oracle", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = json.dumps(
        build(
            args.decomp_root.resolve(),
            args.oracle.resolve() if args.oracle is not None else None,
        ),
        indent=2,
        sort_keys=True,
    )
    if args.output is None:
        print(result)
    else:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(result + "\n")


if __name__ == "__main__":
    main()
