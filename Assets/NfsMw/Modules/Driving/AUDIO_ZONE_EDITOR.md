# Audio Zone Editor

The Audio Zone Editor is the authoring and diagnostics surface for the city's
acoustic spaces. It supports open streets, enclosed spaces, tunnels,
underpasses, parking structures, industrial interiors, garages and frontend
spaces without introducing a second audio backend or a second world/road
network.

The attached `08_Audio_Zone_Editor.md` is the feature brief that drove this
implementation. The Unity URLs supplied with the request are API and editor
workflow references. They are not copied gameplay instructions, and they do
not override the project's existing audio ownership boundaries.

## What is implemented

### Runtime ownership

`AudioZoneWorld` is the single runtime owner for:

- listener-policy resolution;
- zone membership, blend weights and hysteresis;
- deterministic overlap arbitration;
- category-specific gain and filter requests;
- bounded authored ambience admission through `SensoryAudioWorld`;
- portal state queries;
- optional `AudioReverbZone` approximation;
- a bounded diagnostic snapshot for debug UI and traversal tooling.

`SensoryAudioWorld` remains the pooled voice and shared mixer boundary. Audio
zones do not write user volume preferences, music state, police sensing state,
save data or mission state. Music, radio and sirens retain their existing
owners and are neutral by default unless a profile explicitly opts into those
source classes.

### Authoring data

- `AudioZoneProfile` is a reusable `ScriptableObject` with a schema, stable ID,
  category, priority, blend mode, falloff curve, gain, filters, reverb
  approximation, source-class toggles, per-class overrides and ambience layers.
- `AudioZone` is a scene component containing only source geometry and profile
  intent. It supports box, sphere, capsule and convex collider/mesh fallback
  volumes. The core shape and the profile's outside blend boundary are shown
  separately.
- `AudioZonePortal` is an authored acoustic connection. It is not a road link,
  navigation link or police-sensing edge. Its open/closed transmission and
  low-pass values are queried explicitly.
- `AudioZoneTraversalAsset` stores editor traversal evidence, source-scene
  revision and resolved membership. It is not a gameplay save.

### Editor workflow

The tool uses a UI Toolkit-hosted editor window with a bounded IMGUI authoring
surface, a Scene overlay, custom inspectors, Scene handles and gizmos.

Open it from:

`NFS MW Remaster > Driving > Audio Zones > Audio Zone Editor`

The Scene overlay can also open the window, create box/sphere/capsule zones,
validate loaded content and frame the current selection. The component
inspectors expose the same actions for users who prefer the Inspector.

The window tabs are:

1. **Zones** — catalog/search zones and portals, create objects, assign a
   profile, inspect serialized data, focus a volume and create an authored
   portal.
2. **Profiles** — search reusable assets, create category defaults, inspect
   ambience/source overrides and assign A/B slots.
3. **Preview** — run a disposable `EditorSceneManager.NewPreviewScene`, mute
   production listeners temporarily, audition resident clips, follow an
   authored listener path, solo a zone and compare profile A/B or dry mode.
4. **Trace** — inspect the live `AudioZoneWorld` snapshot: listener policy,
   primary zone, active influences, effective filters/reverb and obstruction
   query age.
5. **Validation** — filter diagnostics by severity/text and jump to the
   offending zone, portal or profile.
6. **Publish** — run validation, save assets, inspect the source revision and
   review the ownership checklist. “Publish” does not bake IDs or mutate player
   state.

## Quick start

1. Open the Audio Zone Editor.
2. Create an `OpenStreet` profile in the Profiles tab. Assign diagnostic clips
   or project-owned clips to its ambience layers.
3. In the Zones tab create a box zone, assign the profile and resize it with
   Scene handles. The inner wire shape is the core; the translucent outer
   bounds include blend distance and hysteresis.
4. Create a second profile such as `Underpass` or `Garage`, create a zone and
   assign it.
5. Select the first zone and create a portal. Set the state to `Open` or
   `Closed`; leave `WorldControlled` for a gameplay adapter that will own the
   state later.
6. Add one `AudioZoneWorld` to the scene. Assign the existing
   `SensoryAudioWorld`, choose a listener policy and assign a fallback profile
   for unloaded/outside regions.
7. Press **Validate**. Resolve errors before treating the scene as production
   content.
8. In Preview select Profile A, optionally Profile B, add path points and press
   **Start disposable preview**. Stop/close the preview after listening.
9. Use **Publish** to save authored assets and scene changes after validation.

The synthetic fixture is available at:

`NFS MW Remaster > Driving > Audio Zones > Build Audio Zone Test Scene`

It creates `Assets/NfsMw/Scenes/Tests/AudioZoneTest.unity`, four synthetic profiles and a
four-space traversal path. Existing profiles at the canonical demo paths are
never reset when the fixture is rebuilt; hand-authored content is preserved.
The fixture uses the project's original deterministic diagnostic WAV assets,
not ripped or decoded Need for Speed audio.

## Runtime contract

### Listener policy

`AudioZoneWorld` supports:

- `RenderedAudioListener` — the active `SensoryAudioWorld.Listener`, then a
  loaded `AudioListener` fallback;
- `ListenerVehicle` — a supplied vehicle transform;
- `VehicleThenListener` — vehicle if available, otherwise rendered listener;
- `ExplicitTransform` — a supplied gameplay/cockpit/camera anchor.

Choose deliberately. A chase camera outside a tunnel and a vehicle inside it
are different authoring cases. The selected policy is written into the runtime
snapshot so a mismatch is diagnosable.

### Composition and determinism

Candidates are ordered by profile priority (descending), blend weight
(descending), then stable ID (ordinal ascending). `maxActiveZones` truncates
the sorted list, so a dense overlap cannot grow the hot path without bound.

For each `SensoryCategory`:

- `PriorityOverride` and `ExclusiveCategory` select a deterministic winner;
- `WeightedBlend` combines gain linearly and filter frequencies in log space;
- `AdditiveAmbience` participates in the blend and contributes authored layers;
- gains, filters and reverb values are clamped to backend-safe ranges.

Ambience admission is separately limited by `maxAmbienceLayers`. A layer is
keyed by stable zone ID and layer ID so continuous loops are updated instead
of restarted on every sample. Existing `FeedbackVoiceBudget` priority and
distance rules still decide the final voice admission.

### Obstruction and portals

Dynamic obstruction remains the responsibility of the existing
`SensoryAudioWorld` voice pool, which performs budgeted ray checks and exposes
the last query time. Zone evaluation never becomes police detection evidence.
Portal transmission is an explicit bounded query through
`TryGetPortalTransmission`. This implementation does not claim diffraction,
reflection or a full acoustic path solver. A production gameplay adapter can
provide the `WorldControlled` state and combine portal attenuation with its
own source/listener context.

### Backend approximation

The current project uses Unity `AudioSource`, `AudioLowPassFilter`,
`AudioHighPassFilter`, the existing `SensoryMixProfile` routes and an optional
native `AudioReverbZone`. Profile dry/wet, reflection and send intent is
represented by supported gain/filter/reverb fields. Unsupported middleware or
device-specific spatializer behavior is intentionally surfaced as a preview
limitation rather than simulated by a hidden DSP engine.

## Safety and lifecycle rules

- Stable IDs are generated once and are not regenerated by a rebake.
- Profile and traversal assets are versioned with explicit schemas.
- Scene and asset edits use Unity serialization and Undo where the editor
  creates objects; publishing is an explicit save action.
- Demo rebuilding is additive for existing canonical profiles.
- Preview objects, sources, filters, the preview listener and the preview scene
  are disposable. Window close, assembly reload, Play Mode transitions and
  repeated Start/Stop calls restore production listener enabled states.
- The preview never calls `SensoryAudioWorld`, changes PlayerPrefs, changes
  user mixer preferences or writes save/economy state.
- Missing/unloaded clips are counted and reported. A silent source is not
  reported as a successful audition.
- The runtime resets environment processing and restores a native reverb zone
  when `AudioZoneWorld` is disabled.

## Validation rules

The validator checks:

- stable IDs, duplicate IDs and unsupported profile schemas;
- missing profiles and invalid filter/reverb/ambience values;
- empty/non-finite volume bounds and missing convex sources;
- disabled zones and missing ambience clips;
- portal endpoint/self-reference/duplicate/disconnected warnings;
- missing or multiple `AudioZoneWorld` owners and missing
  `SensoryAudioWorld` references;
- multiple loaded `AudioListener` components and missing fallback information;
- equal-priority overlapping override zones;
- loaded zone/portal count budgets.

Validation is deliberately not a perceptual guarantee. Representative engines,
tires, impacts, traffic, sirens and radio still require a listening pass on the
target build and device.

## Testing

Focused edit-mode coverage is in
`Assets/NfsMw/Modules/Driving/Tests/Editor/AudioZoneTests.cs`. It covers geometry membership,
vertical bounds, capsule/sphere behavior, hysteresis, profile validation,
portal direction/transmission, deterministic runtime arbitration, defensive
mix cloning and traversal monotonicity.

Run the tests from the Unity Test Runner with the **EditMode** filter, or from
the command line using the Unity Test Framework package version installed in
`Packages/packages-lock.json`. The repository's broad editor assembly may also
contain unrelated legacy compile failures; report those separately from the
focused Audio Zones assembly check.

## Known limitations and integration seams

- Road-corridor-derived zones are not generated here. The tool consumes
  authored scene geometry; a future road adapter must use the existing road
  network as its source of truth.
- Convex membership uses the supplied collider's `ClosestPoint`/bounds fallback
  and does not implement arbitrary non-convex polygon decomposition.
- The current portal query is direct and bounded rather than a multi-hop
  propagation solver.
- `WorldControlled` is treated as open until a gameplay owner supplies a
  closed-state adapter; this is visible in the inspector and validator copy.
- The editor preview uses authored filters and disposable Unity sources. It
  cannot prove target-device spatializer output, streamed-clip timing, DSP
  load, or final mix loudness.
- The optional native reverb component is a coarse approximation. It is
  disabled/restored by the zone world and should not be shared with another
  owner without an explicit integration decision.

## Relevant project files

- Runtime: `Assets/NfsMw/Modules/Driving/Runtime/Sensory/AudioZones/`
- Existing voice/mixer owner:
  `Assets/NfsMw/Modules/Driving/Runtime/Sensory/SensoryAudioWorld.cs`
- Editor: `Assets/NfsMw/Modules/Driving/Editor/AudioZones/`
- Tests: `Assets/NfsMw/Modules/Driving/Tests/Editor/AudioZoneTests.cs`
- Test fixture: `Assets/NfsMw/Scenes/Tests/AudioZoneTest.unity` after running the menu
  command
- Research and audit record: `Assets/NfsMw/Modules/Driving/AUDIO_ZONE_EDITOR_RESEARCH.md`

