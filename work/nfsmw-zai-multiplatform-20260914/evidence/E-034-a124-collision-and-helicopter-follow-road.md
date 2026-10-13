# E-034: A124 collision ordering and helicopter follow-road pursuit

## Binary evidence

- SLES `AIPerpVehicle::OnCausedCollision` checks `IVehicle::IsDestroyed`
  before querying `IPursuitAI`, calculates collision cost before the police
  damage-reporting block, and only then evaluates pursuit acquisition.
- SLES contains the otherwise missing
  `AIActionHeliPursuit::FollowRoadPursuit` at `0x110b08` (492 bytes).
- SLES `AIActionHeliPursuit::Update` uses a five-entry pursuit-mode switch:
  straight line, follow road, search pattern, skid-hit approach, and skid-hit
  strike. The later targets use the existing four-entry mode set.
- Direct GNU MIPS disassembly establishes the follow-road behavior: return to
  straight-line mode within 50 metres, copy player velocity to the helicopter,
  update path navigation with a speed-derived 15-to-35-metre look-ahead,
  restart stale path finding, steer to the nav at 20 metres using
  `RoadNetSpeed`, and drive with flags `7`.

## Reconstruction

- Restored the A124 collision guard and statement ordering while preserving
  the later SLUS order behind compile-time guards.
- Reconstructed the missing A124-only follow-road mode, method declaration,
  implementation, and update-switch case.
- Tested and rejected an A124 `-fshort-bool` ABI hypothesis: the historical
  EE GCC rejects that option. No unsupported flag or unsafe one-byte-field
  workaround remains in the source.

## Validation

- SLES `AIPerpVehicle::OnCausedCollision`: 85.084% -> 99.54%.
- SLES `AIActionHeliPursuit::FollowRoadPursuit`: absent -> 98.39838%; the
  reconstructed body is 488 bytes against the exact 492-byte target.
- SLES `AIActionHeliPursuit::Update`: 77.61468% -> 99.57798% at the exact
  436-byte target size.
- SLES zAI `.text`: 90.02599% -> 90.34374%.
- SLUS zAI `.text` remains exactly 86.05584%; its collision function remains
  99.71831%, and the A124-only helicopter code is excluded.
- Both SLES and SLUS zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v62.json` and
`/private/tmp/slus-zai-layout-v16.json`.
