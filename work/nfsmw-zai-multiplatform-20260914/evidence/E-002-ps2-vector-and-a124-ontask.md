# E-002: PS2 vector primitives and A124 pursuit task shape

## Claim

The PS2 targets inline several VU0 vector operations, and the SLES A124 `AIPursuit::OnTask` omits later race/stat gating before its pursuit-time update.

## Evidence

- Direct objdiff against `orig/SLES-53558-A124/NFS.ELF` improved `AIVehicle::UpdateSimplePhysics` from 52.05294% to 89.44118% after reconstructing the PS2 inline vector normalization, length-square, matrix initialization, and A124 speed/acceleration path.
- Direct objdiff against `orig/SLUS-21351/SLUS_213.51` improved the same function from 62.378986% to 90.110695% using the shared PS2 vector reconstruction.
- Ghidra CLI decompilation of SLES `OnTask__9AIPursuitP10HSIMTASK__f` at `0x0003ea88` shows the time accumulation immediately after the perpetrator query and does not recover the later-build race/stat predicate. The decompiler also reports bad-instruction truncation, so objdiff remains the deciding oracle.
- A124-only reconstruction of the older pursuit-time flow, pursuit-attribute ordering, heat gate, debug formation override, forced-bail hook, final-epic omission, and pursuit-meter formula increased SLES `AIPursuit::OnTask` from 73.83598% to 80.631836%. The PS2 object builds successfully; the current direct report is `sles-zai-a124-current.json`.
- A fresh SLUS build retained `AIPursuit::OnTask` at 93.97875%, confirming that the A124 conditionals do not regress the later PS2 source path.

## Reconstruction

- `UVectorMath.hpp`: PS2 inline VU0 cross-normalize, normalize, length-square, and packed identity-matrix initialization.
- `WWorldPos.h`: PS2 inline normal normalization.
- `AIVehicle.cpp`: A124 simple-physics acceleration and top-speed path.
- `AIPursuit.cpp`: A124 pursuit-time update control-flow shape.
- `AIPursuit.cpp`: A124 heat, debug override, forced-bail, epic-pursuit, and meter behavior.

## Limitations

The SLES-only Juice telemetry calls in `AIPursuit::OnTask` are still absent because their source declarations are not yet reconstructed. They are visible in the target call stream and remain a next-step item.
