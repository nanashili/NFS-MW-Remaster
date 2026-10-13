# E-005: A124 IVehicle list layout

- SLES `AICopManager` accesses place `mIVehicleList` at `0x164`, `mIPursuitList` at `0x19c`, `mRoadBlockList` at `0x1a4`, and `mSpeech` at `0x1b0`.
- The reconstructed A124 object previously placed the latter three fields 40 bytes too late because `ListableSetStorageSize<IVehicle, 10>` used the later 20-pointer capacity.
- `IVehicle.h` now selects capacity 10 for `EA_BUILD_A124` and preserves capacity 20 for later builds.
- SLES zAI rebuild passed. Direct weighted matched-function coverage increased from `92.210250597579%` to `92.22030902182007%`.
- Representative SLES changes: `ResetCopsForRestart` `59.090187% -> 59.098145%`, `UpdatePursuits` `73.07646% -> 73.077965%`, and `UpdateSupportCops` `39.440475% -> 39.458332%`.
- SLUS zAI rebuild passed and retains its later list layout. The current shortened-target direct weighted result is `95.69083580294371%`; `ResetCopsForRestart` is `99.80498%` and `UpdatePursuits` is `93.8422%`.
- Ghidra CLI confirmed the SLES `AIActionRace::Update` entry at `0x00026688` contains the older online-player/extrapolated-car follow path currently represented only by a dead source stub. That block remains a separate reconstruction item.
