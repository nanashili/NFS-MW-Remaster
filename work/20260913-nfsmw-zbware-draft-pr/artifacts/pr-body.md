## Summary

- Reconstruct deterministic zBWare math, vector, matrix, quaternion, list, memory, slot-pool, chunk, string, printf, and script helpers.
- Recover the PC `bDebug`/ticker paths from the local `SPEED_EXE_1_3` target, including Win32 API declarations, callback/debug output, pointer validation, process launch, performance-counter timing, wraparound, and interrupt stubs.
- Recover the PC `MemoryPool` layout and lifecycle, exact hermite basis scratch behavior, D3DX transpose path, random wrappers, bounded wide copy, and bounding-box source shapes from Ghidra and instruction diffs.
- Add high-confidence `SPEED_EXE_1_3` symbol identities for the recovered PC helpers.
- Keep platform/runtime-only behavior explicitly scoped rather than inventing host-specific behavior.

## Validation

- `git diff --check`
- `python3 configure.py --version SPEED_EXE_1_3 --no-progress`
- `ninja build/SPEED_EXE_1_3/src/Speed/Indep/SourceLists/zBWare.obj`
- `ninja -C .`
- Ghidra 12.1.3 + ghidra-cli 0.2.2 `doctor`, import, and bridge decompile
- Focused single-unit `objdiff-cli` report with `functionRelocDiffs=none`

Focused zBWare result: `.text` fuzzy match improved from 59.901512% (9,121/35,235 exact bytes; 129/467 exact functions) to 67.43484% (11,360/35,235 exact bytes; 159/467 exact functions). The Ghidra-guided layout pass made `MemoryPool::Init`, `MemoryPool::Close`, `bInitMemoryPool`, `bMemoryInit`, `bMemoryCreatePersistentPool`, `hermite_basis`, `bGetPoolSize`, and additional bounding-box paths exact. `bTransposeMatrix` is instruction-exact when imported-function relocation spelling is ignored.

## Ghidra notes

The available PC `speed.exe` sample was imported as an explicit local fallback (`NeedForSpeed` / `speed.exe`). Headless analysis over `0x0045c929..0x004664a0` exported 224 functions, and the CLI bridge analyzed 26,066 functions. The repository's recommended `NFSMWRELEASE.ELF` and `NFS.ELF` inputs are not present locally (only `.gitkeep` placeholders), so the DWARF dump/split and exact ELF project setup remain follow-up work requiring those binaries or decomp.dev access.

## Notes

This is a static reconstruction pass. The sample and generated analysis artifacts are not included in the PR. No game process, launcher, timer, process creation, debug output, or Codeine transport was executed; runtime/platform validation remains follow-up work.
