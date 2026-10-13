# E-036: PS2 VU0 three-vector add-scale primitive

## Binary evidence

- SLES `AIVehicleHelicopter::SetDestinationVelocity` is at `0x123ed0` and
  contains an inlined seven-instruction VU sequence for `AddScale`.
- The PS2 `UVectorMath.hpp` reconstruction had no inline
  `VU0_v3addscale`; calls therefore fell back to an external function and
  could not reproduce either PS2 retail object.
- The instruction sequence loads both input vectors and the output vector,
  transfers the scalar to `vf4`, adds the input vectors, scales the sum, and
  stores the result.

## Reconstruction

- Restored the PS2 inline assembly implementation of `VU0_v3addscale`.
- Expressed the preceding destination-velocity scale through the equivalent
  two-argument in-place overload, matching the target source shape.

## Validation

- SLES `SetDestinationVelocity`: 57.407406% at 108/88 bytes -> 99.62963% at
  the exact 108/108-byte size.
- SLES zAI `.text`: 90.37911% -> 90.395515%.
- SLUS `SetDestinationVelocity`: 99.62963% at 108/108 bytes.
- SLUS zAI `.text`: 86.05584% -> 86.07213%.
- Both PS2 zAI objects build successfully with the historical EE GCC.

Artifacts: `/private/tmp/sles-zai-layout-v67.json` and
`/private/tmp/slus-zai-layout-v18.json`.
