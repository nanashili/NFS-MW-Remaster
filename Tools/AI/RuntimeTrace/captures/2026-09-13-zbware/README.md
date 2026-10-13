# zBWare runtime capture — 2026-09-13

This is an owner-operated NFSMW PC 1.3 schema-4 capture using only the
`zbware` profile. The target, catalogue, ASI, and run configuration identities
match the embedded capture manifest.

## Result

- Target SHA-256: `80774c2e5d619b4f120b48d4462896fd504c263399d203a238769cffde1d253c`
- Catalogue SHA-256: `f4a3b185e80f1b65d4ec3bd51676585f42f83b524e6f70212eb5fb40e2ce0526`
- Run-config SHA-256: `35e400ed446ff43c40d17c66e73e79bac78bf2a7ddee35c534751ed4afc97c7c`
- Profile: schema 4, `zbware`, 16 enabled hooks, entry-only
- Observed span: 588.4948119 seconds on two game threads
- Records: 3,313 individually digest-valid entries, physical sequence 1–3,313
- Reached: 12 of 16 enabled hooks

Reached hooks and sampled entries:

| Hook | Divisor | Samples | Last observed call index |
| --- | ---: | ---: | ---: |
| `bInitTicker` | 1 | 2 | 2 |
| `bGetTicker` | 4096 | 201 | 819201 |
| `bGetTickerDifference` | 1024 | 269 | 274433 |
| `MemoryPool::AddFreeMemory` | 8 | 2654 | 21225 |
| `MemoryPool::Init` | 1 | 8 | 8 |
| `MemoryPool::Close` | 1 | 1 | 1 |
| `MemoryPool::AllocateMemory` | 256 | 83 | 20993 |
| `MemoryPool::VerifyPoolIntegrity` | 16 | 1 | 1 |
| `bInitMemoryPool` | 1 | 6 | 6 |
| `bCloseMemoryPool` | 1 | 1 | 1 |
| `bMemoryInit` | 1 | 2 | 2 |
| `bWareMalloc` | 256 | 85 | 21505 |

Unreached in this scenario: `bReleasePutString`, `bLaunchWindows`,
`bHasTickerExpired`, and `MemoryPool::TraceNewPool`. This is not evidence that
the retail game never calls them.

Observed argument evidence includes a `60000.0` ticker wraparound input, pool
IDs `2, 3, 4, 5, 6, 9`, six corresponding initialized pool sizes from 327,680
through 51,200,000 bytes, sampled pool allocations from 24 through 2,225,172
bytes, and alignments `16, 64, 128`. These are entry-time values only.

## Integrity boundary

The decoded prefix has no digest failure, duplicate, missing sequence, tail
fragment, reported queue loss, budget rejection, read fault, or write failure.
Queue high-water was 19 of 2,048 slots. The pool snapshots contributed 351,616
fully valid bytes. All 85 `bWareMalloc` debug-text reads were unresolved because
the sampled pointer was null.

The game process exited before `stop_capture.sh` could have its request consumed.
The status log therefore has no `stop-request-accepted` or `capture-closed`
transition, the header remains `active`, and there is no terminal record. The
decoder correctly reports `structurally_complete_closed_capture=false` and
`lossless_admitted_capture=false`. Use this file as a validated contiguous
runtime prefix and reachability observation, not as a coherently sealed capture.

The installed configuration was returned to `enabled=0` after collection.

## Reproduce

```bash
python3 Tools/AI/RuntimeTrace/decoder.py \
  Tools/AI/RuntimeTrace/captures/2026-09-13-zbware/capture-20260913-202053-224.bin \
  --format analysis \
  --output /tmp/zbware-analysis.json
```

The capture remains entry-only. It does not prove return values, allocation
success, per-call duration, or semantic identities for raw pool offsets.
