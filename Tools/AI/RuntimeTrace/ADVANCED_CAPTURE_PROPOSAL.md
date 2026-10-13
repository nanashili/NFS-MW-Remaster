# Advanced AI runtime capture proposal

Status: frozen raw proposal retained for review provenance; superseded by
`ADVANCED_CAPTURE_ADJUDICATION.md`. It is not a claim of live coverage.

## Objective and evidence boundary

Advance the owner-operated NFSMW PC 1.3 tracer from the nine-hook schema-2
probe into a broad, auditable capture system for racer control, traffic,
pursuit/roadblocks, helicopter behavior, target tracking, and GPS. The tracer
must remain observation-only: it may redirect execution through verified
trampolines, but must not alter game arguments, return values, persistent game
files, or AI decisions.

“Capture everything” means that the catalogue covers the known runtime
decision boundaries and that a planned set of scenario runs can capture them
at useful resolution. It does not mean enabling every hot hook at divisor 1 in
one run. The capture itself must say exactly what it observed and what it did
not observe.

The only supported target is the existing PC 1.3 `speed.exe` with SHA-256
`80774c2e5d619b4f120b48d4462896fd504c263399d203a238769cffde1d253c`,
preferred image base `0x00400000`, and PE `SizeOfImage` `0x00678e4e`.
Every enabled entry point remains guarded by exact bytes from that target.

## System map

```text
exact speed.exe entry
  -> verified per-profile JMP
  -> generated entry trampoline
       save integer registers + flags
       bounded argument/object-region capture
       enqueue one immutable entry record
       optionally replace return address with shared exit trampoline
       replay relocated stolen instructions
  -> original function body
  -> shared exit trampoline (sampled calls only)
       save result registers + flags
       enqueue exit record with the same call id
       restore the real return address and machine state
  -> original caller

producer threads -> ordered bounded FIFO -> one writer thread -> capture.bin
                                                       -> status.log

capture.bin header
  = target identity + raw INI hash + resolved profile + exact hook manifest
capture.bin records
  = calls + raw regions + validity + monotonic loss/integrity counters
decoder
  = schema 1/2 compatibility + schema 3 manifest-driven decoding/reporting
```

The game owns all AI state and execution. The ASI owns only target validation,
trampolines, capture buffers, and output. The decoder owns interpretation and
must keep raw bytes distinct from source-derived semantic hypotheses.

## Required invariants

1. An anchor mismatch disables the whole requested capture before any hook is
   patched.
2. A probe restores integer registers, flags, caller stack, and the original
   return destination exactly. Functions with floating-point return values are
   excluded until floating-point state preservation has separate evidence.
3. The writer emits accepted records in global sequence order. Queue pressure,
   budget rejection, read faults, stale-frame pruning, and write failures are
   counted; none are silently presented as complete coverage.
4. A sampled entry that installs an exit return address reserves capacity for
   the exit first. Reaching the record budget stops new entries while already
   installed exits remain valid and drain.
5. Arguments are read only up to the catalogue's declared count. All arbitrary
   memory reads are bounded and report per-region validity.
6. Multiple-inheritance `this` adjustments are per hook and binary-grounded.
   A manifest records both the incoming `ECX` and the normalized base.
7. An entry/exit pair has an explicit global call id, parent call id, and
   per-thread depth. The decoder never infers identity only from LIFO order.
8. The capture is self-describing: a decoder does not need a guessed or later
   INI to know the resolved hooks, sampling, bytes, offsets, and profile.
9. The ASI remains pinned while hooks or saved return addresses can reach its
   code. It does not free executable stubs during ordinary process teardown.
10. Static validation, build success, synthetic decoder tests, and controlled
    gameplay evidence are reported separately.

## Schema 3 candidate

### Header

Use a fixed 16 KiB header. A core block records schema/record sizes, target and
configuration SHA-256 values, image/process/time identity, resolved profile,
budget, queue size, and flags. It is followed by a fixed-size manifest entry
for every catalogue hook. Each entry records:

- id, name, VA/RVA, source-recovery class, profile mask, and resolved enabled
  state;
- divisor, declared stack argument count, signed `this` adjustment, return
  kind, and capture flags;
- exact stolen bytes and any explicitly supported relative-instruction
  relocation;
- four capture-region specifications: source selector, signed offset,
  requested length, phases, and a conservative label.

The header stores the raw INI hash as provenance, while the resolved manifest
is authoritative for what actually ran.

### Records

Use fixed 1,536-byte records so files remain seekable and truncation can be
recognized. Entry and exit records contain:

- event kind, global sequence, thread id, QPC, hook address, call id, parent
  call id, and depth;
- incoming `ECX`, normalized object base, original return address, `EAX`,
  `EDX`, flags, declared arguments, and an argument-valid mask;
- four 256-byte sparse regions. Each carries the resolved source address,
  requested/captured sizes, source selector, specification index, and a
  16-bit validity mask for its sixteen 16-byte blocks;
- monotonically observed integrity counters and an optional bounded action or
  diagnostic label.

Checkpoint and terminal records use the same envelope with hook id zero and
explicit final counters. A missing terminal record means only that the file is
prefix-decodable, not that capture was complete.

### Safe sparse reads

First attempt one `ReadProcessMemory` call for each 256-byte region. If the
whole range is not readable, use `VirtualQuery` plus bounded 16-byte fallbacks
and set validity only for blocks actually read. This preserves the fast path
while handling page boundaries and partial objects without inventing zeros as
observed state.

### Ordered queue, budget, and exits

Build a record in producer-local storage. Under a short queue lock, assign the
sequence and append it to a FIFO; the writer removes records in the same order
and performs file I/O outside the lock. A write failure stops admission, is
reported to `status.log`, and leaves an explicit nonzero failure counter when
the capture file remains writable.

Reserve one record for entry-only sampling or two records for an entry/exit
pair before emitting the entry. When the data budget is exhausted, disable new
entries, continue accepting reserved exits, wait a bounded drain interval,
then emit a terminal record. Queue-full loss is still possible and is explicit.

Each TLS frame records its call ids, real return address, entry stack pointer,
arguments, and normalized object. On a later entry, frames proven stale by
stack growth are pruned and counted. A shared exit trampoline always recovers
the real return address from an installed frame before doing optional capture.

## Profiles and run set

Profiles choose which hooks are patched; per-hook INI values can then disable
or override divisors. Candidate profiles are:

- `survey`: low-rate high-level anchors used to confirm scenario activity;
- `core`: current goal, vehicle, spawn, and pursuit boundaries;
- `racer`: racer action and control-output pipeline;
- `traffic`: density/spawn/validation/traffic-action pipeline;
- `pursuit`: pursuit state, cop scheduling, formation, roadblocks, and terminal
  transitions;
- `helicopter`: helicopter state, LOS, steering/action, fuel/exit behavior;
- `navigation`: target lifecycle and GPS update/engagement;
- `all`: every catalogue hook with conservative divisors, intended for survey
  only, never described as full-rate capture.

A complete evidence collection is a matrix of profiles and controlled
scenarios (free roam, race, ordinary traffic, pursuit escalation, roadblock,
cooldown/escape/bust, helicopter search/strike/exit, GPS engage/replan), not a
single oversized trace.

## Candidate hook catalogue

All VAs and prologues below are from the exact installed target. `adjust`
normalizes incoming `ECX`; `static` means there is no object in `ECX`.
Recovery labels describe source/binary correspondence, not runtime safety.

| id | profile | anchor | VA | args | adjust | recovery / note |
|---:|---|---|---:|---:|---:|---|
| 1 | core | `AIVehicle::OnTaskSimulate` | `0x004081e0` | 1 | 0 | existing |
| 2 | core | `AIVehicle::OnTask` | `0x004082d0` | 2 | -8 | existing Activity subobject |
| 3 | core | `AIGoal::ChooseAction` | `0x0042b070` | 1 | 0 | existing |
| 4 | core | `AISpawnManager::CheckSpawnPosition` | `0x00419fc0` | 5 | 0 | existing |
| 5 | core | `AISpawnManager::GetSpawnLocation` | `0x0042fa40` | 3 | 0 | existing |
| 6 | pursuit | `AICopManager::SpawnPursuitIVehicle` | `0x00430e60` | 2 | 0 | existing |
| 7 | pursuit | `AIPursuit::UpdateFormation` | `0x00443300` | 1 | 0 | existing |
| 8 | traffic | `AITrafficManager::SpawnTraffic` | `0x00439a10` | 0 | 0 | existing |
| 9 | pursuit | `AIPursuit::OnTask` | `0x00443ba0` | 2 | -8 | existing Activity subobject |
| 10 | core | `AIVehicle::Update` | `0x004292a0` | 1 | 0 | partial source recovery |
| 11 | racer | `AIVehicleRacecar::Update` | `0x0042aa80` | 1 | 0 | exact |
| 12 | racer | `AIActionRace::Update` | `0x00428720` | 1 | 0 | partial; raw regions only |
| 13 | racer | `AIVehicle::OnGasBrake` | `0x00408430` | 1 | 0 | near |
| 14 | racer | `AIVehicle::OnDriving` | `0x00408690` | 1 | 0 | exact |
| 15 | racer | `AIVehicle::OnSteering` | `0x00415290` | 1 | 0 | partial |
| 16 | racer | `AIVehiclePid::OnGasBrake` | `0x0042a0b0` | 1 | 0 | partial |
| 17 | racer | `AIVehiclePid::OnSteering` | `0x0042a390` | 1 | 0 | partial |
| 18 | traffic | `AITrafficManager::OnTask` | `0x00439cd0` | 2 | -8 | exact Activity subobject |
| 19 | traffic | `AITrafficManager::Update` | `0x00439bb0` | 1 | 0 | exact; receives `dT` |
| 20 | traffic | `AITrafficManager::NextSpawn` | `0x00426230` | 0 | 0 | partial |
| 21 | traffic | `AITrafficManager::GetAvailableTrafficVehicle` | `0x00426380` | 2 | 0 | partial |
| 22 | traffic | `AITrafficManager::ValidateVehicle` | `0x00410b00` | 2 | 0 | near |
| 23 | traffic | `AIVehicleTraffic::StartDriving` | `0x00423190` | 1 | -0x73c | exact ITrafficAI subobject |
| 24 | traffic | `AIActionTraffic::Update` | `0x00411cf0` | 1 | 0 | near |
| 25 | traffic | `AIAvoidable::UpdateAllAvoidables` | `0x0043a810` | 1 | static | exact |
| 26 | pursuit | `AICopManager::OnTask` | `0x0043ef00` | 2 | -8 | exact Activity subobject |
| 27 | pursuit | `AICopManager::UpdateRoadBlocks` | `0x00439ee0` | 0 | 0 | partial |
| 28 | pursuit | `AIPursuit::RequestRoadBlock` | `0x004194b0` | 0 | -0x48 | IPursuit subobject |
| 29 | pursuit | `AICopManager::CreateRoadBlock` | `0x0043dd30` | 4 | 0 | near |
| 30 | pursuit | `AIRoadBlock::Construct` | `0x0043db80` | 4 | static | exact; 16-byte `Sim::Param` |
| 31 | pursuit | `AIVehiclePursuit::StartRoadBlock` | `0x00423430` | 0 | -0x70c | IPursuitAI subobject |
| 32 | pursuit | `AIVehiclePursuit::Update` | `0x0042ac90` | 1 | 0 | exact |
| 33 | pursuit | `AIPursuit::BailPursuit` | `0x004198e0` | 0 | -0x48 | near; IPursuit subobject |
| 34 | pursuit | `AICopManager::PursuitIsEvaded` | `0x00411070` | 1 | 0 | near |
| 35 | pursuit | `AICopManager::SpawnPursuitHelicopter` | `0x004269a0` | 1 | 0 | exact |
| 36 | helicopter | `AIVehicleHelicopter::Update` | `0x0042adb0` | 1 | 0 | partial |
| 37 | helicopter | `AIVehicleHelicopter::CanSeeTarget` | `0x004170d0` | 1 | -0x70c | exact IPursuitAI subobject |
| 38 | helicopter | `AIActionHeliPursuit::Update` | `0x004277e0` | 1 | 0 | partial |
| 39 | helicopter | `AIActionHeliExit::Update` | `0x004279f0` | 1 | 0 | partial |
| 40 | helicopter | `AIVehicleHelicopter::OnDriving` | `0x00417a20` | 1 | 0 | partial |
| 41 | navigation | `AITarget::TrackAll` | `0x004237f0` | 0 | static | exact |
| 42 | navigation | `AITarget::TrackInternal` | `0x004180f0` | 0 | 0 | exact |
| 43 | navigation | `AITarget::Aquire(ISimable*)` | `0x00423860` | 1 | 0 | exact |
| 44 | navigation | `AITarget::Aquire(vector)` | `0x004238b0` | 1 | 0 | exact; relocate stolen `CALL rel32` |
| 45 | navigation | `AITarget::Aquire(vector,vector)` | `0x00423900` | 2 | 0 | exact; relocate stolen `CALL rel32` |
| 46 | navigation | `AITarget::Clear` | `0x00409e60` | 0 | 0 | exact lifecycle proxy |
| 47 | navigation | `Gps::Update` | `0x0042c390` | 1 | 0 | near |
| 48 | navigation | `Gps::Engage` | `0x0042c6c0` | 2 | 0 | near |

The catalogue must be validated mechanically against exact PE bytes. The two
target-acquisition hooks with a relative call are either relocated explicitly
with a test of the computed destination or omitted; copied relative bytes are
never executed unchanged from a new trampoline address.

## Conservative region policy

Profiles select broad raw windows rather than pretending every source comment
is a verified retail layout:

- vehicle/racer/pursuit-vehicle: object head plus windows around control/goal
  state and subtype tail;
- action objects: head and action-specific tail;
- traffic/cop/pursuit managers: enough overlapping windows to cover their
  bounded object size, plus pointer arguments on decision hooks;
- helicopter: object head and windows around pursuit/heli subtype state;
- target/GPS: full small target objects and overlapping GPS windows;
- static hooks: argument pointees or global-manager pointers only when their
  derivation is explicit; otherwise registers/arguments with unused regions.

Decoder field names from schema 2 remain available only where already
binary-validated. New windows default to address/offset/raw bytes and may gain
typed overlays after live invariants corroborate them.

## Implementation and verification sequence

1. Freeze this proposal and the schema-2 baseline hashes.
2. Complete primary, independent red-team, and simplifier reviews; adjudicate
   every issue before editing runtime code.
3. Add schema-3 fixtures and decoder/validator tests before or alongside the
   runtime rewrite. Preserve schema-1/2 decoding.
4. Implement the manifest, profiles, safe reads, ordered queue, budgeted exits,
   integrity records, and exact relocation support.
5. Validate the exact target and every catalogue prologue, build the i386 ASI
   with warnings as errors, and run focused Python tests.
6. Run fresh adversarial reviews until two consecutive rounds produce no new
   unresolved P0/P1/P2 issue.
7. Only then install the still-disabled build. Perform controlled profile and
   scenario runs, measure overhead/loss, decode them, and update the evidence
   contract. Static completion is not live capture completion.

## Acceptance gates

- No enabled hook lacks an exact target-byte validator entry.
- Synthetic concurrency tests or an equivalent deterministic harness prove FIFO
  ordering, pairing, budget drain, and explicit loss behavior.
- Corrupt/truncated headers, manifests, regions, and records fail closed or are
  handled only under an explicit truncated-tail option.
- A schema-3 summary states profile, enabled hooks/divisors, terminal status,
  missing pairs, sequence gaps/order, loss/read/write/unwind counters, and raw
  target/config hashes.
- Controlled gameplay runs show no crash or changed return/argument behavior,
  bounded loss, and useful events for every selected profile.
- The final report distinguishes source recovery, hook reachability, observed
  values, inferred semantics, and unobserved gaps.
