# E-045: PS2 curvature speed-limit operation order

## Binary evidence

- SLES `GetSpeedLimitForCurvature(float, float, float)` is at `0x1154a0`
  and is exactly 48 bytes.
- Retail computes the side force and minimum denominator before taking the
  absolute curvature. The combined source-list build scheduled the independent
  absolute-value operation earlier.

## Reconstruction

- Added a PS2-only compiler scheduling boundary after computing the minimum
  denominator. This preserves the recovered formula and retail operation order.

## Validation

- SLES function: 66.666664% -> 100.0%, exact 48/48-byte match.
- SLES zAI `.text`: 90.48506% -> 90.490814%.
- SLUS function: 66.666664% -> 100.0%, exact 48/48-byte match.
- SLUS zAI `.text`: 86.12519% -> 86.130905%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v86.json` and
`/private/tmp/slus-zai-layout-v29.json`.
