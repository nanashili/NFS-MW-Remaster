# zBWare schema-4 runtime profile

The `zbware` profile adds 16 entry hooks to the existing fail-closed PC 1.3
tracer. The exact retail prologues were recovered with Ghidra CLI and are also
validated byte-for-byte against the supported `speed.exe` before installation
or activation.

| ID | Hook | Default divisor | Entry evidence |
| ---: | --- | ---: | --- |
| 49 | `bReleasePutString` | 1 | channel, text pointer, bounded text bytes |
| 50 | `bLaunchWindows` | 1 | command pointer and bounded command bytes |
| 51 | `bInitTicker` | 1 | minimum wraparound time |
| 52 | `bGetTicker` | 4096 | sampled ticker use |
| 53 | `bGetTickerDifference` | 1024 | start/end ticks |
| 54 | `bHasTickerExpired` | 256 | start tick and duration |
| 55 | `MemoryPool::TraceNewPool` | 1 | pool identity and bounded pool bytes |
| 56 | `MemoryPool::AddFreeMemory` | 8 | pool, memory, size, debug-name pointer |
| 57 | `MemoryPool::Init` | 1 | pool, backing memory, size, debug-name pointer |
| 58 | `MemoryPool::Close` | 1 | pool identity and bounded pool bytes |
| 59 | `MemoryPool::AllocateMemory` | 256 | allocation parameters and pool bytes |
| 60 | `MemoryPool::VerifyPoolIntegrity` | 16 | verification flag and pool bytes |
| 61 | `bInitMemoryPool` | 1 | pool index, backing memory, size, debug-name pointer |
| 62 | `bCloseMemoryPool` | 1 | pool index |
| 63 | `bMemoryInit` | 1 | initialization reachability |
| 64 | `bWareMalloc` | 256 | allocation parameters and bounded debug text |

`bFunkCallASync` is intentionally absent: the PC target is a one-byte `ret`
stub, so it cannot satisfy the tracer's five-byte admission rule and would not
reveal a working asynchronous transport. The unary ticker-difference overload
is also omitted; the two-argument implementation is the useful calculation
boundary and keeps the catalogue at a fixed 64 hooks.

## Controlled runtime run

Set the installed configuration to:

```ini
[trace]
enabled=1
schema=4
profile=zbware
capture_exits=0
max_events=100000
checkpoint_interval=4096
flush_interval_ms=1000
zbware_probe=0
```

Set `zbware_probe=1` only for a controlled reachability run. It emits one
synthetic call to `bReleasePutString` while leaving release output disabled,
`bLaunchWindows` with an empty mutable command so no process is launched, and
`bHasTickerExpired`. It also enables
`bMemoryTracing` only until the first `MemoryPool::TraceNewPool` entry or a
30-second timeout. The decoder labels these records `synthetic_probe` or
`probe_assisted`; they are not evidence of natural retail call paths.

Then launch with `launch_capture.sh`, enter the front end, load a save, drive
for a short repeatable scenario, and request a coherent close with
`stop_capture.sh` before exiting the game. Decode the resulting file with
`decoder.py --format analysis`.

The run must establish activation without an anchor/patch failure, gameplay
stability, which of the 16 hooks are reached, observed argument ranges, region
read coverage, queue/budget loss, and terminal integrity. It cannot establish
return values, allocation success, function duration, full semantic memory
layouts, or general absence of calls. Those claims require a separately
reviewed exit-capture design or additional targeted observation.
