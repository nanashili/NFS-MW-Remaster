# E-039: PS2 AITarget null-target return path

## Binary evidence

- SLES `AITarget::IsTarget(const AITarget *)` is at `0x126768` and is exactly
  160 bytes.
- The retail function gives the null-pointer case its own immediate false
  return, then shares a later false return for invalid targets.
- The prior combined condition merged all false cases and produced different
  branches and delay-slot scheduling.

## Reconstruction

- Split the null-pointer check from the validity checks.
- Added a PS2-only empty compiler barrier on the null path so the retail early
  return remains distinct in the source-list build.

## Validation

- SLES function: 69.45% -> 100.0%, exact 160/160-byte match.
- SLES zAI `.text`: 90.42033% -> 90.437904%.
- SLUS function: 69.45% -> 100.0%, exact 160/160-byte match.
- SLUS zAI `.text`: 86.09678% -> 86.11424%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v74.json` and
`/private/tmp/slus-zai-layout-v22.json`.
