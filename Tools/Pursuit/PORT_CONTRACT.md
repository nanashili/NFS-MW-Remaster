# PC 1.3 cops and pursuit port contract

This contract defines what an independent Unity implementation may reproduce
from the current evidence, what must remain configurable, and what cannot yet
be called retail-equivalent. It deliberately does not alter the existing Unity
police runtime.

## Fidelity boundary

The installed `speed.exe` is byte-identical to the PC 1.3 corpus oracle, so
ledger virtual addresses apply to that executable. That does not make every
recovered source function exact. Use these implementation tiers:

| Tier | Evidence | Implementation rule |
|---|---|---|
| A — binary-backed | 100% function match plus verified oracle identity | Implement as a focused, deterministic component and test every visible branch. |
| B — high confidence | 95%–99.999% near match | Preserve control flow and constants, but label behavior source-correlated until an oracle test passes. |
| C — candidate | 0%–94.999% partial match or source without a direct report mapping | Put behind a narrow interface/configuration seam; do not bake in a retail-equivalence claim. |
| D — unresolved | 0% match, absent mapping, or unsolved dependency | Keep explicit as unsupported/unverified; never invent a substitute and silently call it Most Wanted behavior. |

The checked ledger is the authority for each function's tier. Source comments
or plausible game behavior cannot promote a function by themselves.

## Ownership model

Keep the original deep boundaries without recreating its inheritance tree:

| Unity-side boundary | Responsibility | Source analogue |
|---|---|---|
| `MostWantedCopManager` | Cop pool, pursuit instances, roadblocks, request queues, support, update order, teardown | `AICopManager` |
| `MostWantedPursuit` | One target's status, heat, visibility memory, formations, bust/evade progress, requests and statistics | `AIPursuit` |
| `MostWantedCopUnit` | Per-vehicle goal and observed target state; emits driving intent | `AIVehiclePursuit` / `AIVehicleCopCar` |
| `MostWantedRoadBlock` | Layout instance, engaged units, passed/dodged/strip state and release | `AIRoadBlock` |
| `MostWantedHelicopter` | Fuel and helicopter-specific goals; flight remains an adapter until calibrated | `AIVehicleHelicopter` and actions |
| `MostWantedSpikeStrip` | Swept wheel intersection and puncture/cleanup | `SpikeStrip` |

The manager owns allocation and lifecycle. Units must not spawn themselves,
commit pursuit results, mutate global heat, or teleport to solve navigation.
Unity physics and road queries remain adapters; the pure pursuit model emits
commands and events.

Minimal external seams are sufficient:

- `IPursuitAttributeSource` supplies heat-level attributes and encounter mode.
- `IPursuitWorldQuery` supplies roads, visibility, hidden zones, tunnel state,
  camera/offscreen state, and target/unit transforms.
- `IPursuitSpawnPlanner` owns the unresolved normal placement algorithm.
- `IPursuitRandom` makes weighted choices and roadblock timing deterministic in
  tests.
- `IPursuitResultSink` commits the terminal pursuit statistics once.
- `IPursuitClock` exposes source-task delta units without assuming seconds.

## Exact manager order

`AICopManager::OnTask` is an exact 109-byte match at `0x0043ef00`. Preserve its
system order:

1. update breaker zones;
2. update patrol cars;
3. update active pursuits and their request queues;
4. update roadblocks;
5. service explicit spawn requests;
6. decrement the support delay.

Do not merge these into a unit-by-unit Unity `Update`, because that changes
which state each later stage observes.

## Pursuit lifecycle

The recovered per-target states are:

| Value | State | Meaning |
|---:|---|---|
| 0 | `InitialChase` | Active pursuit before a backup transition. |
| 1 | `BackupRequested` | Pursuit has crossed its contingent threshold and requested reinforcement. |
| 2 | `Cooldown` | Target has broken recent visual contact; search/evade accumulation runs. |
| 3 | `Busted` | Terminal capture. |
| 4 | `Evaded` | Terminal escape. |

`AIPursuit::OnTask` is a 4,415-byte 95.30769% near match at `0x00443ba0`.
Implement its contract as source-time state, not Unity wall time:

- At creation, select min/max heat from race/career context, initialize
  `InitialChase`, and retain the source's final-epic heat 6 branch.
- Snapshot `FullEngagementCopCount`, `NumCopsToTriggerBackup`, and
  `evadetimeout` when the pursuit locks attributes.
- Outside cooldown, advance heat by `dT / TimePerHeatLevel` and clamp it to the
  selected bounds.
- Treat a unit's target observation as recent while its seen age is below 7
  source-time units. Enter the cooldown path only after that visibility grace
  and the 2.5-unit meter-mode transition.
- Add hidden-zone credit as `dT * HiddenZoneTimeMultiplier`; calculate evade
  progress as `(timeSinceSeen + hiddenZoneCredit) / evadetimeout`.
- On reacquisition, multiply displayed evade progress by `0.93` per lifecycle
  update until the cooldown transition completes.
- Permit bust charge only when target speed is below the active `BustSpeed` and
  the nearest pursuing cop is within 15 horizontal world units. Charge by
  `dT * 0.25`, decay by `dT * 0.5`, clamp at zero, and transition after the
  accumulated value exceeds 5.
- Preserve the source's final-epic visibility override as a separate encounter
  rule.

`SimTask::Run` at `0x006ecd90` is only 92.242424% matched and is explicitly
marked `UNSOLVED` in the current source. The pursuit constructs a variable-rate
0.25 simulation task and a variable-rate 1.0 bust task, but those values must
not be documented as seconds until scheduler behavior is solved or measured.
Tests should advance source-task deltas explicitly.

### Formation identifiers

| Value | Formation |
|---:|---|
| 1 | PIT |
| 2 | Box in |
| 3 | Rolling block |
| 4 | Follow |
| 5 | Helicopter pursuit |
| 6 | Herd |
| 7 | Stagger follow |

Formation selection alternates stagger-follow opportunities with weighted
choices and applies a full-engagement cap. `UpdateFormation` is only 87.33038%
matched, so preserve the identifiers and scheduling boundary now; calibrate
slot geometry and selection weights before shipping.

## Admission, visibility, and unit goals

Patrol admission is separated from pursuit lifecycle:

- `WatchForPerps` is exact at `0x0042f570` and inspects the player plus eligible
  racer vehicles.
- A target must pass that unit's visibility test.
- `CheckForPursuit` is 96.60564% matched at `0x00429ef0`. It suppresses a fresh
  pursuit only when all four are true: no active 911 call, no traffic hit in
  the preceding one source-time unit, heat at most 3, and speed below 65 mph.
- Ground-car LOS uses attribute front/rear ranges with 150/50 fallbacks, a rear
  cone gate, pursuit-meter state, and a world ray from a +0.5 vertical offset.
  `CanSeeTarget` is only 80.46544% matched, so geometry belongs in
  `IPursuitWorldQuery` until oracle-tested.
- The exact `AIVehiclePursuit::Update` at `0x0042ac90` samples visibility after
  a 0.25 accumulator and records `-0.25` as the fresh seen age.

The exact unit goal endpoints are patrol, flee/helicopter exit, static
roadblock/helicopter roadblock, pursuit/helicopter pursuit, and support. Port
those transitions before adding detailed tactics. The unit owns its formation
offset, seen age, engagement radius, siren state, and breaker/chicken/damaged
flags; the pursuit owns shared state.

## Requests, pools, and support

### Cop requests

`CopRequest` is 96.38318% matched at `0x0042ba50`:

- return no request after busted/bailed state or while the pursuit spawn timer
  is active;
- optionally force helicopter need during cooldown search;
- subtract the current contingent from desired `CopCountRecord` values;
- weighted-select one still-needed type while respecting helicopter delay.

Manager source defaults observed in the correlated snapshot are eight active
cars, one active helicopter, two patrol cars, a 150–400 world-unit spawn band,
a 60-unit lockout, and a fixed-rate 0.5 manager task. Reuse an inactive cache
entry first. An active unit is eligible for recycling only after more than five
offscreen source-time units and more than 100 world units from the view.

`SpawnPursuitCar` and `SpawnPursuitHelicopter` are exact. The regular named-car
placement path `SpawnPursuitIVehicle` is 0%, `AISpawnManager` is only 35.173794%
weighted, and `GetSpawnLocation` is 0%. Keep normal placement behind
`IPursuitSpawnPlanner`; do not substitute a convenient behind-player spawn and
call it recovered behavior.

### Roadblock requests

`RequestRoadBlock` is 85.048546% matched at `0x004194b0`. Its candidate
contract is:

- deny after busted/bailed state, while a roadblock exists, before the minimum
  support delay, or before the timer expires;
- reset its timer to a random value in `[8, 12)` source-time units;
- return the previous pending-request state (`4` or `0`) before updating the
  next probabilistic request;
- attenuate cooldown/search probability by target distance from the search
  radius.

### Ground support

`RequestGroundSupport` is 99.815285% matched at `0x00419680`. It checks only
while the target is visible, at 10 source-time-unit intervals, and after the
minimum delay. Leader support has priority; heavy and leader strategies then
use weighted selection, with Cross-specific behavior gated by Cross
availability.

Recovered heavy strategy sizes are 1 e-brake vehicle, 2 coordinated e-brake
vehicles, 2 head-on rammers, or 4 static-roadblock SUVs. Leader support is one
Cross unit or a three-car V with source offsets `(0,-20)`, `(-4,-24)`, and
`(4,-24)`. Heavy units are sourced ahead; leader units are sourced behind.
The RAM strategy applies a three-unit manager support delay. These placement
details stay candidate until the unmatched spawn path is closed.

## Roadblocks and spike strips

`CreateRoadBlock` is a 2,619-byte 95.77343% near match at `0x0043dd30`.
Preserve these high-confidence rules:

- choose a valid, non-decision traffic segment at least 40 world units long,
  approximately 250 units ahead of the target;
- map road-width index to minimum cops `{2,2,3,3,4,4,5}`;
- choose the best predefined layout that fits the width and available cars;
- clamp lateral layout scaling to `1..1.14`;
- suppress spike layouts during cooldown;
- cap a layout at six elements; the source contains 15 regular layouts plus a
  sentinel and 9 spike-capable layouts plus a sentinel;
- keep roadblock cars stationary with zero gas, full brake, zero steering, and
  zero nitrous.

Nearby roadblock cars engage inside 300 world units. Source structure marks a
stall/exploit after more than 20 task-delta units when the target remains
inside 300 units and a same-level roadblock cop is within 500 horizontal units;
cooldown marks it immediately. The direct distance method and static-roadblock
update lack report mappings, so this rule stays Tier C.

The exact spike subsystem is Tier A:

1. iterate wheels that are on the ground and not already punctured;
2. construct a swept wheel box with half-dimensions
   `(0.15, wheelRadius, wheelRadius)`;
3. sweep it by `wheelVelocity * dT` against strip collision geometry;
4. puncture the intersecting wheel once;
5. after the strip collision map is inactive, place a trigger and kill the
   owning strip simable.

When a pursuit is told a spike was hit, its 99.960785% near-match handler may
promote up to three nearest roadblock cops into the active pursuit if that
promotion has not already occurred.

## Helicopter, tactics, and damage

Tier A behavior:

- `SpawnPursuitHelicopter` is exact and enforces a singleton active helicopter.
- `AIVehicleHelicopter::UpdateFuel` is exact: configured fuel decreases to
  zero, then the unit enters its exit goal.
- `AIActionHeadOnRam::Update` is exact: target-directed steering at 100 kph.
- `AIActionJackKnife::Update` is exact: accelerate through setup, then apply
  handbrake/full steering with optional trailer detach.
- `DamageCopCar::OnImpact` is exact: movable scenery below mass 2000 is filtered
  from normal impact damage; heavier/immobile scenery and non-scenery impacts
  delegate to base behavior.

Candidate helicopter behavior includes hidden-from-helicopter state, a 75-unit
close visual sphere, a configured forward range with a 250 fallback,
tunnel/world occlusion, and 20-unit last-seen tolerance at close range. Its LOS
is 82.481674% matched. The pursuit action's straight/search/skid-hit modes are
81.7875%; exit flight is 93.360214%. Keep flight commands replaceable and do
not tune Unity forces directly from these partial functions.

## Pursuit statistics and terminal flow

`CalcTotalCostToState` is a 99.583336% near match. The recovered calculation is:

```text
destroyed cops        * 5000
helicopters spawned   * 2000
roadblocks deployed   *  500
damaged cops          *  250
traffic cars hit      *  500
spike strips deployed *  250
heli strips deployed  *  225
cop cars deployed     *  250
support vehicles      *  450
+ property damage value
```

On pursuit end, busted resets the pursuit streak and reports failure; evaded
commits evasion telemetry. The manager then commits career/pursuit data and
infractions and sends involved cops to flee. Teardown is partial-confidence,
and all three reported `GInfractionManager` functions are unmatched with an
empty current `.cpp`, so offense semantics must remain outside the fidelity
claim.

The existing Unity model's fine settlement and durable outcome behavior is a
separate product contract. Do not infer that wallet/fine policy from the PC
binary, and do not let a Most Wanted pursuit port bypass its persistence
guarantees. Adapt a terminal `Busted`/`Evaded` result into that boundary only
after the pursuit aggregate has finalized exactly once.

## Verification matrix

| Area | Required deterministic tests | Oracle gate before retail claim |
|---|---|---|
| Manager order | One tick records the six exact phases in order. | Trace the manager task during a controlled pursuit. |
| Lifecycle | Heat clamp, backup threshold, seven-unit sight memory, cooldown transition, hidden-zone credit, reacquisition decay, bust charge/decay, both terminal states. | Calibrate source task deltas against wall time. |
| Admission/LOS | All four admission predicates independently; visible/hidden transitions. | Capture front/rear/raycast boundary cases. |
| Requests | Contingent subtraction, helicopter delay, one-request roadblock lag, support priority/weights. | Seed/capture request timing and weighted outcomes. |
| Pool/spawn | Inactive reuse and recycle gates. | Recover or capture named-car placement and `AISpawnManager`. |
| Roadblocks | Width table, layout fit, spike cooldown suppression, pass/dodge/release. | Map distance/static-update hooks and capture layout selection. |
| Spikes | Grounded/already-punctured filters, swept intersection, one-time puncture, cleanup. | Focused comparison is desirable, but machine-code recovery is exact. |
| Helicopter | Fuel-to-exit transition and mode interface. | Trace LOS, circular search, skid strike, and exit geometry. |
| Results | Exactly-once terminal event and cost formula fixtures. | Recover infraction accumulation and teardown edge cases. |

## Port phases

1. Freeze the ledger and source hashes used by each change.
2. Build a pure C# state model for Tier A ownership/order, unit goals, spike
   behavior, and exact tactic/damage rules.
3. Add Tier B lifecycle/admission/request rules with source-delta tests and
   explicit confidence metadata.
4. Connect Unity road, LOS, vehicle intent, pooling, and persistence adapters;
   do not change physics ownership.
5. Capture controlled PC oracle scenarios and promote individual Tier B/C
   behaviors only when their traces agree.
6. Solve the Tier D scheduler, spawn, infractions, and missing hooks before any
   “full retail-equivalent cops and pursuit” claim.

The present evidence completes the reverse-engineered behavioral contract. It
does not complete phases 2–6 or authorize replacing the current Unity police
runtime without a separate implementation decision.
