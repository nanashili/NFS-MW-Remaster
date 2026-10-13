# E-038: PS2 AITarget copy-acquire call shape

## Binary evidence

- SLES `AITarget::Aquire(const AITarget *)` is at `0x126718` and is exactly
  80 bytes.
- Both successful branches use ordinary `jal` calls and then return through a
  shared epilogue.
- In the reconstructed zAI source list, EE GCC converted those terminal calls
  into tail jumps, producing a 64-byte body.

## Reconstruction

- Added PS2-only empty compiler barriers after both overload calls. This
  preserves behavior while preventing the amalgamated translation unit from
  introducing tail calls that are absent from the retail object.

## Validation

- SLES function: 64.5% at 64/80 bytes -> 99.5% at the exact 80/80-byte size.
- SLES zAI `.text`: 90.410255% -> 90.42033%.
- SLUS function: 64.5% at 64/80 bytes -> 99.5% at the exact 80/80-byte size.
- SLUS zAI `.text`: 86.08678% -> 86.09678%.
- Both PS2 zAI objects build successfully. Remaining function differences are
  call-relocation labels caused by source-list amalgamation; emitted call
  opcodes and instruction layout match.

Artifacts: `/private/tmp/sles-zai-layout-v73.json` and
`/private/tmp/slus-zai-layout-v20.json`.
