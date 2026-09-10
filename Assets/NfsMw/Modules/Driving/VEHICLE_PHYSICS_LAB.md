# Vehicle Physics Lab guide

## Open the tool

In the Unity editor, open **NFS MW Remaster > Driving > Vehicle Physics Lab**. The quickest first-run path is:

1. Click **Create Demo**.
2. Select the generated `StandingLaunch.asset` in the **Experiment** field.
3. Click **Validate** in the left panel.
4. Open **Experiments** and click **Run**.
5. Inspect **Telemetry**, **Comparison**, and **Calibration**. Reports can be exported with **JSON** or **CSV**.

The demo uses the first valid `RacingVehicleSetup` found in the project. Review the selected vehicle and track before treating the result as a production baseline.

## Author an experiment

Create an experiment from the toolbar or from the Project window with **Create > NFS MW Remaster > Driving > Vehicle Physics Lab Experiment**. Assign:

- a canonical `RacingVehicleSetup`;
- a `VehiclePhysicsLabTrack` fixture;
- an experiment kind;
- a fixed step between 0.005 and 0.05 seconds;
- warmup, safety, capture, and evaluation budgets;
- generated, recorded, or live input.

The top-level target/start fields are authoring conveniences. Threshold evaluation uses the `evaluation` block, so set `evaluation.targetSpeedKph`, `evaluation.targetStopSpeedKph`, and `evaluation.requireTargetSpeed` explicitly for a regression case.

### Input modes

- **Generated** drives a repeatable stimulus for the selected experiment. `stepTime`, steering, throttle, sine frequency/amplitude, transition time, handbrake, and nitrous configure it.
- **Recorded** interpolates a bounded, non-decreasing array of `VehiclePhysicsLabControlKey` values. This is the preferred mode for repeatable input-latency comparisons.
- **Live** reads the editor keyboard through the Input System: W throttle, S brake, A/D steering, Space handbrake, and either Shift nitrous. Live runs are for qualitative inspection and should not be used as golden regression input.

The runner captures the boundary input and the final input consumed by `VehicleController` separately. This makes response shaping visible instead of mislabeling it as tire behavior.

## Test fixtures

`VehiclePhysicsLabTrack` supports flat straight, constant-radius, slalom, lane-change, ramp, surface-sweep, and contact-arena fixtures. The track asset is only a compact experiment description. The actual colliders are created in a disposable preview physics scene for each run.

To visualize the authoring track in a normal scene:

1. In **Test Track**, click **Create scene anchor**.
2. Select the anchor and open the SceneView **Vehicle Physics Lab** overlay.
3. Use **Activate track tool**. Position changes and width changes are Undoable.

The anchor is not a runtime road and does not replace the authoritative road/lane network.

## Effective configuration and safe apply

The **Configuration** view resolves values in this order:

```text
VehicleTuning → installed performance upgrades → scenario temporary overrides
```

Each supported field displays its effective value, source, and legal range. Temporary overrides only modify the runtime copy used by the runner. The source asset changes only after **Apply Overrides to Vehicle…**, the confirmation dialog, `Undo.RecordObject`, validation, and an asset save.

If the setup has upgrades, review the warning carefully: applying a value to base tuning can still be changed by the setup’s later performance build. For a clean experiment, compare the effective table and keep the scenario override instead.

## Reading a report

Every report includes:

- stable run/definition/vehicle fingerprints;
- Unity version, platform, fixed step, warmup, seed, and sample phase;
- raw/final inputs and shared vehicle telemetry;
- pose, velocity, acceleration, yaw, slip validity, wheel contacts/load/slip/force, RPM/gear/torque, assists, nitrous, and collision events where exposed;
- bounded samples and diagnostics;
- metrics recomputed from the captured samples.

`time_to_target` and `stopping_distance` are unavailable when the required interval was not captured. A failure to reach 0–100 or 100–0 within the configured budget is censored/failed, not extrapolated. Low-speed slip angle is explicitly invalid below the stable speed threshold.

The SceneView trajectory uses the report’s captured world positions. Force vectors and wheel contact discs can be enabled from the overlay or window. The key diagnostic question is whether the observed turn is represented by input, wheel forces, the shared assist yaw contribution, or a collision event.

## Comparison and capability bake

Run the same generated or recorded case twice with different effective configurations. The **Comparison** view aligns candidate samples to baseline measurement timestamps and does not apply an arbitrary latency-hiding shift. It reports metric deltas and charts raw series.

To publish a measured AI capability:

1. Assign a `RacingCapabilityProfile` to `publishToCapability`.
2. Capture enough longitudinal coverage; the bake requires at least eight samples and two positive acceleration bins.
3. Open **Calibration** and click **Bake measured capability**.
4. Review the Undo record and the stored run/fingerprint/coverage evidence.

The bake preserves existing values for unsupported bins/regions and marks measured channels explicitly. Do not bake a stock tune and use it for an upgraded drift build without rerunning the experiment.

Reference evidence can be attached from **Calibration**. It stores the source path and provenance metadata only. A video without synchronization/input overlays is qualitative evidence; it is not converted into steering or tire-force telemetry.

## Regression suites

Create a `Vehicle Physics Lab Suite`, add valid experiment assets, set repetitions, and enable **Run Suite** in **Regression**. Every entry gets a fresh isolated rig. Stop, close, assembly reload, Undo/Redo, and Play Mode transitions dispose the active preview.

For parameter optimization, create a `Vehicle Physics Lab Sweep`, assign a valid base experiment, and enable one to four approved tuning axes. Each axis has an inclusive minimum/maximum and 2–32 samples; the product of the axes must stay within the explicit trial budget (maximum 1024). **Run parameter sweep** enumerates mixed-radix combinations in deterministic order, disposes every trial rig before starting the next one, records the applied override values and label in each report, and stops on the first failed trial when configured to do so. A sweep never edits the base experiment or vehicle asset. Treat its output as calibration candidates until a designer reviews the reports and explicitly bakes a capability profile or applies a source change.

The sweep is intentionally an optimizer seam rather than an opaque optimizer: the bounded axis/override contract can later be driven by a grid, Latin hypercube, or search strategy without changing the runner, telemetry, or report format. The current editor implementation provides deterministic bounded grid enumeration and fresh-rig isolation; it does not claim an optimum from a sparse grid.

## Command-line execution

Run authored suites in batch mode with an explicit asset and output path:

```text
/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit -projectPath "/Users/tihan-nico/NFS MW Remaster" \
  -physicsLabBatch \
  -physicsLabDefinition Assets/NfsMw/Modules/Driving/Examples/VehiclePhysicsLab/StandingLaunch.asset \
  -physicsLabOutput /tmp/vehicle-physics-lab/standing-launch.json \
  -physicsLabSeeds 1,2,3 \
  -physicsLabTimeoutSeconds 300 \
  -logFile /tmp/vehicle-physics-lab/unity.log
```

Use exactly one of `-physicsLabDefinition` or `-physicsLabSuite`. The value may be an `Assets/...` path or an asset GUID. If the output has no extension or names an existing directory, the runner writes `physics-lab-batch.json`; otherwise it writes the requested JSON path and a sibling `.csv` file.

Exit status:

- `0`: every requested run passed;
- `1`: invalid input, failed run, or exception;
- `2`: incomplete run, cancellation, or wall-clock timeout.

An incomplete run includes its partial samples and diagnostics. It is never folded into an all-passed result.

## EditMode tests

From the Unity Test Runner, select the `NfsMwRemaster.Driving.Tests` EditMode assembly and run the `VehiclePhysicsLabTests` fixture. From the command line, the installed Test Framework supports the usual EditMode invocation; verify the package version and local project license before relying on CI results:

```text
/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "/Users/tihan-nico/NFS MW Remaster" \
  -runTests -testPlatform editmode \
  -testResults /tmp/vehicle-physics-lab/editmode-results.xml \
  -testFilter NfsMwRemaster.Driving.Tests.VehiclePhysicsLabTests \
  -quit -logFile /tmp/vehicle-physics-lab/editmode.log
```

Tests cover pure time-aligned analysis, censored targets, stopping distance, legal override ranges, track sampling, report validation, and the isolated runner lifecycle. The runner test does not create or modify project assets.

The sweep model is covered by the same EditMode fixture for mixed-radix enumeration and trial-budget validation. Batch execution currently targets definitions and suites; use the editor Regression view for sweeps so the designer can inspect each candidate before publishing it.

## Troubleshooting

| Symptom | Meaning / action |
| --- | --- |
| `PHYSICS_LAB_VEHICLE` | Assign a valid canonical setup with tuning, four wheels, supported dimensions, and valid gravity. |
| `PHYSICS_LAB_TRACK` | Assign a valid track asset; check length/width/geometry values. |
| `LINE_PREFAB_*` | The optional prefab is not a physics-only fixture. Remove it to use the synthetic four-wheel rig or supply a compliant prefab. |
| `TARGET_NOT_REACHED` | The result is censored. Increase the time budget only when that is part of the intended test; do not treat it as a measured target time. |
| `STALE_INPUT` | The authored definition, setup, track, override, or shared simulation revision changed during the run. Start a fresh run. |
| `NON_FINITE_STATE` / `SAFETY_LIMIT` | Inspect tuning, wheel contact, force vectors, and the report’s last valid sample before widening limits. |
| no live response | Focus the SceneView/editor window and confirm the Input System keyboard is available. Use recorded input for a deterministic check. |
| no capability bake | Confirm the report has at least eight valid samples and two positive acceleration speed bins. |

The preview scene intentionally uses the project’s current gravity and solver settings as read-only evidence. It does not alter global physics settings. Cross-platform results require tolerance-based build measurements; fixed seeds do not imply bit-identical PhysX trajectories.
