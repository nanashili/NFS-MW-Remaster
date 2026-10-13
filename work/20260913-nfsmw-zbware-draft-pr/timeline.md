# Timeline (append-only)

## 2026-09-13T20:14:50+02:00 | lead | init
- action: case-init
- command_or_ref: skills/scripts/case-init.sh
- result_summary: case directory created; scope ready_for_act=true
- artifacts: [scope.md, workitems.md]
- evidence_ids: []
- decision_delta: [case_initialized]
- carry_forward_refs: [scope.md]
- next: open PRIMARY SKILL.md and ACT within scope

## 2026-09-13T20:17:30+02:00 | lead | isolate-verify
- action: isolated the working-tree zBWare diff onto a clean branch based on upstream main and ran the configured SPEED_EXE_1_3 zBWare build
- command_or_ref: git diff --binary origin/main -- src/Speed/Indep/bWare | git apply --index; python3 configure.py --version SPEED_EXE_1_3 --no-progress; ninja -C . build/SPEED_EXE_1_3/src/Speed/Indep/SourceLists/zBWare.obj; ninja -C .
- result_summary: commit 4c46b8fc contains 17 zBWare files; target object compiled and the configured build check completed
- artifacts: [report/draft-pr.md, report/pr-publication-flow.mmd]
- evidence_ids: [E-001, E-002]
- decision_delta: [isolated_zbware_only, static_build_verified]
- carry_forward_refs: [scope.md, workitems.md]
- next: publish branch through the authorized nanashili fork

## 2026-09-13T20:18:24+02:00 | lead | publish
- action: created the nanashili fork and pushed feature/zbware-reconstruction-pr
- command_or_ref: gh repo fork dbalatoni13/nfsmw --clone=false; git push --set-upstream nanashili feature/zbware-reconstruction-pr
- result_summary: fork https://github.com/nanashili/nfsmw and branch published successfully
- artifacts: []
- evidence_ids: [E-003]
- decision_delta: [fork_created, branch_published]
- carry_forward_refs: [scope.md, evidence/E-001.md]
- next: open the requested draft pull request against upstream main

## 2026-09-13T20:20:49+02:00 | lead | contribution
- action: opened and verified draft pull request #138
- command_or_ref: gh pr create --repo dbalatoni13/nfsmw --base main --head nanashili:feature/zbware-reconstruction-pr --draft; gh pr view 138 --repo dbalatoni13/nfsmw
- result_summary: PR is OPEN and DRAFT, authored by nanashili, with the upstream main base and exactly 17 zBWare files
- artifacts: [report/draft-pr.md]
- evidence_ids: [E-004]
- decision_delta: [draft_pr_open]
- carry_forward_refs: [scope.md, workitems.md, evidence/E-003.md]
- next: maintainer review; runtime/platform validation remains follow-up

## 2026-09-13T21:18:00+02:00 | lead | ghidra-static
- action: installed and configured Ghidra CLI, imported the available PC sample, and ran headless decompilation over the zBWare address range
- command_or_ref: `ghidra doctor`; `ghidra import .../speed.exe --project NeedForSpeed --program speed.exe`; `ghidra decompile 0x45cc70 ... --json`; `analyzeHeadless ... -postScript ExportZbware.java`
- result_summary: Ghidra 12.1.3 and ghidra-cli 0.2.2 passed doctor; CLI import analyzed 26,066 functions; the bounded headless export emitted 224 functions. The canonical ELF/DWARF inputs remain unavailable locally.
- artifacts: [artifacts/ghidra-cli-config.yaml, artifacts/ghidra/zbware-decomp.txt, artifacts/ghidra/analyze.log, scripts/ExportZbware.java]
- evidence_ids: [E-005, E-006]
- decision_delta: [ghidra_cli_static_path_validated, elf_dwarf_input_blocker_recorded]
- carry_forward_refs: [scope.md, evidence/E-005.md, evidence/E-006.md]
- next: integrate only high-confidence PC helper identifications and rerun the focused matcher

## 2026-09-13T21:22:00+02:00 | lead | rebuild-remeasure
- action: added the statically recovered PC debug/ticker implementations and high-confidence symbol names, rebuilt the zBWare object, and generated a single-unit objdiff report
- command_or_ref: `ninja build/SPEED_EXE_1_3/src/Speed/Indep/SourceLists/zBWare.obj`; `build/tools/objdiff-cli report generate -p artifacts/objdiff/unit-report -o artifacts/objdiff/unit-report/report.json -f json-pretty`
- result_summary: focused zBWare `.text` fuzzy match improved from 59.901512% (9,121/35,235 bytes; 129/467 functions) to 63.802383% (9,788/35,235 bytes; 147/467 functions). The full project report remains intentionally unclaimed because other candidate objects are absent.
- artifacts: [artifacts/objdiff/zbware-final.json, artifacts/objdiff/unit-report/report.json]
- evidence_ids: [E-007]
- decision_delta: [pc_debug_helpers_reconstructed, focused_match_improved]
- carry_forward_refs: [report/draft-pr.md, evidence/E-007.md]
- next: push commit 63ed5602 and refresh draft PR #138 description

## 2026-09-13T21:24:43+02:00 | lead | contribution-refresh
- action: pushed the Ghidra-assisted follow-up commit and updated the existing upstream draft PR
- command_or_ref: `git push nanashili feature/zbware-reconstruction-pr`; `gh pr view 138 --repo dbalatoni13/nfsmw`
- result_summary: branch now contains commit 63ed5602; PR #138 remains OPEN/DRAFT with 19 changed files and nanashili as author.
- artifacts: [report/draft-pr.md]
- evidence_ids: [E-004, E-007, E-008]
- decision_delta: [draft_pr_updated]
- carry_forward_refs: [scope.md, evidence/E-004.md, evidence/E-007.md]
- next: maintainer review; runtime/platform validation and canonical ELF/DWARF import remain follow-up

## 2026-09-13T22:05:00+02:00 | lead | ghidra-layout-reconstruction
- action: used the bounded Ghidra output to recover the PC MemoryPool layout, hermite scratch-matrix lifecycle, D3DX transpose path, bounded string copy, random wrappers, and bounding-box source shapes
- command_or_ref: `case-guard.sh`; Ghidra decompilation at `0x45d9a0`, `0x45ea70`, `0x45ed30`, `0x45f850`, `0x45fbd0`, `0x461220`, and `0x464d80..0x466200`; focused `decomp-diff.py` iterations
- result_summary: the 0x64-byte MemoryPool with its third intrusive list made five memory paths exact; hermite_basis, bGetPoolSize, and additional bounding-box paths are exact; the PC transpose instruction stream matches with import-relocation spelling ignored
- artifacts: [artifacts/ghidra/zbware-decomp.txt, artifacts/objdiff/zbware-final.json, artifacts/objdiff/unit-report/report.json]
- evidence_ids: [E-006, E-009]
- decision_delta: [memory_pool_layout_recovered, hermite_exact, pc_transpose_recovered, focused_match_improved_again]
- carry_forward_refs: [scope.md, evidence/E-009.md]
- next: commit and publish the measured static reconstruction; preserve runtime and missing ELF/DWARF boundaries

## 2026-09-13T21:56:01+02:00 | lead | contribution-final
- action: verified the final Ghidra-guided reconstruction commit and refreshed description on upstream draft PR #138
- command_or_ref: `git push nanashili feature/zbware-reconstruction-pr`; `gh pr view 138 --repo dbalatoni13/nfsmw --json number,state,isDraft,author,headRefName,headRepositoryOwner,baseRefName,files,commits,url`
- result_summary: PR #138 remains OPEN/DRAFT under nanashili; head is 5a4924520496daaad0918466c451108366149431 with three commits and 19 changed files.
- artifacts: [report/draft-pr.md, artifacts/pr-body.md]
- evidence_ids: [E-009, E-010]
- decision_delta: [final_static_batch_published]
- carry_forward_refs: [scope.md, evidence/E-009.md, evidence/E-010.md]
- next: maintainer review; canonical ELF/DWARF and runtime/platform validation remain follow-up
