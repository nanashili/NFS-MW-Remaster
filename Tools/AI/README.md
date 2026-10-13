# Need for Speed: Most Wanted PC 1.3 full zAI recovery ledger

This directory contains the deterministic evidence ledger and recovered
behavioral contract for the complete PC 1.3 `zAI` object unit. It covers the
shared vehicle runtime, goals/actions, racers, traffic, cops/pursuits,
helicopter AI, spawn admission, targeting, steering, road navigation, GPS,
messages, attributes, and engine-interface seams.

It is an analysis package, not a replacement Unity AI runtime and not a claim
of full retail equivalence.

## Current result

- The installed CrossOver `speed.exe` and corpus oracle are byte-identical:
  SHA-256 `80774c2e5d619b4f120b48d4462896fd504c263399d203a238769cffde1d253c`.
- The complete preferred-image zAI range is
  `[0x00401050, 0x00446a8f)`: 250,735 code bytes and 3,696 functions.
- The current v2 ledger classifies 556 exact, 205 near, 128 partial, and 2,807
  unmatched functions.
- It identifies 664 zAI/adjacent contracts, 75 engine contracts, and 227
  dependencies while retaining 2,730 unidentified-original-code gaps.
- All 53 recovered AI source/header files, 36 translation-unit includes, input
  artifacts, source candidates, owners, recovery markers, and selected
  architecture anchors have fixity metadata.
- Exact anchors include construction/reset/simulation actuation, action
  selection, racer and traffic lifecycle points, target tracking, manager task
  dispatch, and multiple pursuit boundaries.
- A controlled 291.253-second CrossOver gameplay run produced 15,386 contiguous
  records and 7,693 exact entry/exit pairs with no recorded drops or pairing
  errors. It exercised vehicle think/simulate, goal choice, traffic and normal
  spawn, pursuit-vehicle spawn, pursuit tasks, and formation updates.
- Tracer 0.3.4/schema 3 is statically ready for controlled follow-up runs. Its
  exact-target catalogue covers 48 entry hooks through eight profile presets,
  records bounded raw regions with explicit validity, and embeds manifest,
  counter, target/config/catalogue, header, and per-record integrity data. It
  has not been installed or run, so it contributes no schema-3 gameplay values
  yet.
- Hard gaps still include the 2,730-function unidentified surface, stripped
  racer/math/steering functions, the pull-over discrepancy, controller recovery
  defects, full scheduler policy, and gameplay paths outside those nine traced
  anchors. The broader schema-3 catalogue makes those paths observable; only
  controlled profile captures can close them.

Read [`AI_SYSTEM_CONTRACT.md`](AI_SYSTEM_CONTRACT.md) before using the ledger.
Police/pursuit internals have a deeper companion contract at
[`../Pursuit/PORT_CONTRACT.md`](../Pursuit/PORT_CONTRACT.md).

## Rebuild without overwriting the snapshot

The extractor uses Python's standard library. It consumes the PC 1.3
`report.json`, `symbols.json`, and `splits.json`, walks the complete recovered AI
source set, joins every report entry to its preferred-image symbol under a hard
name-and-size invariant, hashes the inputs, and optionally verifies the
installed oracle.

```bash
ZAI_DECOMP='/Users/tihan-nico/.codex/worktrees/1a8f/NFS MW Remaster/Library/PCDecompilation/nfsmw-pc-match'
ZAI_ORACLE='/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted/speed.exe'
ZAI_VERIFY_DIR="$(mktemp -d)"

python3 Tools/AI/extract_zai_evidence.py "$ZAI_DECOMP" \
  --oracle "$ZAI_ORACLE" \
  --output "$ZAI_VERIFY_DIR/zai-evidence.json"

python3 -m json.tool "$ZAI_VERIFY_DIR/zai-evidence.json" >/dev/null
cmp -s Tools/AI/zai-evidence.json "$ZAI_VERIFY_DIR/zai-evidence.json"

jq -S 'del(.input_manifest_sha256,.inputs)' \
  Tools/AI/zai-evidence.json >"$ZAI_VERIFY_DIR/frozen-semantic.json"
jq -S 'del(.input_manifest_sha256,.inputs)' \
  "$ZAI_VERIFY_DIR/zai-evidence.json" >"$ZAI_VERIFY_DIR/fresh-semantic.json"
cmp -s "$ZAI_VERIFY_DIR/frozen-semantic.json" \
  "$ZAI_VERIFY_DIR/fresh-semantic.json"
```

The decompilation worktree and generated objdiff report are active research
inputs and may move independently of this project. The checked
`zai-evidence.json` is the current v2 snapshot used by the report. The case also
preserves the first capture as `zai-evidence-v1.json`. If regeneration differs,
compare input/source hashes and semantic records; do not overwrite the checked
snapshot without treating the result as a new evidence version.

The first `cmp` requires the exact frozen top-level inputs. A later verification
observed another `report.json` hash with the same size and an otherwise
byte-identical ledger: only `.input_manifest_sha256` and `.inputs` differed.
The second comparison is the recorded semantic-projection check for that known
provenance-only drift; it must not be used to ignore function, source, anchor,
metric, or gap changes.

## Read the ledger

Show the unit boundary and recovery totals:

```bash
jq '{binary_identity, unit, inventory_summary, gap_summary, claim_boundary}' \
  Tools/AI/zai-evidence.json
```

Show subsystem coverage:

```bash
jq -r '.subsystems[] |
  [.name, .function_count, .code_bytes,
   .weighted_fuzzy_match_percent, (.recovery_classes | tojson)] | @tsv' \
  Tools/AI/zai-evidence.json
```

Show selected architecture anchors:

```bash
jq -r '.anchors[] | .matches[] |
  [.qualified_callable, .va_start, .size,
   .fuzzy_match_percent, .recovery_class] | @tsv' \
  Tools/AI/zai-evidence.json
```

Show the largest identified unmatched functions:

```bash
jq -r '.gap_summary.largest_identified_unmatched[] |
  [.demangled_name, .va_start, .size, .subsystem] | @tsv' \
  Tools/AI/zai-evidence.json
```

Show every source recovery marker:

```bash
jq -r '.source_recovery_markers[] |
  [.path, .line, .excerpt] | @tsv' Tools/AI/zai-evidence.json
```

## Confidence classes

| Class | Rule | Claim boundary |
|---|---:|---|
| `exact_binary_match` | exactly 100% | Reported function machine code matches the verified oracle. |
| `near_match` | 95% to below 100% | High-confidence behavior; inspect differences before identity claims. |
| `partial_match` | above 0% to below 95% | Candidate structure/constants; keep replaceable and calibrate. |
| `unmatched` | 0% | Fidelity gap; do not invent a substitute. |

The percentage is objdiff function similarity, not probability. `va_start` and
`va_end_exclusive` are absolute preferred-image addresses. A
`report_unit_offset` is an object-report offset and must not be presented as an
executable address.

## Snapshot fixity

| Artifact | SHA-256 |
|---|---|
| `extract_zai_evidence.py` | `408ebb74ae7db87960295daee1f3a3e36fee107a76fc1d4fbeda8ee472fd891e` |
| current `zai-evidence.json` (v2) | `c39a0709a8a42e0bb69b98a34e2862ceb43f247ba3fd220b6964ee2473a6f3ae` |
| source manifest | `8e1246ce48e476dbfcc58369ef5533f691be4f09b065bd6ee8c4a1c4e1be9c35` |
| current input manifest | `aedd969dd0f922e47b13494064d5a3a39e4bd2ea1225ac81487b1df871a2b331` |
| preserved case snapshot v1 | `4cf046636d4d0634973b4bb32e6e5efff728b21c48ed8485e1955cb4620d0735` |
| v1 input manifest recorded by the artifact | `8ca06e12bfb78c6de9c3c901f6cbdfa968061e373e2163d81c235eef16e84825` |

Between v1 and v2, only the active `report.json` input changed among the four
top-level extractor inputs. The complete AI source manifest, symbols, splits,
source-list file, executable identity, function count, range, and unidentified
surface stayed fixed. The evidence case records that supersession explicitly.

## Files

| File | Purpose |
|---|---|
| `extract_zai_evidence.py` | Deterministic full-unit extractor, join validator, classifier, and hasher. |
| `zai-evidence.json` | Current v2 address/source/owner/gap evidence snapshot. |
| `AI_SYSTEM_CONTRACT.md` | Recovered architecture, algorithms, interfaces, gaps, and port gates. |
| `nfsmw-ai-callflow.mmd` | Editable full-system Mermaid callflow. |
| `nfsmw-ai-vehicle-cycle.mmd` | Editable per-vehicle lifecycle/state diagram. |
| `RuntimeTrace/` | Schema 1/2/3 source, exact-target build/validation, decoder, tests, profile workflow, and the fixed controlled gameplay capture. |

No original game executable or decompilation source is copied into this
directory. The
extractor and documentation do not edit the game, corpus, or Unity runtime. The
corrected schema-2 observation ASI is installed separately under the
owner-operated game directory; it does not patch `speed.exe` on disk or modify
Unity runtime code. The schema-3 ASI is built in the project only and remains
uninstalled; its checked configuration remains disabled pending controlled
profile runs.
