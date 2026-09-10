# Police system

This replaces both old police driving paths with a fine-driven, observer-gated encounter and the shared vehicle physics controller. It is a **provisional implementation**, not a fidelity-certified recreation. The reference ledger in [NFS2015_POLICE_REFERENCE.md](NFS2015_POLICE_REFERENCE.md) distinguishes documented behavior from missing measurements.

## Try it in Unity

Open `Assets/NfsMw/Scenes/World/RockportMap.unity` and press Play. The district has civilian traffic, a pool of eight wheel-driven police vehicles, shops and authored hazard sites. Ordinary lawful driving does not initiate an encounter. Speeding, crossing a red light or collisions must be witnessed by an operational officer with actual line of sight.

For the focused sandbox, open `Assets/NfsMw/Scenes/Tests/PursuitTest.unity`. Its on-screen harness can start a scripted pursuit, set a provisional engagement level, pay an offered fine, force a diagnostic outcome or retry a pending settlement. These debug controls deliberately bypass natural initiation; they are not player mechanics. A scripted pursuit starts with resistance, so use a witnessed minor offence to exercise the payment offer.

- Driving uses the existing keyboard/controller mapping. Stop while visible and use **Pay fine** in the HUD during the traffic-stop offer.
- **X** toggles ignition when the vehicle is nearly stationary. Ignition-off removes combustion and nitrous torque. It does not add an unmeasured hiding bonus.
- The HUD displays fine, engagement, state, containment progress, cooldown progress and pending save errors. Cooldown does not reveal officer minimap positions.
- Arrest collects the fine and returns to the authored garage. If the garage recovery area is blocked, relocation waits and retries; its durable directive is not discarded. A safehouse is used only when no garage is authored.

To regenerate the placeholder sandboxes, use **NFS MW Remaster → Build Free Roam Scene** or **Build Pursuit Test Scene**. These builders replace their respective generated scenes; back up hand-authored changes before running them. Free Roam uses the imported BMW M3 E42 player mesh; final police car meshes, siren clips and hazard visuals can be added later.

## Ownership and integration

| Boundary | Owner / contract |
| --- | --- |
| Pursuit lifecycle, fine and knowledge | `PolicePursuitModel`; no wallet, scene or bounty decisions |
| Observed world facts | `VehiclePursuitDirector.ReportObservedOffence` verifies a registered officer through `IVehiclePursuitPerception` |
| Target identity / availability | `IVehiclePursuitTarget`; optional `IPoliceIgnitionState` |
| Navigation and driving | `VehiclePoliceUnit : IVehicleInputSource` → `VehicleController` → powertrain, assists, wheels |
| Durable terminal outcome | `IPoliceOutcomeSettlement`, implemented by `CareerProfileSystem` |
| Historical career bounty | `PoliceCareerAdapter`; one-way facts and engagement mirroring, never pursuit authority |
| Mission / free-roam results | `OutcomeAcknowledged`, then durable world follow-through |
| Roadblocks / spikes | Authored `PoliceRoadHazard` sites and `PoliceSpikeContact` trigger relays |
| Spatial feedback | `PoliceVehicleFeedback`; optional clips, lights and rendered lightbar lenses |

The lifecycle is Patrol → Observed → TrafficStop → Pursuit → Cooldown. Payment, escape or arrest enters OutcomePending. The director releases pursuit only after the complete structured outcome is durable. Exceptions or rejected writes retain the same frozen encounter ID. Reusing that ID with different outcome data is rejected.

Use `Configure`, `ConfigureRules`, `ConfigureNavigation`, `ConfigureUnits`, `SetPerception` and `SetSettlement` for explicit wiring. Do not rebind or retune an active encounter. Alternate settlement adapters must provide idempotent durable writes and reconcile uncertain acknowledgements. `TryStartPursuit` is an explicit mission/test entry; normal world reporters must use the observer-gated API.

## Physical police prefabs

A police root needs a Rigidbody, chassis collider, `VehicleController`, four `VehicleWheel` children, and `VehiclePoliceUnit` as the controller's input source. The builder creates this rig using `Data/PoliceVehicleTuning.asset`. Assign a `RoadNetwork` and optional `RoadTrafficSignals` for patrol and road-routed response. Without roads the pursuit sandbox uses direct target-point steering, not patrol navigation.

Navigation uses lane lookahead, corner braking, finite non-alloc obstacle sensors, signal compliance in patrol, and bounded reverse recovery. Police cannot teleport during ordinary response or write chassis forces. `PlaceWhileInactive` rejects active objects and is reserved for safe pooling/test setup. Insufficient route or sensor certainty produces braking, not a force override. Contact tactics remain opt-in and still cannot inject impulses.

Perception applies a facing cone, close-contact awareness and collider LOS. Hidden targets never update the model's remembered position/velocity. Search routes lead to last-observed knowledge. A streamed/unavailable target pauses timers rather than granting escape or arrest. Arrest requires uninterrupted visible containment at low speed; the Unity adapter measures chassis-edge clearance, not overlapping vehicle centres. Losing contact resets containment progress. Player health is not an arrest condition.

## Explicit provisional policy

These are implementation settings, **not measured NFS constants**:

| Setting | Default |
| --- | --- |
| Payable fine | Strictly below $500; offered only before resistance |
| Traffic / property / collision / disabled-unit fine | $150 / $250 / $300 / $1,000 |
| Resistance / reacquisition increment | $350 / $150 |
| Engagement level starts, 1–5 | $0 / $500 / $1,500 / $5,000 / $11,000 |
| Initial observation / contact-loss grace | 0.5s / 0.7s |
| Stop offer | 8s; continued driving after 2s counts as resistance |
| Escape REP | 100 × terminal engagement level |
| Engine-off cooldown multiplier | 1.0, intentionally neutral |
| Free-roam pool / active response cap | Eight available; default tier caps 2–6 |
| Reinforcement cadence | At most one spawn every 3s; no arrivals during cooldown/outcome settlement |
| Hazards | Roadblocks level 3+, strips level 5; geometry/contact only |

The existing response profile supplies provisional detection, containment, search and cooldown settings. Its old speed/acceleration, ram-force, box-force and disengagement fields are retained for serialized compatibility but do not power a second driving model. Vehicle tuning, not catch-up velocity overrides, determines actual motion. The provisional five-level mapping is not approval of the secondary source's ambiguous fine anchors.

Escape cancels the fine and grants REP, never cash. Historical career bounty is banked separately and does not become cash. Voluntary payment requires the full amount. Arrest collects available cash up to the assessed fine, never a negative wallet; any shortfall is currently waived. This insufficient-funds rule needs reference verification.

## Authored hazards and asset hooks

Place `PoliceRoadHazard` on a site whose local +Z points in the direction of the suspect's approach. Give it a dedicated inactive child containing the physical layout. The clearance envelope must cover all colliders. Preserve an escape lane when authoring roadblocks. The shipped layouts are placeholder barriers, not a verified roadblock roster.

Sites select from last-known approach knowledge. Deployment requires an engaged pursuit, the configured tier, a clear bounded overlap query, a hidden camera-frustum envelope, and at least four seconds of travel distance / 90m separation. Sites never appear directly under a car. They retire only when the encounter is over/pending and the same clearance/visibility checks permit removal. No camera means no deployment. Each site is used at most once per encounter.

Attach `PoliceSpikeContact` to the strip's trigger volume. The site checks target Rigidbody identity, proximity, deployed encounter and duplicate contact. `SpikeContact` and the inspector event are integration hooks for a later **verified** tyre consequence model. There is currently no tyre puncture, forced crash, damage-based bust or hidden traction penalty. Crossing a deployed hazard may report an evasion only if a police observer can verify it; hitting a strip suppresses its dodge credit.

Assign a looping siren clip to `PoliceVehicleFeedback` for spatial playback. The supplied lightbar lenses flash on response/engagement; optional scene Lights can be assigned independently. Sirens stop on search/release/disable and use distance rolloff. No dispatch dialogue, helicopter audio or global siren is fabricated.

## Persistence and migration

Schema **4** adds `police.settlements` and `pendingWorldOutcomeId`. Migrations from 1–3 preserve wallet, vehicle, historical bounty and mission data and add no retroactive police awards. Settlement IDs, enums, required fields, numeric bounds, duplicate IDs and journal balance chains are validated before writes/restores.

Starting a new police encounter discards an unfinished legacy encounter's uncommitted bounty bucket, without awarding it. Already banked career totals and completed encounter counters are preserved. Legacy in-flight bounty is never silently attached to a new encounter.

Cash, REP, history and a pending world directive commit in one profile snapshot. Live wallet/history updates occur only after that commit. If storage throws after writing, the profile adapter reads back and acknowledges only an exact snapshot match; an unresolved write locks further saves pending reload. An initialized economy receives a structured journal receipt. Legacy shop-wallet scenes retain their existing ownership and do not silently activate the exclusive economy journal.

Free roam applies the acknowledged outcome, then clears its world directive on a subsequent successful save. A crash in between replays relocation/mission follow-through without another fine or REP award. Active, unfinished police-world reconstruction is **not** supported: manual/autosave during pursuits is blocked. A committed terminal outcome can restore its interrupted mission long enough to finish the terminal transition.

`RoadPoliceUnit.cs` and its meta are removed, both generated police scenes are migrated, and the old `RoadVehicleMotor` chase branch is removed. Historical bounty sandbox APIs remain for their standalone scene; they are not the live police authority. Custom scenes outside the generated pair need the same wheel-rig migration before opening them with the deleted script reference.

## Verification and remaining gates

Local verification on 2026-09-05, Unity 6000.6.0f1 on this Mac:

- **214/214 EditMode tests passed**, including the final legacy encounter migration regression.
- Police CLI runtime smoke passed: both rebuilt scenes have no missing script references, all police have four production wheels and no road-traffic motor, authored hazards are present, physical movement/LOS and rejected-save retry are exercised.
- Free-roam CLI smoke passed: road clearance, wheel-driven player movement, 32 ambient residents, traffic queues/yielding, shops, save/load, race rewards, witnessed offences, reinforcement arrivals and escape.
- Game-flow CLI smoke passed: boot/menu, profile creation, loading, pause/audio/time restoration, event countdown/restart, results, save/unload/resume and cancellation.

Reports are local temporary artifacts: `/private/tmp/police-reference-final-tests.xml`, `/private/tmp/police-reference-final-smoke.log`, `/private/tmp/police-free-roam-smoke2.log`, and `/private/tmp/police-game-flow-smoke.log`. The isolated editor logs still contain an editor-only `UnityEditor.Search.SearchDatabase` startup exception; passing probes are not a claim of a completely clean Editor console. Previous generated scenes were backed up to `/private/tmp/nfs-police-scenes-before-migration.ApN8BT` before replacement. No user profile files were used for runtime verification.

`PoliceReferenceTests` exercises observation/occlusion, fine boundaries, resistance, reacquisition, missing targets, containment, ignition, hazard fairness, rejected writes, lost acknowledgements, duplicate outcomes, insufficient funds, malformed saves and migration. `FreeRoamSessionTests` covers committed-outcome world recovery and mission restoration.

CLI probes (run in a separate Unity project copy, never alongside the editor on the same project):

```text
Unity -batchmode -nographics -projectPath <copy> -runTests -testPlatform EditMode -testResults <xml> -logFile <log>
Unity -batchmode -nographics -projectPath <copy> -executeMethod NfsMwRemaster.Driving.Editor.DrivingPoliceReferenceSmoke.Run -logFile <log>
Unity -batchmode -nographics -projectPath <copy> -executeMethod NfsMwRemaster.Driving.Editor.DrivingFreeRoamSmoke.BuildAndRun -logFile <log>
```

The police smoke uses actual wheel-driven movement and a real occluder, then injects a storage failure and retries the REP-only escape. It uses an in-memory profile adapter. The broader free-roam smoke checks road clearance, traffic, shops, save/load, races, witnessed speeding, reinforcements and LOS escape with isolated save files.

These checks do not establish shipping CPU/GC budgets, police cornering quality across every road, visual/audio fidelity, a final roster, or a calibrated spike consequence. Collect the reference dataset, add the assets, run sustained highway/downtown playtests and profile representative Mac/PC hardware before shipping. Hard/Impossible tuning must remain a separately accepted extension.
