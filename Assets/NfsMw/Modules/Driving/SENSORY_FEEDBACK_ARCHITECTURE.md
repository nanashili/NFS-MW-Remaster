# Sensory feedback architecture

Audit: 2026-09-05. Targets: macOS/Windows. This is an implementation design, not a claim that a shipping mix or performance target has been achieved.

## 1. Existing-project audit

* `ProjectSettings/ProjectVersion.txt`: Unity **6000.6.0f1**, revision f7f8ed4d1e24.
* `Packages/packages-lock.json`: URP/Core/Shader Graph **17.6.0**, Input System **1.20.0**, Burst **2.0.0** (transitive), Test Framework **1.8.0**. No Cinemachine, VFX Graph, Addressables, Memory Profiler or audio middleware package. No package upgrade is needed.
* Quality index 1 is **PC**, using `Assets/NfsMw/Settings/Rendering/PC_RPAsset.asset`; Graphics default pipeline is unassigned, so quality override matters. PC renderer has SSAO, no decal feature. SRP Batcher is enabled. Mobile has a separate URP asset.
* Audio: 32 real/512 virtual voices, 1024 serialized DSP buffer, platform-selected sample rate, no spatializer. No authored AudioMixer, audio recordings or VFX Graph assets found. `PoliceVehicleFeedback` alone creates a spatial siren source; missing clip means silence.
* Physics: 0.02s fixed timestep, maximum catch-up 0.33333334s, gravity -9.81, solver 6/1, collision-callback reuse enabled. Player/police use `VehicleController`, production raycast `VehicleWheel`, shared `VehiclePowertrain`; important cars already use ContinuousDynamic. Civilian `RoadVehicleMotor` is simplified and does **not** expose tyre/engine simulation.
* Physics outputs RPM, signed engine torque, delivered drivetrain torque, actual wheel angular velocity/slip/load/contact/compression. No physical turbo pressure, clutch travel, water depth or body-damage model exists. Those signals must be explicitly unavailable, not fabricated.
* Camera: custom `VehicleCameraRig`, chase/hood, existing speed FOV and look-ahead. Keep one pose/FOV owner. Game flow already owns `Time.timeScale` and `AudioListener.pause`; sensory code must not compete with it.
* Police: `VehiclePursuitDirector` owns engagement/fines/LOS/cooldown; `OutcomeAcknowledged` is emitted **after durable settlement**. Hazard deployment and unit state are actual world facts. Radio must not announce an uncommitted escape/arrest or expose the hidden target's heading/location.
* Existing pools are fixed authored civilian/police/hazard populations. No reusable transient effects/debris pool, no destruction system, no engine/VFX content library. Mission facts and police offence reporting already exist; feedback must not award cash/bounty itself.

## 2. Racing-game evidence

See `SENSORY_RACING_RESEARCH.md` for the primary-source ledger across NFS, Forza, GT, GRID, DiRT, F1, Burnout, BeamNG and Wreckfest. Implementation choices below are project decisions, not claims to reproduce proprietary internals. A missing primary source is an evidence gap, not permission to invent a technique. No game recordings or copyrighted music are imported.

The completed ledger supports three main choices: physically controlled layers and distinct load recordings (Turn 10), force/velocity/material-driven persistent scrapes (BeamNG Team), and deliberate player-engine versus high-speed pass-by focus (Criterion developers). Capture workflow evidence is stronger than public runtime evidence for GT/GRID/DiRT; F1 sources distinguish older-team interviews from current player-facing settings. Wreckfest 2 findings are explicitly sequel-only. These are transferable design findings, not measured Unity performance or proprietary engine recipes.

## 3. Vehicle telemetry module

`VehicleController` publishes at the end of its fixed-step orchestration, after wheel commands. This describes the latest solved Rigidbody motion and the just-evaluated wheel/powertrain state; it does not pretend Unity has already integrated this step's AddForce calls. A vehicle adapter captures read-only value snapshots, including bounded per-wheel values, signed engine torque/load, nitrous flow, velocity-derived acceleration and yaw. First sample, reset/respawn and replay discontinuities reset filters. No transform-delta physics.

The source interface supports **live physics, motion-only civilian traffic, and telemetry replay**. A capability mask distinguishes absent clutch/boost/damage/water values from measured zero. The traffic adapter does not invent RPM from road speed. Physics remains independently usable without presentation.

## 4. Normalization module

One fixed-step normalizer computes loaded contact slip, axle slip, spin versus lock, drift, speed, engine stress, nitrous, airborne/landing and impact/scrape signals. Attack/release filters and hysteresis prevent threshold chatter. All presentation consumers receive the same normalized values. Curves and limits are content, not mutable ScriptableObject runtime state. NaN/Infinity and invalid delta times must not poison smoothing.

## 5. Audio module

A scene-owned, preallocated voice pool is the sole AudioSource allocator. Generation-checked leases prevent a stolen/recycled voice from being modified by its old owner. Low-priority distant sounds are rejected/culled before gameplay-critical player, tyre and police information. Expired loops are reclaimed; disable/unload resets sources, clip, filters, route and scheduled state. No runtime Instantiate/Destroy loop or unlimited PlayOneShot voices.

One reference engine profile: independently authored RPM regions with on/off-load recordings, equal-power adjacent-region blending and bounded pitch excursion; optional intake, mechanical, transmission and induction layers. Load comes from signed torque, not throttle alone. Startup/shift/limiter/overrun events use actual state transitions. Missing induction telemetry leaves induction silent. Do not synthesize fake boost from a timer. Recordings are a content acceptance gate; diagnostic tones cannot establish engine fidelity.

Front/rear normalized slip conveys understeer/oversteer. The current presenter retains per-wheel slip/rolling emitters for spatial contact and mixed-surface driving, with lower-priority road voices admitted by the global cap; axle audio aggregation is a future measured optimization. Wind uses a response curve and perspective mix. Spatial voices use limited Doppler, not exaggerated pitch dives. Bounded occlusion rays exclude emitter/listener rigs and smooth a low-pass. Native AudioReverbZone components can be authored for tunnels; no acoustic solver or completed tunnel content is included.

## 6. Mixer and settings

Master -> Vehicle (Player/OtherVehicle), Effects (Tires/Impacts/Environment/UI), Police (Radio/Sirens), Music. Exposed user category gains are separate from contextual snapshot gains so transitions do not override user preferences. Small snapshot set: FreeRoam, Race, Pursuit, Cooldown, Paused, Crash. Continuous engine/nitrous/threat values stay out of snapshot combinatorics. Radio ducks music; preserve player/tyre readability. No master overcompression.

Settings: master/vehicle/effects/music/police volumes, camera motion, haptics, flashes and subtitles. Store presentation preferences separately from career transactions. Motion blur is not introduced; existing URP volume authors must keep it optional. Solo/mute is development-only. Pause freezes physics-driven envelopes and the DSP timeline through the existing game-flow owner.

## 7. VFX and render constraints

Use built-in ParticleSystem for current moderate smoke/dust/spray/flame/sparks and bursts. Prewarm fixed pools, hard-cap systems/particles, use distance admission and configurable budgets, and clear particles on release. Automatic hardware quality selection is not implemented. Surface-authored selection, actual contact points/normals and normalized slip drive emission. Nitrous uses profile-local exhaust ports; the generated reference port is provisional until the final vehicle mesh is supplied.

Use a bounded mesh skid strip, avoiding a new URP decal renderer feature and projector per wheel. No runtime material cloning. Shared materials + vertex colors; any MaterialPropertyBlock use must be profiled because of SRP Batcher tradeoffs. No full-screen flash, motion streak or distortion shader without visibility and GPU evidence. VFX Graph remains optional only after Metal/Windows GPU profiling proves CPU particles insufficient.

## 8. Camera/haptics module

One composition function supplies clamped local inertia, directional damped collision/landing impulses and nitrous FOV to the existing speed-aware rig. It does not move the camera independently or integrate shake into the follow position. Shake is deterministic and bounded, scaled by accessibility. Haptics consumes the same state through Input System, resets on pause/focus loss/disable/device change, and is optional. Existing speed FOV is scaled by motion preference and is not counted twice. Additional nitrous chase-distance pullback is deliberately not layered on the current rig.

## 9. Surfaces and collision materials

One `SensorySurfaceProfile` reference on `VehicleSurface` supplies stable surface ID, physics grip/rolling resistance and sound/particles/skid eligibility. Preserve legacy Configure(name, grip, rolling) behavior without interpreting names. Unclassified collider uses explicit neutral road fallback; airborne is distinct. Cache wheel collider metadata until contact changes. Collision pair data is symmetric and separate from road slip data. Author metal/concrete/glass/wood/plastic mappings; never emit sparks for all materials.

## 10. Impacts and destruction

Classify contacts from normal relative speed and impulse/mass, retaining point, normal, tangent speed, material and instigator. OnCollisionEnter emits one transient; persistent contact maintains a scrape envelope, not repeated heavy impacts. Child-collider pairs and pooled reuse need explicit reset.

Destruction has one authority: a stable-ID destructible evaluates an actual impact, marks broken once and emits one semantic fact. Tier 0 cosmetic, tier 1 whole Rigidbody release, tier 2 bounded authored breakup, tier 3 authored set-piece consequences. Debris uses fixed reusable bodies with lifetime/sleep/distance cleanup and capped impulses. Pool capacity must never determine whether a gameplay barrier breaks. Physics consequences use authored colliders; cosmetic chunks never silently become a pursuit-breaker kill radius. Mission/police adapters observe the same fact; rewards remain owned by existing durable settlement modules.

## 11. Pursuit module

One pursuit adapter samples engagement, visible threat, unit density, bust pressure, cooldown and actual hazard deployment at 10Hz, with immediate state-change handling. Radio selects authored semantic lines, priority, heat/state eligibility, expiry, deduplication, per-line cooldown and interruption; revalidate at dequeue and during playback. No random unrelated chatter or exact hidden-location callouts. Heavy-impact chatter requires a recent visible player impact. Subtitles exist independently of clip availability and mixer volume.

Siren leases follow actual responding/engaged police state, spatial location and stable phase offsets; do not allocate a full fleet stack. Police lightbar remains a visual adapter and shares flash settings. Radio is optional authored presentation, not a claim of exact NFS 2015 baseline radio behavior.

Music uses authored tempo/meter and equal-length stems. Schedule together on `AudioSettings.dspTime`, change targets at a bar boundary, crossfade rather than restart each state. Transition cancellation, missing/not-loaded clips, pause, device reset and encounter-end stingers are explicit lifecycle cases. No arbitrary soundtrack import.

## 12. Technology research / alternatives

* [AudioMixer](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixer.html): routing/effects/snapshots suit the current scale. Audio Random Containers are useful for variation but not semantic selection; explicit clip sets also work in deterministic tests.
* [PlayScheduled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html): DSP-time scheduling supports musical alignment independent of render frame cadence. Preload loops, stream long music with adequate scheduling lead. [Clip load types](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClipLoadType.html) trade memory for decode/streaming CPU.
* [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) runs on the audio thread. Reject custom DSP/native plugins initially: no evidence sample layering is inadequate, and locks/allocations/Unity calls on that thread add risk. Native middleware would add licensing/toolchain/content complexity without present justification.
* [VFX Graph requirements](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.6/manual/System-Requirements.html): package/platform prerequisites differ from ParticleSystem. No package install until measured benefit. Shader Graph is installed, but compute/full-screen post effects are not free.
* [CCD](https://docs.unity3d.com/6000.0/Documentation/Manual/ContinuousCollisionDetection.html): keep important-car CCD, do not enable expensive sweep modes on all cosmetic debris. [Collision impulse](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collision-impulse.html) is total contact-pair impulse, not independent energy at every contact. No global timestep change or manual Physics.Simulate in gameplay.
* [Unity pooling](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.ObjectPool_1.html) is available; fixed arrays are preferable here for strict preallocated voice/effect/debris limits. Rejected unbounded pool expansion.
* Cinemachine absent: retain existing camera. Jobs/Burst/ECS are not warranted for a few dozen feedback sources without timing evidence. Addressables absent: scene/content profile references are enough; explicit clip import/load policy provides streaming without a new asset-management stack.

## 13. Proposed budgets (NOT measurements)

60fps desktop acceptance target; establish representative minimum Mac/PC hardware before shipping.

| Resource | Low | Standard | High ceiling |
|---|---:|---:|---:|
| Sensory voices, including stems/radio | 20 | 28 | 32 |
| Transient particle systems | 8 | 16 | 24 |
| Total pooled particles | 1024 | 2048 | 4096 |
| Cosmetic Rigidbody debris | 8 | 16 | 32 |
| Skid segments | 256 | 512 | 1024 |
| Occlusion rays per 50ms slice | 1 | 2 | 4 |

Low/Standard/High are proposed authoring configurations, not automatic selectable presets. Current standard content prewarms five particle types within a 16-system limit, with 2048 particles divided across those types. Unused capacity for one type cannot expand another type during a burst.

Reserve perceptual priority for player engine/tyres and radio; reject distant embellishment first. Proposed p95 sensory main thread <=1ms, additional cosmetic physics <=0.5ms, VFX GPU <=1.5ms, warmed feedback loop 0 B managed allocations/frame. Audio DSP <=10% of DSP block and no underruns are **targets**. Existing 32-real-voice setting remains unchanged. Record total game physics/render costs separately; a feedback budget cannot guarantee 50 full police vehicles run at 60fps.

## 14. Authoring workflow

Keep `Assets/NfsMw/Modules/Driving/Data/Sensory` profiles, `Audio/Vehicle/<car>/<layer>/<rpm>_<load>`, `Audio/Police`, `Audio/Music`, `VFX/Vehicle`, `VFX/Collision`, `VFX/Destruction`. Create one properly recorded reference car before multiplying profiles. Loop regions need reference RPM and on/off-load variants; pitch constraints reject bad recordings rather than stretching one sample across the range. Engine short loops: mono, preloaded/decompressed when memory permits. Long music: streaming, matching tempo/duration/sample rate. Loudness/phase/loop seams must be auditioned. Importer validation should report missing/unsafe content, not silently rewrite user assets.

Author wheel-contact surface profiles and impact pairs together; assign exhaust mounts, particle materials/prefabs and fracture representations. Missing content stays silent/no-op with validation diagnostics. Diagnostic test assets are clearly labeled and cannot pass the fidelity gate.

## 15. Debugging and replay

Development panel: RPM/load/gear/capabilities, normalized axle slip/spin/lock, per-wheel surface/load/contact, nitrous, active voices/particles/debris, dropped voice requests, radio queue/subtitle, mixer state and accessibility controls. Temporary category mute/solo supports auditioning. Camera and torque details remain inspectable in value frames/code, not separate GUI plots. Force signals only in the replay adapter, never in vehicle physics. Fixed-capacity telemetry capture/replay preserves wheel/contact/impact values and reset markers. Allocation on explicit in-memory snapshot is allowed, not during capture. Disk export and timeline scrubbing are not implemented.

## 16. Verification scenarios

Tests through public interfaces: invalid numbers/delta times, smoothing/hysteresis, wheelspin versus lock, unloaded wheels, landings, RPM/load blend weights, voice priority/recycle/generation, radio stale-event rejection/repeat/cooldown/interruption, music bar math, material pair symmetry, collision impulse classification, destruction exactly once and pool reuse. Integration: actual player/police wheel telemetry, pause/restart/unload, listener changes, escape settlement and missing content. Existing game-flow/police tests remain regression gates.

Drive/audition matrix: startup/idle/acceleration/redline/up/downshift, real induction when implemented, nitrous, braking/burnout/under/oversteer/drift, all authored surfaces, tunnel/highway/pass-by, light/heavy impact/scrape, sign/set-piece, roadblock, pursuit escalation/cooldown/escape. No test should claim physical turbo/water/damage coverage while those authorities do not exist.

## 17. Stress and profiling

Dedicated isolated scene: player, 24 physical police, 16 simplified physical civilians, bounded debris and 12 destructible signs. Opponent race grids and recorded sirens/music must be added to the final integrated scenario; they are not implied by the headless synthetic admission fixture. Stress feedback independently with replay as well as live physics. [Audio Profiler](https://docs.unity3d.com/6000.0/Documentation/Manual/ProfilerAudio.html) separates voice, DSP, streaming and memory costs. Current markers: `Sensory.Telemetry`, `Sensory.Normalize`, `Sensory.VehicleAudio`, `Sensory.AudioVoices`, `Sensory.Particles`, `Sensory.SkidMesh`, `Sensory.CollisionDispatch`, `Sensory.Destruction`. Use CPU/Physics/Rendering modules, Frame Debugger, and platform GPU capture (Metal tools on Mac; RenderDoc where supported on Windows). Memory Profiler is optional, not installed automatically.

Profile development **players**, not just the Editor; report headless tests as logic/lifecycle evidence only. Capture CPU/physics/audio/GPU/memory/GC/voices/particles/debris together over a long pursuit. Audio-device output, mix intelligibility, overdraw, Windows performance and final asset quality are separate manual acceptance gates.

## 18. Incremental acceptance

Audit/research precedes production changes. Then telemetry/normalization -> reference audio/surfaces/mixer -> pursuit/music/nitrous -> impacts/destruction/camera -> replay/debug -> player profiling -> combined sensory tuning. Code can be verified without final assets, but milestones 4/14/15 cannot honestly be called complete until recordings, actual device output and representative Mac/PC player captures are available. See `SensoryFeedback.md` for implemented setup, verification evidence and open gates.
