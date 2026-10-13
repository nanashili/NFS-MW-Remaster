# E-051: traffic attempt-gate control flow

## Binary evidence

- `AIActionTraffic::CanBeAttempted(float)` is a 316-byte function on both
  PS2 targets.
- Retail performs ordered null rejection for the rigid body, AI, input, and
  vehicle interfaces before entering the navigation/pursuit logic. The
  combined source condition produced a different branch schedule.

## Reconstruction

- Replaced the combined guard with four ordered null early returns while
  preserving the existing navigation and pursuit predicates.

## Validation

- SLES function: 95.379745% -> 99.974686%, exact 316-byte size.
- SLES zAI `.text`: 90.5239% -> 90.52912%.
- SLUS function: 95.379745% -> 99.974686%, exact 316-byte size.
- SLUS zAI `.text`: 86.163765% -> 86.16895%.
- Both PS2 zAI objects build successfully.

The remaining function mismatch is a single compiler branch encoding; no
semantic difference was observed.

Artifacts: `/private/tmp/sles-zai-layout-v107.json` and
`/private/tmp/slus-zai-layout-v36.json`.
