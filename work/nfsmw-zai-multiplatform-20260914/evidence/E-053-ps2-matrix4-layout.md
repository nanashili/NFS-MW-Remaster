# E-053: PS2 `Matrix4` layout and copy ABI

## Binary evidence

- The SLUS retail `AIActionTraffic` constructor copies
  `UMath::Matrix4::kIdentity` with unaligned MIPS `ldl`/`ldr` loads and
  `sdl`/`sdr` stores. This is the same PS2 copy idiom used by the retail
  object at the constructor's matrix-copy offsets.
- Before this change, the reconstructed SLUS build selected the aligned
  `PS2ALIGN16 Matrix4` declaration and emitted aligned `ld`/`sd` pairs. The
  mismatch was therefore a source-level PS2 layout/ABI choice, not a symbol
  naming difference.
- The existing PS2 `Vector4` branch already selects the unaligned layout;
  `Matrix4` needed the same platform predicate.

## Reconstruction

- Changed `UTypes.h` so `Matrix4` uses the unaligned declaration whenever
  `EA_PLATFORM_PLAYSTATION2` is defined.
- Kept `PS2ALIGN16 Matrix4` for non-PS2 targets, including A124/PC/Xbox
  builds where the aligned declaration remains part of the reconstructed
  contract.

## Validation

- SLUS pre-change v47: zAI `.text` `282184` bytes, `87.99221%`; traffic
  constructor source size `680` bytes and match `63.913044%`.
- SLUS post-change v53: zAI `.text` `282184` bytes, `88.21677%`; traffic
  constructor source size `744` bytes and match `94.90761%` against the
  retail `736`-byte body. The emitted matrix copy now uses the retail
  `ldl`/`ldr` + `sdl`/`sdr` sequence.
- SLES post-change v129: zAI `.text` `280360` bytes, `91.50805%`; traffic
  constructor match `98.690216%`, `WorldCollision` match `100%`, and the
  helicopter `Update` match `93.82468%`.
- The remaining SLUS helicopter `Update` mismatch is separate: its retail
  body copies `WWorldPos` with aligned 64-bit chunks and has later object
  offsets. It is tracked as the next ABI investigation rather than being
  attributed to `Matrix4`.

Artifacts: `/private/tmp/slus-zai-layout-v53.json`,
`/private/tmp/sles-zai-layout-v129.json`, `/private/tmp/sles-inventory-now.tsv`,
`/private/tmp/ghidra-sles-worldcollision.txt`, and
`/private/tmp/ghidra-sles-worldcollision-listing.txt`.

Ghidra CLI inventory resolved the full SLES ELF base at `0x100000` and mapped
`WorldCollision` to `0x0011ac50`. The decompiler/listing stopped at the
retail image's bad-instruction boundary, so this evidence claims the symbol
and address mapping only, not a complete decompile.
