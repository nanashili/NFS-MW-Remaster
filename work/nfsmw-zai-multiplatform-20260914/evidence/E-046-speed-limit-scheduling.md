# E-046: PS2 speed-limit expression scheduling

## Binary evidence

- SLES `GetSpeedLimit(float, float, float, float)` is exactly 92 bytes.
- Retail keeps the gravity product, absolute curvature, curvature force, and
  doubled curvature in a specific floating-point scheduling order.
- The source-list build's single expression scheduled those independent
  operations differently despite implementing the same formula.

## Reconstruction

- Split the PS2 calculation into named intermediate values.
- Added PS2-only compiler scheduling boundaries at the recovered dependency
  boundaries. Non-PS2 source and behavior remain unchanged.

## Validation

- SLES function: 82.17391% -> 91.304344%, exact 92/92-byte size.
- SLES zAI `.text`: 90.490814% -> 90.493835%.
- SLUS function: 82.17391% -> 91.304344%, exact 92/92-byte size.
- SLUS zAI `.text`: 86.130905% -> 86.1339%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v88.json` and
`/private/tmp/slus-zai-layout-v30.json`.
