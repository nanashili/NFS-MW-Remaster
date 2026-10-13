# E-048: pursuit and ram attempt-gate control flow

## Binary evidence

- `AIActionPursuitOffRoad::CanBeAttempted(float)` and
  `AIActionRam::CanBeAttempted(float)` are each 124-byte functions.
- The retail sequence checks the four interface pointers in member-layout
  order, rejects pursuit chicken mode, then calls `ShouldDoIt`. The prior
  combined positive condition caused a different branch layout.

## Reconstruction

- Replaced the combined pointer conjunction in both functions with explicit
  null early returns.
- Preserved the chicken rejection and used an explicit true branch around
  `ShouldDoIt` to retain the retail result layout.

## Validation

- SLES pursuit function: 86.129036% -> 99.83871%, exact 124-byte size.
- SLES ram function: 86.129036% -> 99.83871%, exact 124-byte size.
- SLES zAI `.text`: 90.497574% -> 90.5098%.
- SLUS pursuit function: 86.129036% -> 99.83871%.
- SLUS ram function: 86.129036% -> 99.83871%.
- SLUS zAI `.text`: 86.13762% -> 86.149765%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v96.json` and
`/private/tmp/slus-zai-layout-v33.json`.
