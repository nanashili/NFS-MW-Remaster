# E-049: head-on ram attempt-gate control flow

## Binary evidence

- `AIActionHeadOnRam::CanBeAttempted(float)` is a 172-byte function on both
  PS2 targets.
- Retail checks each interface pointer as an independent early rejection and
  uses negative early returns for both drivable-state predicates. The previous
  nested positive form inverted several branch encodings.

## Reconstruction

- Expanded the pointer conjunction into ordered null checks.
- Rewrote the drivable checks as explicit negative early returns, retaining the
  final chicken-mode predicate.

## Validation

- SLES function: 90.23256% -> 100.0%, exact 172/172 bytes.
- SLES zAI `.text`: 90.5098% -> 90.51585%.
- SLUS function: 90.23256% -> 100.0%, exact 172/172 bytes.
- SLUS zAI `.text`: 86.149765% -> 86.15577%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v97.json` and
`/private/tmp/slus-zai-layout-v34.json`.
