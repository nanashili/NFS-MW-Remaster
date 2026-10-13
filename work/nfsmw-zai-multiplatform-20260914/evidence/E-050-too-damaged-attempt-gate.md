# E-050: too-damaged attempt-gate control flow

## Binary evidence

- `AIActionTooDamaged::CanBeAttempted(float)` is a 68-byte function on both
  PS2 targets.
- Retail checks the vehicle and input interfaces as separate null-rejection
  branches before calling `IsDestroyed`. The combined positive condition
  emitted the opposite branch layout.

## Reconstruction

- Replaced the conjunction/`else` form with two ordered null early returns and
  the unchanged `IsDestroyed` result.

## Validation

- SLES function: 67.05882% -> 100.0%, exact 68/68 bytes.
- SLES zAI `.text`: 90.51585% -> 90.5239%.
- SLUS function: 67.05882% -> 100.0%, exact 68/68 bytes.
- SLUS zAI `.text`: 86.15577% -> 86.163765%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v100.json` and
`/private/tmp/slus-zai-layout-v35.json`.
