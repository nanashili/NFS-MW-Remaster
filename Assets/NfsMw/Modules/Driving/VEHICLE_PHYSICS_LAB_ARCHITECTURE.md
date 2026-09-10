# Vehicle Physics Lab architecture

The Vehicle Physics Lab is an editor workbench for the shared driving simulation. It is not a second tire model, road network, garage, save service, or reward system. A run creates a disposable physics fixture, feeds the same `VehicleInputState` contract used by the game, calls the canonical `VehicleController`, and records raw state after the isolated physics scene has advanced.

## Ownership map

| Concern | Authoritative owner | Physics Lab responsibility |
| --- | --- | --- |
| Player, AI, and replay intent | `IVehicleInputSource` / `VehicleInputState` | Provide generated, recorded, or live input through the same boundary |
| Force generation and integration order | `VehicleController`, `VehicleWheel`, `VehiclePowertrain`, `VehicleAssists`, `VehicleNitrous` | Manually invoke the existing controller, then simulate the target `PhysicsScene` |
| Vehicle defaults and installed performance parts | `RacingVehicleSetup` and `VehiclePerformanceBuild` | Resolve a runtime copy and show provenance; never mutate source during a run |
| Surface response | `VehicleSurface` and the shared wheel contact path | Create disposable fixture colliders carrying the authoritative surface component |
| Road topology and racing lines | Road authoring and Racing Line Studio | Consume a capability profile when explicitly baked; never author a competing road graph |
| Career, wallet, garage, missions, and saves | Existing game services | Remain out of scope and are not touched by preview runs |
| Measured capability data | `RacingCapabilityProfile` | Bake only from validated raw samples and record run/fingerprint evidence |

Runtime data contracts live in `NfsMwRemaster.Driving` so reports and definitions can be serialized without leaking `UnityEditor` references into player builds. The Physics Lab editor code is isolated in `NfsMwRemaster.Driving.PhysicsLab.Editor`; unrelated driving-editor tools remain in `NfsMwRemaster.Driving.Editor`.

## Dependency graph

```text
VehicleTuning + performance upgrades
                 |
                 v
       RacingVehicleSetup -----> VehiclePhysicsLabDefinition
                 |                         |
                 v                         v
       RacingVehicleRig <----- VehiclePhysicsLabRunner
                 |                         |
                 v                         v
 VehicleController / wheels ---> VehiclePhysicsLabRunReport
                                           |
                +--------------------------+--------------------------+
                v                          v                          v
       JSON/CSV evidence          comparison/charts          RacingCapabilityProfile
```

`VehiclePhysicsLabTrack` is a compact fixture description, not a production road. `VehiclePhysicsLabTrackAnchor` is authoring-only and visualizes the same description in a normal scene. The runner creates fresh colliders in `EditorSceneManager.NewPreviewScene`, so a run cannot silently add objects to the open world or use the default physics scene.

## Runtime and authoring schemas

All serialized lab definitions have an explicit `schema` and stable `id`:

- `VehiclePhysicsLabTrack` (`CurrentSchema = 1`) describes fixture geometry, dimensions, and surface metadata.
- `VehiclePhysicsLabDefinition` (`CurrentSchema = 1`) describes one experiment, setup, input schedule, timing, thresholds, capture/safety budgets, overrides, and reference provenance.
- `VehiclePhysicsLabSuite` (`CurrentSchema = 1`) references bounded experiment assets and repetitions.
- `VehiclePhysicsLabSweepDefinition` (`CurrentSchema = 1`) references one base experiment and up to four approved tunable axes with a bounded Cartesian trial budget.
- `VehiclePhysicsLabRunReport` (`CurrentSchema = 1`) stores environment, revision/fingerprint, measurement phase, diagnostics, raw samples, metrics, and collision events.

Run reports are plain serializable data. They are not attached to player objects and are retained by the editor window only as a bounded history of 16 reports. Sample and collision buffers are bounded by the definition; imports are rejected above the report budget.

## Simulation invariants

1. A manual run is rejected in Play Mode and if its vehicle is in the default physics scene.
2. Each tick consumes exactly one input, calls `VehicleController.StepSimulation(fixedStep)`, calls `previewScene.GetPhysicsScene().Simulate(fixedStep)`, then samples telemetry.
3. Warmup uses a fresh rig, brakes/handbrake held, and is excluded from measurement time.
4. The source vehicle setup, tuning, upgrades, garage selection, career, wallet, save data, and project-wide physics settings are not mutated by a run.
5. Temporary overrides apply to a runtime tuning copy. Applying them to the source is a separate Undo-recorded editor command.
6. A stale source/setup/track/override fingerprint terminates the run instead of accepting mixed configuration data.
7. A censored threshold (for example, 0–100 not reached before the safety time limit) is reported as unavailable/failed; the lab never extrapolates it into a measured value.
8. Input channels are separated into boundary/raw input, shaped input where exposed, and final input consumed by force generation.
9. Any non-finite state, fixture escape, rollover, velocity-budget breach, or capture-budget breach produces an actionable status.
10. Disposal closes the preview scene and destroys the cloned setup, runtime tuning, fixture objects, meshes, and runner callbacks.

## Editor layers

- `VehiclePhysicsLabWindow` is the UI-only workbench. It provides Vehicle, Configuration, Test Track, Experiments, Telemetry, Comparison, Calibration, and Regression views.
- `VehiclePhysicsLabSceneTools` provides inspectors, a SceneView overlay, track visualization, and an Undo-aware `EditorTool` for moving an anchor/resizing a fixture.
- `VehiclePhysicsLabRunner` owns the transient manual stepping lifecycle.
- `VehiclePhysicsLabSweepRunner` owns bounded deterministic parameter sweeps. It clones the base experiment, merges trial overrides, and disposes each isolated runner before the next trial.
- `VehiclePhysicsLabAnalysis` contains deterministic metric/comparison functions over report samples.
- `VehiclePhysicsLabEditorOperations` owns report validation/export, effective tuning resolution, demo assets, and explicit capability baking.
- `VehiclePhysicsLabBatch` is the bounded command-line adapter. It loads authored assets, runs fresh scenarios, writes JSON/CSV, and exits with 0 (all passed), 1 (failed/invalid), or 2 (incomplete/timeout). Sweeps remain editor-reviewed candidates and are intentionally not silently published by batch mode.

Unity’s installed package manifest does not include the Graph Toolkit package. The lab therefore uses the project’s existing UI Toolkit/IMGUI mix, SceneView overlays, editor tools, Handles, and preview-scene APIs. A node graph would add no value to the core stepping contract and would risk creating a second execution model; generated experiments remain data assets and the numerical path stays testable outside the window.

## Fingerprints and invalidation

The run fingerprint includes the definition serialization, stable definition/vehicle/track identities, effective vehicle fingerprint, shared simulation revision, and Unity version. A capability bake stores the run ID, definition fingerprint, vehicle fingerprint, coverage counts, and an explicit statement that unsupported regions remain assumptions. Racing Line Studio already treats a capability fingerprint as stale when its upstream vehicle contract changes; the lab does not bypass that check.

## Milestone status

| Milestone | Status in this implementation |
| --- | --- |
| M01 telemetry contract | Complete: raw/final input, chassis, wheel, assist, drivetrain, nitrous, and collision channels are exposed where the shared vehicle exposes them |
| M02 isolated runner/reset | Complete: fresh `RacingVehicleRig`, preview physics scene, manual stepping, warmup, cancellation, stale-input and cleanup guards |
| M03 longitudinal baseline | Complete: launch, rolling/coast/brake/repeated brake, gearshift, and nitrous generated cases plus threshold/censoring metrics |
| M04 lateral/drift suite | Complete: step/sine/slalom/skidpad/lane-change/combined/brake-to-drift/sustained/transition cases |
| M05 comparison/configuration | Complete: temporary overrides, legal ranges, source/effective table, time-aligned comparison, explicit Undo apply |
| M06 capability bake | Complete: measured acceleration/braking/lateral envelope bake with coverage/evidence metadata |
| M07 regression/batch | Complete: suite runner, bounded history, CLI selection/seeds/timeout/output, incomplete reports |
| M07b bounded sweeps | Complete: deterministic mixed-radix grid, four-axis/1024-trial caps, fresh rig per trial, override provenance, stop-on-failure |
| M08 contact/scale hardening | Fixture support exists for surfaces, ramp/landing, and contact walls; hardware profiling and long-capture streaming remain project validation work |

## Deliberate limitations

The shared vehicle currently does not expose mechanical damage state, camera/FOV/shake, audio/VFX presentation channels, or an original-game input trace. These are rendered as unavailable rather than inferred. Third-party video may be attached as provenance/reference evidence, but the tool does not reconstruct world-space yaw, tire forces, or steering input from it.

The runner uses the current project-wide gravity and solver values as read-only configuration evidence. It does not change them. Cross-platform PhysX results should be compared with tolerances and build-based measurements; a fixed seed is not a promise of bit-identical trajectories.
