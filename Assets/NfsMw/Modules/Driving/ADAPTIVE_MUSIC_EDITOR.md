# Adaptive Music Editor

The Adaptive Music Editor is the authoring and verification workspace for the
single runtime `AdaptiveMusic` director. It is intended for a Most Wanted-style
frontend → free-roam → event → pursuit → cooldown → outcome flow, while keeping
the runtime data-driven and safe to stream on Mac and PC.

## Open it

Use **NFS MW Remaster → Sensory → Adaptive Music → Adaptive Music Editor**.

The same workspace is available from the `SensoryMusicProfile` Inspector and
from the **Adaptive Music** Scene overlay. The overlay is deliberately small;
the window is the main authoring surface.

The Library scans `Assets/NfsMw/Content/Audio/Music/` on refresh. **Project soundtrack** builds
`AdaptiveMusicSoundtrack.asset` from imported `AudioClip` files without changing
the source folder. A supplied song is represented honestly as one `fullMix`
base stem with a measured section duration and an explicit section-end
transition. It is not duplicated into fake rhythm or tension layers. Add real
stems later when the source session provides them.

The current project contains AAC `.m4a` sources. Unity 6 does not import AAC/M4A
as `AudioClip` assets, so the editor lists them as unsupported and will not
silently assign them. Convert a copy to a Unity-supported format such as OGG,
MP3, WAV, AIFF or FLAC, place the converted files under the same music root,
press **Refresh**, and then build the project soundtrack profile. The original
M4A files remain untouched.

## Workspace tabs

- **Library** — discover profiles, edit the serialized runtime contract, inspect
  schema identity, legacy fields, sections, cues, transitions, stingers,
  intensity and playback policy; scan supplied project soundtrack sources and
  build a generated full-mix arrangement.
- **Graph** — arrange sections and inspect transition edges. Positions are
  stored in a separate `*.layout.asset`; moving a node never changes runtime
  behavior.
- **Timeline** — inspect BPM, meter, bars, loop duration, beat/bar grid and
  stem lanes. The grid is not a fake audio seek bar: preview starts on a DSP
  boundary using the same timing metadata as runtime.
- **Mixer** — edit stem roles, gain, intensity curves, offsets, criticality and
  load policy. The runtime still sends final gains through `SensoryAudioWorld`.
- **Transport** — run a disposable editor audition, inject test snapshots into
  a live fixture director, queue stingers and inspect active/pending stems and
  bounded trace entries.
- **Validation** — deterministic readiness, identity, coverage, timing,
  importer and budget diagnostics with selection actions.
- **Publish** — validate, compute the deterministic `sourceRevision`, save the
  profile and layout. An error leaves the last valid asset untouched.

## Runtime ownership

`AdaptiveMusic` is the one owner of music arbitration and transport. It accepts
semantic requests and gameplay snapshots; it does not own race state, police
heat, GameFlow, user preferences or mixer parameters. `SensoryAudioWorld` is
still the pooled voice-admission, routing, ducking and device-change boundary.
Audio Zones and user settings therefore remain authoritative for their own
domains.

Example integration:

```csharp
music.RequestContext(
    AdaptiveMusicContext.Pursuit,
    pursuitThreat,
    "pursuit.director",
    priority: AdaptiveMusic.ContextPriority(AdaptiveMusicContext.Pursuit),
    quantization: MusicQuantization.Bar);

music.RequestStinger("outcome.escaped", "pursuit.outcome");
```

For an integration adapter or fixture, use the read-only snapshot seam:

```csharp
music.SetGameplaySnapshot(MusicGameplaySnapshot.ForContext(
    AdaptiveMusicContext.Race, 0.55f, "race.session"));
```

The snapshot is an input seam, not a second source of truth. Production
adapters should derive it from the authoritative race, pursuit and flow
systems, then clear it when their ownership ends.

## Authoring contract

Every authored section, transition and stinger has a stable ID. A section has
an eligible context mask, tempo/meter/grid, loop points, harmonic family,
fallback, and synchronized stems. A stem has a role, clip, gain curve, entry
offset, critical flag and loading policy. A transition declares its source,
destination, context, priority, quantization, dwell/cooldown, marker and
compatibility overrides. A stinger declares priority, eligibility, cooldown,
repetition suppression, queue age and interruption policy.

The runtime uses bounded fixed request/trace tables. It arbitrates by semantic
priority and request sequence, then schedules the selected section at a beat,
bar, marker or safe section boundary. Missing clips never silently fabricate a
new director: the selected section follows its explicit fallback or reports a
bounded error. Stale pending requests and queued stingers expire.

## Streaming and performance

Use the section `loadPolicy`, importer preload settings and explicit fallback
sections together. Critical stems should be preloaded before their transition
boundary. Long music can be streamed, but the authoring validator reports
background-load and readiness risks instead of claiming a transition is safe.
Keep `maxConcurrentStems` within the intended voice budget; all output still
passes through the shared `SensoryAudioWorld` pool.

The runtime uses `AudioSettings.dspTime` and future `AudioSource.PlayScheduled`
times so transitions are independent of frame rate. A pause, device change or
large DSP discontinuity releases scheduled leases and reanchors at a safe
boundary. Editor preview uses separate hidden `AudioSource` objects and cleans
them up on window close, assembly reload, scene changes and play-mode changes.

## Fixture sequence

1. For timing/routing tests, open the editor and choose **Fixture profile**.
2. To use supplied songs, convert unsupported sources first, press **Refresh**,
   then choose **Project soundtrack** and review the generated arrangement.
3. Choose **Build test scene**.
4. Open `Assets/NfsMw/Scenes/Tests/AdaptiveMusicTest.unity` and press Play.
5. Use `1` Frontend, `2` Free Roam, `3` Race, `4` Pursuit, `5` Cooldown,
   `6` Escape + stinger, `7` Results + stinger, `F` Arrested, `P` Pause and
   `0` to return to real runtime adapters.
6. In the editor Transport tab, refresh diagnostics and inspect the scheduled
   section, DSP time, stem admission and trace.

Fixture audio is deterministic original synthesis for timing and routing
checks. It is not decoded from Need for Speed recordings. Add licensed
production stems later by replacing the clip references while keeping stable
IDs and compatible musical metadata.

The project soundtrack importer is separate from the synthetic fixture. It
does not claim that a mixed song contains separable gameplay layers, and it
does not decode or modify commercial audio. It only authors direct references
to playable project `AudioClip` assets.

## Legacy migration

Profiles containing the old `bpm`, `beatsPerBar`, `bars`, `stems` and outcome
clips remain loadable through a deterministic compatibility section. Use
**Migrate legacy** to create frontend, free-roam, race, pursuit, cooldown,
escape and results sections plus cues, transitions and stingers. Review the
generated IDs and clips, run validation, then publish.

## Verification

The editor tests cover DSP quantization, invalid timing rejection, section
duration, context arbitration priority, duplicate IDs and legacy loadability.
They do not claim musical quality, device-driver behavior or final frame-time
budgets; those still require platform profiling with production audio.
