# Recovered Most Wanted physics port

The driving controller uses the C# port of [dbalatoni13/nfsmw](https://github.com/dbalatoni13/nfsmw/tree/3bb4ab24df54840a290310fe6d249bc13abe8235), pinned to `3bb4ab24df54840a290310fe6d249bc13abe8235`. There is one simulation path. The previous tire solver, handling model, assist component, model selector and velocity-clamping governor have been removed. Source `SPEED_LIMITER` now reduces throttle through the engine's ECU calculation.

## Source ownership

| Unity code | Recovered source |
| --- | --- |
| `MostWantedTire`, `MostWantedDifferential` | `SuspensionRacer.cpp`: tire forces, slip/lock transitions, wheel inertia and differential splits |
| `MostWantedChassis` | `SuspensionRacer.cpp`: steering histories/tables, Ackermann, yaw friction, drift, burnout and wall steering; `Chassis.cpp`: grip/traction scaling, aerodynamics and jump stabilization |
| `VehicleWheel` | `SuspensionRacer.cpp::DoWheelForces`: suspension progression, compression/rebound damping, valving/digression, sway, ground load and combined wheel forces |
| `VehiclePowertrain`, `VehicleNitrous`, `MostWantedVehicleMath` | `EngineRacer.cpp` and physics info helpers: torque tables, engine braking, flywheel inertia, clutch/road reaction, transmission, induction and NOS |
| `MostWantedDriverControls`, `PlayerVehicleInput` | `PInput.cpp`: pedal endpoint snapping, reverse selection, pedal swap, coasting brake and handbrake precedence; Unity device/action adapter |
| `VehicleController` | Frame sequencing, force/torque aggregation, center of mass, bottom-out correction, landing and sleep; Unity input, module, reset and telemetry integration |

The upstream CC0 license is retained in `Assets/NfsMw/Modules/Driving/Tools/DrivingMechanics/Source/nfsmw-LICENSE`.

## Fidelity boundary

This is a recovered-source port, not verified retail-binary equivalence. The upstream build labels the physics units `NonMatching`. The PC lateral-force branches contain comments instead of an implementation because their multiplier was DRM-protected. This port uses the available console equation, including the upstream provisional `GripFactor = 2.5` and load-sensitivity tables. Those values must not be described as a recovered PC retail match.

Unity supplies raycast contacts, collision detection, rigidbody mass/inertia and force integration. These replace the original collision-world and rigidbody interfaces; contact ordering, collision impulses and numerical trajectories therefore remain an engine boundary. Surface/weather profiles supply the source tire's surface coefficients. Input arbitration, device calibration, ignition, visual fitment, camera and module hooks remain project integration code. Player pedal processing now follows `InputPlayer` as detailed below.

## Driver controls and engine braking (source-port.2)

Reference: PS2 Black Edition (SLUS-21351), `dbalatoni13/nfsmw` commit `3bb4ab24df54840a290310fe6d249bc13abe8235`, `PInput.cpp::FetchInput/DoShifting/DoAutoReverse/DoAutoBraking` and `EngineRacer.cpp::GetBrakingTorque/OnTaskSimulate`. Local gears equal source GearID minus one (reverse -1, neutral 0, first 1).

The controller consumes shift events before reverse selection, pedal remapping, automatic braking, and handbrake precedence. Reverse selection uses signed vehicle speed: enter below 2.5 m/s with brake > 0 and gas == 0; leave above -5 m/s when brake == 0 or gas > 0. These rules also apply in manual transmission and do not wait for driven-wheel spin to stop. In reverse both pedals swap. With neither pedal pressed, forward/neutral braking is `1 - .75 * Ramp(speed, 0, 8)` below 8 m/s (zero at/above 8); reverse braking is `1 - .75 * Ramp(-speed, 0, 10)`, including its .25 floor. Handbrake always clears service brake after these operations.

Manual shift actions can reach neutral and are not blocked by the shift timer. Automatic shift actions use sport-shift rejection rules and a 1.25-second hold that decays only while shift potential exists. Shift delays use SHIFT_SPEED times the selected ratio, quartered for downshifts. Shift potential now uses transmission RPM and clutch state; automatic upshifts require all four wheels grounded with slip < 4, and automatic downshifts can skip gears. Existing shift-point computation/data remain a separate fidelity limit: `UpshiftRpm` documents the upstream induction ambiguity, and the current downshift RPM is an authored tuning field.

Engine braking interpolates ENGINE_BRAKING across IDLE..MAX_RPM after clamping RPM to IDLE..RED_LINE. Multi-sample fractions clamp to [0,1]; the source's single-sample path does not. Braking uses engine torque after induction/NOS scaling, flips sign only when a non-neutral engaged clutch would otherwise accelerate the axle against its selected direction, and blends with drive torque using the processed throttle.

Unity input queues retain the latest sampled shift direction and consume it once, including through free-roam and authority adapters. Simultaneous keyboard up/down is sampled up then down (down wins); this is a deterministic device-adapter choice, not a reconstruction of the original action timestamps. PAD_DEAD_ZONE is only declared in recovered source, so the player's 0.08 pedal threshold is explicitly authored calibration; interior pedal values are not rescaled. Keyboard/gamepad binding, steering calibration, direct N/R keys, camera/reset eligibility, input focus, and ignition remain Unity integration. Original force-stop masks, action-queue blocking semantics, staging/drag/human-AI ownership and perfect-launch engine torque overrides do not have complete equivalent gameplay providers here. Do not claim 100% retail driver-system equivalence.

Traffic and racing AI supply final controls via `IVehicleInputControlPolicy` and bypass player pedal assists. Inactive free-roam controls preserve their full brake/handbrake hold. Physics Lab enables player processing outside fixture warmup so its recorded/live inputs exercise the production player path. Police recovery still supplies player-style reverse pedal intent.

`MostWantedDriverControlsTests` covers source boundary vectors, manual/sport shifting, braking interpolation/sign and production clutch integration. `VehicleFrameworkPhysicsRegressionTests` covers the full controller pipeline, direct-control ownership and queued shifts through authority. Validation results for this revision are recorded separately from the historical source-port.1 results below.

The checked-in `Tests/Editor/driver-controls-reference.csv` contains 1,536 outputs from the original C++ `DoAutoReverse` and `DoAutoBraking` bodies, with the final `FetchInput` handbrake override. The standalone `Tools/DrivingMechanics/driver-controls-reference.cpp` retains these bodies verbatim with minimal interface substitutes. Compile it using `clang++ -std=c++17 -O0` and redirect the executable's stdout to regenerate the CSV. The Unity test compares the managed routines against every row; source interface queries are assumed to succeed and vehicle speed is supplied directly, so this does not validate the original rigidbody or input device runtime.

Running the same oracle executable with an `engine` argument emits `Tests/Editor/engine-braking-reference.csv`: 84 outputs of the verbatim recovered `GetBrakingTorque` and `UTIL_InterprolateIndex` functions. The managed comparison covers RPM endpoints/clamping, interpolation, out-of-range multi-sample fractions, the unclamped single-sample path, and different incoming engine torques. Axle-sign and clutch guards are tested separately in the managed helper and running powertrain.

Validation on 2026-09-11: the final focused run passed **90 tests**, including all **1,620 C++ reference vectors**, production controller/queue ownership checks, engine clutch checks, captured-vehicle stopping, and reverse-to-forward simulation. Results: `Evidence/validation-20260911T073408Z.hc89dN/tests.xml`. The broader run passed **156 of 158 tests** (`Evidence/validation-20260911T073205Z.GJym7r/tests.xml`); the two failures occur while opening `WeatherDemo.unity` because its Demo Circuit prefab GUID `6c6b2517b661e4316844564e0f1d8980` has no asset. The reference is also present in the checked-in HEAD scene. This missing scenery asset was not changed by the driver/braking work. The final focused rerun followed removal of unused pedal-state fields; no functional changes followed the broader run. Validation used a complete isolated project copy and Unity 6000.6.0f1 EditMode; no rendered driving session or original-game trajectory comparison was performed. See `Evidence/driver-controls-verification-20260911.json` for source hashes and scope.

The chassis frame represents drag style, staging, perfect launch/shift, blown tires and speedbreaker inputs. The current controller uses ordinary driving defaults for gameplay states that do not yet have a project provider. NOS is connected. Original race orchestration, AI catch-up cheats and tire-heat gameplay are not recreated by the driving solver.

## Data and migration

`MostWantedDrivingImporter` reads resolved engine, transmission, chassis, tire, brake, induction and NOS fields from the preserved local capture. Length/rate conversions follow their individual source units: wheelbase/axle/track are meters; ride height/travel/roll center/valving are inches; section width is millimeters; spring/shock rates and brake torque are converted to SI. `AERO_CG` and weight bias remain source percentages.

`Evidence/source-port-migration.json` records the selected source records and migration. BMW profiles use explicit first-linked BMW records with authored wheel visual fitment. Anonymous/NPC profiles use a labeled BMW or GTI donor and retain their authored engine statistics. First-linked records are not proof of the active stock configuration in the retail game. Shared tuning assets and embedded scene tuning were migrated; scene geometry and object IDs were preserved.

Common tuning fields remain available to the configuration, parts and presentation APIs. Active source suspension, grip, brake and differential coefficients live in `mostWanted`; performance upgrades update those active coefficients. Historical evidence and backups describe older implementations and are not current validation claims.

## Historical verification (source-port.1)

Run EditMode tests in a task-owned Unity copy using the existing `Tools/DrivingMechanics/validate.sh` workflow. Source equation tests cover tire forces and lock transitions, differential splits, steering/grip tables, engine inertia, neutral revving and NOS hysteresis. Physics Lab integration cases use the production controller in isolated physics scenes. These tests establish the managed equations and integration behavior they exercise; they do not compare trajectories against a running original game.

The final focused run passed 248 tests with zero failures. One installed-game integration test was skipped because `BLACKBOX_GAME` was not supplied. Results and source hashes are in `Evidence/source-port-tests.xml` and `Evidence/source-port-verification.json`. Validation used current Driving code in an isolated Unity copy with an earlier snapshot of other modules and omitted heavy visual assets; it is not a rendered gameplay or whole-project validation.

The separate scene-coverage run encountered a missing Demo Circuit asset in `WeatherDemo.unity` (GUID `6c6b2517b661e4316844564e0f1d8980`). The reference also exists in the baseline scene. Its earlier result file is retained separately; four unrelated failures in that earlier run were corrected and pass in the final focused suite.
