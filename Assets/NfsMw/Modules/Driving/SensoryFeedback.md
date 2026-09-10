# Scene audio: setup and retained shared services

The legacy vehicle feedback components, F8 preview panel, telemetry recorder, camera/haptic effects, vehicle particle/skid presenter, and automatic audition objects were removed on 2026-09-08. `VehicleAudio` owns vehicle playback and samples `VehicleController` directly. Its Most Wanted bank path remains the original-bank playback path. The shared audio pool, traffic adapters, police radio/sirens, music and destruction services remain available. Historical verification below describes the earlier implementation, not current acceptance results.

Implemented against Unity **6000.6.0f1**, URP **17.6.0**, Input System **1.20.0** on macOS. No new packages, middleware, physics-rate changes or game recordings were introduced. Read `SENSORY_FEEDBACK_ARCHITECTURE.md` for the audited design and `SENSORY_RACING_RESEARCH.md` for the primary-source research.

This is an integrated code/content-authoring system, **not a finished recorded mix or a certified shipping-performance result**. The shared reference profiles now include an original diagnostic sound pack; missing clips in other profiles intentionally stay silent. Generated sounds, particles, primitive vehicles and debris are diagnostic content, not NFS recordings. See `Audio/Diagnostic/README.md` for sound provenance, replacement and import policy.

## Run it in the editor

1. Open `Assets/NfsMw/Scenes/Tests/SensoryTest.unity` and press Play.
2. Drive with WASD/arrows; Space is handbrake, Left Shift is nitrous, C switches camera, R resets.
3. Select the vehicle's **VehicleAudio** component for bank, channel and playback diagnostics. Use its rehearsal tools for isolated audio authoring. Gameplay scenes no longer install a second audition vehicle or the F8 sound-preview panel.

The scene contains the player, 24 physical police rigs, 16 civilian `RoadVehicleMotor` cars and 12 breakable signs. It is a stress/test layout, not a finished race or city. Automatic career persistence is disabled on its player. The automated smoke separately drives the police rigs with fixed commands; it does not prove 24-unit tactical balancing.

Editor menus:

- **NFS MW Remaster > Sensory > Build Sensory Test Scene** regenerates this diagnostic scene and missing reference assets. It asks to save unsaved scenes before interactive use.
- **NFS MW Remaster > Sensory > Install in Open Scene** wires scene audio and shared environment services in a scene with a `VehicleCameraRig` and explicit player target. Vehicles use `VehicleAudio` directly. Save the scene afterward. An existing sensory world is preserved; the installer does not retune your authored configuration or discover later spawns automatically.
- **NFS MW Remaster > Sensory > Audit Content** reports unassigned slots, structural profile problems and risky default audio imports without rewriting them. Review platform-specific overrides separately.
- **NFS MW Remaster > Sensory > Install Diagnostic Audio** creates missing original test WAVs and fills empty slots on the shared reference profiles. It preserves assigned sounds and existing WAV/import settings. The supplied pack is already installed; you do not need to run this before pressing Play.

`DrivingDemo`, `FreeRoam` and `PursuitTest` are migrated in place. Original scenes are backed up during this implementation; existing surface grip/rolling resistance is preserved. Future demo/free-roam/pursuit builders include sensory installation. No new shop, career or reward ownership is introduced.

## Ownership and extension points

| Module / adapter | Responsibility |
|---|---|
| `VehicleController` / `VehicleFeedbackSampler` | Actual physics/powertrain authority and read-only snapshots sampled inside VehicleAudio |
| `IVehicleFeedbackSource` | `Frame`, `Sampled`, `Impact`; implemented by physical vehicles, motion-only traffic and isolated replay |
| `FeedbackNormalizer` | Shared slip/spin/lock/drift/load/speed/nitrous/landing interpretation and filters |
| `VehicleAudio` | Own and release vehicle playback using the selected bank or authored sound profile |
| `SensoryAudioWorld` / `SensoryEffectsWorld` / `SkidMarkWorld` | Own pooled output, admission and reclamation |
| `PursuitSensoryBridge` / `AdaptiveMusic` | Read police/session facts; radio eligibility, contextual mix, synchronized stems and committed-outcome stingers |
| `VehicleCameraRig` | Chase/cockpit camera position, rotation and speed-based FOV |
| `DestructibleProp` / `DestructionWorld` | Exactly-once physical break per reset generation, semantic fact and bounded cosmetic debris |
| `DestructionGameplayAdapter` | Forward that fact to existing observed-offence and mission systems; never directly award money/bounty |

Value snapshots do not grant mutation access to physics. Motion is in metres/seconds; wheel angular speed is radians/second; load is newtons; torque is Nm; RPM stays RPM. `Capabilities` distinguishes unavailable data from measured zero. Boost, clutch travel, water depth and body damage are **not available from current physics** and are not invented. Simplified civilian cars have motion/road noise, not fake speed-to-RPM engines.

To add a physical vehicle, configure its `VehicleController`/wheels and add one `VehicleAudio`. Assign its `VehicleSensoryProfile` or explicit Most Wanted bank override. VehicleAudio finds its parent controller, or accepts an explicit controller through `Configure`. For runtime spawns, configure while inactive before activation. Police additionally need `PoliceVehicleFeedback.ConfigureSensory`. Register new vehicle colliders through `DestructionWorld.SetIgnoredVehicles` with the complete current collider list so cosmetic debris cannot push them. The installer wires all existing active/inactive authored rigs, not arbitrary future prefabs.

For another simulator, implement `IVehicleFeedbackSource` on a MonoBehaviour. Publish value frames on the main thread at its authoritative cadence. Use monotonic time/sequence values and increment `Epoch` for respawn, pool reuse or discontinuities. Reset filters and expose `default` while disabled. Publish collision facts once, not per rendered frame. Do not add a second vehicle controller or another AudioSource pool.

## Author one recorded reference car first

Content lives in `Assets/NfsMw/Modules/Driving/Data/Sensory`:

- `ReferenceVehicle.asset`: three provisional layer layouts, each with five ordered RPM regions and distinct on/off-load slots. Assign licensed/original recordings, correct reference RPM, gains, perspective curves and bounded pitch ranges. Up to five layers/eight regions are supported. Optional transmission/induction use the same layered model; induction remains silent without an actual Boost capability.
- `AsphaltDry.asset`, `AsphaltWet.asset`, `Gravel.asset` and the other material-named surface assets: stable IDs, optional physics grip/rolling resistance, separate road/slip/impact/scrape/destruction sounds, contact/impact particles and skid eligibility. `VehicleSurface.SetProfile(profile, false)` changes presentation without altering existing physics tuning. Assign profiles to road/material colliders; runtime does not infer materials from names.
- `ImpactPairs.asset`: symmetric light/heavy/scrape/particle mappings for material pairs. Duplicate reversed pairs are rejected. A provided pair is authoritative; fill its slots or remove it to use surface fallback. Do not map wood/glass to universal metal sparks.
- `PoliceFeedback.asset`: recorded siren variants and semantic radio cues with original subtitles, priority, expiry, heat eligibility, repetition cooldown and interruption rules.
- `AdaptiveMusic.asset`: up to four equal-duration stems, declared BPM/meter/bar length and intensity curves, plus optional escaped/arrested/fine-paid stingers. Stems share a DSP origin and rejoin stolen voices at the appropriate phase. Stingers start at the next bar only after durable police outcome acknowledgement.
- `SensoryMix.asset` and `Sensory.mixer`: nine routes and six contextual snapshots. Settings gains live on parent groups; snapshots affect child output groups so they cannot overwrite player volume preferences.
- `Diagnostic*.prefab`, `Skids.mat`, `DiagnosticSequence.asset`: explicitly temporary art/replay content. Replace or duplicate profiles to author final content.

The installed test pack has 126 mono WAVs covering 148 clip slots, including three sirens, 13 semantic radio tones and three exact-duration music stems. Engine cues include five reference RPMs (900/1800/3200/5000/7200) per layer/load state. These are bounded offline synthesis assets for testing the real telemetry/mixer pipeline; they do not satisfy recorded-car fidelity acceptance. If nothing is heard, check the VehicleAudio bank assignment and channel mix, the Game view Mute Audio toggle, system output device and pause state.

Set `exhaustPorts` in vehicle-local space (at most four) for each real mesh. Nitrous particles use every port; its loop uses the first. Four-wheel cars can request separate per-wheel slip and road voices; the shared budget gives player slip higher priority than embellishment.

Engine loops should normally be mono and preloaded; audition memory versus decode cost before changing load mode. Long music can use Streaming while short loops generally should not. The world only admits clips whose data is Loaded; a custom loading screen must preload non-preloaded content asynchronously before use. It never blocks a collision callback to load audio. Review loop seams, on/off-load phase, pitch range, loudness, headroom and sample alignment on real speakers/headphones. The audit uses Unity's [AudioImporterSampleSettings](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/AudioImporterSampleSettings.html); its warnings are review prompts, not automatic compression policy.

Mixer tree:

```text
Master [MasterVolume]
├─ Vehicle [VehicleVolume] → Player, OtherVehicle
├─ Effects [EffectsVolume] → Tires, Impacts, Environment, UI
├─ Police  [PoliceVolume]  → Radio, Sirens
└─ Music   [MusicVolume]   → Music
```

Snapshots: FreeRoam, Race, Pursuit, Cooldown, Paused, Crash. Recent severe player impacts temporarily use Crash; stems do not restart for it. Radio has band-pass filtering and ducks music. Sirens are spatial, phase-varied and share the same voice pool. Native `AudioReverbZone` and `AmbientAudioEmitter` can be authored for city/tunnel ambience; no final acoustic zones are supplied.

## Limits, lifecycle and streaming

Standard scene budgets are 28 AudioSources, 2048 particles shared across five prewarmed types within a 16-system ceiling, 16 cosmetic debris bodies and 512 skid segments. Two rotating occlusion rays run per 50ms slice, not for every vehicle each frame. Other-vehicle audio is culled at 120m; general spatial admission is 180m; particles at 100m. Debris cleans up by distance, six-second lifetime or sleep. These are bounded-resource choices, not measured frame-time guarantees or automatic quality presets.

Every sound—including sirens, music, radio and ambient loops—must lease a world voice. Lower numeric priority wins; a lease has a generation so stale owners cannot update/return a stolen source. Clip/material changes reacquire leases; moving wheel anchors update their offsets. No unbounded `PlayOneShot`. All filters/routes are reset on reuse. Custom particle entries must have one ParticleSystem; nested/sub-emitter physics cannot bypass the budget. Automatic emission and particle collision are disabled by the world.

All scene pools prewarm once. Distance culling and fixed scene pools reduce work; this is not an Addressables/world-sector streamer. No new asset-loading stack was introduced. Disabled/unloaded worlds return voices and debris and clear particles. Respawn resets telemetry/camera history; game flow remains the sole pause/time/audio owner. Audio-device reset invalidates voice leases; active presenters/stems can reacquire.

The shared mixer still reads presentation preferences from `sensory.preferences.v1`, separate from transactional career saves. The legacy preferences UI and vehicle camera/haptic effects have been removed. VehicleAudio exposes its own per-vehicle channel mix.

Replay must run on a separate audition object with no parent/child gameplay Rigidbody. It copies/validates 2..6000 finite, ordered frames, preserves recorded reset epochs, drives only its own transform and emits presentation events—not offences, missions, destruction, wallet or settlement commands. Captures are in-memory; disk export, timeline editing and camera replay are not implemented.

## Destruction authoring

Give each `DestructibleProp` a stable unique ID, scene world, child intact visual, explicit blocking colliders, material and impact threshold. Never assign the prop root as the intact visual. Tier choices:

- Cosmetic: hide/swap authored visuals and disable listed blockers.
- WholeBody: release an explicitly assigned Rigidbody with a capped physical impulse.
- Chunks: remove the blocker and request bounded cosmetic fragments from the scene pool.
- SetPiece: additionally activate an authored broken representation. Consequential colliders must be authored explicitly; cosmetic debris is not an invisible police kill radius.

`ResetProp` restores this instance and advances its generation. World break state is scene-local; cross-load destruction persistence and final pre-fractured landmark content are not implemented. The shared debris template is cosmetic, not a runtime fracture solver. For varied meshes, author baked broken representations and keep dynamic pieces within a measured scene budget.

`DestructionWorld.Broken` is the single fact. The gameplay adapter forwards only player-instigated facts as observed PropertyDamage through `FreeRoamTraffic` and as `world.destroyed` with stable identity through `MissionHost`. The old generic prop-collision offence path excludes these props, preventing duplicate reporting. Pool exhaustion cannot prevent a gameplay barrier from breaking. Settlement remains owned by existing durable gameplay code.

## Historical verification and remaining acceptance gates (2026-09-05)

Verified in an isolated copy of the project, without touching live career saves:

- Baseline: 214/214 EditMode tests passed before changes.
- Current regression: 251/251 passed, including 22 original sensory cases and 15 diagnostic-audio cases for deterministic/load-sensitive PCM, headroom, siren loop splices, aligned stems, WAV encoding, invalid samples, imported content and the four scenes' shared profile references.
- Game-flow smoke passed boot/menu, profile alias creation, scene loading, pause/time/audio restoration, event countdown/restart/results, reward/save/resume and cancellation paths.
- Saved SensoryTest smoke covers live four-wheel telemetry, 24 moving physical police/16 civilian rigs, budget saturation, stale lease safety, exactly-once destruction, pause, replay stop, respawn and scene reload. Its silent PCM fixture is restricted to the synchronous allocator assertion, then released; the stress phase checks installed engine, music and siren playback through the actual pool. This remains pipeline evidence, not listening/fidelity acceptance.
- Before the sound-pack addition, a macOS development player build succeeded (188,430,702 bytes). A standalone `-batchmode -force-metal` startup run selected Apple M1 Pro/Metal and initialized the scene without logged runtime errors before being stopped. This older build does not include the diagnostic WAVs. It was not an interactive visual review, a final-content listening test or a frame-rate benchmark. Windows player validation remains open.
- Pre-sound-pack content baseline: zero assigned audio clips and 146 unassigned slots. The diagnostic audio addition fills the shared reference slots; see the diagnostic pack verification below. Source scenes match the migrated copies, with no unresolved serialized script GUIDs in the four installed scenes.

Implementation-run evidence (2026-09-05, temporary local paths): `/private/tmp/sensory-accepted-tests.xml`, `/private/tmp/sensory-final-smoke.log`, `/private/tmp/sensory-gameflow-smoke.log`, `/private/tmp/sensory-mac-player-build.log`, `/private/tmp/sensory-mac-player-run.log`. Original scene backups: `/private/tmp/nfs-sensory-before-install.qIKFLU/`; pre-install reference asset backup: `/private/tmp/nfs-sensory-assets.LsrOQr/`. Copy these elsewhere if you need long-term retention; `/private/tmp` is not archival storage.

Headless profiler counters in the initial stress run returned zero and are **not usable CPU or GC measurements**. A pre-existing Unity 6000.6 `UnityEditor.Search.SearchDatabase` indexing exception also appears during some editor startups; the smoke exempts only that editor stack, never runtime errors. Passing tests do not imply an entirely clean Editor console.

Diagnostic audio verification (2026-09-05):

- 15/15 targeted sound tests and 251/251 full EditMode regression passed.
- The Metal-enabled batch Editor stress run played installed engine, music and siren clips, reached 28 pooled voices, and measured non-silent `AudioListener.GetOutputData` output (peak 0.433 in that short run). This confirms DSP output, not listening quality or an all-scenarios clipping guarantee. All four installed scenes reference the populated shared profiles.
- A second install reported `created=0 assignedSlots=0`; vehicle, police and music profile hashes were unchanged. Existing assignments are preserved.
- Content audit: 126 unique assigned clips, zero unassigned slots, zero import warnings and zero invalid profiles. This is test-content completeness, not recorded-car fidelity.
- Evidence: `/private/tmp/sensory-audio-tests.xml`, `/private/tmp/sensory-audio-regression.xml`, `/private/tmp/sensory-audio-smoke.log`, `/private/tmp/sensory-audio-idempotence.log`, `/private/tmp/sensory-audio-audit.log`. Pre-audio shared profiles are backed up at `/private/tmp/nfs-before-diagnostic-audio.p69Csz/`. Temporary files are not archival storage.
- No interactive listening review, Windows output/device test or new standalone build is claimed for this sound-pack addition.

CLI entry points (run one Unity process at a time; never concurrently against the same project):

```sh
# Use your isolated project's absolute path; the source project can stay open.
Unity -batchmode -nographics -projectPath /private/tmp/your-isolated-project \
  -runTests -testPlatform EditMode -testResults /private/tmp/sensory-tests.xml \
  -logFile /private/tmp/sensory-tests.log

Unity -batchmode -nographics -quit -projectPath /private/tmp/your-isolated-project \
  -executeMethod NfsMwRemaster.Driving.Editor.DrivingDemoBuilder.BuildSensoryTestScene \
  -logFile /private/tmp/sensory-build-scene.log

# Sound smoke: use the Metal/audio-enabled Editor mode validated here.
Unity -batchmode -force-metal -projectPath /private/tmp/your-isolated-project \
  -executeMethod NfsMwRemaster.Driving.Editor.DrivingSensorySmoke.Run \
  -logFile /private/tmp/sensory-smoke.log

Unity -batchmode -nographics -quit -projectPath /private/tmp/your-isolated-project \
  -executeMethod NfsMwRemaster.Driving.Editor.DrivingSensorySmoke.BuildMacPlayer \
  -logFile /private/tmp/sensory-player-build.log
```

Here `Unity` means `/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity` on this Mac. Supply the licensing configuration required by your installation; do not hard-code another session's IPC identifier. Do **not** add `-quit` to `-runTests` or the async PlayMode smoke. Batch smoke/migration/player helpers deliberately require an isolated `/private/tmp/` project. The macOS verification build targets `Builds/SensoryVerification.app` inside that copy.

Open gates before calling the requested sensory fidelity production-ready:

1. Supply and tune the first real recorded engine, tyre/surface sounds, sirens, original/localized radio and licensed music stems/stingers. Asset-free layering is not the milestone-4 reference-car acceptance.
2. Author final exhaust/contact art, fracture/set-piece meshes and ambience/reverb zones; test mixed surfaces, rain/water when supported by physics, occlusion, pass-bys, clipping, transparency and skid fading in a rendered player.
3. Profile development players on representative minimum Mac and Windows hardware with final content, F8 hidden, including opponents plus dense traffic/20+ police. Record CPU p50/p95/p99, whole-game physics, audio DSP/streaming/underruns, GPU/overdraw, memory and warmed managed allocations. A build alone cannot prove these.
4. Exercise actual audio output/device replacement, gamepad reconnection/haptics and accessibility on both platforms. Tune volumes and feedback together during sustained pursuits, not in isolated sound previews.
5. Test content streaming and long-session unload/reload with production city assets. Ship measured presets only after these captures; the architecture budget table is a target, not a benchmark result.

Milestones 1–3 and the asset-ready code paths for 5–13 are present. Milestone 4 remains content-gated; milestones 14–15 remain device/performance/final-content acceptance work. Do not label these open gates complete based on headless tests.
