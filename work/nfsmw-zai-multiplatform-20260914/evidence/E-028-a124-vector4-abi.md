# E-028: A124 `UMath::Vector4` ABI

The SLES instruction stream for `AIVehicle::WorldCollision` copies `Vector4`
aggregates with unaligned `ldl`/`ldr` and `sdl`/`sdr` sequences. The later PS2
source ABI instead gives the type 16-byte alignment and generates aligned
doubleword copies.

Restoring the older A124 contract by leaving `UMath::Vector4` naturally aligned
only under `EA_BUILD_A124` produces an exact `WorldCollision` match:

- before: 55.887417%, target/current 604/480 bytes
- after: 100%, target/current 604/604 bytes
- SLES zAI `.text`: 89.01699% -> 89.62933%

This is a shared ABI correction rather than a function-local workaround. The
whole SLES object improved substantially. The SLUS path keeps `ALIGNVEC`, builds
successfully, and remains at 86.05584% `.text` in the currently configured
full-object comparison.

Artifacts: `/private/tmp/sles-zai-layout-v36.json` and
`/private/tmp/slus-zai-layout-v7.json`.
