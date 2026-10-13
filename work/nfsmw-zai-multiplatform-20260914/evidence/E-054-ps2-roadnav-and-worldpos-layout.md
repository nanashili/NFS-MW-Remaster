# E-054: PS2 `WRoadNav` and `WWorldPos` layout

## Binary evidence

- The SLUS retail `AIVehicle` constructor allocates each `WRoadNav` at
  `0x350` bytes and places the second member at `this + 0x520`. The previous
  non-A124 reconstruction allocated `0x340` bytes and placed that member at
  `this + 0x510`; the resulting `0x20` base-size error shifted every
  helicopter field after the road-nav pair.
- A124 already selected the aligned `USpline` declaration. Applying that
  same alignment to PS2 makes `WRoadNav` use the retail `0x350` allocation and
  restores the `+0x20` derived-object offsets.
- The SLUS retail helicopter `Update` copies `WWorldPos` as eight aligned
  64-bit `ld`/`sd` pairs through offset `0x38`. The previous non-A124
  bitfield layout was `0x3C` bytes and emitted unaligned `ldl`/`ldr` and
  `sdl`/`sdr` instructions.

## Reconstruction

- `USpline` is now `ALIGN_16` for `EA_BUILD_A124` or
  `EA_PLATFORM_PLAYSTATION2`; other platforms retain the unaligned class.
- PS2 `WWorldPos` bitfields use an `unsigned long long` storage unit, making
  the non-A124 object `0x40` bytes and selecting the retail aligned copy
  sequence. Non-PS2, non-A124 builds retain the original 32-bit bitfield
  layout.

## Validation

- SLUS v53 baseline: zAI `.text` `88.21677%`; helicopter `Update` was
  `62.88889%` at a `524`-byte source body, with `0x910`/`0x914` field
  offsets and unaligned `WWorldPos` copying.
- With PS2 `USpline` alignment (v56): zAI `.text` `88.23374%`; the
  helicopter field offsets moved to the retail `0x930`/`0x934` positions.
- With the PS2 `WWorldPos` storage unit (v58): zAI `.text` `88.29549%`; both
  target and source helicopter `Update` bodies are `468` bytes and the
  function match is `99.82906%`. The copy instruction sequence is exact;
  only the retail/local symbol labels remain different.
- SLES v130 remains stable at zAI `.text` `91.50805%`, with
  `WorldCollision` at `100%` and helicopter `Update` at `93.82468%`.

Artifacts: `/private/tmp/slus-zai-layout-v53.json`,
`/private/tmp/slus-zai-layout-v56-uspline.json`,
`/private/tmp/slus-zai-layout-v58-uspline-wworldpos-clean.json`, and
`/private/tmp/sles-zai-layout-v130-uspline-wworldpos-clean.json`.
