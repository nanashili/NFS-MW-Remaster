# E-040: PS2 AITarget simable-acquire early return

## Binary evidence

- SLES `AITarget::Aquire(ISimable *)` is at `0x1265b0` and is exactly 108
  bytes.
- After `CanAquire`, the retail function branches out on failure. The success
  path assigns the simable and validity flag, restores saved registers, and
  tail-jumps to `TrackInternal`.
- The prior positive nested condition generated a normal call and a 96-byte
  body in the combined zAI translation unit.

## Reconstruction

- Restored an explicit negative early return after `CanAquire`, leaving the
  successful acquisition as the terminal path.

## Validation

- SLES function: 80.703705% at 96/108 bytes -> 99.44444% at the exact
  108/108-byte size.
- SLES zAI `.text`: 90.437904% -> 90.44518%.
- SLUS function: 80.703705% at 96/108 bytes -> 99.44444% at the exact
  108/108-byte size.
- SLUS zAI `.text`: 86.11424% -> 86.121475%.
- Both PS2 zAI objects build successfully. Remaining differences are only
  source-list-local relocation labels for the three calls/jump.

Artifacts: `/private/tmp/sles-zai-layout-v75.json` and
`/private/tmp/slus-zai-layout-v23.json`.
