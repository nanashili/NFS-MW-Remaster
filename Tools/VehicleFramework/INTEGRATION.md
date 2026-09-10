# Vehicle Framework integration guide

This guide describes the current integration points for the shared vehicle runtime. It is source grounded: a definition is authoring data, a configuration is per-instance mutable state, a resolved configuration is a disposable runtime copy, and presentation reads runtime telemetry.

## Runtime ownership and order

The authoritative configuration path is:

```text
VehicleDefinition.factoryTuning
  -> VehiclePerformanceBuild (compatible upgrades)
  -> VehicleCustomizationBuild (compatible body parts and physical effects)
  -> VehicleTuningAdjustment[] (bounded absolute tuning)
  -> ResolvedVehicleConfiguration.Tuning
  -> VehicleController / VehicleModuleHost simulation
  -> telemetry and presentation
```

`VehicleDefinition` is a `ScriptableObject` identified by stable `vehicleId` and `variantId`. It owns manufacturer, model, year, factory tuning, prefab, catalog references, capabilities, supported semantic slots, and optional tuning limits. `VehicleConfiguration` is the instance owner. On initialization it validates the definition, selects its catalogs, and asks the module context to replace tuning. `VehicleConfigurationResolver.Resolve` clones the factory tuning, sorts upgrades and customization by category and stable ID, applies authored physical effects, then applies each absolute adjustment after intersecting definition, part, and runtime safety limits. It records a readable factory-to-final breakdown and disposes the runtime copy on replacement.

Do not edit a `VehicleTuning` asset during play. A spawned vehicle must own its resolved copy. `RacingVehicleSetup.CreateEffectiveTuning` follows the same resolver for Physics Lab and racing-line fixtures. `VehiclePerformanceSystem` and `VehicleCustomizationSystem` remain the owners of installed parts; the configuration resolver is the single composition boundary.

`VehicleController` owns the simulation and accepts one `IVehicleInputSource` through `SetInputSource`. Generated player vehicles assign `VehicleInputAuthority`: an owner acquires control with `TryAcquire(owner, source)` and releases it with `Release(owner)`; competing owners are rejected. Player input buffers discrete shifts until the fixed step consumes them. Physics Lab uses its recorded-input adapter and manual stepping in an isolated physics scene. Simulation runs through the controller/module fixed-step path; presentation modules consume telemetry after simulation. Do not make camera, renderer, audio, or editor code a second physics owner.

The controller resolves pending configuration before stepping powertrain, contacts and assists. Wheel forces use the current physics scene, including after a scene move. One combined longitudinal/lateral force budget limits each contact. Service-brake ABS is evaluated before mechanical handbrake torque is added. Assists clear per-tick intervention telemetry, accumulate the largest wheel intervention, and suppress stabilization immediately after substantial collisions. The optional governor reduces drive/brakes overspeed; disabling it removes the target-speed enforcement without assigning velocity.

## Parts, compatibility, and fitment

Performance parts implement the performance interfaces and are installed through `VehiclePerformanceBuild` or `VehiclePerformanceSystem`. Installation validates stable IDs, category replacement, vehicle and variant compatibility, dependencies, exclusions, and authored tuning limits. Removal rebuilds from the immutable factory baseline; it never reverses a previous multiplication.

Visual parts implement the customization interfaces and are installed through `VehicleCustomizationSystem`. Categories replace one another intentionally; stock is represented by removal. `TryInstall`, `TryRemove`, `BeginPreview`, `TryApplyPreview`, and `CancelPreview` validate the complete candidate before committing it and request a runtime reconfiguration after a commit.

Physical fitment is explicit metadata: wheel radius, wheel offset, track width, clearance, and authored physical effects may alter resolved tuning only when the part declares them. Bounds and required semantic slots are validated. A mesh, spoiler, paint, wheel finish, or material change is cosmetic unless it implements the physical-effect interface. Visual fitment must not be presented as changed handling without a resolved parameter contribution.

A body assembly uses `VehicleAssemblyDefinition`: an explicit body source, presentation source, mirror source, four wheel bindings, semantic sockets, collision dimensions, and an optional player-input flag. Source scripts are not copied or executed by assembly. Socket IDs are stable identities; renaming one is a binding migration.

## Presentation authoring

Add `VehiclePresentationBindings` to the presentation root and bind semantic lamps, windows, wiper pivots, steering wheel, cockpit camera, gauges, gear display, pedals, driver targets, wheel/caliper transforms, and mirror surfaces. `VehiclePresentationRole` distinguishes Player, Opponent, Traffic, Parked, and Garage presentation policies. `VehicleCapabilities` on the definition declares what the vehicle supports; missing optional bindings must disable that capability safely and produce an authoring diagnostic.

Brake lights and dashboard indicators are driven from vehicle telemetry and presentation state. They must not read a particular keyboard key. Headlight, high-beam, tail, reverse, hazard, glass, cockpit, and mirror bindings are explicit and may be absent where the definition capability is disabled. The bindings component includes safe defaults and `ValidateBindings` reports missing semantic IDs, wheels, cockpit, glass, and optional wiper rigs.

## Studio, generation, and example content

Open the native IMGUI authoring surface at **Racing Tools > Vehicles > Vehicle Profiles**. The workflow is identity/catalogue, model bindings, framework tuning and compatible parts, validation, preview, then Build/publication. It uses `SerializedObject` for editable state and keeps preview state separate from committed state. `VehicleProfilePublication` publishes a listing for an already assembled prefab, records ownership and fingerprints, and rejects stale plans or externally modified generated listings; it does not edit a source prefab, create a purchase, or modify a career save.

The framework example menu is **Racing Tools > Vehicles > Build Framework Examples**, implemented by `VehicleFrameworkExamples.cs`. The delivered examples already exist under `Assets/NfsMw/Modules/Driving/Examples/VehicleFramework/`: FWD and RWD definitions, tuning, body/presentation sources, sockets, catalogs, assembled prefabs and playable scenes. The menu preserves existing authored output on subsequent runs. Prefab configuration updates preserve GUIDs and model binding identities; geometry changes belong in Prefab Mode. Scene generation records component overrides after camera/audio/effects integration so those references survive reopening.

The Physics Lab is **NFS MW Remaster > Driving > Vehicle Physics Lab**. It uses `RacingVehicleSetup`, the same resolver and `VehicleController`, and a disposable preview `PhysicsScene`. The fixture receives the fully resolved tuning; its factory definition, parts and adjustments are cleared on the disposable setup to prevent a second resolution from undoing upgrades or applying body effects twice. Input modes are generated, recorded, or live; recorded input is the reproducible option. The window reports measured pose, velocity, acceleration, slip, wheel channels, RPM, gear, drivetrain torque, assists, nitrous, collisions, and input channels exposed by the runtime. It does not claim original-game calibration or expose unavailable presentation and damage channels.

## Persistence and migration

`VehicleConfiguration` implements the career vehicle state participant. It captures definition and variant IDs, schema version 2, tuning adjustments, while the performance and customization systems capture stable installed part IDs. Restore validates definition and variant identity, rebuilds saved performance and customization candidates, resolves the complete configuration, and commits only after validation. Missing modules or incompatible saved parts return an explicit failure.

Legacy prefabs may have no `VehicleDefinition`; they continue using their existing factory tuning, but saved tuning adjustments require migration to an assigned definition. `VehicleDriveLayout.LegacyBindings` retains numeric value zero for old serialized data; FWD, RWD, and AWD use explicit values. `VehicleTuningAdjustment.parameter` uses the existing runtime Physics Lab parameter enum, so its numeric values are save-compatible. Stable vehicle, part, socket, and listing IDs must be preserved when labels or asset paths change.

Career save files are owned by `CareerSaveRepository`. It keeps legacy candidates visible for validation on load and refuses implicit archival when legacy migration files remain. Vehicle integration should use that repository and the existing `CareerVehicleData` contract rather than adding a second save format.

## Gameplay and presentation boundaries

Store publication continues registering an `AssetVehicleCarStoreProduct` referencing the assembled prefab. Existing spawn, garage, wallet, unlock and ownership systems retain their responsibilities. The example scenes use `AttachVehicleCareer`, `VehicleStoreSystem`, `VehicleShopTestHarness`, `VehicleCameraRig`, `VehicleAudio` and the sensory world; no alternative purchase/save runtime was added. Shop purchases enter the existing validated transaction path. Studio part installation is an authoring action, not a purchase.

Rendering remains in the rendering assembly. `VehicleMirrorRenderer` uses HDRP StandardRequest capture with one bounded texture per eligible mirror. It disables all mirror surfaces during capture, restores them afterward, applies horizontal UV reflection, and releases cameras/textures on disable or ineligible roles. Quality controls bound resolution, distance and refresh; traffic and parked vehicles do not allocate live mirrors. Lamp shadows, cockpit detail, effects and audio retain independent quality controls.

Physics Lab is a physics fixture: audio, cameras and detailed presentation are checked in PlayMode separately. Mechanical damage evolution, original-game input traces and missing model-specific shader capabilities are not fabricated. See [AUTHORING.md](AUTHORING.md), [MIGRATION.md](MIGRATION.md), [REFERENCE.md](REFERENCE.md) and [VALIDATION.md](VALIDATION.md) for operation, migration, evidence and measured limits.
