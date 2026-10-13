# Need for Speed: Most Wanted pursuit recovery ledger

This directory turns the verified PC 1.3 decompilation match report into a
deterministic, address-bearing evidence ledger for the cops and pursuit system.
It is a reverse-engineering artifact, not a claim that the full retail system
has already been reproduced in Unity.

## Current result

- The installed CrossOver `speed.exe` and the corpus oracle are byte-identical:
  SHA-256 `80774c2e5d619b4f120b48d4462896fd504c263399d203a238769cffde1d253c`.
- The checked ledger contains 354 functions across 11 pursuit-related
  subsystems, plus 39 mapped key-function anchors and 5 explicit unmapped
  key-function gaps.
- Spike-strip behavior is the strongest recovered subsystem: six reported
  functions, 956 bytes, 100% weighted match.
- The central pursuit lifecycle is broadly recovered, but scheduler timing,
  normal named-car spawn placement, infractions, several hooks, and controlled
  pursuit runtime behavior are not complete.

Read [PORT_CONTRACT.md](PORT_CONTRACT.md) before implementing against the
ledger. The full analysis and evidence boundaries are in
[the reverse-engineering report](../../2026-09-11_reverse-engineering-nfsmw-pursuit-report.md).

## Rebuild the ledger

The generator uses only Python's standard library. It reads the PC 1.3
`report.json`, `symbols.json`, and `splits.json`, hashes every selected source,
joins report functions to preferred-image virtual addresses, then verifies the
installed oracle when supplied.

```bash
PURS_DECOMP='/Users/tihan-nico/.codex/worktrees/1a8f/NFS MW Remaster/Library/PCDecompilation/nfsmw-pc-match'
PURS_ORACLE='/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted/speed.exe'
PURS_TMP="$(mktemp -d)"

python3 Tools/Pursuit/extract_pursuit_evidence.py "$PURS_DECOMP" \
  --oracle "$PURS_ORACLE" \
  --output "$PURS_TMP/pursuit-evidence.json"

jq '{binary_identity, subsystems, key_function_gaps}' \
  "$PURS_TMP/pursuit-evidence.json"
```

The decompilation worktree and its generated objdiff report are active research
inputs and may move independently of this project. The checked
`pursuit-evidence.json` is the frozen evidence snapshot used by the report. A
later rebuild should be reviewed as a new snapshot instead of overwriting the
meaning of the checked one.

## Read the ledger

Show the high-value function anchors:

```bash
jq -r '.key_functions[] |
  [.subsystem, .demangled_name, .va_start, .size,
   .fuzzy_match_percent, .recovery_class] | @tsv' \
  Tools/Pursuit/pursuit-evidence.json
```

Show subsystem coverage:

```bash
jq -r '.subsystems[] |
  [.name, .function_count, .code_bytes,
   .weighted_fuzzy_match_percent,
   (.function_classes | tojson)] | @tsv' \
  Tools/Pursuit/pursuit-evidence.json
```

Show unresolved key mappings:

```bash
jq -r '.key_function_gaps[]' Tools/Pursuit/pursuit-evidence.json
```

## Confidence classes

| Class | Rule | Permitted claim |
|---|---:|---|
| `exact_binary_match` | exactly 100% | The reported machine-code function matches the oracle; source semantics may be ported with focused tests. |
| `near_match` | 95% to below 100% | High-confidence behavioral contract; inspect differences and avoid instruction-identity claims. |
| `partial_match` | above 0% to below 95% | Candidate structure or constants only; isolate behind tests and runtime calibration. |
| `unmatched` | 0% | Fidelity gap; do not fill it with guessed behavior. |

The percentage is objdiff's per-function fuzzy match, not a probability. A
subsystem's value is byte-weighted. `va_start` and `va_end_exclusive` are
absolute preferred-image virtual addresses from `symbols.json`;
`report_unit_offset` is an objdiff unit offset and must not be presented as an
executable address.

## Files

| File | Purpose |
|---|---|
| `extract_pursuit_evidence.py` | Deterministic ledger generator and source/input hasher. |
| `pursuit-evidence.json` | Frozen PC 1.3 evidence snapshot used by the report. |
| `PORT_CONTRACT.md` | Confidence-bounded Unity implementation contract and test gates. |
| `nfsmw-pursuit-callflow.mmd` | Editable Mermaid source for the recovered system callflow. |

No game executable, decompilation source, or Unity police runtime file is
modified by this tool.
