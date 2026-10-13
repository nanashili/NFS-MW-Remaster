# NFSMW PC 1.3 gameplay capture — 2026-09-11

This directory preserves the first non-empty controlled gameplay capture from
the owner-operated CrossOver installation. The run began at
`2026-09-11T14:16:10.087695Z` and spans 291.2530456 seconds of traced time.

## Integrity

- Target: `speed.exe` PC 1.3, SHA-256
  `80774c2e5d619b4f120b48d4462896fd504c263399d203a238769cffde1d253c`.
- Capture: 9,231,856 bytes, 15,386 records, sequences 1–15,386.
- Pairing: 7,693 exact entry/exit pairs; no missing, duplicate, unmatched, or
  mismatched events.
- Tracer-reported drops: zero.
- Capturing thread: 216.

## What was observed

The run exercised all nine configured hooks. Full-rate observations include
3,803 goal choices, 1,920 traffic-spawn calls, 54 normal spawn-location calls,
9 spawn-position checks, and 11 pursuit-vehicle spawn attempts. Sampled hooks
bound vehicle simulation to 201,729–201,856 calls, vehicle tasks to
12,993–13,056 calls, pursuit tasks to 1,297–1,312 calls, and formation updates
to 257–264 calls.

Observed successful results include 89 traffic spawns, three normal spawn
locations, three accepted spawn positions, and one pursuit-vehicle spawn. Goal
states at return were `AIActionTraffic` 3,516 times at score 0.1,
`AIActionRace` 200 times at score 0, and `AIActionPursuitOffRoad` 87 times at
score 0.01. The five transition classes are in `analysis.json`.

The three successful normal spawn outputs were:

| Segment | Lane | Time step | Accepted position |
| ---: | ---: | ---: | --- |
| 830 | 3 | 0.164520263671875 | (-1069.9456, 61.6776, 4374.7095) |
| 812 | 3 | 0.6930084228515625 | (-781.1291, 31.2346, 3946.9849) |
| 978 | 1 | 0.7348785400390625 | (-1128.6018, 71.3948, 3489.6670) |

The retail spawn-manager limits decoded as 150 and 400. During the observed
pursuit, maximum heat was 5, heat scale was 1, the breaker timer was -1, the
busted-timer task handle was 140, and total pursuit time reached
17.98248863220215 seconds in the sampled exits.

Observed callback `dT` ranges were 0.0016666668–0.0083333338 for vehicle
simulation, 0.0060416651–0.33333334 for sampled vehicle think tasks, and
0.0066666673–0.13333334 for sampled pursuit tasks. These are runtime values,
but a single mixed gameplay run does not establish the scheduler's policy or a
general source-unit-to-wall-clock conversion.

## Schema-1 correction

The capture was produced by tracer 0.1.0 and embeds schema 1. Its raw object
snapshots and call/result records are valid, but its specialized value slots
for vehicle, spawn-manager, and pursuit objects were based on non-retail header
offsets. Decoder 0.2 ignores those mislabeled slots and re-decodes only fields
whose retail offsets were established from the exact target binary. The
original slots remain available only as untrusted hexadecimal values in JSONL.

For task callbacks, schema 1 snapshots begin at the `Sim::IActivity` subobject,
eight bytes after the complete `AIVehicle` or `AIPursuit` object. Decoder 0.2
accounts for that adjustment. It also reports `HSIMTASK` values as integer
handles: the typedef is pointer-shaped, but the engine assigns small monotonic
integers and does not treat them as object addresses.

Fields beyond the 256-byte schema-1 snapshot, including vehicle top speed and
late pursuit timers/meters, cannot be recovered from this capture. Tracer 0.2
normalizes complete-object snapshots and captures the corrected specialized
slots for future runs. It was built, target-validated, and installed after this
run; the exact 0.1.0 ASI that produced this capture is preserved here.

## Reproduce the decoded artifacts

From the repository root:

```bash
python3 Tools/AI/RuntimeTrace/decoder.py \
  Tools/AI/RuntimeTrace/captures/2026-09-11-gameplay/capture-20260911-141610-212.bin \
  --format summary \
  --config Tools/AI/RuntimeTrace/captures/2026-09-11-gameplay/NFSMWAITrace.ini \
  --output /tmp/nfsmw-runtime-summary.json

python3 Tools/AI/RuntimeTrace/decoder.py \
  Tools/AI/RuntimeTrace/captures/2026-09-11-gameplay/capture-20260911-141610-212.bin \
  --format analysis \
  --config Tools/AI/RuntimeTrace/captures/2026-09-11-gameplay/NFSMWAITrace.ini \
  --output /tmp/nfsmw-runtime-analysis.json
```

Compare the outputs byte-for-byte with `summary.json` and `analysis.json`, then
verify the preserved artifacts with `shasum -a 256 -c SHA256SUMS` from this
directory.

Instrumented durations in the JSON include tracer, snapshot, and
`ReadProcessMemory` overhead. They establish compatibility only and are not
uninstrumented performance benchmarks.
