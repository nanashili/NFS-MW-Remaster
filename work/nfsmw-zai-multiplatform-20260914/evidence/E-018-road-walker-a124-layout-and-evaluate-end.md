# E-018: road_walker A124 layout and evaluate_end reconstruction

## Evidence

- Direct SLES instruction references use `direction` at `0x40`, `futurepoint` at `0x50`, `targetpoint` at `0x60`, and `startpoint` at `0xa0`.
- Those offsets require 16-byte alignment for the `UMath::Vector3` members on PS2.
- The SLES `evaluate_end` target constructs and initializes both `WRoadNav` objects before scoring, does not increment `numevaluates`, and omits the two retail early score exits.
- Its final normalized target-direction dot product is evaluated but the score receives the already-loaded `24.0f` value.
- The target `VU0_v3lerp` sequence is the eight-instruction VU0 load/subtract/scale/add/store implementation, not the scalar placeholder.

## Reconstruction

- Added `PS2ALIGN16` to the five affected `road_walker` vector members.
- Restored the PS2 VU0 implementation of `VU0_v3lerp`.
- Added SLES A124-only `evaluate_end` construction, counter, early-exit, and final-score behavior while preserving the SLUS/retail path.
- Restored the A124 traversal rules: `walk_all_paths` uses the target's 100-call entry cap, and `walk_road`/the recursive segment loop omit the later retail evaluation and walk limits.

## Verification

- SLES `evaluate_end`: `57.550724%` at target/current `1104/1176` before this investigation, now `99.07609%` at exact `1104/1104`.
- SLUS `evaluate_end`: `99.96667%` at exact `1200/1200`.
- SLES `walk_all_paths`: `54.49223%` to `69.11399%`.
- SLES `walk_road`: `90.63231%` to `92.30308%`.
- SLES whole `.text` similarity rose from `87.631805%` to `87.86613%` on the same `280360`-byte target section.
- Both SLES and SLUS zAI objects build successfully with the pinned EE-GCC toolchain.

Artifacts: `/private/tmp/sles-evaluate-end-v8.json`, `/private/tmp/slus-evaluate-end-v8.json`, `/private/tmp/sles-walk-all-v3.json`, `/private/tmp/sles-walk-road-v2.json`, `/private/tmp/slus-walk-road-v2.json`.
