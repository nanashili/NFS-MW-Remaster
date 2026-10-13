#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent
CATALOGUE_PATH = ROOT / "hook_catalogue.json"
GENERATED_PATH = ROOT / "generated_hook_catalogue.h"

SOURCE_CODES = {
    "none": 0,
    "normalized_this": 1,
    "raw_this": 2,
    "absolute": 32,
    **{f"arg{index}": 16 + index for index in range(8)},
}
RETURN_CODES = {"unknown": 0, "void": 1, "bool": 2, "pointer": 3, "integer": 4}
ARG_TYPE_CODES = {"raw": 0, "integer": 0, "float": 1, "pointer": 2, "handle": 3}
RELOCATION_CODES = {None: 0, "call_rel32": 1}
RECOVERY_CLASSES = frozenset({"existing", "exact", "near", "partial"})
PROFILE_NAME_RE = re.compile(r"^[a-z][a-z0-9_]*$")
CONFIG_KEY_RE = re.compile(r"^sample_[A-Za-z0-9_]+$")
REGION_LABEL_RE = re.compile(r"^[a-z0-9_]+$")


class CatalogueError(RuntimeError):
    pass


def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    output: dict[str, object] = {}
    for key, value in pairs:
        if key in output:
            raise CatalogueError(f"duplicate JSON object key {key!r}")
        output[key] = value
    return output


def parse_int(value: object, *, field: str) -> int:
    if isinstance(value, bool):
        raise CatalogueError(f"{field}: Boolean is not an integer")
    if isinstance(value, int):
        return value
    if isinstance(value, str):
        try:
            return int(value, 0)
        except ValueError as exc:
            raise CatalogueError(f"{field}: invalid integer {value!r}") from exc
    raise CatalogueError(f"{field}: expected integer, got {type(value).__name__}")


def parse_bytes(value: object, *, field: str) -> bytes:
    if not isinstance(value, str):
        raise CatalogueError(f"{field}: expected hexadecimal string")
    try:
        return bytes.fromhex(value)
    except ValueError as exc:
        raise CatalogueError(f"{field}: invalid hexadecimal bytes") from exc


def _bounded_text(value: object, *, field: str, capacity: int) -> str:
    if not isinstance(value, str) or not value:
        raise CatalogueError(f"{field}: expected non-empty string")
    encoded = value.encode("ascii", errors="strict")
    if any(byte < 0x20 or byte > 0x7E for byte in encoded):
        raise CatalogueError(f"{field}: control characters are not allowed")
    if len(encoded) >= capacity:
        raise CatalogueError(f"{field}: {value!r} does not fit {capacity}-byte manifest field")
    return value


def _matches(value: str, pattern: re.Pattern[str], *, field: str) -> str:
    if pattern.fullmatch(value) is None:
        raise CatalogueError(f"{field}: unsupported characters in {value!r}")
    return value


def _c_string(value: str) -> str:
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def load_catalogue(path: Path = CATALOGUE_PATH) -> dict[str, Any]:
    try:
        raw = path.read_bytes()
        data = json.loads(raw, object_pairs_hook=reject_duplicate_keys)
    except (OSError, json.JSONDecodeError) as exc:
        raise CatalogueError(f"cannot read catalogue {path}: {exc}") from exc
    if not isinstance(data, dict) or data.get("catalogue_version") != 1:
        raise CatalogueError("unsupported or missing catalogue_version")

    target = data.get("target")
    if not isinstance(target, dict):
        raise CatalogueError("target must be an object")
    target["_name"] = _bounded_text(target.get("name"), field="target.name", capacity=32)
    digest = target.get("sha256")
    if not isinstance(digest, str) or len(digest) != 64:
        raise CatalogueError("target.sha256 must be 64 hexadecimal characters")
    try:
        target["_sha256"] = bytes.fromhex(digest)
    except ValueError as exc:
        raise CatalogueError("target.sha256 is not hexadecimal") from exc
    if digest != digest.lower():
        raise CatalogueError("target.sha256 must use canonical lowercase hexadecimal")
    target["_image_base"] = parse_int(target.get("image_base"), field="target.image_base")
    target["_image_size"] = parse_int(target.get("image_size"), field="target.image_size")
    if not 0 < target["_image_base"] <= 0xFFFFFFFF:
        raise CatalogueError("target.image_base is outside uint32")
    if not 0 < target["_image_size"] <= 0xFFFFFFFF - target["_image_base"] + 1:
        raise CatalogueError("target.image_size is outside the 32-bit address space")

    profiles = data.get("profiles")
    if not isinstance(profiles, list) or not profiles:
        raise CatalogueError("profiles must be a non-empty list")
    profile_by_name: dict[str, dict[str, Any]] = {}
    profile_ids: set[int] = set()
    profile_masks: set[int] = set()
    for profile in profiles:
        if not isinstance(profile, dict):
            raise CatalogueError("profile entries must be objects")
        name = _matches(
            _bounded_text(profile.get("name"), field="profile.name", capacity=32),
            PROFILE_NAME_RE,
            field="profile.name",
        )
        profile_id = parse_int(profile.get("id"), field=f"profile {name}.id")
        mask = parse_int(profile.get("mask"), field=f"profile {name}.mask")
        if profile_id <= 0 or profile_id in profile_ids:
            raise CatalogueError(f"profile {name}: duplicate/invalid id {profile_id}")
        if mask <= 0 or mask > 0xFFFFFFFF or mask in profile_masks:
            raise CatalogueError(f"profile {name}: duplicate/invalid mask 0x{mask:x}")
        if name in profile_by_name:
            raise CatalogueError(f"duplicate profile name {name}")
        profile["_id"] = profile_id
        profile["_mask"] = mask
        profile_by_name[name] = profile
        profile_ids.add(profile_id)
        profile_masks.add(mask)
    ordered_profile_ids = [int(profile["_id"]) for profile in profiles]
    if ordered_profile_ids != list(range(1, len(profiles) + 1)):
        raise CatalogueError("profile ids must be contiguous and ordered from 1")
    if "all" not in profile_by_name or profile_by_name["all"]["_mask"] != 0xFFFFFFFF:
        raise CatalogueError("the all profile must have mask 0xffffffff")
    for name, profile in profile_by_name.items():
        mask = int(profile["_mask"])
        if name != "all" and mask & (mask - 1) != 0:
            raise CatalogueError(f"profile {name}: non-all masks must contain exactly one bit")

    templates = data.get("region_templates")
    if not isinstance(templates, dict):
        raise CatalogueError("region_templates must be an object")
    hooks = data.get("hooks")
    if not isinstance(hooks, list) or not hooks:
        raise CatalogueError("hooks must be a non-empty list")
    ids: list[int] = []
    addresses: set[int] = set()
    keys: set[str] = set()
    names: set[str] = set()

    for hook in hooks:
        if not isinstance(hook, dict):
            raise CatalogueError("hook entries must be objects")
        hook_id = parse_int(hook.get("id"), field="hook.id")
        name = _bounded_text(hook.get("name"), field=f"hook {hook_id}.name", capacity=72)
        key = _matches(
            _bounded_text(hook.get("config_key"), field=f"hook {hook_id}.config_key", capacity=48),
            CONFIG_KEY_RE,
            field=f"hook {hook_id}.config_key",
        )
        recovery = _bounded_text(hook.get("recovery"), field=f"hook {hook_id}.recovery", capacity=16)
        if recovery not in RECOVERY_CLASSES:
            raise CatalogueError(f"hook {hook_id}: unknown recovery class {recovery!r}")
        address = parse_int(hook.get("va"), field=f"hook {hook_id}.va")
        if address < target["_image_base"] or address >= target["_image_base"] + target["_image_size"]:
            raise CatalogueError(f"hook {hook_id}: VA 0x{address:08x} is outside the target image")
        if address % 8 != 0:
            raise CatalogueError(f"hook {hook_id}: VA 0x{address:08x} is not 8-byte aligned for atomic patching")
        if address + 8 > target["_image_base"] + target["_image_size"]:
            raise CatalogueError(f"hook {hook_id}: eight-byte atomic patch extends outside the target image")
        if hook_id in ids or address in addresses or key in keys or name in names:
            raise CatalogueError(f"hook {hook_id}: duplicate id, address, key, or name")
        ids.append(hook_id)
        addresses.add(address)
        keys.add(key)
        names.add(name)

        hook_profiles = hook.get("profiles")
        if not isinstance(hook_profiles, list) or not hook_profiles:
            raise CatalogueError(f"hook {hook_id}: profiles must be a non-empty list")
        profile_mask = 0
        for profile_name in hook_profiles:
            if not isinstance(profile_name, str):
                raise CatalogueError(f"hook {hook_id}: profile names must be strings")
            if profile_name == "all" or profile_name not in profile_by_name:
                raise CatalogueError(f"hook {hook_id}: invalid explicit profile {profile_name!r}")
            profile_mask |= int(profile_by_name[profile_name]["_mask"])

        divisor = parse_int(hook.get("default_divisor"), field=f"hook {hook_id}.default_divisor")
        if not 1 <= divisor <= 1_000_000_000:
            raise CatalogueError(f"hook {hook_id}: default divisor is out of range")
        args = hook.get("args")
        if not isinstance(args, list) or len(args) > 8:
            raise CatalogueError(f"hook {hook_id}: args must contain at most eight entries")
        arg_type_bits = 0
        arg_names: set[str] = set()
        for index, arg in enumerate(args):
            if not isinstance(arg, dict):
                raise CatalogueError(f"hook {hook_id}: argument {index} must be an object")
            arg_name = _bounded_text(arg.get("name"), field=f"hook {hook_id}.args[{index}].name", capacity=48)
            if arg_name in arg_names:
                raise CatalogueError(f"hook {hook_id}: duplicate argument name {arg_name!r}")
            arg_names.add(arg_name)
            arg_type = arg.get("type")
            if arg_type not in ARG_TYPE_CODES:
                raise CatalogueError(f"hook {hook_id}: unknown argument type {arg_type!r}")
            arg_type_bits |= ARG_TYPE_CODES[str(arg_type)] << (index * 2)

        adjustment = parse_int(hook.get("this_adjust", 0), field=f"hook {hook_id}.this_adjust")
        if not -(1 << 31) <= adjustment < (1 << 31):
            raise CatalogueError(f"hook {hook_id}: this_adjust is outside int32")
        static = hook.get("static", False)
        if not isinstance(static, bool):
            raise CatalogueError(f"hook {hook_id}: static must be Boolean")
        if static and adjustment != 0:
            raise CatalogueError(f"hook {hook_id}: static hooks cannot declare a this adjustment")
        return_kind = hook.get("return_kind", "unknown")
        if return_kind not in RETURN_CODES:
            raise CatalogueError(f"hook {hook_id}: unknown return kind {return_kind!r}")
        stolen = parse_bytes(hook.get("stolen"), field=f"hook {hook_id}.stolen")
        lengths = hook.get("instruction_lengths")
        if not isinstance(lengths, list) or not lengths or len(lengths) > 8:
            raise CatalogueError(f"hook {hook_id}: invalid instruction_lengths")
        instruction_lengths = [parse_int(item, field=f"hook {hook_id}.instruction_lengths") for item in lengths]
        if any(length <= 0 or length > 15 for length in instruction_lengths):
            raise CatalogueError(f"hook {hook_id}: instruction length outside 1..15")
        if len(stolen) < 5 or len(stolen) > 16 or sum(instruction_lengths) != len(stolen):
            raise CatalogueError(f"hook {hook_id}: stolen bytes and instruction lengths disagree")

        relocation = hook.get("relocation")
        relocation_kind: str | None = None
        relocation_offset = 0
        relocation_target = 0
        if relocation is not None:
            if not isinstance(relocation, dict):
                raise CatalogueError(f"hook {hook_id}: relocation must be an object")
            relocation_kind = relocation.get("kind")
            if relocation_kind not in RELOCATION_CODES or relocation_kind is None:
                raise CatalogueError(f"hook {hook_id}: unsupported relocation {relocation_kind!r}")
            relocation_offset = parse_int(relocation.get("offset"), field=f"hook {hook_id}.relocation.offset")
            relocation_target = parse_int(relocation.get("target"), field=f"hook {hook_id}.relocation.target")
            if relocation_kind == "call_rel32":
                if relocation_offset < 0 or relocation_offset + 5 > len(stolen) or stolen[relocation_offset] != 0xE8:
                    raise CatalogueError(f"hook {hook_id}: call_rel32 does not point at a complete E8 instruction")
                displacement = struct.unpack_from("<i", stolen, relocation_offset + 1)[0]
                actual_target = (address + relocation_offset + 5 + displacement) & 0xFFFFFFFF
                if actual_target != relocation_target:
                    raise CatalogueError(
                        f"hook {hook_id}: relocation target 0x{actual_target:08x} != declared 0x{relocation_target:08x}"
                    )
                boundaries = set()
                at = 0
                for length in instruction_lengths:
                    boundaries.add(at)
                    at += length
                if relocation_offset not in boundaries:
                    raise CatalogueError(f"hook {hook_id}: relocation is not on an instruction boundary")

        relative_instructions: list[tuple[int, str]] = []
        at = 0
        for length in instruction_lengths:
            opcode = stolen[at]
            if opcode in {0xE8, 0xE9}:
                relative_instructions.append((at, "call_rel32" if opcode == 0xE8 else "unsupported"))
            elif opcode == 0xEB or 0x70 <= opcode <= 0x7F or 0xE0 <= opcode <= 0xE3:
                relative_instructions.append((at, "unsupported"))
            elif opcode == 0x0F and length >= 2 and 0x80 <= stolen[at + 1] <= 0x8F:
                relative_instructions.append((at, "unsupported"))
            at += length
        expected_relative = [] if relocation_kind is None else [(relocation_offset, relocation_kind)]
        if relative_instructions != expected_relative:
            raise CatalogueError(
                f"hook {hook_id}: relative instructions {relative_instructions!r} are not exactly covered by relocation metadata"
            )

        if "regions" in hook and "region_template" in hook:
            raise CatalogueError(f"hook {hook_id}: specify regions or region_template, not both")
        if "regions" in hook:
            regions = hook["regions"]
        else:
            template_name = hook.get("region_template", "none")
            if template_name not in templates:
                raise CatalogueError(f"hook {hook_id}: unknown region template {template_name!r}")
            regions = templates[template_name]
        if not isinstance(regions, list) or len(regions) > 3:
            raise CatalogueError(f"hook {hook_id}: at most three regions are allowed")
        resolved_regions: list[dict[str, Any]] = []
        for index, region in enumerate(regions):
            if not isinstance(region, dict):
                raise CatalogueError(f"hook {hook_id}: region {index} must be an object")
            source = region.get("source")
            if source not in SOURCE_CODES:
                raise CatalogueError(f"hook {hook_id}: region {index} has unknown source {source!r}")
            offset = parse_int(region.get("offset", 0), field=f"hook {hook_id}.regions[{index}].offset")
            length = parse_int(region.get("length"), field=f"hook {hook_id}.regions[{index}].length")
            label = _matches(
                _bounded_text(region.get("label"), field=f"hook {hook_id}.regions[{index}].label", capacity=16),
                REGION_LABEL_RE,
                field=f"hook {hook_id}.regions[{index}].label",
            )
            if not 1 <= length <= 256:
                raise CatalogueError(f"hook {hook_id}: region {index} length is outside 1..256")
            if source == "none":
                raise CatalogueError(f"hook {hook_id}: region {index} cannot use source none with a nonzero length")
            if source == "absolute":
                image_end = target["_image_base"] + target["_image_size"]
                if not target["_image_base"] <= offset or offset + length > image_end:
                    raise CatalogueError(f"hook {hook_id}: absolute region {index} is outside the exact target image")
            elif not -(1 << 31) <= offset < (1 << 31):
                raise CatalogueError(f"hook {hook_id}: region {index} offset is outside int32")
            if str(source).startswith("arg") and int(str(source)[3:]) >= len(args):
                raise CatalogueError(f"hook {hook_id}: region {index} references an undeclared argument")
            if source == "normalized_this" and static:
                raise CatalogueError(f"hook {hook_id}: static hook region {index} cannot use normalized_this")
            resolved_regions.append(
                {"source": source, "source_code": SOURCE_CODES[str(source)], "offset": offset, "length": length, "label": label}
            )

        hook["_id"] = hook_id
        hook["_name"] = name
        hook["_config_key"] = key
        hook["_recovery"] = recovery
        hook["_address"] = address
        hook["_rva"] = address - target["_image_base"]
        hook["_profile_mask"] = profile_mask
        hook["_default_divisor"] = divisor
        hook["_arg_type_bits"] = arg_type_bits
        hook["_this_adjust"] = adjustment
        hook["_flags"] = 1 if static else 0
        hook["_return_code"] = RETURN_CODES[str(return_kind)]
        hook["_stolen"] = stolen
        hook["_instruction_lengths"] = instruction_lengths
        hook["_relocation_code"] = RELOCATION_CODES[relocation_kind]
        hook["_relocation_offset"] = relocation_offset
        hook["_relocation_target"] = relocation_target
        hook["_regions"] = resolved_regions

    if ids != list(range(1, len(hooks) + 1)):
        raise CatalogueError("hook ids must be contiguous and ordered from 1")
    data["_raw_sha256"] = hashlib.sha256(raw).digest()
    data["_profile_by_name"] = profile_by_name
    return data


def _c_bytes(raw: bytes, capacity: int) -> str:
    padded = raw + bytes(capacity - len(raw))
    return "{" + ", ".join(f"0x{value:02x}u" for value in padded) + "}"


def render_header(catalogue: dict[str, Any]) -> str:
    hooks = catalogue["hooks"]
    profiles = catalogue["profiles"]
    lines = [
        "/* Generated by generate_catalogue.py; do not edit by hand. */",
        "#ifndef NFSMW_AI_TRACE_GENERATED_HOOK_CATALOGUE_H",
        "#define NFSMW_AI_TRACE_GENERATED_HOOK_CATALOGUE_H",
        "",
        f"#define TRACE_GENERATED_HOOK_COUNT {len(hooks)}u",
        f"#define TRACE_GENERATED_PROFILE_COUNT {len(profiles)}u",
        f"#define TRACE_CATALOGUE_IMAGE_BASE 0x{catalogue['target']['_image_base']:08x}u",
        f"#define TRACE_CATALOGUE_IMAGE_SIZE 0x{catalogue['target']['_image_size']:08x}u",
        f"#define TRACE_CATALOGUE_TARGET_NAME {_c_string(catalogue['target']['_name'])}",
        "",
        f"static const uint8_t kExpectedTargetSha256[32] = {_c_bytes(catalogue['target']['_sha256'], 32)};",
        f"static const uint8_t kCatalogueSha256[32] = {_c_bytes(catalogue['_raw_sha256'], 32)};",
        "",
        "static const TraceProfileDefinition kProfileDefinitions[TRACE_GENERATED_PROFILE_COUNT] = {",
    ]
    for profile in profiles:
        lines.append(f"    {{{profile['_id']}u, 0x{profile['_mask']:08x}u, {_c_string(profile['name'])}}},")
    lines.extend(["};", "", "static const TraceHookDefinition kHookDefinitions[TRACE_GENERATED_HOOK_COUNT] = {"])
    for hook in hooks:
        lengths = bytes(hook["_instruction_lengths"])
        regions = list(hook["_regions"])
        while len(regions) < 3:
            regions.append({"source_code": 0, "offset": 0, "length": 0, "label": ""})
        lines.extend(
            [
                "    {",
                f"        {hook['_id']}u, 0x{hook['_address']:08x}u, 0x{hook['_rva']:08x}u, 0x{hook['_profile_mask']:08x}u,",
                f"        {hook['_default_divisor']}u, {len(hook['args'])}u, {hook['_this_adjust']}, {hook['_flags']}u,",
                f"        {hook['_return_code']}u, 0x{hook['_arg_type_bits']:08x}u, {hook['_relocation_code']}u,",
                f"        {hook['_relocation_offset']}u, 0x{hook['_relocation_target']:08x}u,",
                f"        {_c_bytes(hook['_stolen'], 16)}, {len(hook['_stolen'])}u, {len(hook['_instruction_lengths'])}u,",
                f"        {_c_bytes(lengths, 8)},",
                f"        {_c_string(hook['_name'])}, {_c_string(hook['_config_key'])}, {_c_string(hook['_recovery'])},",
                "        {",
            ]
        )
        for region in regions:
            offset = int(region["offset"]) & 0xFFFFFFFF
            lines.append(
                f"            {{{region['source_code']}u, (int32_t)0x{offset:08x}u, {region['length']}u, {_c_string(region['label'])}}},"
            )
        lines.extend(["        },", "    },"])
    lines.extend(["};", "", "#endif", ""])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate and generate the NFSMW AI hook catalogue.")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--write", action="store_true", help="write generated_hook_catalogue.h")
    mode.add_argument("--check", action="store_true", help="fail if generated_hook_catalogue.h is stale")
    parser.add_argument("--catalogue", type=Path, default=CATALOGUE_PATH)
    parser.add_argument("--output", type=Path, default=GENERATED_PATH)
    args = parser.parse_args()
    try:
        rendered = render_header(load_catalogue(args.catalogue))
    except (CatalogueError, UnicodeError) as exc:
        parser.exit(1, f"catalogue validation failed: {exc}\n")
    if args.write:
        args.output.write_text(rendered, encoding="utf-8")
        return 0
    if args.check:
        try:
            current = args.output.read_text(encoding="utf-8")
        except OSError as exc:
            parser.exit(1, f"generated catalogue missing: {exc}\n")
        if current != rendered:
            parser.exit(1, f"generated catalogue is stale: run {Path(__file__).name} --write\n")
        return 0
    sys.stdout.write(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
