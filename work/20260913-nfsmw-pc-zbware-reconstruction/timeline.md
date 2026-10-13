# Timeline (append-only)

## 2026-09-13T16:47:46+02:00 | lead | init
- action: case-init
- command_or_ref: skills/scripts/case-init.sh
- result_summary: case directory created; scope ready_for_act=true
- artifacts: [scope.md, workitems.md]
- evidence_ids: []
- decision_delta: [case_initialized]
- carry_forward_refs: [scope.md]
- next: open PRIMARY SKILL.md and ACT within scope

## 2026-09-13T16:52:00+02:00 | cre | scope-review
- action: scope-and-tool-gate
- command_or_ref: reverse-engineering/SKILL.md; reverse-engineering/references/re-agent-workflow.md; tool-index.md
- result_summary: authorized offline PE sample; static reconstruction is actionable; runtime-only APIs remain out of scope
- artifacts: [scope.md]
- evidence_ids: []
- decision_delta: [roles=lead+cre+doc, phase=triage->static]
- carry_forward_refs: [scope.md]
- next: baseline matching snapshot, then pure zBWare reconstruction

## 2026-09-13T17:24:00+02:00 | cre | static-reconstruction
- action: reconstruct-zbware
- command_or_ref: src/Speed/Indep/SourceLists/zBWare.cpp; target symbol map; retail objdump
- result_summary: deterministic zBWare core routines reconstructed across containers, math, geometry, strings, scripting, memory, slot pools, printf, and packet headers
- artifacts: [../..]
- evidence_ids: [E-002]
- decision_delta: [WI-002=complete, runtime-boundary-isolated]
- carry_forward_refs: [scope.md, evidence/E-002.md]
- next: build and focused object comparison

## 2026-09-13T17:26:00+02:00 | cre | match-verification
- action: rebuild-and-compare
- command_or_ref: ninja build/SPEED_EXE_1_3/src/Speed/Indep/SourceLists/zBWare.obj; objdiff-cli focused unit
- result_summary: zBWare object builds; focused .text fuzzy match 59.85073%; key list-sort routines reach 100%
- artifacts: [evidence/E-001.md, evidence/E-003.md]
- evidence_ids: [E-001, E-003]
- decision_delta: [WI-003=complete, target-identity-validated]
- carry_forward_refs: [evidence/E-001.md, evidence/E-003.md]
- next: document static/runtime boundary and handoff

## 2026-09-13T17:28:00+02:00 | doc | handoff
- action: document-static-runtime-boundary
- command_or_ref: retail import table; remaining bWare STRIPPED/TODO inventory
- result_summary: transport/server/debug/VM/tracing and D3DX transpose dependency remain runtime or platform boundaries; no target execution performed
- artifacts: [evidence/E-004.md]
- evidence_ids: [E-004]
- decision_delta: [WI-004=complete, report-and-journal-ready]
- carry_forward_refs: [evidence/E-004.md, report/zbware-reconstruction.md]
- next: strict case review
