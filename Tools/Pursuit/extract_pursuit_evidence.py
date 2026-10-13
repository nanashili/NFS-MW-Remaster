#!/usr/bin/env python3
"""Build a deterministic, address-bearing NFSMW PC pursuit recovery ledger."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


TARGETS: tuple[dict[str, Any], ...] = (
    {
        "name": "cop_manager",
        "classes": ("AICopManager",),
        "sources": ("src/Speed/Indep/Src/AI/Activities/AICopManager.cpp",),
    },
    {
        "name": "pursuit_lifecycle",
        "classes": (
            "AIPursuit",
            "PursuitFormation",
            "BoxInFormation",
            "FollowFormation",
            "PitFormation",
            "RollingBlockFormation",
            "HerdFormation",
            "StaggerFollowFormation",
        ),
        "sources": (
            "src/Speed/Indep/Src/AI/AIPursuit.h",
            "src/Speed/Indep/Src/AI/Common/AIPursuit.cpp",
        ),
    },
    {
        "name": "roadblocks",
        "classes": ("AIRoadBlock", "AIActionStaticRoadBlock"),
        "sources": (
            "src/Speed/Indep/Src/AI/AIRoadBlock.h",
            "src/Speed/Indep/Src/AI/Common/AIRoadBlock.cpp",
            "src/Speed/Indep/Src/AI/Common/AIRoadBlockSetups.cpp",
            "src/Speed/Indep/Src/AI/Actions/AIActionStaticRoadBlock.cpp",
        ),
    },
    {
        "name": "spawn_admission",
        "classes": ("AISpawnManager",),
        "sources": (
            "src/Speed/Indep/Src/AI/AISpawnManager.h",
            "src/Speed/Indep/Src/AI/Common/AISpawnManager.cpp",
        ),
    },
    {
        "name": "ground_units",
        "classes": ("AIVehicleCopCar", "AIVehiclePursuit", "AIActionPursuitOffRoad"),
        "sources": (
            "src/Speed/Indep/Src/AI/AIVehicleCopCar.h",
            "src/Speed/Indep/Src/AI/AIVehiclePursuit.h",
            "src/Speed/Indep/Src/AI/Common/AIVehicleCopCar.cpp",
            "src/Speed/Indep/Src/AI/Common/AIVehiclePursuit.cpp",
            "src/Speed/Indep/Src/AI/Actions/AIActionPursuitOffRoad.cpp",
        ),
    },
    {
        "name": "unit_tactics",
        "classes": (
            "AIActionHeadOnRam",
            "AIActionJackKnife",
            "AIActionRam",
            "AIActionStopShort",
        ),
        "sources": (
            "src/Speed/Indep/Src/AI/Actions/AIActionHeadOnRam.cpp",
            "src/Speed/Indep/Src/AI/Actions/AIActionJackKnife.cpp",
            "src/Speed/Indep/Src/AI/Actions/AIActionRam.cpp",
            "src/Speed/Indep/Src/AI/Actions/AIActionStopShort.cpp",
        ),
    },
    {
        "name": "helicopter",
        "classes": ("AIVehicleHelicopter", "AIActionHeliPursuit", "AIActionHeliExit"),
        "sources": (
            "src/Speed/Indep/Src/AI/AIVehicleHelicopter.h",
            "src/Speed/Indep/Src/AI/Common/AIVehicleHelicopter.cpp",
            "src/Speed/Indep/Src/AI/Actions/AIActionHeliExit.cpp",
            "src/Speed/Indep/Src/AI/Actions/AIActionHeliPursuit.cpp",
        ),
    },
    {
        "name": "vehicle_damage",
        "classes": ("DamageCopCar", "DamageHeli", "RBCop"),
        "sources": (
            "src/Speed/Indep/Src/Physics/Behaviors/DamageCopCar.cpp",
            "src/Speed/Indep/Src/Physics/Behaviors/DamageHeli.h",
            "src/Speed/Indep/Src/Physics/Behaviors/DamageHeli.cpp",
            "src/Speed/Indep/Src/Physics/Behaviors/RBCop.cpp",
        ),
    },
    {
        "name": "spike_strips",
        "classes": ("SpikeStrip",),
        "sources": ("src/Speed/Indep/Src/Physics/Behaviors/SpikeStrip.cpp",),
    },
    {
        "name": "infractions",
        "classes": ("GInfractionManager",),
        "sources": (
            "src/Speed/Indep/Src/Gameplay/GInfractionManager.h",
            "src/Speed/Indep/Src/Gameplay/GInfractionManager.cpp",
        ),
    },
    {
        "name": "scheduler_dependency",
        "classes": ("SimTask",),
        "sources": ("src/Speed/Indep/Src/Sim/Common/Simulation.cpp",),
    },
)


KEY_FUNCTIONS = (
    "AIPursuit::LockInPursuitAttribs",
    "AIPursuit::CalcTotalCostToState",
    "AIPursuit::OnTask",
    "AIPursuit::UpdateFormation",
    "AIPursuit::CopRequest",
    "AIPursuit::RequestRoadBlock",
    "AIPursuit::RequestGroundSupport",
    "AIPursuit::SpikesHit",
    "AIPursuit::UpdateJerk",
    "AICopManager::UpdatePursuits",
    "AICopManager::CreateRoadBlock",
    "AICopManager::UpdateRoadBlocks",
    "AICopManager::SpawnPursuitCar",
    "AICopManager::SpawnPursuitHelicopter",
    "AICopManager::SpawnPursuitIVehicle",
    "AICopManager::UpdateSupportCops",
    "AICopManager::OnTask",
    "AISpawnManager::GetSpawnLocation",
    "AISpawnManager::CheckSpawnPosition",
    "AIRoadBlock::GetMinDistanceToTarget",
    "AIActionStaticRoadBlock::Update",
    "AIVehicleCopCar::WatchForPerps",
    "AIVehicleCopCar::CheckForPursuit",
    "AIVehicleCopCar::CanSeeTarget",
    "AIVehiclePursuit::StartPatrol",
    "AIVehiclePursuit::StartFlee",
    "AIVehiclePursuit::StartRoadBlock",
    "AIVehiclePursuit::StartPursuit",
    "AIVehiclePursuit::Update",
    "AIVehiclePursuit::UpdateSiren",
    "AIActionHeadOnRam::Update",
    "AIActionJackKnife::Update",
    "AIActionRam::Update",
    "AIActionStopShort::Update",
    "AIActionHeliPursuit::Update",
    "AIActionHeliExit::Update",
    "AIVehicleHelicopter::CanSeeTarget",
    "AIVehicleHelicopter::UpdateFuel",
    "DamageCopCar::OnImpact",
    "DamageHeli::OnImpact",
    "RBCop::ModifyCollision",
    "SpikeStrip::OnCollide",
    "SpikeStrip::OnTaskSimulate",
    "SimTask::Run",
)


def file_record(root: Path, relative: str) -> dict[str, Any]:
    path = root / relative
    data = path.read_bytes()
    return {
        "path": relative,
        "size": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
    }


def recovery_class(percent: float) -> str:
    if percent == 100.0:
        return "exact_binary_match"
    if percent >= 95.0:
        return "near_match"
    if percent > 0.0:
        return "partial_match"
    return "unmatched"


def classify_owner(demangled: str) -> tuple[str, str] | None:
    for target in TARGETS:
        for class_name in target["classes"]:
            if f"{class_name}::" in demangled:
                return target["name"], class_name
    return None


def build(root: Path, oracle: Path | None) -> dict[str, Any]:
    report_path = root / "build/SPEED_EXE_1_3/report.json"
    symbols_path = root / "config/SPEED_EXE_1_3/symbols.json"
    splits_path = root / "config/SPEED_EXE_1_3/splits.json"
    report = json.loads(report_path.read_text())
    symbols = json.loads(symbols_path.read_text())

    symbols_by_name: dict[str, list[dict[str, Any]]] = {}
    for symbol in symbols["functions"]:
        symbols_by_name.setdefault(symbol["name"], []).append(symbol)

    functions: list[dict[str, Any]] = []
    unit_summaries: dict[str, Any] = {}
    for unit in report["units"]:
        relevant_to_unit = False
        for function in unit["functions"]:
            demangled = function.get("metadata", {}).get("demangled_name", "")
            owner = classify_owner(demangled)
            if owner is None:
                continue
            relevant_to_unit = True
            subsystem, class_name = owner
            percent = float(function.get("fuzzy_match_percent", 0.0))
            candidates = symbols_by_name.get(function["name"], [])
            symbol = candidates[0] if len(candidates) == 1 else None
            size = int(function["size"])
            functions.append(
                {
                    "subsystem": subsystem,
                    "owner": class_name,
                    "mangled_name": function["name"],
                    "demangled_name": demangled,
                    "size": size,
                    "fuzzy_match_percent": percent,
                    "recovery_class": recovery_class(percent),
                    "unit": unit["name"],
                    "report_unit_offset": f"0x{int(function['address']):08x}",
                    "va_start": f"0x{symbol['start']:08x}" if symbol else None,
                    "va_end_exclusive": f"0x{symbol['end']:08x}" if symbol else None,
                    "symbol_size": symbol["end"] - symbol["start"] if symbol else None,
                }
            )
        if relevant_to_unit:
            unit_summaries[unit["name"]] = {
                "source_path": unit.get("metadata", {}).get("source_path"),
                "measures": unit.get("measures", {}),
                "sections": unit.get("sections", []),
            }

    functions.sort(key=lambda item: (int(item["va_start"], 16) if item["va_start"] else 2**64, item["mangled_name"]))

    subsystems: list[dict[str, Any]] = []
    for target in TARGETS:
        selected = [entry for entry in functions if entry["subsystem"] == target["name"]]
        total_bytes = sum(entry["size"] for entry in selected)
        matched_weight = sum(entry["size"] * entry["fuzzy_match_percent"] for entry in selected)
        class_counts: dict[str, int] = {}
        for entry in selected:
            class_counts[entry["recovery_class"]] = class_counts.get(entry["recovery_class"], 0) + 1
        subsystems.append(
            {
                "name": target["name"],
                "classes": list(target["classes"]),
                "function_count": len(selected),
                "code_bytes": total_bytes,
                "weighted_fuzzy_match_percent": round(matched_weight / total_bytes, 6) if total_bytes else 0.0,
                "function_classes": class_counts,
                "sources": [file_record(root, path) for path in target["sources"]],
            }
        )

    key_functions: list[dict[str, Any]] = []
    key_function_gaps: list[str] = []
    for key in KEY_FUNCTIONS:
        marker = f"{key}("
        matches = [entry for entry in functions if marker in entry["demangled_name"]]
        if matches:
            key_functions.extend(matches)
        else:
            key_function_gaps.append(key)

    corpus_oracle = file_record(root, "orig/SPEED_EXE_1_3/speed.exe")
    binary_identity: dict[str, Any] = {
        "corpus_oracle": corpus_oracle,
        "installed_oracle": None,
        "byte_identity_verified": None,
    }
    if oracle is not None:
        oracle_data = oracle.read_bytes()
        installed = {
            "size": len(oracle_data),
            "sha256": hashlib.sha256(oracle_data).hexdigest(),
        }
        binary_identity["installed_oracle"] = installed
        binary_identity["byte_identity_verified"] = (
            installed["size"] == corpus_oracle["size"]
            and installed["sha256"] == corpus_oracle["sha256"]
            and oracle_data == (root / corpus_oracle["path"]).read_bytes()
        )

    return {
        "schema_version": 1,
        "target": "Need for Speed Most Wanted PC 1.3 cops and pursuit",
        "address_semantics": {
            "va_start": "absolute preferred image virtual address from symbols.json",
            "va_end_exclusive": "exclusive absolute image virtual address from symbols.json",
            "report_unit_offset": "objdiff unit-relative offset; not an executable virtual address",
        },
        "recovery_classes": {
            "exact_binary_match": "objdiff fuzzy_match_percent is exactly 100",
            "near_match": "objdiff fuzzy_match_percent is at least 95 but below 100; inspect differences before exact behavioral claims",
            "partial_match": "objdiff fuzzy_match_percent is above 0 but below 95",
            "unmatched": "no reported fuzzy match or a reported value of 0",
        },
        "binary_identity": binary_identity,
        "inputs": [
            file_record(root, "build/SPEED_EXE_1_3/report.json"),
            file_record(root, "config/SPEED_EXE_1_3/symbols.json"),
            file_record(root, "config/SPEED_EXE_1_3/splits.json"),
        ],
        "unit_summaries": unit_summaries,
        "subsystems": subsystems,
        "key_functions": key_functions,
        "key_function_gaps": key_function_gaps,
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
        args.output.write_text(result + "\n")


if __name__ == "__main__":
    main()
