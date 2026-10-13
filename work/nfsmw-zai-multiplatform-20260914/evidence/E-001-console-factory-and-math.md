# E-001: Console factory and PS2 math reconstruction

- `AIGoal::AddAction` target sizes were 188 bytes on both PS2 binaries and 180 bytes on GameCube; the prior reconstruction inlined `Factory::CreateInstance` and produced 264/232-byte bodies.
- Moving the console template definition out of the class restored direct calls and produced 100.00% instruction matches on SLUS, SLES A124, and GOWE69. PC remained 100.00% at 132 bytes.
- The two residual SLUS relocation mismatches identified target address `0x005735b8`; cross-platform symbol tables and allocation call context identify it as `gFastMem` (size `0x32c`). Adding that mapping made SLUS `AddAction` exact.
- Ghidra headless was run against the existing SLUS project at `0x00134cf8` for `Gps::Render`; its decompiler encountered unsupported/bad instruction data, so objdiff instruction/relocation evidence remained authoritative for that function.
- Original PS2 call sites use `UMath::Mod`, `VU0_Sin`, and `VU0_Cos`; restoring those platform-specific calls improved both PS2 whole-unit matches without changing PC.

Current code-section match scores after validation:

- GOWE69: 97.72023%
- SLES-53558-A124: 85.596565%
- SLUS-21351: 88.00568%
- SPEED_EXE_1_3: 92.5414%
- EUROPEGERMILESTONE: 20.977163% (latest mapped baseline; unaffected by these guarded changes)
