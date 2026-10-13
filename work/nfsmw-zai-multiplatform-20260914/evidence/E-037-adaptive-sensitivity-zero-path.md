# E-037: Adaptive-controller zero-derivative path

## Binary evidence

- SLES `AdaptivePIDControllerBase::GetSensitivityDerivative(float)` is at
  `0x125e48` and is exactly 180 bytes.
- The target keeps the coefficient-derivative input unchanged after deciding
  it is effectively zero. It compares the model-error derivative directly
  against `0.0f` and emits a distinct zero-return path.
- The prior source assigned zero back into the input parameter. That allowed
  EE GCC to reuse the parameter register and merge the return path, producing
  a 168-byte body not present in either retail PS2 binary.

## Reconstruction

- Removed the synthetic parameter assignment and restored direct comparisons
  against `0.0f`.

## Validation

- SLES function: 63.88889% at 180/168 bytes -> 86.666664% at the exact
  180/180-byte size.
- SLES zAI `.text`: 90.395515% -> 90.410255%.
- SLUS function: 86.666664% at 180/180 bytes.
- SLUS zAI `.text`: 86.07213% -> 86.08678%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v71.json` and
`/private/tmp/slus-zai-layout-v19.json`.
