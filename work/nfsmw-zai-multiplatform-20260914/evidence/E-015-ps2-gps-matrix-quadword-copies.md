# E-015: PS2 GPS render-matrix quadword copies

The PS2 target tail copies the complete right, up, forward, and position vectors
into the render matrix with 16-byte vector loads/stores, then overwrites each
matrix row's `w` lane. The previous reconstruction populated all twelve xyz
members with scalar stores.

The PS2 branch now expresses the target quadword copies through `bVector4`
assignment. Other platforms retain scalar member population.

## Verification

- SLES EE-GCC build: passed.
- SLES `Gps::Render`: `55.08992% -> 62.850136%` direct objdiff.
- SLES target/current size: `1468 / 1512` bytes.
- SLES whole mapped non-section function-and-object weighted result:
  `90.49335611372207% -> 90.53526156535469%` over `281388` target bytes.
- SLUS EE-GCC build: passed.
- SLUS `Gps::Render`: `59.41129% -> 65.13441%` direct objdiff.
- SLUS target/current size: `1488 / 1512` bytes.

Artifacts: `/private/tmp/sles-gps-render-v16.json`,
`/private/tmp/sles-zai-layout-v16.json`, and
`/private/tmp/slus-gps-render-v16.json`.
