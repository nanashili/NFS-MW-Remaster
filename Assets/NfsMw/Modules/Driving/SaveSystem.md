# Hardened career persistence

## What is connected

Existing `JsonCareerProfileStorage` scene bindings now use `CareerSaveRepository`.
Manual saves, exclusive new-career creation, economy commits and safe-point
autosaves share validated immutable-generation storage. No scene rebuild is needed.
The economy engine still must not share ownership of a slot with legacy gameplay
writers; persistence hardening detects conflicts but does not perform that economy cutover.

`CareerProfileSystem` captures into detached data, updates its committed snapshot
only after storage succeeds, validates/migrates before restoring participants, and
returns detached profile snapshots. Observer failures cannot undo a committed save.
If runtime restoration fails, it attempts rollback and disables further saving until
a successful reload. The legacy participant interface cannot guarantee arbitrary
scene side effects are reversible; a rollback error is reported, never hidden.

## Editor / player use

- Existing **New Career / Resume** and safehouse save/load paths keep working.
- **Tools > Driving > Save Inspector** lists slots using bounded headers. It can
  verify full profiles, list corrupted and staged artifacts, copy a career into a
  new slot, and archive/restore slot directories. No whole scene needs to run.
- Archive is disabled in Play mode and never erases save bytes. Provide the protected
  active slot when using backend tooling. Legacy `.json`/`.bak` evidence is not
  implicitly moved; such slots deliberately refuse archive pending review.
- Select `CareerProfileSystem` in Play mode to inspect dirty/saved revisions,
  queue priority, failures and snapshot time, or request a safe-point autosave.
- Player-facing resume/load messages identify fallback to an older valid generation;
  detailed diagnostics remain in the inspector and `LastRecovery`.

## Storage protocol

Default root remains `Application.persistentDataPath/CareerProfiles`. A slot has a
case-safe hex-encoded `slot-*` directory containing immutable `.save` generations.
UTF-8 JSON is the **payload**, not the entire file. Each binary container contains:

1. eight-byte `NFSSAVE1` magic;
2. little-endian 32-bit header length (maximum 4096);
3. UTF-8 JSON header;
4. SHA-256 header digest (32 bytes);
5. UTF-8 JSON payload of exactly the declared length.

Header fields: container format, profile schema, slot, logical generation,
operation GUID, parent operation GUID, UTC ticks, display name, byte count and
payload SHA-256. Timestamps are display data, never conflict ordering authority.
No compression or encryption is enabled. Checksums are not anti-cheat protection.

Commit: validate -> exclusive repository lock -> compare expected head -> write
unique staging file -> flush -> reopen/check -> same-directory rename to unique
generation -> retention -> acknowledge. The rename is the **local process-crash
commit point**. Maintenance failures after publication produce warnings, not false
claims that nothing committed. Uncertain acknowledgement requires reload.

Three validated current-schema generations are retained by default (configurable
2–32). Corrupt files, old-schema generations and original legacy files are not
silently deleted. A pending file is never considered committed, even when complete.
Inspection exposes these files; analysis retention can consume disk and needs an
explicit support/cleanup policy. A 1024-generation scan ceiling fails closed.
There is no required mutable manifest: immutable headers rebuild the slot list.

Every payload is hashed from its actual bytes on read. A bounded 64-entry per-backend
cache avoids repeating domain validation for previously validated identical hashes.
No timestamp-only integrity cache is used. Input limits: 16 MiB payload, 64 JSON
levels, 100000 entries per collection, 65536 characters per string, 200 per content
ID. Limits reject rather than truncate player state. Slot IDs allow 1–64 letters,
numbers, hyphens and underscores; invalid names are rejected, not sanitized into
another player's slot.

## Recovery, versions and conflicts

- Container reader/writer: **1**. Profile reader: **1–3**, writer: **3**.
- Explicit `v1 -> v2` migration adds an empty economy journal; no reward, reputation
  or ownership is inferred. Migration writes a new generation and preserves its source.
- Explicit `v2 -> v3` adds an empty mission section. No historical mission payouts
  are inferred. The mission framework persists instances, checkpoints and claim
  receipts in the same aggregate as cash; see `MissionFramework.md`.
- Unknown newer container/profile/economy versions block loading and overwriting.
- Missing critical sections, invalid balances, duplicate IDs/properties, broken
  economy balance chains, receipt-total mismatches and malformed containers fail validation.
- Corrupt latest data falls back to the newest fully valid older generation. All
  invalid evidence stays intact. If no candidate validates, load fails: no silent new game.
- `RecoverGeneration` explicitly restores retained logical state by making a new
  commit, not by rewinding/deleting the current file.
- An existing slot must be loaded before a new adapter can overwrite it. Expected-head
  comparisons include retained file content; external changes cause a conflict.
- Concurrent physical operations contend on a shared exclusive lock. A busy storage
  operation fails/retries rather than interleaving writes. Divergent same-generation
  or adjacent parent histories are rejected. This is **not a complete cloud merger**;
  gaps in remote history require a platform conflict adapter, not timestamp selection.

## Autosave and lifecycle

`FreeRoamSession` configures the safe point: driving or inside a location, with no
active pursuit. Event/pursuit saving remains disabled. At safe points the host
checks for changed authoritative snapshots every five seconds. Debounce is two
seconds, minimum interval five seconds, maximum dirty duration thirty seconds;
unsafe gameplay may defer longer. Failed writes retain dirty state and retry with
backoff; stale/uncertain sessions stop autosaving until reload.

Modules can call `MarkPersistentStateChanged()` or
`RequestSave(CareerSaveReason.Checkpoint/Manual/CriticalProgression/Suspend)`.
Requests coalesce and the highest queued priority wins. Main-thread capture produces
a detached string; a single worker validates, hashes and writes it. Mutations marked
during an in-flight operation retain a newer dirty revision after it completes.
Unmarked mutations are detected by the next safe-point snapshot comparison.

`ProfileSaved` means committed; `RequestSave` only means queued. The synchronous
`TrySave` contract still means completed success/failure and rejects while an async
operation is in flight. Load and slot/storage switching cannot race that operation.
Custom hosts must explicitly configure their safe-point predicate. Pause saving,
when enabled, queues an urgent safe-point request; quit saving is best effort and
is never the only durability strategy. Game-flow's existing lifecycle restrictions
remain in force. Account/cloud switching is not implemented by local alias changes.

## Verification and scope

EditMode tests exercise round trips, live snapshot isolation, malformed data,
migration, slot identity, corruption/truncation fuzzing, every injected write
checkpoint, stale writers, locks, asynchronous storage, recovery, copy/archive,
retention over 1000 commits and dirty-revision behavior.

`Tools/SaveFaultHarness` loads the **actual compiled driving assembly**, not a
separate model. It kills child .NET processes at each write checkpoint and checks
the committed state after restarting. Evidence is created only in a uniquely named
temporary directory and deliberately retained. Use `bench` for a 10000-vehicle,
100000-upgrade-ID fixture with changed state on each write. See its README.

Not shipping-qualified by these tests: true power/device loss, parent-directory
durability, native Windows/IL2CPP player behavior, console storage/certification,
platform user authentication, Steam/cloud synchronization and cloud conflict UI.
The desktop backend uses `FileStream.Flush(true)`; it does **not** claim a verified
hardware power-loss guarantee. Do not enable Auto-Cloud over the raw directory or
advertise console/cloud support without the corresponding adapter and tests.
Settings remain outside the career aggregate; no new settings subsystem was added.
Large-world incremental saves and ledger compaction require measured domain designs,
not arbitrary splitting of dependent wallet/inventory state.

Research, failure matrix and architectural decisions: `SaveSystemDesign.md`.

### Local validation measurements (2026-09-05)

- Unity 6000.6 EditMode: **156/156 passing**, including 1000 successive commits,
  corruption recovery, migration and write-stage fault injection. Final report:
  `/private/tmp/save-hardening-complete-check.xml` (temporary local evidence).
- **270 actual child-process kills passed** across all nine write checkpoints in
  52.26 seconds, using the final compiled storage assembly. Restart checks selected
  the expected old or newly committed state; staging files were never promoted.
  Evidence directory:
  `/var/folders/_4/k81y692d14z7fyycs1w_jn900000gn/T/nfs-save-kill-272d646be04f484fbcb86fc3197c0cc9`.
- The native desktop benchmark used changing state across 20 commits: approximately
  1.97 MB JSON, 135.90 ms mean / 215.72 ms maximum storage commit. Its total managed
  allocations across commits and verification loads were approximately 2.61 GB;
  this is cumulative allocation, not peak resident memory or a player-build result.
- Actual Unity detached snapshot plus formatted serialization of 10000 vehicles
  and 100000 upgrade IDs produced 4,400,250 bytes and took **108.10 ms** in the final
  Editor run. Earlier runs ranged from 100 to 115 ms. This main-thread operation
  exceeds a frame budget: large-profile incremental capture/compaction remains a
  release gate. Moving file I/O to a worker does not eliminate snapshot cost.

These fixtures are stress measurements on this Mac, not Windows qualification or
representative shipping-profile budgets. Profile real content in player builds
before setting maximum supported garage/journal sizes.
