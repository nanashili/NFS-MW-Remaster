After completing the current task, **rebuild and integrate the vehicle system in our existing Unity project into a production-ready, highly customisable, modular vehicle framework**.

This must support the complete vehicle experience: driving physics, powertrain, handling, headlights, brake lights, windows, mirrors, first-person cockpit animation, engine audio, performance upgrades, body kits, visual customisation, and persistent vehicle configurations.

**Do not build a disconnected demonstration, a single oversized car-controller script, or a collection of settings that do not affect actual behaviour.** Build one coherent system that our existing vehicle tools, gameplay systems, and vehicles can use.

The goal is sophisticated behaviour with straightforward authoring: I should be able to configure a new vehicle, connect its model parts, choose its handling characteristics, add compatible upgrades, and drive it without writing vehicle-specific code.

### 1. Inspect the project before changing the implementation

Inspect the repository, architecture documentation, current vehicle implementation, prefabs, scenes, assembly definitions, Unity version, render pipeline, and installed packages.

Identify the existing owners of vehicle profiles, physics, input, audio, customisation, garage functionality, save data, weather integration, and editor tooling.

**Extend or replace the implementation behind the existing integration points rather than creating competing systems.** The Vehicle Profile Studio, Vehicle Physics Lab, audio tools, and integrated internal editor must continue to use the same production vehicle runtime.

Preserve existing asset references, identifiers, `.meta` files, and saved configurations wherever possible. Where a breaking change is necessary, provide a migration with validation rather than silently invalidating existing vehicles.

Do not introduce a Unity upgrade, render-pipeline migration, DOTS rewrite, paid dependency, or unrelated architecture overhaul as part of this work.

Use official documentation matching the installed versions. Use Context7 and available research tools when available, but verify their recommendations against the actual project.

### 2. Research the reference games without inventing their implementation

For this task, use the following reference priority:

**Primary reference: Need for Speed: Most Wanted (2005).** Inspect the user-provided local installation, existing decoding tools, and available vehicle data to investigate how its vehicles are configured and behave.

**Fallback reference: Need for Speed (2015).** Where the Most Wanted implementation cannot be meaningfully recovered or verified, use observable NFS 2015 behaviour and reliable technical evidence to establish handling and customisation targets.

Treat these as reference behaviours and authoring profiles within one Unity implementation—not reasons to maintain separate physics engines.

Distinguish clearly between:

| Evidence level          | Meaning                                                                                    |
| ----------------------- | ------------------------------------------------------------------------------------------ |
| Verified decoded data | A value or structure recovered from an identified source, with its interpretation checked. |
| Measured behaviour      | A repeatable observation from a controlled driving test.                                   |
| Inferred behaviour      | A plausible explanation supported by observations but not confirmed internally.            |
| Original approximation  | Our implementation choice intended to reproduce a particular driving experience.           |

Do not claim that locating configuration files recovers the complete physics model. Do not label guessed coefficients, generic arcade assists, or arbitrary curves as “the original NFS physics.”

For recovered parameters, record the source, game version where identifiable, units, confidence, conversion rules, and the Unity parameter they influence. Validate values through driving tests instead of assuming similarly named parameters have identical meanings.

Keep source installations read-only. Keep proprietary binaries and decoded game assets out of source control. Do not bypass DRM or require unauthorised downloads.

If reference files or compatible analysis tools are unavailable, document the specific limitation and continue with an explicitly labelled, independently implemented approximation. Do not let unavailable reference material block the rest of the vehicle system.

### 3. Establish clear architecture and state ownership

Separate **vehicle definitions**, **installed configuration**, **runtime simulation state**, and **presentation**.

| Layer                   | Responsibility                                                                                            |
| ----------------------- | --------------------------------------------------------------------------------------------------------- |
| Vehicle definition      | Manufacturer, model, year, variant, factory specifications, supported capabilities, and asset references. |
| Installed configuration | Selected engine parts, transmission, tyres, body kit, wheels, paint, and tuning settings.                 |
| Resolved configuration  | Validated effective parameters calculated from the definition and installed configuration.                |
| Runtime state           | Current speed, engine RPM, gear, wheel contact, slip, steering, braking, damage, and temporary effects.   |
| Presentation            | Mesh animation, lights, glass, mirrors, cockpit instruments, sound, and visual effects.                   |
| Integration             | Input, AI, cutscenes, weather, road surfaces, garage operations, and persistence.                         |

Use the existing ScriptableObject authoring conventions for reusable definitions, but treat those definitions as read-only during gameplay. Store mutable state per vehicle instance so that modifying one spawned car cannot modify every car sharing the same asset. Unity documents ScriptableObjects as a mechanism for shared data, which makes this separation important. [ScriptableObject documentation](https://docs.unity3d.com/6000.5/Documentation/Manual/class-ScriptableObject.html). ([Unity Documentation][1])

Use composition rather than a deep inheritance hierarchy. Modules should have clear responsibilities, explicit dependencies, and predictable initialisation and shutdown.

A suitable conceptual structure is:

```text
Vehicles/
├── Definitions/
├── Runtime/
│   ├── Simulation/
│   ├── Powertrain/
│   ├── Handling/
│   ├── Configuration/
│   └── Presentation/
├── Integrations/
├── Editor/
├── Tests/
└── Documentation/
```

Adapt this to the repository’s established structure. Do not create duplicate abstractions or empty folders merely to match the example.

Keep the simulation independent of editor code, render-pipeline-specific components, and individual model hierarchies. Use interfaces at meaningful integration boundaries—not an interface, factory, and service locator for every small class.

### 4. Rebuild the driving simulation around measurable behaviour

Build a configurable **arcade-oriented vehicle simulation with physically coherent foundations**. It should support responsive street racing, controlled drifting, believable suspension movement, and distinct vehicle personalities.

#### Physics backend

Evaluate the existing backend before replacing it. Compare its capabilities against the required contact behaviour, handling control, stability, performance, and debugging needs.

If using Unity `WheelCollider`, account for its ray-based wheel contact and separate slip-based tyre friction system. Do not assume ordinary `PhysicsMaterial` friction automatically controls its tyre grip. [WheelCollider documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WheelCollider.html). 

If a custom wheel-contact or tyre-force implementation is necessary, document the specific limitation it addresses and verify the replacement against repeatable tests.

**Never apply two competing sets of suspension or tyre forces to the same wheels.** Keep one authoritative simulation path.

#### Powertrain and braking

Support configurable torque curves, idle RPM, redline, rev limiting, engine inertia, engine braking, throttle response, and forced induction where enabled.

Implement automatic and manual transmission modes with gear ratios, final drive, reverse, neutral, shift timing, clutch behaviour, and configurable shift logic. Prevent unstable gear hunting.

Support FWD, RWD, and AWD through drivetrain configuration, including torque distribution and appropriate differential behaviour.

Braking must support brake torque, front/rear bias, handbrake axle selection, and configurable ABS. Drive brake-light presentation from the resulting vehicle state rather than directly from a particular keyboard key.

Nitrous, where enabled, must have a defined effect, capacity, consumption, activation rules, and observable contribution to performance.

#### Steering, tyres, and suspension

Expose low-speed steering range, speed-sensitive steering, response rates, return-to-centre behaviour, and optional countersteering assistance.

Support configurable wheel radius, suspension travel, spring and damper settings, ride height, anti-roll behaviour, chassis mass, centre of mass, and aerodynamic parameters.

Model longitudinal and lateral grip, braking slip, wheelspin, and controlled transitions between gripping and sliding. Prevent incompatible force demands from producing unlimited combined grip.

Connect grip to the existing road-surface and weather systems. Wetness should influence the configured tyre/surface interaction, not simply activate an unrelated global “rain handling” multiplier.

Separate genuine simulation parameters from cosmetic adjustments. For example, label camber or wheel-width changes as cosmetic unless their physical effects are actually implemented.

#### Handling assistance

Implement assists as identifiable, independently configurable contributions.

Stability control, traction control, drift assistance, countersteering assistance, and arcade yaw assistance must expose their activation conditions, strength, limits, and telemetry.

Do not conceal handling problems with unrestricted sideways forces, constant rotation overrides, or unconditional velocity rewriting. Assistance must respect grounded state, speed, collisions, and player intent.

#### Top speed and tuning

Allow authors to configure a desired performance target and an optional speed governor.

Without a governor, top speed should be a result of the configured powertrain, gearing, wheel radius, resistance, and aerodynamic model—not a value assigned directly to velocity.

Show the difference between **configured target**, **estimated result**, and **measured result**. Do not display fabricated acceleration or top-speed figures as simulation results.

#### Simulation timing

Run physics work through the project’s fixed-step simulation flow. Unity’s `FixedUpdate` is tied to its fixed physics-update interval; input sampling and visual smoothing should not become competing physics update paths. [FixedUpdate documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.FixedUpdate.html). 

Buffer discrete input actions where necessary, define update order explicitly, and test behaviour at different rendering frame rates.

### 5. Implement modular vehicle presentation

Each feature must have authoring data, explicit model bindings, validation, runtime behaviour, and a safe disabled state when unsupported.

#### Headlights and exterior lighting

Support headlights, low/high beams, daytime running lights, tail lights, brake lights, reverse lights, indicators, hazards, and optional fog lights.

Expose beam direction, range, intensity, colour, emissive material bindings, shadow settings, and quality restrictions.

Keep lamp emission separate from actual scene illumination. A glowing headlight texture must not be considered a working headlight.

Support automatic lighting using the existing day/night and environmental systems, with a manual override. Handle simultaneous states correctly, including tail lights plus braking and hazards overriding individual indicators.

Allow body-kit-specific light assemblies to replace both visual parts and their bindings.

#### Windows and glass

Provide independently configurable windscreen, rear-window, and side-window bindings.

Support tint, material variants, and optional window-opening animations. Connect rain, wetness, dirt, cracks, or breakage only where the required material and mesh capabilities exist.

Provide optional wiper animation and weather integration. Missing window pivots, separate glass meshes, or wiper rigs must produce useful authoring warnings—not runtime exceptions.

#### Mirrors

Support side mirrors and the interior rear-view mirror for first-person driving.

Evaluate a version-compatible planar-reflection or camera/render-texture implementation. HDRP planar reflection probes expose refresh and culling controls that can be used when selecting an appropriate mirror strategy. [HDRP planar reflection documentation](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/Planar-Reflection-Probe.html). 

Expose resolution, refresh rate, visibility rules, culling layers, and quality settings. Verify orientation, field of view, clipping, and whether a moving vehicle behind the player appears correctly.

Do not use a static environment reflection as the only implementation of a functional rear-view mirror. Prevent recursive rendering and unnecessary mirror rendering for distant traffic.

#### First-person cockpit

Support steering-wheel animation with configurable local axis, neutral pose, steering ratio, rotation range, and smoothing.

Drive it from the resolved steering state, including the relationship between steering-column movement and road-wheel angle—not from an unrelated animation loop.

Provide bindings for speedometer, tachometer, gear display, pedals, gear lever, dashboard indicators, and optional driver-hand targets where a compatible rig exists.

Integrate cockpit camera anchors with the existing camera system. Make vibration, impact movement, and acceleration effects configurable and avoid applying the same movement twice.

#### Wheels and exterior movement

Animate wheel rotation, steering, and suspension from simulation state.

Support independent wheel and brake-caliper bindings so calipers can follow steering and suspension without incorrectly spinning with the wheel.

Support optional exhaust effects, tyre smoke, skid marks, and collision effects through the existing effects pipeline.

### 6. Build real performance upgrades and tuning

Upgrades must modify the parameters used by the simulation. Do not implement them as disconnected UI ratings.

Support data-driven upgrade categories covering engine components, induction, transmission, clutch, differential, tyres, brakes, suspension, weight reduction, aerodynamics, and nitrous where enabled.

Every upgrade must define compatibility, affected parameters, dependencies, exclusions, tuning limits, and applicable visual or audio changes.

Use one documented configuration-resolution pipeline:

```text
Factory definition
    → Compatible installed parts
    → Validated tuning adjustments
    → Resolved vehicle configuration
    → Runtime simulation
```

Apply temporary effects separately from the persistent installed configuration.

Define modifier ordering explicitly. Recalculate from the immutable baseline whenever the installed configuration changes; do not repeatedly multiply already modified values.

Installing and removing an upgrade must return the vehicle to the correct previous configuration without accumulated drift.

Expose a readable breakdown showing the factory value, contributing upgrades, tuning adjustment, final value, and units.

Integrate prices, ownership, unlock conditions, and purchases with existing garage/economy systems. Do not create a second wallet or inventory implementation.

### 7. Implement body kits and visual customisation as compatible parts

Use stable part identifiers and explicit mounting slots rather than model-name checks or arbitrary child-object toggles.

Support complete body kits and individual parts, including bumpers, fenders, side skirts, bonnets, spoilers, mirrors, exhausts, light assemblies, wheels, tyres, and supported interior parts.

Each part must declare its supported vehicle variants, required slots, conflicts, dependencies, model bindings, material options, and any explicitly authored physical effects.

A body-kit installation may affect multiple slots. Validate the complete proposed configuration before applying it. Either apply the complete valid change or leave the previous configuration intact.

Handle wheel fitment, radius, offset, track width, and suspension clearance through explicit configuration and validation. Distinguish visual fitment changes from actual contact-geometry changes.

Do not infer meaningful aerodynamic improvements from a spoiler mesh alone. Physical changes must come from authored data and be visible in the resolved configuration.

Support paint, material finishes, liveries, wheel finishes, and glass tint through the existing material pipeline. Avoid accidentally changing shared materials on unrelated vehicles.

Separate garage preview from committed configuration, with clear apply, cancel, and reset behaviour.

### 8. Make vehicle authoring beginner-friendly

Extend the existing Vehicle Profile Studio and integrated internal editor. **All new editor interfaces must use Unity IMGUI and follow the project’s established Unity Foundations design guidance.**

Provide a guided workflow:

**Choose manufacturer/model/year/variant → assign model → bind parts → configure handling → add compatible upgrades → validate → preview → generate or update prefab.**

Organise settings into clear groups such as Overview, Model Bindings, Driving, Powertrain, Wheels and Suspension, Lights and Glass, Cockpit, Audio, Customisation, and Diagnostics.

Provide an approachable default view with presets and an advanced view for detailed parameters. Use searchable tables, units, tooltips, sensible ranges, contextual warnings, and reset-to-default actions.

Use explicit semantic bindings for wheels, lights, glass, mirrors, steering wheel, cameras, and mounting sockets. Automatic detection may suggest bindings, but must show confidence and allow correction.

Use Unity’s serialized editing mechanisms for appropriate fields, with Undo/Redo and prefab-override handling. `SerializedObject` and `SerializedProperty` support these editor workflows. [SerializedObject documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedObject.html). 

Validation must identify the affected part and explain how to fix the problem. Preview operations must restore temporary state and must not silently modify canonical assets.

Preserve selection, active vehicle context, and useful panel state when switching between the profile editor, physics lab, and audio tools.

### 9. Integrate audio, gameplay control, and persistence

Reuse the existing RPM/load-based engine-audio system and vehicle audio profiles.

Publish coherent telemetry for engine RPM, load, throttle, gear changes, boost, wheel slip, surface type, collisions, and damage. Sound must follow the simulation rather than an independent estimated engine state.

Provide one control contract for player input, AI, test playback, and cutscenes. Define authority and handover rules so multiple systems cannot simultaneously steer or accelerate the same vehicle.

Keep renderer and camera decisions outside the simulation.

Persist vehicle identity, installed upgrades, tuning, selected body parts, paint, and other supported state through the existing save system. Use stable identifiers and schema versions rather than relying on asset paths.

Handle missing or incompatible saved parts with explicit validation and a safe fallback. Do not silently load a partially invalid configuration.

### 10. Keep complexity and performance under control

Advanced customisation must not mean every feature runs at maximum cost on every vehicle.

Provide capability profiles and quality controls for player vehicles, nearby opponents, traffic, parked vehicles, and garage previews. Disable unsupported or unused presentation modules without changing the authoritative handling of actively simulated vehicles.

Budget mirrors, shadow-casting lights, cockpit detail, effects, and audio voices independently.

Cache references and precompute configuration-derived values. Avoid repeated scene searches, unnecessary material instances, reflection-based dispatch, unbounded telemetry history, and allocations in steady-state simulation paths.

Target zero managed allocations in our steady-state simulation code after warm-up, and verify the result rather than asserting it.

Measure CPU time, GPU cost, allocations, and memory on the project’s actual target hardware. Separate simulation cost from rendering and audio cost.

Keep normal authoring and gameplay workflows compatible with macOS/Apple Silicon. Optional reference-analysis tooling must not become a Windows-only runtime dependency.

### 11. Validate through the existing Vehicle Physics Lab

The Physics Lab must run the same production simulation and resolved configurations used in gameplay.

Create repeatable tests with recorded input, fixed starting conditions, configuration snapshots, and explicit tolerances.

| Test area                 | Required verification                                                                       |
| ------------------------- | ------------------------------------------------------------------------------------------- |
| Acceleration and gearing  | RPM progression, shifts, wheelspin, acceleration times, and measured top speed.             |
| Braking                   | Stopping distance, brake bias, ABS intervention, and handbrake behaviour.                   |
| Cornering and drifting    | Steering response, grip transition, drift initiation, recovery, and assist contributions.   |
| Road contact              | Slopes, bumps, kerbs, airborne wheels, landings, and uneven surfaces.                       |
| Weather and surfaces      | Dry/wet transitions and tyre/surface behaviour without discontinuous force spikes.          |
| Upgrades                  | Correct resolution, dependency rejection, removal, and restoration of baseline values.      |
| Customisation             | Valid kit installation, invalid fitment rejection, preview cancellation, and rollback.      |
| Presentation              | Light combinations, cockpit steering, instruments, wheel animation, and mirror correctness. |
| State isolation           | Two vehicles sharing one definition maintain independent runtime and installed states.      |
| Lifecycle and persistence | Spawn/despawn, pooling reset, scene changes, save/load, and migration.                      |
| Performance               | Single-vehicle and representative multi-vehicle scenarios on target hardware.               |

Unit-test configuration resolution and compatibility rules separately from physics tests.

Do not require bit-identical cross-platform physics playback as an acceptance condition. Use repeatable scenarios and defined numerical tolerances, and report variation honestly.

### 12. Implement in dependency-ordered milestones

| Milestone                            | Required outcome                                                                                                  |
| ------------------------------------ | ----------------------------------------------------------------------------------------------------------------- |
| 1. Audit and reference investigation | Existing ownership map, baseline measurements, evidence report, backend decision, and migration plan.             |
| 2. Configuration and core simulation | Validated definitions, resolved configurations, input contract, and one working vehicle integrated into gameplay. |
| 3. Handling and powertrain           | Drivetrain variants, suspension, tyres, assists, surfaces, telemetry, and regression scenarios.                   |
| 4. Vehicle presentation              | Lighting, glass, mirrors, cockpit, wheel animation, audio, and effects integration.                               |
| 5. Upgrades and customisation        | Compatible parts, tuning, body kits, garage transactions, and persistence.                                        |
| 6. Authoring and release validation  | Integrated IMGUI workflows, migrations, example vehicles, performance measurements, tests, and documentation.     |

Keep each milestone compilable and testable. Do not leave the project broken while attempting the entire rebuild in one pass.

Deliver complete implementations rather than placeholder modules, empty interfaces, fake telemetry, or unimplemented buttons. When an external asset or environment capability is unavailable, distinguish that limitation from unfinished code.

### Definition of done

I must be able to configure at least two vehicles with different drivetrain and handling characteristics, using the same runtime modules and no vehicle-specific controller scripts.

For each vehicle, I must be able to bind its model parts, configure headlights and brake lights, set up working first-person steering and mirrors, adjust its performance, install compatible upgrades and body kits, preview the changes, drive the result, and save and restore the configuration.

The existing tools must operate on that same configuration and simulation. Changes made through the editor must produce measurable, explainable changes in the vehicle—not merely different inspector values.

Finish with the implemented architecture, changed integration points, migration instructions, authoring guide, reference-confidence report, tests actually executed, measured performance, and remaining limitations.

**Start by inspecting the repository, then implement the milestones. Do not stop after producing a plan.**

[1]: https://docs.unity3d.com/6000.5/Documentation/Manual/class-ScriptableObject.html?utm_source=chatgpt.com "ScriptableObject"
