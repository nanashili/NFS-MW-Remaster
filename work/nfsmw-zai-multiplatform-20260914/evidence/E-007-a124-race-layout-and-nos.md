# E-007: A124 race layout and NOS behavior

- The checked-in PS2 type database and SLES instruction offsets constrain the A124 `AIActionRace` layout: the four boolean fields occupy the old compiler's four-byte slots, an additional four-byte gap precedes `mLastFindPosition`, and the later `mPerpetrator` and `mBottleTime` members are absent.
- The SLES constructor independently confirms those offsets: `mLastFindPosition` begins at `0xc0`, `mLastAccel` at `0xcc`, `mLastSpeed` at `0xd0`, `mPerformanceBias` at `0xd4`, and `mResetTask` at `0xfc`.
- A124 obtains `IPerpetrator` dynamically from `GetVehicle()` inside `ComputePotentials`; it does not retain the interface in the action object. Reconstructing that query raised `ComputePotentials` from `95.931206%` to `98.22604%` and the constructor from `90.84615%` to `99.871796%`.
- The A124 binary exports `GetPotentialNOS(float, bool)` returning `bool`, unlike the later three-argument floating-point helper. Its recovered checks and threshold calculation now match all `180` target bytes (`100.0%`).
- All layout and signature differences are guarded by `EA_BUILD_A124`. SLUS retains its later layout and rebuilt successfully; representative results remain `AIActionRace::Update` `99.03877%`, constructor `99.10588%`, and `ComputePotentials` `99.65048%`.
- SLES rebuilt successfully. `AIActionRace::Update` is currently `85.59564%`; its remaining mismatch is principally the later NOS decision/control-flow block rather than the recovered helper or object layout.
