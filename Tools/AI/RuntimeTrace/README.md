# NFSMW PC 1.3 AI and zBWare runtime tracer

This directory contains an observation-only 32-bit ASI for the owner-operated
CrossOver install of Need for Speed: Most Wanted PC 1.3. Schema 4 records
sampled AI and zBWare function entries and bounded raw memory regions. It does not replace
return values, change arguments, or modify `speed.exe` on disk.

The tracer fails closed unless all of these match:

- `speed.exe` SHA-256
  `80774c2e5d619b4f120b48d4462896fd504c263399d203a238769cffde1d253c`;
- PE32/i386, preferred image base `0x00400000`, and exact PE
  `SizeOfImage` value `0x00678e4e`;
- every enabled function prologue and relocated instruction anchor;
- schema 4, a known profile, valid sampling/budget settings, and the generated
  catalogue identity.

Hook entry addresses must also be eight-byte aligned. The supported Ultimate
ASI Loader calls the exported `InitializeASI` callback synchronously after the
ASI's minimal `DllMain`; the callback pins the module and waits for validation
and hook installation to finish. The worker repeatedly enumerates and suspends
the other process threads until the set is stable, verifies that no instruction
pointer is inside a stolen span, and commits each five-byte near jump as one
aligned eight-byte `InterlockedCompareExchange64`. The trailing three bytes are
preserved. A process-wide `FlushInstructionCache` completes before those
threads resume and before the callback returns. The atomic word replacement
prevents a memory-level torn patch; instruction-fetch safety relies on the
synchronous startup gate, thread quiescence, and cache flush together. This
contract assumes no external process injects a new thread during the bounded
installation transaction.

Installation fails closed if loader/config identity, target words, processor
features, cache flush, protection restore, or thread handling fails. Rollback
is performed only while every suspended thread remains quiescent. The worker
retains each suspended-thread handle and retries `ResumeThread` until its one
tracer-owned suspend count is released or the thread has terminated; it never
closes a still-live handle after a failed resume or abandons that count.

Tracer 0.4.2 has a versioned catalogue of 64 hooks. Schema 4 uses a fixed
32 KiB header: its 1 KiB core is followed by 64 fixed-size manifest entries and
zero-checked reserved padding. Each capture embeds its
resolved hook manifest, profile, sample divisors, target/config/catalogue
digests, process identity, queue/read budgets, lifecycle state, and a SHA-256
digest over the mutable header core. Every
1,024-byte data record includes exact call and sample indices, raw argument
bits, the unadjusted and normalized `this` values, bounded raw regions, and
16-byte validity masks. The writer also stores a 128-bit-truncated SHA-256 over
each complete record. Records are written in sequence order. A coherently
closed capture ends with one terminal record containing loss, read-fault,
write-failure, queue, and budget counters.

Schemas 3 and 4 are deliberately entry-only. `capture_exits` must be `0`; there is no
return-value, duration, or implicit state-transition claim. Raw region labels
are observation coordinates, not proof of C++ field semantics.

## Profiles and scenario runs

Capturing every hook at full rate in one run would distort the process and
produce misleading data. Use repeatable, profile-scoped runs instead:

| Profile | Hooks | Suggested scenario |
| --- | ---: | --- |
| `survey` | 12 | One representative free-roam/race/pursuit pass to establish reachability. |
| `core` | 6 | Normal driving, task cadence, goal selection, and ordinary spawn checks. |
| `racer` | 7 | A full race with steering, throttle/brake, reverse recovery, and NOS use. |
| `traffic` | 9 | Dense free roam with traffic spawning, reuse, validation, and avoidance. |
| `pursuit` | 13 | Pursuit escalation, roadblock request/creation, evasion, and bailout. |
| `helicopter` | 6 | High-heat helicopter arrival, search/visibility, attack, and exit. |
| `navigation` | 8 | GPS engage/reroute plus moving-target acquisition and clearing. |
| `zbware` | 16 | Startup, ticker, memory-pool lifecycle, integrity checks, and sampled allocation activity. |
| `all` | 64 | Diagnostic only; keep conservative divisors and validate stability first. |

The hook counts above are the profile defaults. An explicit nonzero per-hook
override may expand that preset; `0` may remove a hook. The embedded resolved
manifest, rather than the profile name alone, is authoritative for each run.
The zBWare hook inventory, exclusions, and controlled-run checklist are in
[`ZBWARE_RUNTIME.md`](ZBWARE_RUNTIME.md).

Use the same save state, route, heat level, duration, and player actions when
comparing runs. An observed sampled entry proves only that the hook was reached
in that scenario. An unobserved hook does not prove it is unused.

## Configure, build, and install

The checked-in and distributed INI are intentionally disabled:

```ini
[trace]
enabled=0
schema=4
profile=survey
capture_exits=0
max_events=100000
checkpoint_interval=4096
flush_interval_ms=1000
zbware_probe=0
```

Set `enabled=1` only for a planned run. Profiles resolve their default sample
divisors from `hook_catalogue.json`. Override an individual hook with its
catalogue `config_key`, for example:

```ini
sample_AIVehicleRacecar_Update=64
```

`0` disables that hook; `N` records calls `1, 1+N, 1+2N, ...`. The default
100,000-data-record limit is about 98 MiB plus the header and terminal record.
Reaching the limit closes admission and requests a coherent terminal record;
the original game functions continue to run.

For a controlled `profile=zbware` reachability run only, `zbware_probe=1`
invokes hooks 49, 50, and 54 once with inert arguments and temporarily enables
memory tracing until hook 55 is observed or 30 seconds elapse. Schema-4 records
mark direct calls as `synthetic_probe` and the new-pool call as
`probe_assisted`. The setting is rejected for every other profile and remains
disabled in distributed configuration.

Build and statically validate the ASI:

```bash
./Tools/AI/RuntimeTrace/build.sh
python3 Tools/AI/RuntimeTrace/validate_target.py \
  --target '<game>/speed.exe' \
  --asi Tools/AI/RuntimeTrace/dist/NFSMWAITrace.asi \
  --loader '<game>/dinput8.dll' \
  --loader-config-root '<game>'
```

`install.sh` runs this build again before it validates or copies anything, so
installation cannot silently reuse a stale `dist/NFSMWAITrace.asi` after a
source edit. A successful install therefore includes catalogue generation,
native contracts, stop-control fixtures, a fresh PE32 build, ASI validation,
and the exact installed-target/loader/configuration checks below.

Install after editing the project INI if no installed schema-4 configuration
exists:

```bash
./Tools/AI/RuntimeTrace/install.sh
```

The installer validates the exact target, ASI, audited Ultimate ASI Loader
binary, and effective loader settings. The supported binary is the `dinput8.dll`
from the official [Ultimate ASI Loader v9.7.1 release](https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/tag/v9.7.1),
whose [tagged source](https://github.com/ThirteenAG/Ultimate-ASI-Loader/blob/v9.7.1/source/dllmain.cpp)
defines the admitted configuration precedence and callback flow. Its supported
SHA-256 is
`7675b24a2e408349c8a59b87e3ae8724ad8a89eff21c96a9c109650a3c1316e0`.
Recognizing loader exports or callback strings is not enough: a changed loader
must be reviewed before its callback timing can be admitted to this startup
contract. The validator also rejects disabled plugin loading and the unsafe
configuration that loads plugins directly from loader `DllMain`; the
synchronous startup gate requires deferred loading. The installer backs up
changed tracer files and does not replace the existing ASI loader or widescreen
fix. It preserves a different installed schema-4 INI by default. To
deliberately replace that INI with the distribution copy:

```bash
NFSMW_TRACE_REPLACE_CONFIG=1 ./Tools/AI/RuntimeTrace/install.sh
```

## Capture lifecycle

With the installed INI set to `enabled=1`, launch the known CrossOver target:

```bash
./Tools/AI/RuntimeTrace/launch_capture.sh
```

The launcher reruns installation and exact target/loader/configuration
validation immediately before starting the game. Manually copying the ASI and
launching outside this path bypasses the startup safety gate and is unsupported.

While the game is still running, finish the planned scenario and use another
terminal to request a coherent close:

```bash
./Tools/AI/RuntimeTrace/stop_capture.sh
```

Wait for `capture-closed` before exiting the game. `stop_capture.sh` does this
for up to 30 seconds by default and fails honestly on a reported write or
finalization error. A forced exit, crash, or normal process exit before this
handshake can leave only an incomplete capture prefix; process detach cannot
guarantee a terminal record.

Stopping collection is not an in-process uninstall. After activation, the ASI
is pinned and its entry stubs remain installed for the rest of that game
process. `capture_active=0` makes every stub take its tested inactive path:
restore flags, replay the exact relocated prologue, and continue without
recording. Activation/finalization failures use the same inert fallback. This
avoids a second live code-mutation transaction; the operating system reclaims
the hooks and stubs when the game process exits. `DLL_PROCESS_DETACH` only
closes admission and wakes the writer—it does not restore hooks or promise a
terminal record.

Runtime files are written beneath the game directory:

```text
scripts/NFSMWAITrace/status.log
scripts/NFSMWAITrace/capture-YYYYMMDD-HHMMSS-PID.bin
scripts/NFSMWAITrace/capture-YYYYMMDD-HHMMSS-PID.bin.stop.request
```

Status lines have UTC timestamps and carry the capture filename on every
lifecycle transition. `stop_capture.sh` selects the newest nonterminal run and
creates only that run's request file, so overlapping or historical status lines
cannot redirect the request. A request made during `tracer-starting` remains in
place until activation consumes it. The tracer removes the per-run request
after acceptance, failure, or finalization.

## Decode and integrity

```bash
python3 Tools/AI/RuntimeTrace/decoder.py '<capture.bin>'
python3 Tools/AI/RuntimeTrace/decoder.py '<capture.bin>' \
  --format analysis --output analysis.json
python3 Tools/AI/RuntimeTrace/decoder.py '<capture.bin>' \
  --format jsonl --output runtime.jsonl
python3 Tools/AI/RuntimeTrace/decoder.py '<capture.bin>' \
  --format csv --output runtime.csv
```

Schema-3/4 CLI decoding streams JSONL/CSV records and uses bounded accumulators
for summary/analysis, so memory does not grow with the complete capture or raw
region payload. Summary and analysis omit raw region hex while reporting valid
byte/block coverage, hook reachability, sampling-index gaps, queue losses,
budget rejections, read faults, write failures, and terminal integrity.

The decoder rejects a header-core or record digest mismatch before using its
payload. These unkeyed hashes detect accidental corruption; they are not a MAC
and do not establish authorship against a malicious editor.
`structurally_complete_closed_capture=true` requires verified hashes, a closed
header, physical sequence `1..N`, exactly one final terminal record, zero
truncated tail, valid monotonic counter equations, and no reported write
failure.
`lossless_admitted_capture=true` additionally requires every admitted data
record to have been queued and written. Budget rejections are reported
separately: they describe calls refused after the configured capture limit, not
silent record loss.

Use `--tolerate-truncated-tail` only to inspect an incomplete prefix. The
decoder still reports the exact tail byte count and will not call such a file a
structurally complete capture. Never treat bytes from invalid 16-byte blocks as
observed values.

Schemas 1, 2, and 3 remain decodable after the schema-4 extension. Schema 1
specialized labels used non-retail offsets, so the decoder exposes the original
slots only as untrusted raw values and re-decodes only binary-validated fields.
The controlled capture, producing ASI/INI, decoded outputs, and hashes are in
[`captures/2026-09-11-gameplay`](captures/2026-09-11-gameplay/README.md).

## Verification boundary

```bash
python3 -m unittest discover -s Tools/AI/RuntimeTrace/tests -v
python3 -m py_compile \
  Tools/AI/RuntimeTrace/decoder.py \
  Tools/AI/RuntimeTrace/generate_catalogue.py \
  Tools/AI/RuntimeTrace/validate_target.py
```

The build also runs a native contract harness against the exact SHA-256 and the
entry-stub/atomic-patch builders shared by the runtime. It proves the
`FXSAVE`/cdecl/`FXRSTOR` stack balance, relocated-call rebasing, complete jump
word, preserved trailing bytes, and both wrap directions of 32-bit `rel32`
addressing. The PE validator requires two emitted
`lock cmpxchg8b` sites plus the cache/protection/thread APIs. Installation
mandatorily rebuilds before installed-target validation requires the audited
Ultimate ASI Loader digest and safe deferred-loader settings. Static checks
establish target identity, catalogue consistency,
instruction relocation, record contracts, and PE properties. They cannot prove
CrossOver/gameplay stability, semantic field identity, or complete profile
reachability. Those require controlled live runs and post-capture integrity
review. Keep the installed tracer disabled when it is not actively collecting
evidence.
