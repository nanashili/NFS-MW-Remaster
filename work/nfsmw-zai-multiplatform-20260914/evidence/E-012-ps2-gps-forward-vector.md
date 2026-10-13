# E-012: PS2 GPS arrow forward-vector reconstruction

The SLES `Gps::Render` target instruction stream constructs the GPS arrow's
forward vector from unscaled cosine and sine values. The `0.2f` scale is
introduced by the following cross-product operations, rather than being
applied to the forward vector first.

The PS2-only source branch now preserves that order. The GameCube and PC/Xbox
branches are unchanged.

## Verification

- SLES EE-GCC build: passed.
- SLES `Gps::Render`: `44.607628% -> 47.185287%` direct objdiff.
- SLES target/current size: `1468 / 1592` bytes.
- SLES whole mapped non-section function-and-object weighted result:
  `90.44037087961117% -> 90.45418860709063%` over `281388` target bytes.
- SLUS EE-GCC build: passed after the shared A124 `pursuitlevels` layout change.
- SLUS `AIPursuit::OnTask` remains `93.97875%` at `5836 / 5852` bytes.
- SLUS `Gps::Render` is now `51.612904%` at `1488 / 1592` bytes.

Ghidra 12.1.3 headless located the SLES function at object address `0x44640`.
Its decompiler stopped at unsupported relocated instruction data, so the direct
objdiff instruction stream remains the authoritative evidence for this change.

Artifacts: `/private/tmp/sles-gps-render-v7.json`,
`/private/tmp/sles-zai-layout-v9.json`, `/private/tmp/slus-gps-render-v7.json`,
and `/private/tmp/slus-ontask-layout-v7.json`.
