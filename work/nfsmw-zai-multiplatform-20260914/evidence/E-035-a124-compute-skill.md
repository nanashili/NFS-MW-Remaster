# E-035: A124 race skill computation

## Binary evidence

- SLES `AIPerpVehicle::ComputeSkill` is at `0x11eab0` and is exactly 376
  bytes.
- After the initial `GRaceStatus` existence check, the A124 function reads
  the race context directly. It has no later-platform `IsPlayer` virtual call.
- Its quick-race branch calls `GetDifficulty` directly and maps difficulty
  values `0`, `1`, and `2` to base skills `0.0`, `0.5`, and `1.0`. Other
  values fall back to `0.5`. It does not query catch-up settings or index the
  later quick-race skill tables.

## Reconstruction

- Restored the older A124 entry condition and explicit difficulty switch
  behind `EA_BUILD_A124`.
- Preserved the existing player exclusion and catch-up-table algorithm for
  SLUS and later targets.

## Validation

- SLES `ComputeSkill`: 73.82979% at 376/412 bytes -> 99.989365% at the exact
  376/376-byte size.
- SLES zAI `.text`: 90.34374% -> 90.37911%.
- SLUS zAI rebuilds and remains exactly 86.05584%; its 412-byte
  `ComputeSkill` remains 99.31068%.

Reproduce with:

```sh
python3 configure.py -v SLES-53558-A124
ninja -C . build/SLES-53558-A124/src/Speed/Indep/SourceLists/zAI.o
build/tools/objdiff-cli diff -p . -u main/Speed/Indep/SourceLists/zAI -o /private/tmp/sles-zai-layout-v65.json
```

Artifacts: `/private/tmp/sles-zai-layout-v65.json` and
`/private/tmp/slus-zai-layout-v17.json`.
