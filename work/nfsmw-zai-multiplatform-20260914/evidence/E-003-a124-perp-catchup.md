# E-003: A124 perpetrator catch-up flow

## Claim

The SLES A124 `AIPerpVehicle::Update` uses an older catch-up path: race-parameter `GetCatchUp`, no player-performance multiplier, the four-argument `ComputeCatchUpSkill`, and unconditional positive/negative glue tables without later SpeedTrap branches.

## Evidence

- The SLES target call stream contains `GetCatchUp__C15GRaceParameters` and `ComputeCatchUpSkill__11GRaceStatusP10GRacerInfoP8PidErrorPfT3`.
- It lacks the later-build performance-default, simple-physics, five-argument catch-up, and race-type calls emitted by the reconstruction.
- Reconstructing the A124-only path increased direct objdiff for `Update__13AIPerpVehiclef` from 48.104424% (2084 reconstructed bytes) to 61.523895% (1844 reconstructed bytes).
- `build/SLES-53558-A124/src/Speed/Indep/SourceLists/zAI.o` builds successfully with the recovered declaration and source flow.

## Remaining gap

The two SLES-only Juice `GameEvent` blocks remain absent. They account for much of the target's remaining size advantage and require reconstruction of the Juice interfaces.
