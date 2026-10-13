# E-008: A124 player-hide Juice events

## Claim

SLES A124 `AIPerpVehicle::Update` emits Juice `PURSUIT / PLAYER_HIDE` telemetry while the pursued player enters a hiding zone and when the hide latch succeeds.

## Evidence

- The target function at object address `0x0002ec30` calls `Juice::GameHook::GameEvent` twice with target strings `PURSUIT`, `PLAYER_HIDE`, `Atte`, and `Succ`.
- The target obtains the pursuit target through `AITarget::QueryInterface<ISimable>`, calls `ISimable::GetPlayer` at vtable slot 9 (`0x48`), then supplies `MWExtension::GetImageName`, `MWExtension::GetPlayerPosition`, and `mHiddenZoneTimer` to each event.
- Target instructions `0x0001f2b8` through `0x0001f444` contain the two event paths; the attempt latch is the four-byte object-local static at `D_005DA4AC`.
- Reconstructing these A124-only paths raised direct objdiff for `Update__13AIPerpVehiclef` from `61.523895%` (1,844-byte reconstruction) to `85.32743%` (2,364-byte reconstruction versus the 2,260-byte target).
- Restoring the PS2-recorded `ISimable` method order places `GetPlayer` at slot 9 and agrees with both the checked-in PS2 type record and target call instruction.
- `build/SLES-53558-A124/src/Speed/Indep/SourceLists/zAI.o` and `build/SLUS-21351/src/Speed/Indep/SourceLists/zAI.o` compile successfully with the EE GCC toolchain.

## Ghidra observation

Ghidra 12.1.3 headless selected `Update__13AIPerpVehiclef.NON_MATCHING` at `0x0002ec30`, but its decompiler stopped at bad instruction data after the static string-hash initialization. The target objdiff instruction and relocation stream therefore remains authoritative for the recovered tail.

## Remaining gap

Register allocation and branch scheduling still differ in the restored tail, and earlier pursuit/zone setup differences leave this function below a complete instruction match.
