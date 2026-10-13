# Timeline (append-only)

## 2026-09-14T08:58:43+02:00 | lead | init
- action: case-init
- command_or_ref: skills/scripts/case-init.sh
- result_summary: case directory created; scope ready_for_act=true
- artifacts: [scope.md, workitems.md]
- evidence_ids: []
- decision_delta: [case_initialized]
- carry_forward_refs: [scope.md]
- next: open PRIMARY SKILL.md and ACT within scope

## 2026-09-14T10:25:00+02:00 | lead | reconstruction
- action: reconstruct-shared-factory-and-ps2-math-calls
- command_or_ref: objdiff-cli focused and whole-unit diffs; Ghidra headless DecompileFunction.java
- result_summary: restored out-of-line Factory::CreateInstance on PS2/GameCube; AIGoal::AddAction now exact on SLUS, SLES, and GOWE69 while PC remains exact; mapped SLUS gFastMem; restored platform-specific UMath::Mod and external PS2 VU0 trigonometry calls
- artifacts: [evidence/gowe69-zai-current.json, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json, evidence/pc-zai-current.json, scripts/DecompileFunction.java]
- evidence_ids: [E-001]
- decision_delta: [shared_factory_definition_was_originally_out_of_class_on_console_compilers, ps2_uses_external_vu0_trigonometry]
- carry_forward_refs: [scope.md, evidence/gowe69-zai-current.json, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]
- next: reconstruct highest-loss shared zAI functions and extend X360 mappings

## 2026-09-14T12:30:00+02:00 | lead | reconstruction
- action: reconstruct-ps2-vector-primitives-and-a124-pursuit-control-flow
- command_or_ref: direct objdiff-cli SLES/SLUS reports; Ghidra headless DecompileFunction.java at SLES 0x0003ea88
- result_summary: raised PS2 UpdateSimplePhysics to 89.44118% SLES and 90.110695% SLUS; raised SLES AIPursuit::OnTask from 73.83598% to 77.29481%; both PS2 objects build
- artifacts: [evidence/E-002-ps2-vector-and-a124-ontask.md, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]
- evidence_ids: [E-002]
- decision_delta: [ps2_vector_operations_are_inline_vu0_sequences, a124_ontask_omits_later_race_stat_wrappers]
- carry_forward_refs: [evidence/E-002-ps2-vector-and-a124-ontask.md, evidence/sles-zai-a124-current.json]
- next: reconstruct SLES Juice telemetry block and remaining highest-weight zAI functions

## 2026-09-14T13:15:00+02:00 | lead | reconstruction
- action: refine-a124-pursuit-task
- command_or_ref: repeated direct SLES objdiff; SLUS regression build and direct objdiff
- result_summary: raised SLES AIPursuit::OnTask from 77.29481% to 80.631836%; SLUS remains 93.97875%
- artifacts: [evidence/E-002-ps2-vector-and-a124-ontask.md, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]
- evidence_ids: [E-002]
- decision_delta: [a124_reads_pursuit_attributes_after_heat, a124_heat_increment_has_narrow_cooldown_gate, a124_retains_debug_override_and_forced_bail_hooks, a124_omits_final_epic_override_and_high_speed_meter_shortcut]
- carry_forward_refs: [evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]
- next: reconstruct SLES Juice telemetry declarations and call block, then address next weighted loss

## 2026-09-14T14:00:00+02:00 | lead | reconstruction
- action: reconstruct-a124-perp-catchup
- command_or_ref: SLES direct call-stream comparison and repeated objdiff-cli build/diff
- result_summary: raised SLES AIPerpVehicle::Update from 48.104424% to 61.523895%; SLES zAI object builds
- artifacts: [evidence/E-003-a124-perp-catchup.md, evidence/sles-zai-a124-current.json]
- evidence_ids: [E-003]
- decision_delta: [a124_uses_race_parameter_catchup_flag, a124_has_no_performance_multiplier, a124_uses_four_argument_compute_catchup, a124_has_no_speedtrap_glue_branches]
- carry_forward_refs: [evidence/E-003-a124-perp-catchup.md, evidence/sles-zai-a124-current.json]
- next: reconstruct Juice interfaces and telemetry blocks or proceed to Gps::Render weighted loss

## 2026-09-14T14:40:00+02:00 | lead | reconstruction
- action: reconstruct-ps2-gps-vu0-path
- command_or_ref: SLES and SLUS direct instruction comparison, builds, and objdiff-cli reports
- result_summary: raised Gps::Render from 40.013622% to 44.607628% SLES and from 41.85753% to 49.069893% SLUS
- artifacts: [evidence/E-004-ps2-gps-vu0.md, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]
- evidence_ids: [E-004]
- decision_delta: [ps2_gps_direction_and_basis_math_are_inline_vu0]
- carry_forward_refs: [evidence/E-004-ps2-gps-vu0.md, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]
- next: refine PS2 Gps::Render scalar control flow and reconstruct Juice interfaces

## 2026-09-14T15:20:00+02:00 | lead | reconstruction
- action: correct-a124-ivehicle-list-layout
- command_or_ref: Ghidra headless DecompileFunction.java at SLES AIActionRace::Update 0x00026688; direct objdiff-cli SLES/SLUS reports
- result_summary: restored the A124 10-pointer IVehicle list storage specialization and downstream AICopManager field offsets; both PS2 zAI objects build; directly mapped SLES weighted function coverage increased to 92.220309%, while SLUS remains healthy at 95.690836% against the shortened target
- artifacts: [evidence/E-005-a124-vehicle-list-layout.md, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]
- evidence_ids: [E-005]
- decision_delta: [a124_ivehicle_list_capacity_is_10, later_ivehicle_list_capacity_is_20, sles_aiactionrace_contains_online_extrapolated_follow_path]
- carry_forward_refs: [evidence/E-005-a124-vehicle-list-layout.md, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]
- next: reconstruct the A124 online-player/extrapolated-car path in AIActionRace::Update

## 2026-09-14T16:10:00+02:00 | lead | reconstruction
- action: reconstruct-a124-online-extrapolated-race-path
- command_or_ref: Ghidra headless SLES decompilation at 0x00026688; focused objdiff-cli SLES/SLUS diffs and PS2 rebuilds
- result_summary: restored the A124-only IOnlinePlayer/ExtrapolatedCar path and corrected the A124 ISimable and IOnlinePlayer virtual slots; SLES AIActionRace::Update rose from 82.872154% to 85.97225%, while SLUS remains 99.03877%
- artifacts: [evidence/E-006-a124-online-extrapolated-race.md, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]
- evidence_ids: [E-006]
- decision_delta: [a124_isimable_getowner_slot_0x48, a124_ionlineplayer_getextrapolatedcar_slot_0x18, a124_race_online_follow_path_restored]
- carry_forward_refs: [evidence/E-006-a124-online-extrapolated-race.md]
- next: reduce the remaining SLES AIActionRace::Update control-flow and register-allocation differences, then return to the largest cross-platform weighted losses

## 2026-09-14T16:45:00+02:00 | lead | reconstruction
- action: restore-a124-aiactionrace-layout-and-nos-contract
- command_or_ref: PS2_types.nothpp layout evidence; focused SLES/SLUS constructor, ComputePotentials, GetPotentialNOS, and Update objdiff-cli comparisons
- result_summary: restored the A124-only class layout and dynamic IPerpetrator query; constructor reached 99.871796%, ComputePotentials 98.22604%, and the recovered 180-byte boolean GetPotentialNOS function reached 100%
- artifacts: [evidence/E-007-a124-race-layout-and-nos.md, evidence/sles-zai-a124-current.json, evidence/slus-zai-current.json]

- time: 2026-09-14T11:56:00+02:00
- action: Reconstructed the two A124 player-hide Juice event paths and corrected the PS2 ISimable vtable ordering.
- command_or_ref: Ghidra 12.1.3 headless at SLES zAI object address 0x0002ec30; focused and whole-object objdiff; SLES and SLUS EE-GCC builds
- result_summary: Raised SLES AIPerpVehicle::Update from 61.523895% to 85.32743%; both PS2 variants build; Ghidra decompilation truncated on bad instruction data, so relocation-level evidence was used.
- decision_delta: The formerly missing Juice blocks are gameplay telemetry with a one-shot attempt latch, not AI catch-up logic. ISimable::GetPlayer is PS2 vtable slot 9.
- artifacts: [evidence/E-008-a124-player-hide-juice-events.md, evidence/sles-zai-a124-current.json]
- evidence_ids: [E-007]
- decision_delta: [a124_aiactionrace_size_0x100, a124_perpetrator_is_queried_not_stored, a124_getpotentialnos_returns_bool]
- carry_forward_refs: [evidence/E-006-a124-online-extrapolated-race.md, evidence/E-007-a124-race-layout-and-nos.md]
- next: reconstruct the A124 NOS decision tail in AIActionRace::Update and then re-rank the remaining cross-platform losses

## 2026-09-14T12:04:15+02:00 | lead | reconstruction
- action: reconstruct-a124-reset-cops-for-restart
- command_or_ref: direct SLES and SLUS objdiff-cli instruction comparisons; focused EE-GCC builds
- result_summary: restored the A124 nested pursuit/vehicle teardown and removed target-absent passes; SLES ResetCopsForRestart rose from 38.816975% to 99.933685% with identical 1508-byte size; SLUS remains 99.80498% with identical 1928-byte size
- artifacts: [evidence/E-009-a124-reset-cops-for-restart.md, /private/tmp/sles-reset-v5.json, /private/tmp/slus-reset-current.json]
- evidence_ids: [E-009]
- decision_delta: [a124_unspawns_cops_inside_each_pursuit_loop, a124_has_no_isactive_guard_here, a124_killvehicle_path_has_no_explicit_vector_clear]
- carry_forward_refs: [evidence/E-009-a124-reset-cops-for-restart.md]
- next: refresh the SLES whole-object report and re-rank the largest remaining mapped zAI losses

## 2026-09-14T12:20:00+02:00 | lead | reconstruction
- action: reconstruct-a124-update-pursuits-telemetry-and-latches
- command_or_ref: SLES target relocation stream and rodata; focused SLES/SLUS objdiff-cli reports; EE-GCC builds
- result_summary: restored busted-pursuit Juice events, formation logging, A124 destruction statistics, and ForcePursuitStart handling; SLES UpdatePursuits rose from 73.077965% to 98.4033%, while SLUS remains 93.8422%
- artifacts: [evidence/E-010-a124-update-pursuits.md, /private/tmp/sles-update-pursuits-v5.json, /private/tmp/sles-zai-after-update-pursuits.json, /private/tmp/slus-update-pursuits-regression.json]
- evidence_ids: [E-010]
- decision_delta: [a124_busted_pursuit_emits_two_juice_events, a124_destroyed_cop_stats_have_no_race_gate, a124_force_pursuit_latch_runs_after_request_vehicle]
- carry_forward_refs: [evidence/E-010-a124-update-pursuits.md]
- next: re-rank remaining SLES function losses and reconstruct the largest behavior-bearing gap

## 2026-09-14T13:05:00+02:00 | lead | reconstruction
- action: reconstruct-a124-aipursuit-layout-telemetry-and-cooldown-tail
- command_or_ref: PS2 type layout, direct SLES instruction/relocation comparison, focused SLES/SLUS objdiff-cli reports, and EE-GCC builds
- result_summary: restored the A124 class and pursuitlevels layouts, formation telemetry, heat policy, ForcePursuitNeverEnd cap, cooldown transition, and evade tail; SLES AIPursuit::OnTask rose from 80.65467% to 87.18893% with exact 5780-byte size, two pursuit-car helpers rose above 99.7%, and SLUS remains 93.97875%
- artifacts: [evidence/E-011-a124-aipursuit-layout-and-formation-telemetry.md, /private/tmp/sles-aitask-layout-v6.json, /private/tmp/sles-zai-layout-v6.json, /private/tmp/slus-aipursuit-regression-v5.json]
- evidence_ids: [E-011]
- decision_delta: [a124_has_no_allow_stats_field, a124_last_known_location_is_16_byte_aligned, a124_pursuitlevels_omits_inline_cops_array, a124_force_never_end_caps_evade_at_0_9, a124_cooldown_tail_omits_later_clamps]
- carry_forward_refs: [evidence/E-011-a124-aipursuit-layout-and-formation-telemetry.md]
- next: reconstruct the remaining A124 pursuit-meter arithmetic and then re-rank the largest SLES weighted losses

## 2026-09-14T13:32:00+02:00 | lead | reconstruction
- action: reconstruct-ps2-gps-forward-vector-and-verify-slus-layout-guard
- command_or_ref: Ghidra 12.1.3 headless inventory/decompile; direct SLES and SLUS objdiff-cli instruction comparisons; EE-GCC builds
- result_summary: corrected the PS2 GPS arrow forward-vector scale order; SLES Gps::Render rose from 44.607628% to 47.185287%, whole mapped weighted similarity rose to 90.45418860709063%, and SLUS rebuilt cleanly with AIPursuit::OnTask unchanged at 93.97875%
- artifacts: [evidence/E-012-ps2-gps-forward-vector.md, /private/tmp/sles-gps-render-v7.json, /private/tmp/sles-zai-layout-v9.json, /private/tmp/slus-gps-render-v7.json, /private/tmp/slus-ontask-layout-v7.json]
- evidence_ids: [E-012]
- decision_delta: [ps2_gps_forward_is_unit_before_cross_product_scaling, ghidra_sles_zai_render_address_is_0x44640]
- carry_forward_refs: [evidence/E-012-ps2-gps-forward-vector.md]
- next: reconstruct the remaining SLES Gps::Render stack/vector construction differences, then re-rank platform losses

## 2026-09-14T14:02:00+02:00 | lead | reconstruction
- action: reconstruct-ps2-gps-vu-length-and-packed-matrix-init
- command_or_ref: direct SLES/SLUS objdiff instruction streams, Ghidra headless function inventory, focused and whole-object EE-GCC builds
- result_summary: isolated the target VU camera-speed sequence to GPS and restored the packed PS2 bMatrix4 identity constructor; SLES Gps::Render rose to 50.991825%, SLUS rose to 55.36828%, and SLES whole mapped weighted similarity rose to 90.46979789209217%
- artifacts: [evidence/E-013-ps2-gps-vu-length-and-matrix-init.md, /private/tmp/sles-gps-render-v13.json, /private/tmp/sles-zai-layout-v13.json, /private/tmp/slus-gps-render-v13.json]
- evidence_ids: [E-013]
- decision_delta: [gps_uses_callsite_specific_vu_length_variant, ps2_bmatrix4_constructor_uses_packed_identity_stores]
- carry_forward_refs: [evidence/E-013-ps2-gps-vu-length-and-matrix-init.md]
- next: reconstruct the remaining GPS angle and cross-product expression ordering, then re-rank the SLES losses

## 2026-09-14T14:18:00+02:00 | lead | reconstruction
- action: reconstruct-ps2-gps-angle-control-ordering
- command_or_ref: direct SLES and SLUS objdiff instruction/control-flow comparisons; focused and whole-object EE-GCC builds
- result_summary: restored first-draw-first control flow and strict angle comparisons; SLES Gps::Render rose from 50.991825% to 55.08992%, SLUS rose from 55.36828% to 59.41129%, and SLES whole mapped weighted similarity rose to 90.49335611372207%
- artifacts: [evidence/E-014-ps2-gps-angle-control.md, /private/tmp/sles-gps-render-v14.json, /private/tmp/sles-zai-layout-v14.json, /private/tmp/slus-gps-render-v14.json]
- evidence_ids: [E-014]
- decision_delta: [gps_first_draw_branch_precedes_smoothing, gps_angle_direction_and_wrap_bounds_are_strict]
- carry_forward_refs: [evidence/E-014-ps2-gps-angle-control.md]
- next: reconstruct remaining GPS vector cross-product and matrix population ordering, then re-rank SLES behavior-bearing losses

## 2026-09-14T14:36:00+02:00 | lead | reconstruction
- action: reconstruct-ps2-gps-render-matrix-quadword-copies
- command_or_ref: direct SLES and SLUS target/current tail instruction comparison; focused and whole-object EE-GCC builds
- result_summary: replaced PS2 scalar matrix population with the target four quadword vector copies; SLES Gps::Render rose from 55.08992% to 62.850136%, SLUS rose from 59.41129% to 65.13441%, and SLES whole mapped weighted similarity rose to 90.53526156535469%
- artifacts: [evidence/E-015-ps2-gps-matrix-quadword-copies.md, /private/tmp/sles-gps-render-v16.json, /private/tmp/sles-zai-layout-v16.json, /private/tmp/slus-gps-render-v16.json]
- evidence_ids: [E-015]
- decision_delta: [ps2_gps_render_matrix_uses_four_quadword_vector_copies]
- carry_forward_refs: [evidence/E-015-ps2-gps-matrix-quadword-copies.md]
- next: reconstruct remaining GPS cross-product implementation and close the final 44-byte SLES size delta
# 2026-09-14 15:10 - implicit generated-message destructors

- command_or_ref: direct SLES/SLUS objdiff-cli focused reports and SLES whole-unit report
- result: removed redundant explicit destructors from MReqRoadBlock and MReqBackup; UpdateSupportCops reached 99.55357% SLES and 99.53571% SLUS at exact size; SLES whole-unit weighted score rose by 0.890583 percentage points on the same denominator
- artifacts: [evidence/E-016-message-implicit-destructors.md, /private/tmp/sles-update-support-cops-v2.json, /private/tmp/slus-update-support-cops-v2.json, /private/tmp/sles-zai-layout-v17.json]
# 2026-09-14 15:28 - A124 pursuit-evaded telemetry

- command_or_ref: SLES target instruction and rodata reconstruction; direct SLES/SLUS objdiff-cli reports
- result: reconstructed missing A124 PURSUIT/END/Escaped Juice event, selected the no-argument player-position symbol, gated the retail SoundAI branch, and used the implicit MPerpEscaped destructor; exact function sizes with 99.675% SLES and 99.28571% SLUS
- artifacts: [evidence/E-017-a124-pursuit-evaded-telemetry.md, /private/tmp/sles-pursuit-evaded-v3.json, /private/tmp/slus-pursuit-evaded-v3.json, /private/tmp/sles-zai-layout-v18.json]

# 2026-09-14 15:48 - road_walker A124 layout and evaluate_end

- command_or_ref: Ghidra-derived member offsets plus direct SLES/SLUS objdiff instruction streams and repeated EE-GCC builds
- result: restored PS2-aligned road_walker vectors, the VU0 v3 lerp primitive, and SLES A124 evaluation/traversal behavior; evaluate_end rose from 57.550724% to 99.07609% at exact size, SLUS reached 99.96667%, walk_all_paths rose to 69.11399%, and walk_road rose to 92.30308%
- artifacts: [evidence/E-018-road-walker-a124-layout-and-evaluate-end.md, /private/tmp/sles-evaluate-end-v8.json, /private/tmp/slus-evaluate-end-v8.json, /private/tmp/sles-walk-all-v3.json, /private/tmp/sles-walk-road-v2.json, /private/tmp/slus-walk-road-v2.json]

# 2026-09-14 16:18 - PS2 VU0 distance and A124 traffic spawning

- command_or_ref: direct SLES/SLUS instruction and relocation comparisons plus focused and whole-object EE-GCC builds
- result: restored the PS2 VU0 Vector4 xyz distance-square primitive and the distinct A124 traffic-spawn algorithm; SLES FindSpawnPoint rose from 62.331707% to 99.82927% at exact size, while the guarded SLUS retail path remains 99.60317% at exact size
- artifacts: [evidence/E-019-ps2-v4-distance-and-a124-find-spawn-point.md, /private/tmp/sles-world-collision-v2.json, /private/tmp/slus-world-collision-v2.json, /private/tmp/sles-find-spawn-point-v4.json, /private/tmp/slus-find-spawn-point-v4.json, /private/tmp/sles-zai-layout-v24.json, /private/tmp/slus-zai-layout-v4.json]

# 2026-09-14 16:42 - A124 pursuit siren behavior

- command_or_ref: Ghidra 12.1.3 headless at SLES zAI address 0x00033610; direct SLES/SLUS objdiff instruction comparisons; focused and whole-object EE-GCC builds
- result: removed the target-absent A124 roadblock scan/force-wail override and aligned equivalent siren-state branches; SLES UpdateSiren rose from 75.417175% to 97.36503%, SLUS improved to 99.15733% at exact size, and both variants build
- artifacts: [evidence/E-020-a124-update-siren-roadblock-guard.md, /private/tmp/sles-update-siren-v2.json, /private/tmp/slus-update-siren-v2.json, /private/tmp/sles-zai-layout-v26.json]

# 2026-09-14 13:48 - A124 human wrong-way behavior and layout

- command_or_ref: direct SLES/SLUS objdiff instruction comparison and repeated EE-GCC builds
- result: reconstructed A124-specific wrong-way gates, inherited navigation filters, route-direction decision, and the 12-byte A124 human-member layout gap; SLES UpdateWrongWay rose from 57.190475% at 544 bytes to 99.007935% at the exact 504-byte target size, while the guarded SLUS result remained unchanged
- artifacts: [evidence/E-021-a124-human-wrong-way.md, /private/tmp/sles-update-wrong-way-v3.json, /private/tmp/sles-zai-layout-v28.json, /private/tmp/slus-update-wrong-way-v2.json]

# 2026-09-14 13:52 - A124 spawned-vehicle Juice statistics

- command_or_ref: Ghidra 12.1.3 headless at SLES zAI address 0x0002b548 plus direct SLES/SLUS objdiff instruction comparison
- result: reconstructed the missing A124 per-race spawn counters for police, police helicopters, and traffic; SLES AIVehicle::SetSpawned rose from 61.09655% at 368 current bytes to an exact 100% 580-byte match, while SLUS remains 99.51087% at 368 bytes
- artifacts: [evidence/E-022-a124-set-spawned-juice-stats.md, /private/tmp/sles-set-spawned-v2.json, /private/tmp/sles-zai-layout-v29.json, /private/tmp/slus-set-spawned-v1.json]

# 2026-09-14 13:55 - A124 unspawned-vehicle Juice statistics

- command_or_ref: direct SLES target instruction reconstruction plus focused and whole-unit SLES/SLUS objdiff builds
- result: reconstructed the mirrored A124 police, police-helicopter, and traffic unspawn counters; SLES AIVehicle::UnSpawn rose from 50.54717% at 224 current bytes to 99.90566% at the exact 424-byte target size, while SLUS remains 99.28571% at 224 bytes
- artifacts: [evidence/E-023-a124-unspawn-juice-stats.md, /private/tmp/sles-unspawn-v1.json, /private/tmp/sles-zai-layout-v30.json, /private/tmp/slus-unspawn-v1.json]

# 2026-09-14 13:58 - A124 race preparation tail

- command_or_ref: direct SLES target instruction and rodata reconstruction plus focused and whole-unit SLES/SLUS objdiff builds
- result: restored A124 input unblocking, race-filter/nav reset, AIGoalRa selection, and unclamped heat; SLES AIVehicleRacecar::PrepareForRace rose from 67.189545% at 464 current bytes to 99.803925% at the exact 612-byte target size, while SLUS remains 99.69827%
- artifacts: [evidence/E-024-a124-prepare-for-race.md, /private/tmp/sles-prepare-race-v2.json, /private/tmp/sles-zai-layout-v31.json, /private/tmp/slus-prepare-race-v1.json]

# 2026-09-14 14:11 - A124 helicopter update and copy contracts

- command_or_ref: Ghidra 12.1.3 headless at SLES zAI address 0x00033f40; direct SLES instruction comparison; focused and whole-object EE-GCC builds
- result: reconstructed the A124 out-of-line player lookup, memberwise WCollisionTri/WWorldPos copy behavior, 64-bit packed-counter storage, and target-absent unused clamp; SLES AIVehicleHelicopter::Update rose from 66.09091% to 93.82468%, five additional callers became exact, and SLES zAI .text rose from 88.45702% to 88.93593%
- artifacts: [evidence/E-025-a124-helicopter-update-copy-contracts.md, /private/tmp/sles-heli-update-v3.json, /private/tmp/sles-zai-layout-v32.json, /private/tmp/slus-zai-layout-v5.json]

# 2026-09-14 14:16 - A124 AI-control behavior overrides

- command_or_ref: Ghidra 12.1.3 headless at SLES zAI address 0x00027c00; target relocation and IVehicle interface correlation; focused and whole-object EE-GCC builds
- result: reconstructed the A124 AIGoalRa selection and direct SuspensionSimple/input behavior-override transition; SLES AIVehicleHuman::SetAiControl rose from 48.96154% at 188 current bytes to 99.61539% at the exact 312-byte target size, and SLES zAI .text rose from 88.93593% to 88.99277%

## E-027 - A124 traffic density

- source: SLES Ghidra decompilation and instruction/relocation comparison for `AITrafficManager::ComputeDensity`
- result: removed the retail pursuit-density multiplier from the A124 path and reconstructed its direct-return control flow; the function rose from 74.046875% at 256/296 bytes to 100% at 256/256 bytes, SLES zAI `.text` rose to 89.01699%, and both SLES and SLUS objects build

## E-028 - A124 Vector4 ABI

- source: Ghidra CLI at SLES `AIVehicle::WorldCollision` 0x0002ac50 and direct aggregate-copy instruction comparison
- result: restored natural rather than 16-byte type alignment for `UMath::Vector4` on A124; `WorldCollision` rose from 55.887417% at 604/480 bytes to 100% at 604/604 bytes and SLES zAI `.text` rose from 89.01699% to 89.62933%; SLUS retains the later aligned ABI and rebuilds unchanged

## E-029 - A124 GPS render ordering

- source: SLES `Gps::Render` instruction/relocation comparison against Ghidra-confirmed control flow
- result: restored the A124 rear-view aspect rule, output-only inverse-camera matrix storage, and allocation-before-basis ordering; SLES `Gps::Render` rose from 62.850136% at 1468/1512 bytes to 70.14714% at 1468/1452 bytes and whole zAI `.text` reached 89.667854%; SLUS rebuilds with its later path unchanged

## E-030 - A124 Matrix4 and traffic constructor

- source: Ghidra CLI at SLES `AIActionTraffic` constructor 0x0001bf50 plus aggregate-copy, field-offset, and relocation comparison
- result: restored natural A124 `Matrix4` type alignment, explicit aligned traffic navigation member, and the historical two-argument collision-listener overload; the constructor rose from 66.771736% at 736/680 bytes to 98.690216% at 736/736 bytes and SLES zAI `.text` reached 89.90999%; SLUS rebuilds unchanged
- artifacts: [evidence/E-026-a124-set-ai-control-behavior-overrides.md, /private/tmp/sles-set-ai-control-v2.json, /private/tmp/sles-zai-layout-v33.json, /private/tmp/slus-set-ai-control-v1.json]
# E-031: A124 CopRequest wave and cooldown limits

- Restored the A124-only pursuit-wave cap and cooldown `NumPatrolCars`
  recalculation in `AIPursuit::CopRequest`.
- SLES function match: 74.59649% -> 87.61053%.
- SLES `.text`: 89.90999% -> 89.96335%.
- SLUS remains 86.05584%.
# E-032: A124 collision-triggered pursuit debug state

- Restored the A124 `DebugMenu::mCurPursuitState = 2` write after an
  intentional collision starts pursuit targeting.
- SLES `OnCausedCollision`: 84.906% -> 85.076%.
- SLES `.text`: 89.96335% -> 89.96457%.

# E-033: A124 road-nav spline layout and helicopter navigation

- Recovered `WRoadNav` layout directly from SLES `WRoadNav::WRoadNav` at
  `0x4664e8`: aligned `USpline` members begin at `0x1d0`, use a `0x70` stride,
  and place `fDeadEnd` at `0x320`.
- Restored A124-only `USpline` alignment plus target vector-operation and loop
  ordering in `AIActionHeliPursuit::UpdateNavPos`.
- SLES `UpdateNavPos`: 76.281044% -> 96.07843% at 612/612 bytes.
- SLES `.text`: 89.96457% -> 90.02599%; SLUS remains 86.05584%.
- artifacts: [evidence/E-033-a124-road-nav-spline-layout-and-heli-nav.md, /private/tmp/sles-zai-layout-v48.json, /private/tmp/slus-zai-layout-v12.json]

# E-034: A124 collision and helicopter follow-road pursuit

- Restored the SLES collision guard/order and reconstructed the missing
  A124-only `AIActionHeliPursuit::FollowRoadPursuit` mode from the ELF.
- SLES `OnCausedCollision`: 85.084% -> 99.54%; `FollowRoadPursuit`: absent ->
  98.39838%; helicopter `Update`: 77.61468% -> 99.57798%.
- SLES `.text`: 90.02599% -> 90.34374%; SLUS remains exactly 86.05584%.
- artifacts: [evidence/E-034-a124-collision-and-helicopter-follow-road.md, /private/tmp/sles-zai-layout-v62.json, /private/tmp/slus-zai-layout-v16.json]

# E-035: A124 race skill computation

- Recovered the SLES-only `ComputeSkill` entry condition and quick-race
  difficulty mapping from `0x11eab0`; the older target has no player or
  catch-up query in this path.
- SLES `ComputeSkill`: 73.82979% at 376/412 bytes -> 99.989365% at 376/376.
- SLES `.text`: 90.34374% -> 90.37911%; SLUS rebuilds and remains exactly
  86.05584%.
- artifacts: [evidence/E-035-a124-compute-skill.md, /private/tmp/sles-zai-layout-v65.json, /private/tmp/slus-zai-layout-v17.json]

# E-036: PS2 VU0 three-vector add-scale primitive

- Recovered the missing PS2 inline `VU0_v3addscale` sequence from SLES
  `AIVehicleHelicopter::SetDestinationVelocity` at `0x123ed0`.
- `SetDestinationVelocity`: 57.407406% at 108/88 bytes -> 99.62963% at
  108/108 on both PS2 targets.
- SLES `.text`: 90.37911% -> 90.395515%; SLUS `.text`: 86.05584% ->
  86.07213%.
- artifacts: [evidence/E-036-ps2-v3addscale.md, /private/tmp/sles-zai-layout-v67.json, /private/tmp/slus-zai-layout-v18.json]

# E-037: Adaptive-controller zero-derivative path

- Recovered the direct zero comparisons and distinct return path in SLES
  `GetSensitivityDerivative(float)` at `0x125e48`.
- Function match: 63.88889% at 180/168 bytes -> 86.666664% at 180/180 on
  both PS2 targets.
- SLES `.text`: 90.395515% -> 90.410255%; SLUS `.text`: 86.07213% ->
  86.08678%.
- artifacts: [evidence/E-037-adaptive-sensitivity-zero-path.md, /private/tmp/sles-zai-layout-v71.json, /private/tmp/slus-zai-layout-v19.json]

# E-038: PS2 AITarget copy-acquire call shape

- Recovered the ordinary-call layout in SLES `AITarget::Aquire(const AITarget *)`
  at `0x126718`; the zAI source-list build had incorrectly tail-called both
  overloads.
- Function match: 64.5% at 64/80 bytes -> 99.5% at 80/80 on both PS2 targets.
- SLES `.text`: 90.410255% -> 90.42033%; SLUS `.text`: 86.08678% ->
  86.09678%.
- artifacts: [evidence/E-038-aitarget-copy-acquire-call-shape.md, /private/tmp/sles-zai-layout-v73.json, /private/tmp/slus-zai-layout-v20.json]

# E-039: PS2 AITarget null-target return path

- Recovered the distinct null-target early return in SLES
  `AITarget::IsTarget(const AITarget *)` at `0x126768`.
- Function match: 69.45% -> 100.0% at the exact 160/160-byte size on both PS2
  targets.
- SLES `.text`: 90.42033% -> 90.437904%; SLUS `.text`: 86.09678% ->
  86.11424%.
- artifacts: [evidence/E-039-aitarget-is-target-null-path.md, /private/tmp/sles-zai-layout-v74.json, /private/tmp/slus-zai-layout-v22.json]

# E-040: PS2 AITarget simable-acquire early return

- Recovered the negative `CanAquire` early return and terminal
  `TrackInternal` tail jump in SLES `AITarget::Aquire(ISimable *)` at
  `0x1265b0`.
- Function match: 80.703705% at 96/108 bytes -> 99.44444% at 108/108 on both
  PS2 targets.
- SLES `.text`: 90.437904% -> 90.44518%; SLUS `.text`: 86.11424% ->
  86.121475%.
- artifacts: [evidence/E-040-aitarget-simable-early-return.md, /private/tmp/sles-zai-layout-v75.json, /private/tmp/slus-zai-layout-v23.json]

# E-041: A124 pursuit-end overrides

- Recovered the A124-only `ForcePursuitEnd` one-shot and
  `ForcePursuitNeverEnd` persistent override paths from SLES
  `AIPursuit::ShouldEnd() const` at `0x1301c8`.
- SLES function match: 65.674416% at 140/172 bytes -> 100.0% at 172/172.
- SLES `.text`: 90.44518% -> 90.466415%.
- SLUS correctly retains its 140-byte implementation and 86.121475% zAI
  `.text` total.
- artifacts: [evidence/E-041-a124-pursuit-end-overrides.md, /private/tmp/sles-zai-layout-v77.json, /private/tmp/slus-zai-layout-v25.json]

# E-042: A124 end-formation debug-state cleanup

- Recovered the missing A124 debug pursuit-level, state, and formation resets
  from SLES `AIPursuit::EndCurrentFormation()` at `0x12be38`.
- Function match: 35.833332% at 20/48 bytes -> 47.083332% at the exact 48/48
  bytes; remaining differences are scheduling and relocation ordering.
- SLES `.text`: 90.466415% -> 90.46836%; SLUS remains 86.121475% with its
  retail 20-byte implementation.
- artifacts: [evidence/E-042-a124-end-formation-debug-state.md, /private/tmp/sles-zai-layout-v81.json, /private/tmp/slus-zai-layout-v26.json]

# E-043: A124 cop-lockout behavior

- Recovered the compact A124 `AICopManager::LockoutCops(bool)` behavior at
  SLES `0x10a660`: 45-second default and no later speech/race override path.
- Function match: 0.0% at 240/36 bytes -> 100.0% at 36/36.
- SLES `.text`: 90.46836% -> 90.48132%; SLUS remains 86.121475% with its
  newer 240-byte implementation.
- artifacts: [evidence/E-043-a124-cop-lockout.md, /private/tmp/sles-zai-layout-v82.json, /private/tmp/slus-zai-layout-v27.json]

# E-044: Helicopter-pursuit finished branch

- Recovered the explicit true/false branch form in SLES
  `AIActionHeliPursuit::IsFinished()` at `0x10fd28`.
- Function match: 74.0% -> 100.0% at 40/40 bytes on both PS2 targets.
- SLES `.text`: 90.48132% -> 90.48506%; SLUS `.text`: 86.121475% ->
  86.12519%.
- artifacts: [evidence/E-044-heli-pursuit-finished-branch.md, /private/tmp/sles-zai-layout-v85.json, /private/tmp/slus-zai-layout-v28.json]

# E-045: PS2 curvature speed-limit operation order

- Recovered the retail floating-point operation order in
  `GetSpeedLimitForCurvature(float, float, float)` at SLES `0x1154a0`.
- Function match: 66.666664% -> 100.0% at 48/48 bytes on both PS2 targets.
- SLES `.text`: 90.48506% -> 90.490814%; SLUS `.text`: 86.12519% ->
  86.130905%.
- artifacts: [evidence/E-045-curvature-speed-order.md, /private/tmp/sles-zai-layout-v86.json, /private/tmp/slus-zai-layout-v29.json]

# E-046: PS2 speed-limit expression scheduling

- Recovered more of the retail expression schedule in
  `GetSpeedLimit(float, float, float, float)` while retaining the exact
  92-byte function size.
- Function match: 82.17391% -> 91.304344% on both PS2 targets.
- SLES `.text`: 90.490814% -> 90.493835%; SLUS `.text`: 86.130905% ->
  86.1339%.
- artifacts: [evidence/E-046-speed-limit-scheduling.md, /private/tmp/sles-zai-layout-v88.json, /private/tmp/slus-zai-layout-v30.json]

# E-047: AIVehicle causality-clear branch shape

- Recovered the retail true/false branch layout in
  `AIVehicle::OnClearCausality(float)`.
- SLES function: 84.70588% -> 100.0%; SLUS: 84.70588% -> 99.70588% (only
  the retail `GetTime` symbol label remains different).
- SLES `.text`: 90.493835% -> 90.497574%; SLUS `.text`: 86.1339% ->
  86.13762%.
- artifacts: [evidence/E-047-on-clear-causality-branch.md, /private/tmp/sles-zai-layout-v93.json, /private/tmp/slus-zai-layout-v31.json]

# E-048: pursuit and ram attempt-gate control flow

- Recovered the retail pointer-check and boolean-result layout in both
  `AIActionPursuitOffRoad::CanBeAttempted(float)` and
  `AIActionRam::CanBeAttempted(float)`.
- Both functions: 86.129036% -> 99.83871% at the exact 124-byte size on both
  PS2 targets.
- SLES `.text`: 90.497574% -> 90.5098%; SLUS `.text`: 86.13762% ->
  86.149765%.
- artifacts: [evidence/E-048-attempt-gate-control-flow.md, /private/tmp/sles-zai-layout-v96.json, /private/tmp/slus-zai-layout-v33.json]

# E-049: head-on ram attempt-gate control flow

- Recovered the ordered null and negative predicate branches in
  `AIActionHeadOnRam::CanBeAttempted(float)`.
- Function: 90.23256% -> 100.0% at 172/172 bytes on both PS2 targets.
- SLES `.text`: 90.5098% -> 90.51585%; SLUS `.text`: 86.149765% ->
  86.15577%.
- artifacts: [evidence/E-049-head-on-ram-attempt-gate.md, /private/tmp/sles-zai-layout-v97.json, /private/tmp/slus-zai-layout-v34.json]

# E-050: too-damaged attempt-gate control flow

- Recovered the separate vehicle/input null checks in
  `AIActionTooDamaged::CanBeAttempted(float)`.
- Function: 67.05882% -> 100.0% at 68/68 bytes on both PS2 targets.
- SLES `.text`: 90.51585% -> 90.5239%; SLUS `.text`: 86.15577% ->
  86.163765%.
- artifacts: [evidence/E-050-too-damaged-attempt-gate.md, /private/tmp/sles-zai-layout-v100.json, /private/tmp/slus-zai-layout-v35.json]

# E-051: traffic attempt-gate control flow

- Recovered the ordered rigid-body, AI, input, and vehicle null checks in
  `AIActionTraffic::CanBeAttempted(float)`.
- Function: 95.379745% -> 99.974686% at the exact 316-byte size on both PS2
  targets; the remaining difference is one compiler branch encoding.
- SLES `.text`: 90.5239% -> 90.52912%; SLUS `.text`: 86.163765% ->
  86.16895%.
- artifacts: [evidence/E-051-traffic-attempt-gate.md, /private/tmp/sles-zai-layout-v107.json, /private/tmp/slus-zai-layout-v36.json]

# E-052: PS2 race-potential ABI and behavior split

- Recovered the PS2 `AIActionRace` overloads: `GetPotentialSpeed(float,
  float)` and `GetPotentialAcceleration(float, float, bool)`, with the later
  drag-aware overloads retained for non-PS2 builds.
- SLES `.text`: 90.52912% -> 91.44376%; SLUS `.text`: 86.16895% ->
  87.05911%.
- The newly mapped potential-speed/acceleration functions have exact retail
  sizes on both PS2 targets; both PS2 zAI objects build successfully.
- artifacts: [evidence/E-052-ps2-race-potential-signatures.md, /private/tmp/sles-zai-layout-v112.json, /private/tmp/slus-zai-layout-v38.json]

# E-053: PS2 Matrix4 layout and copy ABI

- Reconstructed the shared PS2 `Matrix4` declaration so both PS2 targets use
  the unaligned layout required by the retail MIPS copy sequence; non-PS2
  targets retain `PS2ALIGN16`.
- SLUS `AIActionTraffic` constructor: `63.913044%` at 680 source bytes ->
  `94.90761%` at 744 source bytes against the 736-byte retail body. The
  emitted identity-matrix copy now matches the retail `ldl`/`ldr` and
  `sdl`/`sdr` sequence.
- SLUS zAI `.text`: `87.99221%` -> `88.21677%` (282184 bytes).
- SLES zAI `.text`: `91.50805%` at the validated post-change build;
  `AIActionTraffic` constructor is `98.690216%`, `WorldCollision` is `100%`,
  and helicopter `Update` is `93.82468%`.
- Ghidra CLI inventory confirmed the full SLES ELF base (`0x100000`) and
  mapped `WorldCollision` at `0x0011ac50`; decompilation is truncated by a
  bad-instruction boundary, so only the symbol/base mapping is asserted.
- artifacts: [evidence/E-053-ps2-matrix4-layout.md, /private/tmp/slus-zai-layout-v53.json, /private/tmp/sles-zai-layout-v129.json, /private/tmp/sles-inventory-now.tsv]

# E-054: PS2 WRoadNav and WWorldPos layout

- Matched the SLUS retail `WRoadNav` allocation contract (`0x350` bytes;
  second member at `this + 0x520`) by applying the existing A124 `USpline`
  alignment to all PS2 builds.
- Matched the SLUS retail `WWorldPos` copy contract by using an 8-byte PS2
  bitfield storage unit, producing the retail eight-pair `ld`/`sd` copy and
  a `0x40`-byte object. Non-PS2 builds retain the 32-bit bitfield layout.
- SLUS zAI `.text`: `88.21677%` -> `88.29549%`.
- SLUS `AIVehicleHelicopter::Update(float)`: `62.88889%` at 524 bytes ->
  `99.82906%` at the exact 468-byte retail/source size.
- SLES remains stable at `91.50805%` zAI `.text`, `WorldCollision` `100%`,
  and helicopter `Update` `93.82468%`.
- artifacts: [evidence/E-054-ps2-roadnav-and-worldpos-layout.md, /private/tmp/slus-zai-layout-v58-uspline-wworldpos-clean.json, /private/tmp/sles-zai-layout-v130-uspline-wworldpos-clean.json]

# E-055: PS2 traffic ABI and A124 BeginAction

- Split the A124 no-argument `AIVehicleTraffic::StartDriving` ABI from the
  speed-argument PS2/other-platform implementation and matched the A124
  `AIActionTraffic::BeginAction` omission/scaling sequence.
- SLES `StartDriving` is `60/60` bytes at `99.0%`; `BeginAction` is `312/312`
  bytes at `98.46154%`; `SpawnTraffic` is `812/812` bytes at `99.85222%`.
- SLES zAI `.text`: `91.57527%` -> `91.591064%`. SLUS remains `88.29549%`
  with its non-A124 speed ABI preserved.
- Ghidra CLI cross-check recorded the remaining SLUS `IsCollapseActive`
  return/layout mismatch as an open gap rather than applying an unsafe global
  layout change.
- artifacts: [evidence/E-055-ps2-traffic-abi-and-a124-begin-action.md, /private/tmp/sles-zai-traffic-begin-a124-final.json, /private/tmp/slus-zai-traffic-begin-final.json]
