#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
from pathlib import Path
from typing import Any

from generate_catalogue import CATALOGUE_PATH, CatalogueError, load_catalogue


class ValidationError(RuntimeError):
    pass


AUDITED_ULTIMATE_ASI_LOADER_SHA256 = (
    "7675b24a2e408349c8a59b87e3ae8724ad8a89eff21c96a9c109650a3c1316e0"
)


def require_range(data: bytes, offset: int, size: int, *, label: str) -> None:
    if offset < 0 or size < 0 or offset > len(data) - size:
        raise ValidationError(f"{label}: range 0x{offset:x}+0x{size:x} is outside the file")


def read_pe(path: Path) -> tuple[bytes, dict[str, Any], list[dict[str, int | str]]]:
    data = path.read_bytes()
    require_range(data, 0, 0x40, label=str(path))
    if data[:2] != b"MZ":
        raise ValidationError(f"{path}: missing MZ header")
    pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
    require_range(data, pe_offset, 24, label=f"{path} PE header")
    if data[pe_offset : pe_offset + 4] != b"PE\0\0":
        raise ValidationError(f"{path}: missing PE signature")
    machine, section_count, _, _, _, optional_size, characteristics = struct.unpack_from(
        "<HHIIIHH", data, pe_offset + 4
    )
    optional = pe_offset + 24
    require_range(data, optional, optional_size, label=f"{path} optional header")
    if optional_size < 96:
        raise ValidationError(f"{path}: truncated PE32 optional header")
    magic = struct.unpack_from("<H", data, optional)[0]
    if magic != 0x10B:
        raise ValidationError(f"{path}: expected PE32, got optional-header magic 0x{magic:04x}")
    image_base = struct.unpack_from("<I", data, optional + 28)[0]
    size_of_image = struct.unpack_from("<I", data, optional + 56)[0]
    size_of_headers = struct.unpack_from("<I", data, optional + 60)[0]
    dll_characteristics = struct.unpack_from("<H", data, optional + 70)[0]
    directory_count = struct.unpack_from("<I", data, optional + 92)[0]
    available_directories = (optional_size - 96) // 8
    if directory_count > available_directories:
        raise ValidationError(
            f"{path}: {directory_count} data directories exceed the optional-header capacity {available_directories}"
        )
    data_directories = [
        struct.unpack_from("<II", data, optional + 96 + index * 8)
        for index in range(directory_count)
    ]
    section_offset = optional + optional_size
    require_range(data, section_offset, section_count * 40, label=f"{path} section table")
    sections: list[dict[str, int | str]] = []
    for index in range(section_count):
        at = section_offset + index * 40
        raw_name, virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from("<8sIIII", data, at)
        if raw_size:
            require_range(data, raw_offset, raw_size, label=f"{path} section {index} raw data")
        sections.append(
            {
                "name": raw_name.rstrip(b"\0").decode("ascii", errors="replace"),
                "virtual_size": virtual_size,
                "virtual_address": virtual_address,
                "raw_size": raw_size,
                "raw_offset": raw_offset,
            }
        )
    return data, {
        "machine": machine,
        "characteristics": characteristics,
        "dll_characteristics": dll_characteristics,
        "image_base": image_base,
        "size_of_image": size_of_image,
        "size_of_headers": size_of_headers,
        "data_directories": data_directories,
    }, sections


def va_to_file_offset(
    va: int,
    size: int,
    image_base: int,
    sections: list[dict[str, int | str]],
) -> int:
    rva = va - image_base
    for section in sections:
        start = int(section["virtual_address"])
        span = max(int(section["virtual_size"]), int(section["raw_size"]))
        if start <= rva < start + span:
            delta = rva - start
            if delta > int(section["raw_size"]) - size:
                raise ValidationError(f"VA 0x{va:08x}+0x{size:x} falls outside section raw data")
            return int(section["raw_offset"]) + delta
    raise ValidationError(f"VA 0x{va:08x} is not mapped by a PE section")


def rva_to_file_offset(
    rva: int,
    size: int,
    sections: list[dict[str, int | str]],
    *,
    label: str,
) -> int:
    if rva < 0 or size < 0:
        raise ValidationError(f"{label}: invalid RVA/size")
    for section in sections:
        start = int(section["virtual_address"])
        raw_size = int(section["raw_size"])
        if start <= rva and rva - start <= raw_size - size:
            return int(section["raw_offset"]) + (rva - start)
    raise ValidationError(f"{label}: RVA 0x{rva:08x}+0x{size:x} is outside section raw data")


def read_rva_c_string(
    data: bytes,
    rva: int,
    sections: list[dict[str, int | str]],
    *,
    label: str,
    maximum: int = 512,
) -> str:
    raw = bytearray()
    previous_offset: int | None = None
    for index in range(maximum + 1):
        offset = rva_to_file_offset(rva + index, 1, sections, label=label)
        if previous_offset is not None and offset != previous_offset + 1:
            raise ValidationError(f"{label}: string crosses a non-contiguous raw mapping")
        previous_offset = offset
        value = data[offset]
        if value == 0:
            try:
                return raw.decode("ascii")
            except UnicodeDecodeError as exc:
                raise ValidationError(f"{label}: string is not ASCII") from exc
        raw.append(value)
    raise ValidationError(f"{label}: string exceeds {maximum} bytes")


def pe_directory(pe: dict[str, Any], index: int) -> tuple[int, int]:
    directories = list(pe["data_directories"])
    if index >= len(directories):
        return 0, 0
    rva, size = directories[index]
    return int(rva), int(size)


def read_pe_imports(
    data: bytes,
    pe: dict[str, Any],
    sections: list[dict[str, int | str]],
) -> dict[str, list[str]]:
    directory_rva, directory_size = pe_directory(pe, 1)
    if directory_rva == 0 or directory_size == 0:
        return {}
    if directory_size < 20 or directory_size % 20 != 0:
        raise ValidationError("import directory has an invalid descriptor span")
    imports: dict[str, list[str]] = {}
    terminated = False
    for descriptor_index in range(directory_size // 20):
        descriptor_rva = directory_rva + descriptor_index * 20
        descriptor_offset = rva_to_file_offset(
            descriptor_rva,
            20,
            sections,
            label=f"import descriptor {descriptor_index}",
        )
        original_thunk, timestamp, forwarder, name_rva, first_thunk = struct.unpack_from(
            "<IIIII", data, descriptor_offset
        )
        if not any((original_thunk, timestamp, forwarder, name_rva, first_thunk)):
            terminated = True
            break
        if name_rva == 0 or (original_thunk == 0 and first_thunk == 0):
            raise ValidationError(f"import descriptor {descriptor_index} is incomplete")
        dll_name = read_rva_c_string(
            data,
            name_rva,
            sections,
            label=f"import descriptor {descriptor_index} DLL name",
        )
        if not dll_name or dll_name.casefold() in {name.casefold() for name in imports}:
            raise ValidationError(f"duplicate or empty import DLL name {dll_name!r}")
        thunk_rva = original_thunk or first_thunk
        symbols: list[str] = []
        for thunk_index in range(4096):
            thunk_offset = rva_to_file_offset(
                thunk_rva + thunk_index * 4,
                4,
                sections,
                label=f"{dll_name} import thunk {thunk_index}",
            )
            thunk_value = struct.unpack_from("<I", data, thunk_offset)[0]
            if thunk_value == 0:
                break
            if thunk_value & 0x80000000:
                symbols.append(f"#{thunk_value & 0xffff}")
                continue
            hint_offset = rva_to_file_offset(
                thunk_value,
                2,
                sections,
                label=f"{dll_name} import name {thunk_index}",
            )
            require_range(data, hint_offset, 2, label=f"{dll_name} import hint {thunk_index}")
            symbols.append(
                read_rva_c_string(
                    data,
                    thunk_value + 2,
                    sections,
                    label=f"{dll_name} import name {thunk_index}",
                )
            )
        else:
            raise ValidationError(f"{dll_name}: import thunk list exceeds 4096 entries")
        if len(symbols) != len(set(symbols)):
            raise ValidationError(f"{dll_name}: duplicate imported symbol")
        imports[dll_name] = symbols
    if not terminated:
        raise ValidationError("import directory has no null terminator descriptor")
    return imports


def read_pe_exports(
    data: bytes,
    pe: dict[str, Any],
    sections: list[dict[str, int | str]],
) -> list[str]:
    directory_rva, directory_size = pe_directory(pe, 0)
    if directory_rva == 0 or directory_size == 0:
        return []
    if directory_size < 40:
        raise ValidationError("export directory is smaller than IMAGE_EXPORT_DIRECTORY")
    directory_offset = rva_to_file_offset(directory_rva, 40, sections, label="export directory")
    fields = struct.unpack_from("<IIHHIIIIIII", data, directory_offset)
    function_count = int(fields[6])
    name_count = int(fields[7])
    names_rva = int(fields[9])
    ordinals_rva = int(fields[10])
    if name_count > 65536 or name_count > function_count:
        raise ValidationError("export directory has invalid function/name counts")
    if name_count == 0:
        return []
    names_offset = rva_to_file_offset(names_rva, name_count * 4, sections, label="export name table")
    ordinals_offset = rva_to_file_offset(
        ordinals_rva,
        name_count * 2,
        sections,
        label="export ordinal table",
    )
    exports: list[str] = []
    for index in range(name_count):
        name_rva = struct.unpack_from("<I", data, names_offset + index * 4)[0]
        ordinal = struct.unpack_from("<H", data, ordinals_offset + index * 2)[0]
        if ordinal >= function_count:
            raise ValidationError(f"export name {index} has an out-of-range ordinal")
        exports.append(read_rva_c_string(data, name_rva, sections, label=f"export name {index}"))
    if len(exports) != len(set(exports)):
        raise ValidationError("export table contains duplicate names")
    return exports


def validate_target(path: Path, catalogue: dict[str, Any]) -> dict[str, object]:
    data, pe, sections = read_pe(path)
    target = catalogue["target"]
    expected_sha256 = str(target["sha256"]).lower()
    expected_image_base = int(target["_image_base"])
    expected_image_size = int(target["_image_size"])
    digest = hashlib.sha256(data).hexdigest()
    if digest != expected_sha256:
        raise ValidationError(f"{path}: SHA-256 {digest} != required {expected_sha256}")
    if pe["machine"] != 0x14C:
        raise ValidationError(f"{path}: machine 0x{pe['machine']:04x} is not i386")
    if pe["image_base"] != expected_image_base or pe["size_of_image"] != expected_image_size:
        raise ValidationError(
            f"{path}: unexpected image base/size 0x{pe['image_base']:08x}/0x{pe['size_of_image']:x}"
        )
    anchors: list[dict[str, object]] = []
    for hook in catalogue["hooks"]:
        address = int(hook["_address"])
        expected = bytes(hook["_stolen"])
        offset = va_to_file_offset(address, len(expected), pe["image_base"], sections)
        actual = data[offset : offset + len(expected)]
        if actual != expected:
            raise ValidationError(
                f"{hook['_name']} at 0x{address:08x}: {actual.hex(' ')} != {expected.hex(' ')}"
            )
        relocation: dict[str, object] | None = None
        if int(hook["_relocation_code"]) != 0:
            relocation = {
                "kind": "call_rel32",
                "offset": int(hook["_relocation_offset"]),
                "target": f"0x{int(hook['_relocation_target']):08x}",
            }
        anchors.append(
            {
                "id": int(hook["_id"]),
                "name": str(hook["_name"]),
                "address": f"0x{address:08x}",
                "bytes": expected.hex(),
                "instruction_lengths": list(hook["_instruction_lengths"]),
                "relocation": relocation,
            }
        )
    return {
        "target": str(path),
        "sha256": digest,
        "machine": "i386",
        "image_base": f"0x{pe['image_base']:08x}",
        "image_size": pe["size_of_image"],
        "catalogue_sha256": catalogue["_raw_sha256"].hex(),
        "anchor_count": len(anchors),
        "anchors": anchors,
    }


def validate_loader(path: Path) -> dict[str, object]:
    data, pe, sections = read_pe(path)
    digest = hashlib.sha256(data).hexdigest()
    if digest != AUDITED_ULTIMATE_ASI_LOADER_SHA256:
        raise ValidationError(
            f"{path}: SHA-256 {digest} != audited Ultimate ASI Loader "
            f"{AUDITED_ULTIMATE_ASI_LOADER_SHA256}"
        )
    if pe["machine"] != 0x14C or not pe["characteristics"] & 0x2000:
        raise ValidationError(f"{path}: loader must be an i386 DLL")
    exports = read_pe_exports(data, pe, sections)
    if "IsUltimateASILoader" not in exports or b"InitializeASI\0" not in data:
        raise ValidationError(
            f"{path}: loader does not expose the Ultimate ASI Loader identity and InitializeASI callback contract"
        )
    return {
        "loader": str(path),
        "sha256": digest,
        "machine": "i386",
        "dll": True,
        "ultimate_asi_loader": True,
        "initialize_asi_callback": True,
    }


LOADER_INTEGER_KEYS = {
    "dontloadfromdllmain",
    "forceentrypointhook",
    "loadplugins",
}


def read_loader_integer_settings(path: Path) -> dict[str, int]:
    data = path.read_bytes()
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        text = data.decode("utf-16")
    else:
        try:
            text = data.decode("utf-8-sig")
        except UnicodeDecodeError:
            text = data.decode("cp1252")
    section = ""
    values: dict[str, int] = {}
    for line_number, original in enumerate(text.splitlines(), 1):
        line = original.strip()
        if not line or line.startswith((";", "#")):
            continue
        if line.startswith("["):
            if not line.endswith("]"):
                raise ValidationError(f"{path}:{line_number}: malformed INI section")
            section = line[1:-1].strip().casefold()
            continue
        if section != "globalsets" or "=" not in line:
            continue
        raw_key, raw_value = line.split("=", 1)
        key = raw_key.strip().casefold()
        if key not in LOADER_INTEGER_KEYS:
            continue
        if key in values:
            raise ValidationError(f"{path}:{line_number}: duplicate [globalsets] {key}")
        value_text = re.split(r"[;#]", raw_value, maxsplit=1)[0].strip()
        if re.fullmatch(r"[+-]?[0-9]+", value_text) is None:
            raise ValidationError(f"{path}:{line_number}: invalid integer for [globalsets] {key}")
        value = int(value_text, 10)
        if value < -0x80000000 or value > 0x7FFFFFFF:
            raise ValidationError(f"{path}:{line_number}: out-of-range integer for [globalsets] {key}")
        values[key] = value
    return values


def validate_loader_configuration(root: Path, loader_name: str) -> dict[str, object]:
    if not root.is_dir():
        raise ValidationError(f"{root}: loader configuration root is not a directory")
    relative_paths = (
        Path(loader_name).with_suffix(".ini"),
        Path("global.ini"),
        Path("scripts/global.ini"),
        Path("plugins/global.ini"),
        Path("update/global.ini"),
    )
    effective = {
        "dontloadfromdllmain": 1,
        "forceentrypointhook": 0,
        "loadplugins": 1,
    }
    loaded_paths: list[str] = []
    for relative in relative_paths:
        path = root / relative
        if not path.is_file():
            continue
        effective.update(read_loader_integer_settings(path))
        loaded_paths.append(relative.as_posix())
    if effective["loadplugins"] == 0:
        raise ValidationError(
            f"{root}: Ultimate ASI Loader plugin loading is disabled; NFSMWAITrace cannot initialize"
        )
    if effective["dontloadfromdllmain"] == 0 and effective["forceentrypointhook"] == 0:
        raise ValidationError(
            f"{root}: Ultimate ASI Loader is configured to load plugins from DllMain; "
            "NFSMWAITrace requires deferred plugin loading for its synchronous InitializeASI gate"
        )
    return {
        "root": str(root),
        "files_read": loaded_paths,
        "effective": effective,
        "plugin_loading_enabled": True,
        "deferred_plugin_loading": True,
    }


def validate_asi(path: Path, catalogue: dict[str, Any]) -> dict[str, object]:
    data, pe, sections = read_pe(path)
    target_digest = bytes.fromhex(str(catalogue["target"]["sha256"]))
    ascii_digest = str(catalogue["target"]["sha256"]).encode("ascii")
    catalogue_digest = bytes(catalogue["_raw_sha256"])
    if pe["machine"] != 0x14C:
        raise ValidationError(f"{path}: tracer must be i386")
    if not pe["characteristics"] & 0x2000:
        raise ValidationError(f"{path}: tracer is not marked as a DLL")
    required_dll_characteristics = 0x0040 | 0x0100
    if pe["dll_characteristics"] & required_dll_characteristics != required_dll_characteristics:
        raise ValidationError(f"{path}: tracer must enable DYNAMIC_BASE and NX_COMPAT")
    relocation_rva, relocation_size = pe_directory(pe, 5)
    if relocation_rva == 0 or relocation_size == 0:
        raise ValidationError(f"{path}: DYNAMIC_BASE tracer has no base-relocation directory")
    for required in (b"NFSMWAITrace", b"0.4.2", target_digest, catalogue_digest):
        if required not in data:
            raise ValidationError(f"{path}: expected schema-4 identity material is missing")
    if ascii_digest in data:
        raise ValidationError(f"{path}: target digest must be embedded as raw bytes, not ASCII text")
    imports = read_pe_imports(data, pe, sections)
    kernel32 = next((symbols for name, symbols in imports.items() if name.casefold() == "kernel32.dll"), None)
    if kernel32 is None:
        raise ValidationError(f"{path}: KERNEL32.dll import descriptor is missing")
    required_kernel32 = {
        "CreateFileA",
        "CreateEventA",
        "CreateThread",
        "CreateToolhelp32Snapshot",
        "FlushFileBuffers",
        "FlushInstructionCache",
        "GetModuleHandleExA",
        "GetThreadContext",
        "OpenThread",
        "ReadProcessMemory",
        "ResumeThread",
        "SetEndOfFile",
        "SetFilePointerEx",
        "SuspendThread",
        "Thread32First",
        "Thread32Next",
        "VirtualProtect",
        "WaitForSingleObject",
        "WriteFile",
    }
    missing_imports = sorted(required_kernel32 - set(kernel32))
    if missing_imports:
        raise ValidationError(f"{path}: required KERNEL32 imports are missing: {', '.join(missing_imports)}")
    text_section = next((section for section in sections if section["name"] == ".text"), None)
    if text_section is None:
        raise ValidationError(f"{path}: tracer has no .text section")
    text_start = int(text_section["raw_offset"])
    text_end = text_start + int(text_section["raw_size"])
    atomic_patch_opcode_count = data[text_start:text_end].count(b"\xf0\x0f\xc7")
    if atomic_patch_opcode_count < 2:
        raise ValidationError(f"{path}: atomic hook install/rollback instructions are missing")
    exports = read_pe_exports(data, pe, sections)
    if "InitializeASI" not in exports:
        raise ValidationError(f"{path}: required InitializeASI loader callback is not exported")
    forbidden_exports = sorted(name for name in exports if "trace_enter" in name.casefold())
    if forbidden_exports:
        raise ValidationError(f"{path}: internal trace entry point is exported: {', '.join(forbidden_exports)}")
    return {
        "asi": str(path),
        "sha256": hashlib.sha256(data).hexdigest(),
        "machine": "i386",
        "dll": True,
        "dynamic_base": True,
        "nx_compat": True,
        "base_relocations": {"rva": f"0x{relocation_rva:08x}", "size": relocation_size},
        "image_base": f"0x{pe['image_base']:08x}",
        "image_size": pe["size_of_image"],
        "catalogue_sha256": catalogue_digest.hex(),
        "sections": [section["name"] for section in sections],
        "imports": {name: symbols for name, symbols in sorted(imports.items(), key=lambda item: item[0].casefold())},
        "exports": exports,
        "initialize_asi_exported": True,
        "internal_trace_entry_exported": False,
        "atomic_patch_instruction": "lock cmpxchg8b",
        "atomic_patch_opcode_count": atomic_patch_opcode_count,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate the exact NFSMW PC 1.3 target and schema-4 ASI.")
    parser.add_argument("--target", type=Path)
    parser.add_argument("--asi", type=Path)
    parser.add_argument("--loader", type=Path)
    parser.add_argument("--loader-config-root", type=Path)
    parser.add_argument("--catalogue", type=Path, default=CATALOGUE_PATH)
    args = parser.parse_args()
    if args.target is None and args.asi is None and args.loader is None:
        parser.error("provide --target, --asi, and/or --loader")
    if (args.loader is None) != (args.loader_config_root is None):
        parser.error("--loader and --loader-config-root must be provided together")
    result: dict[str, object] = {}
    try:
        catalogue = load_catalogue(args.catalogue)
        if args.target is not None:
            result["target"] = validate_target(args.target, catalogue)
        if args.asi is not None:
            result["asi"] = validate_asi(args.asi, catalogue)
        if args.loader is not None:
            loader_result = validate_loader(args.loader)
            loader_result["configuration"] = validate_loader_configuration(
                args.loader_config_root,
                args.loader.name,
            )
            result["loader"] = loader_result
    except (CatalogueError, OSError, UnicodeError, struct.error, ValidationError) as exc:
        parser.exit(1, f"validation failed: {exc}\n")
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
