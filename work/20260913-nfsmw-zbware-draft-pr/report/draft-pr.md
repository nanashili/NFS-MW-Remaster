# zBWare upstream draft PR

This case records the authorized publication of the zBWare reconstruction and the Ghidra-assisted PC follow-up as a focused draft pull request. Scope is [scope.md](../scope.md); the local PC sample and generated analysis artifacts are used only for offline build/match verification and are not included in the PR.

## Contribution

- upstream: https://github.com/dbalatoni13/nfsmw
- fork: https://github.com/nanashili/nfsmw
- branch: `feature/zbware-reconstruction-pr`
- commits: `4c46b8fc` (`Reconstruct zBWare utility routines`), `63ed5602` (`Reconstruct PC bDebug helpers`), `5a492452` (`Recover PC zBWare layouts and matrix paths`)
- draft PR: https://github.com/dbalatoni13/nfsmw/pull/138
- changed files: 19 (18 zBWare source/header files plus the `SPEED_EXE_1_3` symbol map)

## Validation

The clean branch was configured for `SPEED_EXE_1_3`; `git diff --check`, the zBWare object build, and the configured full build check completed. Ghidra 12.1.3 and ghidra-cli 0.2.2 were installed and passed `ghidra doctor`. The available PC `speed.exe` was imported as an explicit fallback because the repository's canonical ELF inputs are absent locally.

The bounded headless export covered `0x0045c929..0x004664a0` and emitted 224 functions. The CLI bridge analyzed 26,066 functions and returned decompilations for the PC debug/ticker, allocator, matrix, string, and vector addresses. The initial helper implementations and symbol names are in commit `63ed5602`; the subsequent layout/matrix pass is recorded by E-009.

The focused zBWare unit report improved from the prior 59.901512% `.text` fuzzy match (9,121/35,235 exact bytes; 129/467 exact functions) to 67.43484% (11,360/35,235 exact bytes; 159/467 exact functions). This is a unit-level measure; a full project report is not claimed because the clean checkout does not contain the other candidate objects required by `report generate`.

Runtime/platform-dependent behavior remains a maintainer follow-up. The PC code statically reconstructs the Win32 API call paths, but no game process, launcher, timer, process creation, debug output, or Codeine transport was executed.

## Evidence

| ID | Observation |
|---|---|
| E-001 | Clean branch isolates the 17-file zBWare patch. |
| E-002 | zBWare object compilation and configured build check completed. |
| E-003 | nanashili fork and feature branch were published. |
| E-004 | PR #138 was initially OPEN/DRAFT with nanashili as author and upstream `main` as base. |
| E-005 | Ghidra CLI 0.2.2 is configured and its bridge/import/decompile path is working. |
| E-006 | Ghidra 12.1.3 headless analysis exported 224 bounded zBWare functions; canonical ELF/DWARF inputs remain blocked. |
| E-007 | Rebuilt zBWare object and focused report show the 63.802383% `.text` result and 147/467 matched functions. |
| E-008 | The updated PR still is OPEN/DRAFT and now contains both commits and 19 changed files. |
| E-009 | Ghidra-guided PC layout and matrix reconstruction reaches 67.43484% fuzzy match and 159/467 exact functions. |
| E-010 | The final three-commit head is live on the OPEN/DRAFT PR with 19 changed files. |

## Findings

### F-001
- title: zBWare reconstruction is isolated and buildable
- severity: info
- category: reverse_algo
- status: validated
- evidence_ids: [E-001, E-002]
- location: src/Speed/Indep/bWare/Inc and src/Speed/Indep/bWare/Src
- impact: The deterministic zBWare source work can be reviewed independently without pulling unrelated AI or symbol changes from the working tree.
- confidence: high
- repro_steps:
  1. Check the clean branch at `/private/tmp/nfsmw-zbware-pr`.
  2. Run the commands recorded in E-002.
- remediation: n/a for pure RE
- optional_attack:

### F-002
- title: Focused zBWare contribution is available upstream for review
- severity: info
- category: other
- status: validated
- evidence_ids: [E-003, E-004, E-008, E-010]
- location: https://github.com/dbalatoni13/nfsmw/pull/138
- impact: Maintainers can review and request changes before the reconstruction is merged.
- confidence: high
- repro_steps:
  1. Open PR #138.
  2. Confirm the updated draft state, author, base, three commits, final head, and 19-file list.
- remediation: n/a for pure RE
- optional_attack:

### F-003
- title: Ghidra-assisted PC debug/ticker helpers are statically reconstructed
- severity: info
- category: reverse_algo
- status: validated
- evidence_ids: [E-005, E-006, E-007]
- location: src/Speed/Indep/bWare/Src/bDebug.cpp; config/SPEED_EXE_1_3/symbols.json
- impact: The previously stripped PC debug/ticker paths now have source-level Win32 ABI calls, wraparound handling, and high-confidence target symbol identities, improving the focused unit match while keeping runtime-only claims explicit.
- confidence: high
- repro_steps:
  1. Confirm the configured `ghidra-cli` doctor/import/decompile results in E-005.
  2. Inspect the bounded decompilation artifact and range in E-006.
  3. Rebuild and run the focused unit report commands in E-007.
- remediation: n/a for pure RE
- optional_attack:

### F-004
- title: PC MemoryPool layout and matrix scratch behavior are statically recovered
- severity: info
- category: reverse_algo
- status: validated
- evidence_ids: [E-006, E-009]
- location: src/Speed/Indep/bWare/Src/bMemory.cpp; src/Speed/Indep/bWare/Src/bMatrix.cpp
- impact: Recovering the missing third intrusive list, shifted allocator fields, five-matrix hermite scratch layout, and D3DX transpose path materially improves both semantic fidelity and the measured PC object match.
- confidence: high
- repro_steps:
  1. Inspect the bounded target decompilation ranges recorded in E-009.
  2. Rebuild and regenerate the focused unit report.
- remediation: n/a for pure RE
- optional_attack:

## Path

### P-001
- title: Local reconstruction to upstream draft PR
- path_type: callflow
- start: Existing local zBWare reconstruction worktree
- goal: Reviewable draft PR against `dbalatoni13/nfsmw:main`
- steps:
  1. action: Extract only the zBWare diff onto a clean upstream-main branch — evidence: E-001 — finding: F-001
  2. action: Compile the zBWare object and run the configured build check — evidence: E-002 — finding: F-001
  3. action: Create the nanashili fork and publish the feature branch — evidence: E-003 — finding: F-002
  4. action: Open the draft PR against upstream main — evidence: E-004 — finding: F-002
  5. action: Install/configure Ghidra CLI and run bounded headless/static analysis — evidence: E-005, E-006 — finding: F-003
  6. action: Integrate high-confidence PC helpers, rebuild, and rerun the focused unit report — evidence: E-007 — finding: F-003
  7. action: Recover the PC allocator layout and matrix scratch behavior, then remeasure — evidence: E-009 — finding: F-004
  8. action: Publish and verify the final three-commit draft PR head — evidence: E-010 — finding: F-002
- residual_risks: Runtime/platform-specific behavior, canonical ELF/DWARF import, maintainer review, and CI remain pending.

## Timeline

See [timeline.md](../timeline.md) for the append-only execution record.
