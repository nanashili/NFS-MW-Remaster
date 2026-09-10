# Mission / objective framework

## Status and replacement

The shared `MissionGraph` / `MissionRuntime` now owns live free-roam course and pursuit-challenge objective progression. The former `FreeRoamEventProgress` source and its metadata were removed, along with the separate pursuit challenge timer and direct objective wallet payout. Existing scene-bound `FreeRoamEventDefinition` assets still describe courses; `MissionCourse` adapts their geometry into semantic checkpoint events. Police simulation, shops, career requirements and save storage remain authoritative in their respective domains.

This is an implemented framework and live course migration, **not completion of every feature in the supplied 110-requirement brief or shipping certification**. See the remaining integration gates below. Research and architectural decisions are in [MissionFrameworkDesign.md](MissionFrameworkDesign.md).

## Editor entry points

- **Assets > Create > Driving > Timed Destination Example** creates a validated example graph.
- **Create > Driving > Mission Graph** creates an editable JSON-backed definition asset. Its inspector validates content without starting gameplay.
- **Tools > Driving > Mission Debugger** inspects an asset, a `MissionHost`, or the existing `FreeRoamSession`, including node states, dependencies, conditions, timers and a bounded transition timeline.

Existing free-roam scenes use the new course runtime without rebuilding scenes. To run an arbitrary authored graph, add `MissionHost`, assign its definition catalog and career profile, then call `StartMission` with its stable ID. Call `SetCareerFacts` before starting graphs with career requirements. Assign `MissionWorldActions` if nodes declare actions. This is an API-driven host, not a newly added main-menu mission selection screen.

## Authoring contract

Use stable semantic IDs for missions, objectives, events, bindings and reward definitions. Never use Unity instance IDs as durable identity. Definitions are detached when compiled; mutating the original asset cannot alter a running mission.

Built-in primitives are `event`, ordered `sequence`, `condition`, continuous `hold`, successful `timer`, and failing `deadline`. Conditions compose All / Any / Not, numeric facts, objective state, progress, remaining time, and the existing career requirement evaluator. Dependencies create sequential stages; independent eligible nodes run in parallel. Parent IDs group presentation; they do not implicitly aggregate children. Author aggregate success explicitly.

Optional failure does not fail the mission. Exclusive branch groups choose a deterministic eligible branch; authored conditions can converge on a shared tail. Cycles are rejected: use bounded counters rather than graph loops. Custom primitives implement `IMissionPrimitive` and must store their persistent progress in the supplied snapshot, not hidden instance state.

Timers select Mission, Game or Real clocks. Suspended missions stop mission/game time and event progression; real clocks can expire while suspended. There is no offline wall-clock catch-up. Failure wins over success on the same step, including exact deadline ties. Newly activated objectives do not retroactively consume the batch that activated them.

## Event and world integration

`MissionRuntime.Step` accepts an explicit monotonically increasing step, clock deltas and a bounded event batch. Use a stable event ID for each occurrence and stable target IDs for deduplicated entities. Replaying an occurrence cannot advance a later stage. Event batches are ordered by ID, not producer invocation order. Facts are numeric; producers publish authoritative state rather than objectives polling scene objects.

`MissionHost.Publish` queues events and its LateUpdate dispatches them. `MissionArea` emits enter/leave and inside facts (assign the player Rigidbody and a trigger collider); `MissionPursuitBridge` forwards bounty/pursuit state; `MissionRaceBridge` forwards completed solo course outcomes with durable settlement identities. Already-inside areas seed occupancy when a mission starts: use the inside condition when entry before activation should qualify.

`IMissionActions.Reconcile` receives the desired action set for a claim and attempt. Implement adapters idempotently, release only resources they own, and reject missing bindings. The provided `MissionWorldActions` supports owned prefab spawning and reversible GameObject activation. It validates commands before changing the world and releases owned objects on cancellation/disposal. It is not a general snapshot of arbitrary physics or police entities.

`MissionPresentation.Read` supplies detached objective rows and active-only marker IDs for HUD consumers. Existing course HUDs use the new runtime. `MissionRecordedStep` and `MissionReplay.Run` support explicit deterministic test recordings; automatic recording controls and a generic player-facing mission HUD are not included.

## Saving, checkpoints and rewards

Profile schema 3 adds mission instances and permanent settlement claims. Schema 1/2 saves migrate through the existing strict codec with an empty mission section; old unrecorded rewards are not guessed. Player save files are not deleted by this replacement.

Snapshots contain stable IDs, definition version/hash, objective state, timers, deduplication evidence, facts, branches, checkpoints and frozen results. Checkpoint retries keep the same reward claim identity and restore runtime progress while incrementing the attempt. They reconcile action ownership; they do not magically reconstruct every game subsystem.

Content mismatch (including changed content at the same version) fails closed. Pure runtime restoration accepts an explicit `IMissionMigration`; migrate nested checkpoint snapshots as well. The MonoBehaviour catalog and live course adapter do not yet expose a content migration registry. Keep IDs/content stable for existing saves until that integration exists.

Active non-pursuit courses save their course progress, countdown and player pose. Active pursuit restoration is explicitly rejected until a police/world reconstruction adapter is supplied. Do not interpret the presence of a saved objective as proof the entire pursuit world is restorable. Capture/save before releasing a generic host runtime when its progress must be retained.

Successful results freeze cash, reputation and optional catalog grants. `CareerProfile.TrySettleMission` stages these together with the claim and profile, validates the candidate, commits storage once, then adopts wallet/ownership state. Failed writes do not grant rewards; repeated claims are idempotent and conflicting claims fail. Ambiguous writes require exact readback or block further mutation pending reload. Assign `missionRewardCatalog` for grant rewards. Existing initialized economy journals receive a receipt; legacy mode is not silently switched into new economy ownership. This change does not migrate all shop transactions.

Generic hosts expose `RetrySettlement` after failure. Live free-roam settlement retries are throttled to two seconds and retain the successful mission until acknowledgement. Bonuses are evaluated at mission success, not independently paid at intermediate objective completion.

## Verification and operational limits

EditMode tests cover dependency progression, branching, optional failure, deadline ties, paused clocks, duplicate events across reload, Unity JSON round trips, checkpoint retries, cleanup, content mismatch, profile migration and failure-safe settlement. A million irrelevant-event workload exercises routing, but is not a player-build frame-time or allocation budget. Game-flow smoke covers boot/menu, countdown, restart, rewards and save/resume in an isolated Unity project.

Explicit bounds include 2,048 objectives, 8,192 events per step, condition depth 32, bounded deduplication history and a 256-entry diagnostic timeline. Runtime ownership is single-threaded. Subscription reconstruction and snapshot/UI sampling allocate; Windows, IL2CPP, hardware profiling and long-running streaming stress remain release gates.

## Remaining full-brief integration gates

- Full police, traffic, vehicle and streamed-world checkpoint reconstruction and cross-scene binding recovery.
- Dialogue, cinematic, weather, race-start, pursuit-start and permanent-world mutation action adapters.
- Full race-grid/position authority, richer police milestone event producers, typed non-numeric variables and stage-level reward transactions.
- Catalog-level content migration tooling, automatic replay recording UI and a generic player-facing mission HUD.
- Static reachability/satisfiability analysis beyond dependency/condition validation, and shipping-platform performance/fault qualification.

These are not implemented by placeholders or silently substituted with objective-side simulations. Add them through the event, action, migration and authoritative settlement boundaries above.
