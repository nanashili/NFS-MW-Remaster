# Primary review of the advanced AI capture proposal

Status: primary issue list frozen before independent review.

## Baseline

The reviewed schema-2 baseline is:

| artifact | SHA-256 |
|---|---|
| `tracer.c` | `411e06c8957adbcc721110cc53ce280a1ec7ae6385ec1cfe89f262164d6d3be4` |
| `decoder.py` | `2f453768e1538118548b3f6b7e99e991b960b9ec1ab31a7a01b04d3e83ab2294` |
| `tests/test_decoder.py` | `cdced229d91214d7e4aef038068315c716dca8b7ce0fb00e2260b6e330856b39` |
| `NFSMWAITrace.ini` | `d3c0a027c7fe56a2204ee354d4010353db6811d0fd5a0bc6d58125d2b5646aa6` |
| built schema-2 ASI | `4891c922af5958a9b694ebda838ac5ce0ec0ea6fb2f7746da10a1e21de254448` |

The baseline has nine exact-target hooks, 600-byte records, an 8,192-slot
lock-free slot array, implicit per-thread LIFO exit pairing, a 100,000-record
limit, and a decoder that supports schemas 1 and 2.

Microsoft's current API contracts support the review's platform findings:

- `DllMain` runs under the loader lock and should do minimal work:
  <https://learn.microsoft.com/en-us/windows/win32/dlls/dllmain>
- `ReadProcessMemory` fails when the requested range crosses an inaccessible
  area: <https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory>
- synchronous `WriteFile` reports completion and byte count; failures and short
  writes must be checked: <https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-writefile>
- executable changes require coherent protection/cache handling:
  <https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualprotect>
  and <https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-flushinstructioncache>

## Review charter

The review covers target identity, trampoline correctness, calling conventions,
capture fidelity, concurrency, lifecycle, bounded-resource behavior, decoder
truthfulness, profile coverage, and evidence claims. It does not treat recovered
source as proof of retail object layout and does not authorize changing game AI.

Severity meanings:

- P0: can corrupt execution or make the capture actively unsafe;
- P1: can materially falsify or lose evidence, or blocks the stated objective;
- P2: weakens auditability, resilience, or operational usability;
- P3: maintainability or presentation issue without current correctness impact.

## Complete primary issue list

### P0

1. **P0-01: an installed exit trampoline can return to address zero.**
   `trace_exit` returns zero if TLS state is absent or empty even though the
   original return slot may already point to the shared exit stub. Allocation,
   depth, frame storage, and return-slot replacement must form one safe path;
   the exit path must recover a real destination before optional recording.

2. **P0-02: stale TLS frames can pair an exit with the wrong call after SEH or
   nonlocal control flow.** The current implicit LIFO has no call id or stack
   proof. A later exit can jump to another invocation's return address. Record
   entry stack pointers, prune provably unwound frames, assign explicit call
   ids, and count every recovery. Hooks whose control flow cannot be made safe
   must be entry-only.

3. **P0-03: copied relative instructions would branch to the wrong address.**
   `AITarget::Aquire(vector)` overloads reach a `CALL rel32` before five safe
   bytes. The generic trampoline blindly copies stolen bytes. Support and test
   explicit relocation or omit those anchors.

4. **P0-04: a wrong multiple-inheritance adjustment can make capture read an
   unrelated object and can feed invalid pointers into deeper capture.** The
   current adjustment is a hard-coded two-hook conditional. Make adjustment a
   signed manifest field grounded in retail disassembly. Static hooks must not
   treat arbitrary `ECX` as an object.

5. **P0-05: patch installation is a multi-byte concurrent code mutation.** The
   worker continues in parallel with game threads while writing a five-byte
   JMP. Installation needs a startup-only invariant plus a short transaction
   that prevents another thread from executing a partially written site, with
   anchor revalidation and guaranteed rollback/resume on failure.

### P1

6. **P1-01: the physical-slot scan does not preserve global sequence order.**
   Producers can make later slots ready before earlier slots, and the worker
   scans array order. The decoder sorts afterward, hiding on-disk reordering.
   Replace it with an ordered FIFO and report file order before any sorting.

7. **P1-02: failed or short capture writes are silently discarded.** The
   current worker clears a slot regardless of `WriteFile` outcome. Preserve the
   pending record for retry/stop, increment a failure counter, stop admission,
   and surface the failure through both capture integrity (when possible) and
   status output.

8. **P1-03: the hard record limit silently clears queued records and tears
   entry/exit pairs.** Once `g_written_events` reaches the limit, `flush_ring`
   clears remaining ready slots. Reserve the pair budget before entry, stop new
   entries separately, drain reserved exits, and terminalize explicitly.

9. **P1-04: stack arguments are dereferenced without a bounded safe read.**
   Every sampled call reads eight raw words regardless of actual arity. Use the
   catalogue's declared argument count and an explicit validity mask; never
   interpret undeclared stack contents as arguments.

10. **P1-05: a fixed whole-range `ReadProcessMemory` failure turns partially
    readable state into indistinguishable zeros.** Use a one-call fast path and
    page/block fallback with per-block validity and fault counters.

11. **P1-06: the capture is not self-describing.** Schema 2 does not embed hook
    names, exact prologues, resolved divisors, normalization, regions, profile,
    or INI identity. An external config can be wrong. Embed a resolved manifest
    and raw config hash.

12. **P1-07: implicit pairing cannot distinguish recursion, gaps, dropped
    entries, or nonlocal unwinds.** Add call id, parent id, depth, and decoder
    pairing by id; report missing halves without repairing them silently.

13. **P1-08: literal full-rate broad capture would perturb the system being
    observed.** Existing data already contains roughly 201,000 simulate calls
    in one run. Four raw regions on dozens of hot hooks at divisor 1 can create
    gigabytes and millions of cross-process reads. Use profile-scoped multi-run
    evidence, safe defaults, measured overhead/loss, and truthful coverage.

14. **P1-09: source comments are being mistaken for exact retail layouts.**
    Retail disassembly already contradicts several recovered offsets. New
    windows must default to raw offset-labelled bytes; typed overlays require
    binary/live corroboration. Retain schema-2 labels only where already
    validated.

15. **P1-10: the nine-hook surface cannot answer the documented control and
    lifecycle gaps.** It lacks racer steering/throttle/NOS decisions, traffic
    density/validation/despawn/action state, roadblock lifecycle and pursuit
    terminal causes, helicopter LOS/action/exit, target transitions, and GPS.
    A profile-scoped catalogue must cover those boundaries.

16. **P1-11: enabled anchor validation is necessary but the full catalogue also
    needs mechanical offline validation.** A typo in a disabled hook can remain
    latent until a later run. The validator must check every catalogue entry,
    stolen length, instruction boundary, and supported relocation against the
    exact target before distribution.

17. **P1-12: return probes have no declared result ABI.** Integer/pointer/bool
    returns can be observed in `EAX`; 64-bit integers may also use `EDX`.
    Floating and aggregate returns have other rules. Store an explicit return
    kind and keep floating-return functions out until FPU preservation is
    separately proven.

18. **P1-13: process detach cannot guarantee a final flush or safe unpatch.**
    `DllMain` only signals a worker and cannot wait safely under loader lock.
    The module is pinned, so ordinary teardown should leave stubs allocated;
    the format must treat a missing terminal as an incomplete prefix, while
    normal budget/manual stop paths perform full drain outside `DllMain`.

### P2

19. **P2-01: cumulative `dropped_before` is not a final integrity statement.**
    Distinguish queue-full loss, budget rejection, read faults, frame overflow,
    stale-frame pruning, write failures, accepted records, written records, and
    active/reserved exits. Emit checkpoints and a terminal summary.

20. **P2-02: sequence allocation before slot acquisition creates ambiguous
    gaps and wraps a signed `LONG`.** Assign sequence only when appending to the
    ordered queue, use an explicitly unsigned on-disk value, and make decoder
    wrap/duplicate handling conservative.

21. **P2-03: current action capture follows unvalidated pointers and assigns a
    semantic name even when reads fail.** Represent action/object pointers as
    region specifications with validity. A string is optional evidence, not a
    trusted identifier.

22. **P2-04: queue and event memory costs are not stated or bounded per
    profile.** Fix record and ring capacity in the header, cap string/region
    sizes, calculate resident buffer size, and expose queue high-water state.

23. **P2-05: flush behavior trades durability for latency without declaring
    it.** `FlushFileBuffers` every second is expensive but crash loss is bounded
    only informally. Record the policy, flush on terminal/rotation, and report
    forced termination as possibly losing a tail.

24. **P2-06: there is no capture identity beyond filename/time.** Store a
    generated capture id and exact UTC/QPC frequency provenance so multi-profile
    files cannot be accidentally merged as one run.

25. **P2-07: profile resolution and per-hook overrides could produce an empty
    or unexpectedly expensive run.** Fail an empty profile, list every resolved
    divisor in the manifest, cap divisors/ranges, and log a concise activation
    summary before patching.

26. **P2-08: static/global hooks lack a uniform region-source model.** Region
    sources need explicit selectors such as normalized-this, raw-this,
    argument-N, dereferenced argument-N, return `EAX`, or none. Unsupported
    pointer chains must remain uncaptured rather than implicit.

27. **P2-09: broad snapshots can expose unrelated in-process bytes.** Keep
    every source and offset catalogue-defined, cap each region at 256 bytes,
    reject arithmetic overflow, and do not accept arbitrary addresses or sizes
    from the INI.

28. **P2-10: the decoder currently materializes and sorts full captures for
    several outputs.** Schema-3 JSONL and validation paths should stream; summary
    analysis may aggregate bounded state and must disclose when exact global
    pairing requires retained indexes.

29. **P2-11: target hashing occurs while the game continues startup and before
    patching.** Keep all heavy work outside `DllMain`, but make activation state
    explicit (`starting`, `validated`, `patching`, `active`) so a slow hash or
    early exit cannot be mistaken for an active capture.

30. **P2-12: the current tests exercise decoding but not generated trampoline
    destinations or lifecycle invariants.** Add deterministic tests for every
    manifest packing rule, prologue, relative-call destination, pair budget,
    file-order diagnostics, corrupt/truncated inputs, counter monotonicity, and
    schema-1/2 compatibility. Runtime safety still needs controlled game runs.

### P3

31. **P3-01: hook metadata is duplicated across C, Python, INI, README, and the
    target validator.** Generate or mechanically compare these views from one
    checked-in catalogue representation where practical; at minimum add tests
    that fail on divergence.

32. **P3-02: `Aquire` preserves the original game's spelling but is easy to
    “correct” inconsistently.** Keep exact symbol spelling in catalogue names
    and use user-facing aliases only in explanatory text.

## Primary disposition

The objective is feasible only after resolving all P0/P1 items. The proposal's
profile/manifest/schema-3 direction addresses the issue set in principle, but
it is not accepted for implementation until independent red-team and
simplifier passes challenge the design and every issue receives an explicit
adjudication.

