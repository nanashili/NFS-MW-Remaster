# Advanced capture review adjudication

Status: static implementation complete; independent convergence review in
progress. This is not a claim of live hook reachability or gameplay stability.

The frozen raw proposal remains in `ADVANCED_CAPTURE_PROPOSAL.md`. This document
is the controlling plan for schema 3.

## Decision

Implement schema 3 as a profile-preset, **entry-only** tracer. Keep all 48
binary-grounded catalogue entries available, but patch only the selected
profile defaults as modified by explicit overrides. Preserve
x87/MMX/XMM/MXCSR state around every probe with
`FXSAVE`/`FXRSTOR`, preserve integer registers and flags, read only declared
arguments, and use an ordered bounded FIFO with explicit terminal integrity.

Do not overwrite return addresses in schema 3. Result/exit capture is deferred
until a separately reviewed mechanism can prove safe behavior across SEH,
nonlocal unwinds, recursion, and thread teardown. Schema 1/2 decoding remains
available for old captures, but its implicit exit pairing is not reused.

## Revised implementation contract

1. `hook_catalogue.json` is the checked-in authority for hook ids, preferred
   VAs, exact stolen bytes, instruction lengths, supported relocation, argument
   counts, `this` adjustments, profiles, divisors, and bounded raw regions.
   Generated C metadata and Python validation must fail if they drift.
2. The fixed 16 KiB header contains a 1 KiB core and 48 resolved 320-byte
   manifest entries. It records target/config/catalogue/manifest hashes, exact
   target identity, capture id, profile, every resolved divisor, relocation and
   region policy, queue/read budgets, and activation state.
3. A fixed 1,024-byte record contains a 256-byte metadata envelope and at most
   three 256-byte catalogue-defined raw regions. It records file sequence,
   per-hook call and sample indices, exact argument validity, region addresses
   and 16-byte validity masks, and monotonic integrity counters.
4. Producers reserve the data-record budget before reading. They build records
   locally, then assign sequence and append under one short critical section.
   The single writer pops and writes strictly in FIFO order. Queue loss, budget
   rejection, read faults/budget exhaustion, write failures, and high-water
   state are distinct counters.
5. A persistent write/short-write failure stops admission and later records. A
   failed record is never silently cleared. Checkpoints are ordered through the
   same queue; a normal limit/stop path emits and flushes a terminal record. A
   missing terminal record always means an incomplete prefix.
6. Argument collection uses one bounded `ReadProcessMemory` request for the
   return word plus the catalogue's declared arguments, with bounded word
   fallback only after failure. Raw region reads use a whole-range fast path and
   bounded `VirtualQuery`/span fallback. A per-event call budget prevents an
   address fault from expanding into unbounded kernel transitions.
7. Stubs are allocated before patching. The validated Ultimate ASI Loader calls
   `InitializeASI` synchronously with deferred plugin loading. That callback
   pins the ASI and waits while a worker repeatedly enumerates/suspends all
   other process threads to a stable set, checks them clear of stolen spans,
   and revalidates anchors. Every enabled entry VA is eight-byte aligned and its
   five-byte jump plus three unchanged trailing bytes is committed with one
   aligned `InterlockedCompareExchange64`. A process-wide instruction-cache
   flush completes before threads resume and the callback returns. Atomic
   replacement proves memory-word integrity; instruction-fetch safety depends
   on the startup gate, quiescence, and flush together. Rollback is allowed only
   before any suspended thread may have resumed. Any stub whose jump was ever
   reachable remains pinned. Activated stubs deliberately remain installed in
   an inactive pass-through state until process exit; ordinary stop/detach is
   not an in-process uninstall.
8. Decoder schema 3 analysis uses manifest hook ids and per-hook call/sample
   indices. It checks physical file order before analysis, never sorts away an
   ordering defect, validates manifest hashes and bounds, reports reached and
   unreached enabled hooks, and streams JSONL without materializing raw traces.

## Primary issue dispositions

| issue | disposition |
|---|---|
| P0-01 zero return target | Accepted by removal: schema 3 never replaces a return address and has no shared exit stub. |
| P0-02 stale frames after unwind | Accepted by removal: schema 3 has no TLS exit frame. Exit capture requires a separate design/review. |
| P0-03 relative stolen instructions | Accepted: hooks 44/45 declare the only supported `CALL rel32`, its offset, and exact preferred destination; validator/tests recompute it and runtime rebases it against the actual loaded image. |
| P0-04 wrong `this` adjustment | Accepted: signed adjustment/static status is catalogue metadata and is emitted in the manifest; overflow fails the region source. |
| P0-05 concurrent multi-byte patch | Accepted: all catalogue VAs are eight-byte aligned; one `InterlockedCompareExchange64` changes the complete jump word while preserving bytes 5..7. This prevents a memory-level partial word but does not alone serialize instruction fetch. The validated synchronous loader callback, stable suspension of other threads, process-wide `FlushInstructionCache`, then resume/release order provide the full installation contract. External remote-thread injection during this bounded transaction is outside scope. |
| P1-01 physical ordering | Accepted: sequence is assigned while appending to one FIFO; decoder diagnoses physical order before any optional aggregation. |
| P1-02 silent write loss | Accepted: writer retains the current record for bounded retry, truncates a short tail before retry where possible, stops on persistent failure, and reports status/counters. |
| P1-03 limit tears pairs | Accepted by redesign: there are no pairs. Admission consumes a data budget under the queue lock; terminal/control records have separate reserved handling. |
| P1-04 unsafe eight-word read | Accepted: only return word plus declared argument count is read through bounded safe reads; validity is explicit. |
| P1-05 partial read becomes zero | Accepted with a strict budget: whole-span read first, then only bounded readable-span fallback, with validity and counters. |
| P1-06 not self-describing | Accepted: resolved 48-entry manifest plus target/config/catalogue/manifest hashes is embedded. |
| P1-07 implicit LIFO pairing | Accepted by removal: schema 3 is entry-only. Call/sample indices identify observed invocations without inferred pairs. |
| P1-08 perturbing full-rate capture | Accepted: profiles and conservative divisors are authoritative. `all` is a low-rate survey, not a full-rate claim. |
| P1-09 source offsets treated as retail | Accepted: new regions are raw offset-labelled observations; no new typed overlay is claimed. |
| P1-10 nine hooks miss major systems | Accepted: the catalogue covers 48 racer, traffic, pursuit, helicopter, target, and GPS boundaries. Reachability is reported separately. |
| P1-11 incomplete catalogue validation | Accepted: offline validation checks all 48 exact anchors and structural/relocation metadata, not only enabled hooks. |
| P1-12 undeclared return ABI | Accepted by removal: schema 3 does not observe returns. Return kind remains provenance only. |
| P1-13 detach cannot safely finalize | Accepted: module stays pinned; detach only stops admission/wakes the worker. Missing terminal is explicitly incomplete. |
| P2-01 weak loss statement | Accepted: terminal/checkpoint snapshots separate admitted, enqueued, queue loss, budget rejection, read fault, read-budget, write failure, written, and high-water values. |
| P2-02 pre-claim sequence gaps | Accepted: sequence exists only after successful FIFO append and is unsigned 64-bit on disk. |
| P2-03 unvalidated action pointer/name | Accepted: schema 3 stores only catalogue-defined raw regions and validity; it assigns no action name from an untrusted pointer. |
| P2-04 unstated memory cost | Accepted: 1,024-byte records and a fixed 2,048-record queue use 2 MiB of event storage; values are in the header. |
| P2-05 undeclared flush tradeoff | Accepted: configured flush interval and checkpoint interval are embedded; terminal is force-flushed; abrupt termination may lose a tail. |
| P2-06 no capture identity | Accepted: capture id, UTC file time, QPC start/frequency, PID, and all input hashes are embedded. |
| P2-07 profile surprises | Accepted: unknown/empty profiles and malformed/range-invalid overrides fail before patching; manifest/log list resolved hooks. |
| P2-08 static hook source model | Accepted: source selectors are explicit (`none`, normalized/raw `this`, argument pointer, absolute address); no implicit chains. |
| P2-09 snapshot disclosure/overflow | Accepted: source/offset/length is catalogue-only, arithmetic checked, each region capped at 256 bytes, and only three regions exist. |
| P2-10 decoder materializes raw trace | Accepted for raw export: JSONL/CSV iterate records. Summary retains bounded metadata and omits raw bytes unless requested. |
| P2-11 activation ambiguity | Accepted: header/status transition through validated, patching, active, stopping, closed/failed states. |
| P2-12 missing deterministic tests | Accepted: fixtures cover manifest corruption, truncation, order, counters, indices, catalogue relocation, profile resolution, budget/queue models, and schema 1/2 compatibility. Live safety stays a separate gate. |
| P3-01 duplicated metadata | Accepted: checked-in JSON is authoritative; generated/runtime/validator views are compared mechanically. |
| P3-02 `Aquire` spelling drift | Accepted: retail spelling remains exact in catalogue and manifest names. |

## Independent-review additions

| finding | disposition |
|---|---|
| x87/SIMD state was not preserved | Accepted as a new P0. Every entry stub uses aligned `FXSAVE`/`FXRSTOR`; activation fails when the required processor feature is unavailable. |
| sampling bounds assume the first sample survived | Accepted as P1. Every event stores its actual hook call index and deterministic sample index; gaps are observed rather than inferred away. |
| 48-hook metadata lacked exact implementation facts | Accepted. The catalogue includes exact bytes, instruction lengths, argument count, adjustment, regions, and relocation target for every hook. |
| one global lock may perturb hot paths | Partly accepted. Schema 3 keeps the simplest provable FIFO, uses one short append-only critical section, and relies on conservative divisors. Per-thread buffers are deferred until measurements justify complexity. |
| four regions/1,536 bytes were excessive | Accepted. The record is 1,024 bytes with at most three selected regions; unused regions remain zero and do not trigger reads. |
| sparse fallback was unbounded | Accepted. Fast path plus a hard per-event read-call budget; budget exhaustion is recorded. |
| profile semantics were duplicated/unclear | Accepted. One catalogue drives profile masks/defaults; the raw INI only selects a profile and optional explicit overrides. |
| terminal kind was unspecified | Accepted. Event kinds are `entry`, `checkpoint`, and `terminal`; hook id zero is required for controls and forbidden for entries. |
| ASI validator's digest condition was ineffective | Accepted as a concrete bug. Identity-string and forbidden-ASCII-digest checks become independent failures. |
| broad regions could be misreported as semantic coverage | Accepted. Reports distinguish catalogue coverage, enabled hook reachability, raw bytes, source recovery class, and typed semantics; only the first two are schema-3 static/runtime facts. |
| `FXRSTOR` was alleged to use `FXSAVE-4` | Rejected after byte-level stack proof. From aligned `A`, two argument pushes reach `A-8`; `CALL` temporarily reaches `A-12`, its return restores `A-8`, and `add esp,8` restores exactly `A` before `FXRSTOR [esp]`. The native contract models this sequence and checks the emitted operands. |
| a thread created after enumeration could see a torn five-byte patch | Accepted as a P1 against the prior byte-copy implementation. Hook installation and rollback now replace one aligned eight-byte word atomically and preserve its trailing bytes; the ASI validator requires the generated `lock cmpxchg8b` sites. |
| rollback could free a stub reached by a late thread | Accepted as a P0 lifetime counterexample. A successful atomic commit permanently marks the stub reachable; rollback may restore target bytes but never frees that stub before process exit. |
| relocated stolen calls used preferred rather than loaded addresses | Accepted as a P1. Runtime translates the catalogue destination from preferred-image VA to actual-image VA; the shared native contract tests rebasing. |
| thread resume failures were ignored | Accepted as a P1. Resume releases successful threads on each pass but retains every failed live-thread handle and retries; a signaled thread handle proves termination. The startup transaction cannot complete while a tracer-owned suspend count remains unresolved. Rollback is performed only before this release phase. |
| one `ResumeThread` call fails to preserve a pre-existing suspend depth | Rejected. `SuspendThread` adds exactly one tracer-owned count; one successful `ResumeThread` removes exactly that count. Driving the count to zero would incorrectly resume a thread another subsystem intentionally suspended. |
| successful stop or activation failure leaves hooks installed | Accepted as intentional lifecycle, not an uninstall defect. Repatching live code during teardown would add another mutation transaction. The pinned inactive stub restores flags, replays the relocated prologue, and continues; documentation and native tests make this process-lifetime contract explicit. |
| process detach cannot restore hooks or promise a terminal record | Accepted and explicit. `DLL_PROCESS_DETACH` only closes admission and wakes the worker; the module is pinned, hooks remain inert, and a missing terminal record is an incomplete prefix. Waiting or restoring from loader lock is prohibited. |
| decimal INI parsing could wrap before its `uint32` range check | Accepted as a P2 fail-closed defect. Parsing now checks `value > (UINT32_MAX - digit) / 10` before every multiply/add. The native contract rejects both `UINT32_MAX+1` and a decimal equal to `2^64+1000`. |
| decoder accepted an out-of-image `CALL rel32` target when no local catalogue matched | Accepted as a P2 parity defect. Generic schema-3 manifest decoding now requires every relocation target inside the exact target image, matching runtime validation. It also enforces eight-byte hook alignment/span and the fixed schema-3 profile id/name set. Corrupted non-local-catalogue fixtures cover both paths. |
| resuming once leaves an already-suspended thread suspended | Rejected. The pre-existing suspend depth belongs to another subsystem. The tracer adds exactly one count with `SuspendThread` and removes exactly that count with one successful `ResumeThread`; reducing the count to zero would corrupt external state. |
| legacy decoder accepted negative, over-limit, and non-decimal sample divisors | Accepted as a P2 runtime/decoder parity defect. Legacy config loading now accepts only ASCII decimal digits in the runtime range `0..1000000000`; boundary and syntax fixtures cover zero, the maximum, both adjacent invalid values, a sign, and an underscore. |
| a full numeric INI read buffer could be a silently truncated prefix | Accepted as a P2 fail-closed defect. Runtime numeric reads now reject the API's full-buffer sentinel before parsing, including hook overrides, so a long leading-zero prefix cannot become an unintended divisor. |
| path construction ignored truncation and append failures | Accepted as a P2 fail-closed defect. Module-path truncation, every derived-path append, capture-directory creation, and an existing non-directory collision now abort before hashing or patching. |
| a recovered terminal-record write retry hid its transient failure count | Accepted as a P2 integrity defect. The terminal counter snapshot is refreshed before every bounded write attempt, so a retry that succeeds still seals the accumulated write-failure count and the decoder will not certify the capture as structurally complete. |
| hook installation was not instruction-fetch safe | Accepted as a P1. An aligned interlocked write is not treated as instruction-stream synchronization. `DllMain` is minimal; supported Ultimate ASI Loader invokes exported `InitializeASI` synchronously after loading. The callback pins the module and waits for stable thread quiescence, patching, a process-wide instruction-cache flush, and resume before returning. |
| a detached `DllMain` worker could race module unload | Accepted as a P1. `DllMain` no longer creates a worker. `InitializeASI` pins the module before thread creation and does not return until startup has reached active or failed, so no pre-pin worker exists. |
| loader configuration could invoke the synchronous callback from loader `DllMain` | Accepted as a parent P1 counterexample. The exact installer/validator now evaluates Ultimate ASI Loader's ordered `dinput8.ini`/`global.ini` settings, requires plugin loading, and rejects the direct-from-`DllMain` mode. Deferred loading is part of the supported runtime contract. |
| loader identity strings did not prove the audited callback control flow | Accepted as a parent P1 counterexample. The pre-launch validator now requires the exact audited Ultimate ASI Loader SHA-256 in addition to its structural identity and effective settings. A changed loader requires a new control-flow audit before its digest can replace the pinned value; manual ASI copying outside the validated launcher is unsupported. |
| a partial post-commit resume failure led to live rollback | Accepted as a parent P1 counterexample. Release is now a non-abandoning phase: successful threads are released, failed live-thread handles remain owned and are retried, and the function cannot return a partial-failure result that triggers live rollback. |
| one global stop marker could target the wrong run or lose an early request | Accepted as a P2. Lifecycle lines and request files are capture-name scoped. The stop script reconstructs per-run state, selects the newest open run, and an early request persists through startup until accepted or explicitly cleaned on terminal failure. |
| explicit legacy schema expectation could be bypassed by a 1,024-byte record | Accepted as a P2. Record length is only a schema-3 heuristic when no expected schema was supplied; explicit schema 1/2 now requires the legacy record size and marker. |
| profile overrides violate profile scope | Rejected as an implementation defect. The controlling proposal explicitly defines a profile as a preset plus per-hook overrides. Documentation now says a nonzero override can expand the preset and the embedded manifest is authoritative. |
| fallback stack-word addresses could wrap at `UINT32_MAX` | Accepted as a P2. The complete stack span is checked in 64-bit arithmetic before the bulk or word fallback, and each word is bounded against that exclusive end. |
| raw-region capture rejected a valid span ending at `UINT32_MAX` | Accepted as a P3. Shared address math represents the exclusive end as 64-bit `2^32`; native boundary tests cover the final byte, exact-top span, and one-byte overflow. |
| Windows CRLF status lines could defeat stop-script run selection | Accepted as a parent P1 operational counterexample. Both state parsers now remove the trailing carriage return before exact capture-name matching, terminal output uses exact AWK fields rather than a regex, and the shell fixture writes real CRLF lifecycle lines. |
| bounded `ResumeThread` failure handling could abandon a live suspended thread | Accepted as P1. Microsoft documents that only a successful `ResumeThread` decrements the suspend count and that closing a thread handle does not terminate the thread. The worker now retains the handle and retries until its tracer-owned count is released or `WaitForSingleObject(handle, 0)` proves termination. This also avoids the documented `STILL_ACTIVE`/exit-code ambiguity in the prior `GetExitCodeThread` check. A persistent kernel/API failure can now stall the guarded startup transaction, but cannot be converted into an apparently completed startup with an abandoned tracer suspend count. |
| arbitrary 32-bit stub addresses were alleged to exceed signed `rel32` reach | Rejected for this PE32 process. Intel defines `rel32` as a 32-bit target operand in legacy mode; EIP addition is 32-bit modulo arithmetic, so every 32-bit source/destination pair has an encodable displacement. The shared native contract now proves both wrap directions instead of applying the x86-64 +/-2 GiB restriction to 32-bit mode. |
| runtime trusted declared relocation targets without recomputing stolen `CALL rel32` bytes | Accepted as P1 defense in depth. Runtime definition validation now requires exact preferred-VA/RVA identity, instruction-boundary coverage of every relative opcode, only the declared five-byte `CALL rel32`, and a recomputed modulo-32-bit destination equal to the declared in-image target. |
| structurally valid records could conceal accidental payload corruption | Accepted as P2. Schema 3 now stores a full SHA-256 over the mutable 1 KiB header core and a 128-bit-truncated SHA-256 over every complete 1,024-byte record. Hashing occurs on the writer thread after final counter refresh, not on hook producers. Decoder fixtures flip header and captured-region bytes and reject both. Documentation explicitly limits these unkeyed hashes to corruption detection, not authenticity. |
| initial lifecycle status persistence failure was ignored | Accepted as parent P2. The first `tracer-starting` line is the stop-control discovery anchor. Status writes now require a complete write and flush, and startup aborts before hashing or patching if that first durable line cannot be persisted. Later status loss remains diagnostic because the scoped starting line and binary header/terminal state remain authoritative. |
| the pinned loader's configuration order and callback timing lacked exact-version provenance | Closed by exact primary evidence, not by inference from identity strings. The pinned `dinput8.dll` is byte-identical to the official Ultimate ASI Loader v9.7.1 release asset. The v9.7.1 tag at commit `2155f2177733d673a3eb783141ceedd564a0a0e2` defines, in order, `dinput8.ini`, root `global.ini`, `scripts/global.ini`, `plugins/global.ini`, and `update/global.ini`; folds later integer values over earlier ones; defaults `dontloadfromdllmain=1`, `forceentrypointhook=0`, and `loadplugins=1`; defers through the IAT path when either deferred flag is nonzero; and invokes `InitializeASI` synchronously after a successful plugin load. All five files are absent in the current scoped game root, so those defaults are the effective current contract. |
| installation could validate and copy a stale `dist` binary after source changes | Accepted as a P2 operational provenance defect. `install.sh` now runs the complete fail-fast build before installed-target validation or copying. That gate checks generated-catalogue drift, native and stop-control contracts, compiles `tracer.c` as PE32, and validates the resulting ASI, so launch/install cannot silently reuse an earlier build. |
| clean-review timeout allegation left suspended threads | Rejected after direct control-flow proof. Every failed quiescence attempt, including the last, calls the non-abandoning resume phase and zeroes the retained collection before the timeout branch can execute. If a live-thread resume cannot complete, the resume phase itself cannot return and the timeout branch is unreachable. |
| live CrossOver ABI, fault injection, overhead, and hook reachability remain unexecuted for schema 3 | Accepted as the explicit controlled-runtime gate, not a static implementation finding. The PE32 build, shared native stub/patch contract, exact loader/target checks, and static review close what can be proven offline; forced-failure and gameplay behavior require the later owner-controlled profile runs and cannot honestly be promoted to static proof. |

## Review convergence

The first post-implementation fresh review produced the stack allegation and
late-thread/lifecycle findings above. Its actionable safety findings were
implemented or made explicit; the stack allegation was rejected with a
deterministic instruction/stack proof. Later fresh rounds found and closed the
decimal-parser overflow, decoder/runtime relocation-range mismatch, legacy
divisor-parity defect, instruction-fetch/startup lifecycle gap, run-global stop
race, explicit-schema bypass, stack-address wrap, and exact-top region boundary.
Parent counterexample review then closed the deferred-loader precondition and
post-commit partial-resume rollback race, made CRLF status parsing explicit,
and pinned the startup contract to the exact loader binary whose callback flow
was audited. The next blind round led to exact runtime relocation recomputation,
non-abandoning resume recovery, durable initial status, and schema-3
header/record corruption detection. A subsequent clean pair found no confirmed
P0/P1/P2 implementation defect, but its stale-artifact challenge produced the
mandatory pre-install rebuild gate above. Because that was an operational
implementation change, the consecutive clean-round count was reset to zero and
fresh review restarted from that revision. Fresh round A then produced no confirmed
P0/P1/P2 in either independent review after one alleged post-commit rollback
defect was withdrawn against the explicit process-lifetime inert-stub contract.
Fresh round B also produced no confirmed P0/P1/P2. Its alleged timeout/handle
leaks were withdrawn after following the non-abandoning resume helper, which
both resumes and closes every retained handle before returning. Its remaining
bounded-resume challenge was withdrawn because no supported finite,
non-destructive path can remove exactly the tracer-owned suspend count after a
persistent `ResumeThread` failure while preserving pre-existing suspend depth.
The consecutive clean-round count is two. The final independent
first-principles attack then found no confirmed P0/P1/P2 defect after tracing
installation/rollback, stub ABI preservation, queue drain and terminal
sealing, bounded reads, non-abandoning resume recovery, decoder certification,
and build/install provenance end to end. Static review convergence is complete
for this revision.

The required two consecutive fresh independent clean-review pairs and final
first-principles independent attack are complete. These review gates assess
the static tracer only; controlled live profile runs remain a separate
requirement before stability, reachability, overhead, or captured schema-3
values can be claimed.

## Gates after implementation

- Static: catalogue generation/check, exact target/anchor validation, PE32 build,
  schema 1/2/3 decoder tests, queue/budget model tests, and focused review.
- Controlled runtime: one profile at a time, crash/behavior observation,
  reachability, queue/read loss, file terminal integrity, and measured overhead.
- Evidence completion: only after controlled scenarios reach each selected hook
  can the AI contract be updated with observed values. Static availability alone
  is never described as “captured.”
