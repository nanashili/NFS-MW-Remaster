# E-020: A124 pursuit siren reconstruction

## Evidence

- Direct SLES instruction comparison shows the A124 `AIVehiclePursuit::UpdateSiren` target proceeds from race-parameter queries directly into SoundAI/pursuit handling.
- It does not enumerate `IRoadBlock::GetList`, test whether the cop belongs to a roadblock, or apply the later retail force-wail override.
- Ghidra headless decompilation at `0x00033610` independently confirms that no roadblock traversal occurs between the race-parameter calls and SoundAI branch.
- The target stores the high-intensity boolean directly as the initial wail/yelp state rather than emitting a branch diamond.

## Reconstruction

- Guarded the roadblock-list scan and final force-wail behavior out of `EA_BUILD_A124` while preserving them for SLUS/retail builds.
- Collapsed equivalent siren-state branches so EE-GCC emits the target boolean/state shape.

## Verification

- SLES `AIVehiclePursuit::UpdateSiren` rose from `75.417175%` at target/current `1304/1512` to `97.36503%` at `1304/1288`.
- SLES whole-object `.text` similarity rose from `88.04992%` to `88.15285%` on the same `280360`-byte target section.
- SLUS retained its roadblock behavior and improved from `98.10133%` at `1500/1512` to `99.15733%` at exact `1500/1500`.
- Both PS2 zAI objects build successfully with the pinned EE-GCC toolchain.

Artifacts: `/private/tmp/sles-update-siren-v2.json`, `/private/tmp/slus-update-siren-v2.json`, `/private/tmp/sles-zai-layout-v26.json`.
