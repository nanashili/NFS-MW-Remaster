#!/usr/bin/env python3
"""Propose safe target-to-base symbol mappings from unique identical bodies."""

import argparse
import json
from collections import defaultdict
from pathlib import Path


def body(symbol):
    instructions = symbol.get("instructions") or []
    formatted = []
    for entry in instructions:
        instruction = entry.get("instruction")
        if instruction is None:
            return None
        formatted.append(instruction.get("formatted", ""))
    return tuple(formatted) if formatted else None


def opcode_body(symbol):
    opcodes = []
    for entry in symbol.get("instructions") or []:
        instruction = entry.get("instruction")
        if instruction is None:
            return None
        opcode = next((p.get("opcode") for p in instruction.get("parts", []) if p.get("opcode")), None)
        if opcode is None:
            return None
        opcodes.append(opcode["mnemonic"])
    return tuple(opcodes) if opcodes else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("diff", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--project-template", type=Path)
    parser.add_argument("--project-output", type=Path)
    parser.add_argument("--unit")
    parser.add_argument("--symbols", type=Path)
    parser.add_argument("--symbols-patch", type=Path)
    parser.add_argument("--relaxed-min-instructions", type=int, default=0)
    parser.add_argument("--target-prefix")
    args = parser.parse_args()

    data = json.loads(args.diff.read_text())
    left = [s for s in data["left"]["symbols"] if s.get("kind") == "SYMBOL_FUNCTION"]
    right = [s for s in data["right"]["symbols"] if s.get("kind") == "SYMBOL_FUNCTION"]

    left_by_body = defaultdict(list)
    right_by_body = defaultdict(list)
    for symbol in left:
        if symbol.get("target_symbol") is None and (key := body(symbol)):
            left_by_body[key].append(symbol)
    for symbol in right:
        if symbol.get("target_symbol") is None and (key := body(symbol)):
            right_by_body[key].append(symbol)

    mappings = {}
    for key, left_symbols in left_by_body.items():
        right_symbols = right_by_body.get(key, [])
        if len(left_symbols) == 1 and len(right_symbols) == 1:
            target = left_symbols[0]
            base = right_symbols[0]
            if target.get("size") == base.get("size"):
                mappings[target["name"]] = base["name"]

    if args.relaxed_min_instructions:
        left_by_opcodes = defaultdict(list)
        right_by_opcodes = defaultdict(list)
        for symbol in left:
            key = opcode_body(symbol)
            if symbol["name"] not in mappings and key and len(key) >= args.relaxed_min_instructions:
                left_by_opcodes[(symbol.get("size"), key)].append(symbol)
        used_base_names = set(mappings.values())
        for symbol in right:
            key = opcode_body(symbol)
            if symbol["name"] not in used_base_names and key and len(key) >= args.relaxed_min_instructions:
                right_by_opcodes[(symbol.get("size"), key)].append(symbol)
        for key, left_symbols in left_by_opcodes.items():
            right_symbols = right_by_opcodes.get(key, [])
            if len(left_symbols) == 1 and len(right_symbols) == 1:
                mappings[left_symbols[0]["name"]] = right_symbols[0]["name"]

    if args.target_prefix:
        mappings = {key: value for key, value in mappings.items() if key.startswith(args.target_prefix)}

    args.output.write_text(json.dumps(mappings, indent=2, sort_keys=True) + "\n")
    print(f"proposed {len(mappings)} unique exact mappings")

    if args.project_template or args.project_output or args.unit:
        if not (args.project_template and args.project_output and args.unit):
            parser.error("project output requires --project-template, --project-output, and --unit")
        project = json.loads(args.project_template.read_text())
        root = args.project_template.parent.resolve()
        matched = False
        for unit in project["units"]:
            if unit["name"] == args.unit:
                unit["target_path"] = str(root / unit["target_path"])
                unit["base_path"] = str(root / unit["base_path"])
                unit["symbol_mappings"] = mappings
                matched = True
                break
        if not matched:
            parser.error(f"unit not found: {args.unit}")
        args.project_output.parent.mkdir(parents=True, exist_ok=True)
        args.project_output.write_text(json.dumps(project, indent=2) + "\n")

    if args.symbols or args.symbols_patch:
        if not (args.symbols and args.symbols_patch):
            parser.error("symbol patch output requires --symbols and --symbols-patch")
        old_lines = args.symbols.read_text().splitlines()
        new_lines = []
        changed = 0
        for line in old_lines:
            fields = line.split(maxsplit=1)
            replacement = mappings.get(fields[0]) if fields else None
            if replacement:
                new_lines.append(replacement + " " + fields[1])
                changed += 1
            else:
                new_lines.append(line)
        if changed != len(mappings):
            parser.error(f"only found {changed} of {len(mappings)} mapped symbols")
        patch = ["*** Begin Patch", f"*** Update File: {args.symbols.resolve()}"]
        for old, new in zip(old_lines, new_lines):
            if old != new:
                patch.extend(["@@", f"-{old}", f"+{new}"])
        patch.append("*** End Patch")
        args.symbols_patch.write_text("\n".join(patch) + "\n")
        print(f"wrote patch for {changed} symbols")


if __name__ == "__main__":
    main()
