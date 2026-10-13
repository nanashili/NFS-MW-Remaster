# Timeline (append-only)

## 2026-09-13T16:36:07+02:00 | lead | init
- action: case-init
- command_or_ref: skills/scripts/case-init.sh
- result_summary: case directory created; scope ready_for_act=true
- artifacts: [scope.md, workitems.md]
- evidence_ids: []
- decision_delta: [case_initialized]
- carry_forward_refs: [scope.md]
- next: open PRIMARY SKILL.md and ACT within scope

## 2026-09-13T16:35:16+02:00 | lead | route-and-tool-index
- action: refresh reverse-skill tool inventory and route the case
- command_or_ref: skills/scripts/refresh-tool-index.sh; skills/scripts/master-route.sh
- result_summary: routed to R0 general reverse engineering; Python and Java available; Ghidra, IDA, radare2, Binary Ninja, and Frida unavailable
- artifacts: [scope.md]
- evidence_ids: [E-001]
- decision_delta: [use_existing_matching_toolchain_and_local_reports]
- carry_forward_refs: [scope.md]
- next: inspect the current PC matching corpus

## 2026-09-13T16:41:00+02:00 | cre | static-corpus-inventory
- action: validate the retail sample and inspect current objdiff measures
- command_or_ref: validate_target.py; jq build/SPEED_EXE_1_3/report.json; git status --short
- result_summary: exact PC 1.3 identity confirmed; current dirty corpus quantified globally and by unit
- artifacts: [evidence/E-001.md, evidence/E-002.md, evidence/E-004.md]
- evidence_ids: [E-001, E-002, E-004]
- decision_delta: [static_recovery_is_not_runtime_blocked]
- carry_forward_refs: [workitems.md]
- next: correlate the runtime trace boundary

## 2026-09-13T16:47:00+02:00 | cre | runtime-boundary
- action: correlate 48 statically validated hooks with the 12-hook schema-3 survey
- command_or_ref: jq hook_catalogue.json and capture-20260912-205005-gameplay-analysis.json
- result_summary: all 48 anchors are statically locatable; 12 have entry observations; return values, exact transitions, and lifetimes remain outside visibility
- artifacts: [evidence/E-003.md]
- evidence_ids: [E-003]
- decision_delta: [runtime_reserved_for_behavioral_questions]
- carry_forward_refs: [evidence/E-002.md]
- next: produce the decision matrix and report

## 2026-09-13T16:55:00+02:00 | doc | handoff
- action: document findings, solve path, prioritized static queue, and runtime profiles
- command_or_ref: docs-generator and diagram-generator workflows
- result_summary: formal project report and editable Mermaid decision flow produced
- artifacts: [report/nfsmw-pc-static-runtime.md, report/static-runtime-boundary.mmd]
- evidence_ids: [E-001, E-002, E-003, E-004]
- decision_delta: [case_ready_for_strict_review]
- carry_forward_refs: [scope.md, workitems.md]
- next: run strict case review

## 2026-09-13T16:58:00+02:00 | doc | field-journal
- action: write anonymized reusable static/runtime decision pattern and update the journal index
- command_or_ref: skills/field-journal/2026-09-13_legacy-pe-game-static-runtime-boundary.md
- result_summary: target-independent pattern recorded; no router or bootstrap change required
- artifacts: [report/nfsmw-pc-static-runtime.md]
- evidence_ids: [E-001, E-002, E-003, E-004]
- decision_delta: [journal_complete, index_updated]
- carry_forward_refs: [workitems.md]
- next: strict review and handoff

## 2026-09-13T17:00:00+02:00 | lead | strict-review
- action: verify scope, Evidence to Finding to Path traceability, work-item coverage, and artifact hash policy
- command_or_ref: case-review/scripts/review_case.py --verify-hashes --strict --format markdown
- result_summary: PASS with zero errors and zero warnings
- artifacts: [report/case-review.md]
- evidence_ids: [E-001, E-002, E-003, E-004]
- decision_delta: [case_handoff_ready]
- carry_forward_refs: [scope.md, workitems.md, report/nfsmw-pc-static-runtime.md]
- next: user selects a static batch or focused runtime profile
