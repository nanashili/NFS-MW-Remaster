# E-019: PS2 VU0 Vector4 distance and A124 traffic spawn reconstruction

## Evidence

- The PS2 `VU0_v4distancesquarexyz` target uses VU0 quadword loads, vector subtraction, multiply-accumulate across xyz, and a coprocessor-to-CPU result transfer.
- The SLES A124 `AITrafficManager::FindSpawnPoint` target does not use the retail road-direction rotation and speed-dependent offset path.
- Its spawn position is the traffic-center position plus `basis.v2 * 200`, plus traffic-center velocity times `2`, plus a random planar direction times `50`.
- The target normalizes the vector from the traffic-center position to that spawn point, rejects a zero-length result, and conditionally negates it according to `mOncommingChance`.
- Keeping the random planar offset vector separate from the normalized spawn direction reproduces the target stack layout and eliminates the generated size delta.

## Reconstruction

- Restored the PS2 VU0 implementation of `VU0_v4distancesquarexyz`.
- Added the A124-only traffic spawn algorithm while retaining the later retail algorithm for SLUS and other builds.
- Expressed the random direction through direct component stores and retained a separate normalized direction local to reproduce target code generation.

## Verification

- SLES `AIVehicle::WorldCollision` rose from `48.066227%` at target/current `604/484` to `55.887417%` at `604/480`.
- SLUS `AIVehicle::WorldCollision` is `54.92053%` at `604/480`.
- SLES `AITrafficManager::FindSpawnPoint` rose from `62.331707%` at target/current `820/756` to `99.82927%` at exact `820/820`.
- The seven remaining SLES differences are relocation-target mappings; the emitted instruction and function-size shape is otherwise aligned.
- SLUS `AITrafficManager::FindSpawnPoint` remains on the retail path and builds at `99.60317%`, exact `756/756`.
- Current whole-object `.text` similarity is `88.04992%` for SLES and `88.64357%` for SLUS in the direct reports.
- Both PS2 zAI objects build successfully with the pinned EE-GCC toolchain.

Artifacts: `/private/tmp/sles-world-collision-v2.json`, `/private/tmp/slus-world-collision-v2.json`, `/private/tmp/sles-find-spawn-point-v4.json`, `/private/tmp/slus-find-spawn-point-v4.json`, `/private/tmp/sles-zai-layout-v24.json`, `/private/tmp/slus-zai-layout-v4.json`.
