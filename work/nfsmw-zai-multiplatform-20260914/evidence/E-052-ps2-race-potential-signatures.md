# E-052: PS2 race-potential ABI and behavior split

## Binary evidence

- Both PS2 retail targets export `GetPotentialSpeed(float, float)` rather
  than the later drag-aware three-argument overload.
- Both PS2 targets export `GetPotentialAcceleration(float, float, bool)`;
  the later build adds the `is_drag` argument.
- The PS2 targets use the non-drag speed/acceleration tables and execute the
  curvature path without the later drag bypass.

## Reconstruction

- Added a PS2-only overload/signature branch using `EA_PLATFORM_PLAYSTATION2`.
- Kept the existing drag-aware overloads and tables for non-PS2 builds.
- Updated `AIActionRace::Update` to call the platform-correct overloads.

## Validation

- SLES `GetPotentialSpeed`: 0% unmapped -> 91.69376%, exact 2116-byte size.
- SLES `GetPotentialAcceleration`: 0% unmapped -> 84.760414%, exact
  768-byte size.
- SLES `AIActionRace::Update`: 86.17344% -> 84.98513% at 4036 bytes; the
  caller now has the retail two/three-argument call contract and remaining
  differences are inside its larger control-flow body.
- SLES zAI `.text`: 90.52912% -> 91.44376%.
- SLUS `GetPotentialSpeed`: 0% unmapped -> 90.64972%, exact 2124-byte size.
- SLUS `GetPotentialAcceleration`: 0% unmapped -> 90.84831%, exact 712-byte
  size.
- SLUS `AIActionRace::Update`: 99.03877% -> 96.987755% at 3920 bytes; this
  is the expected caller reshaping required by the retail ABI.
- SLUS zAI `.text`: 86.16895% -> 87.05911%.
- Both PS2 zAI objects build successfully in the isolated EE-GCC build.

Artifacts: `/private/tmp/sles-zai-layout-v112.json` and
`/private/tmp/slus-zai-layout-v38.json`.
