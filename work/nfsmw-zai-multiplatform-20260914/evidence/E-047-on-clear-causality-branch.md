# E-047: AIVehicle causality-clear branch shape

## Binary evidence

- `AIVehicle::OnClearCausality(float)` is a 68-byte function on both PS2
  targets.
- Retail lays out the true result as the branch target and the false result as
  fall-through. A direct C++ boolean return produced the inverse branch shape.

## Reconstruction

- Rewrote the common return as an explicit `if (...) return true; return
  false;` branch, preserving the predicate and behavior.

## Validation

- SLES function: 84.70588% -> 100.0%, exact 68/68 bytes.
- SLES zAI `.text`: 90.493835% -> 90.497574%.
- SLUS function: 84.70588% -> 99.70588%; the only remaining byte-level
  difference is the unresolved retail `GetTime` symbol label.
- SLUS zAI `.text`: 86.1339% -> 86.13762%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v93.json` and
`/private/tmp/slus-zai-layout-v31.json`.
