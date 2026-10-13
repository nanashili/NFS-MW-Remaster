from __future__ import annotations

import io
import struct
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import decoder  # noqa: E402


def make_header(*, schema: int = 1) -> bytes:
    return decoder.HEADER.pack(
        b"NMWAITR1",
        schema,
        256,
        600,
        bytes.fromhex("80774c2e5d619b4f120b48d4462896fd504c263399d203a238769cffde1d253c"),
        0x00400000,
        0x00400000,
        0x00678E4E,
        42,
        1,
        2,
        10_000_000,
        9,
        1,
        (b"0.1.0\0" if schema == 1 else b"0.2.0\0").ljust(32, b"\0"),
        b"speed.exe PC 1.3\0".ljust(32, b"\0"),
        bytes(100),
    )


def make_event(
    *,
    phase: int,
    sequence: int,
    action_name: str = "",
    eax: int = 0,
    schema: int = 1,
    hook_id: int = 3,
    this_ptr: int = 0x1000,
    values: tuple[int, ...] | None = None,
    object_writes: tuple[tuple[int, int], ...] = (),
) -> bytes:
    raw = bytearray(600)
    decoder.EVENT_PREFIX.pack_into(
        raw,
        0,
        decoder.EVENT_MAGIC,
        600,
        schema,
        phase,
        sequence,
        1234,
        7,
        hook_id,
        0x0042B070,
        this_ptr,
        0x00401234,
        eax,
        0,
        0x202,
    )
    at = decoder.EVENT_PREFIX.size
    struct.pack_into("<8I", raw, at, 0x3C23D70A, *([0] * 7))
    at += 32
    struct.pack_into("<16I", raw, at, *(values or (0x2000, *([0] * 15))))
    at += 64
    struct.pack_into("<I", raw, at, 256)
    at += 4
    for offset, value in object_writes:
        struct.pack_into("<I", raw, at + offset, value)
    at += 256
    struct.pack_into("<I", raw, at, 0x2000)
    at += 4
    struct.pack_into("<IIIII", raw, at + 0x34, 0x3000, 0xAABBCCDD, 0x4000, 0x5000, 0x3F800000)
    at += 72
    raw[at : at + 48] = action_name.encode()[:47].ljust(48, b"\0")
    return bytes(raw)


class DecoderTests(unittest.TestCase):
    def test_header_and_action_transition(self) -> None:
        capture = make_header() + make_event(phase=1, sequence=2, action_name="AIActionRace") + make_event(
            phase=2, sequence=3, action_name="AIActionTooDamaged"
        )
        stream = io.BytesIO(capture)
        header = decoder.read_header(stream)
        events = list(decoder.iter_events(stream))
        summary = decoder.build_summary(header, events)
        self.assertEqual(header["target_name"], "speed.exe PC 1.3")
        self.assertAlmostEqual(events[0]["args"]["dT"], 0.01, places=7)
        self.assertEqual(events[0]["action"]["score"], 1.0)
        self.assertEqual(summary["action_transitions"], {"AIActionRace -> AIActionTooDamaged": 1})
        self.assertEqual(summary["integrity"]["pair_count"], 1)
        self.assertEqual(summary["integrity"]["pair_mismatch_count"], 0)

    def test_schema1_redecodes_retail_spawn_offsets_from_snapshot(self) -> None:
        event = decoder.decode_event(
            make_event(
                phase=1,
                sequence=1,
                hook_id=4,
                values=(0xDEADBEEF, 0x3F800000, *([0] * 14)),
                object_writes=((0, 0x00891234), (4, 0x43160000), (8, 0x43C80000)),
            )
        )
        self.assertEqual(event["values"]["min_spawn_distance"], 150.0)
        self.assertEqual(event["values"]["max_spawn_distance"], 400.0)
        self.assertEqual(event["legacy_probe_values"][0], "0xdeadbeef")

    def test_schema1_task_snapshot_is_rebased_to_complete_vehicle(self) -> None:
        event = decoder.decode_event(
            make_event(
                phase=1,
                sequence=1,
                hook_id=2,
                this_ptr=0x1008,
                object_writes=(
                    (0x84 - 8, 0x41480000),
                    (0xAC - 8, 0x2000),
                    (0xB0 - 8, 0x3FC00000),
                    (0xCC - 8, 7),
                ),
            )
        )
        self.assertEqual(event["object_base_ptr"], "0x00001000")
        self.assertEqual(event["object_snapshot_ptr"], "0x00001008")
        self.assertEqual(event["values"]["drive_speed"], 12.5)
        self.assertEqual(event["values"]["think_task_handle"], 0x2000)
        self.assertEqual(event["values"]["last_spawn_time"], 1.5)
        self.assertEqual(event["values"]["drive_flags"], 7)
        self.assertIsNone(event["action"])

    def test_schema2_uses_validated_pursuit_slots_and_normalized_snapshot(self) -> None:
        words = (
            0x2000,
            0x3000,
            0x4000,
            1,
            0x00000000,
            0x40A00000,
            0x3F800000,
            0x3F000000,
            0xBF800000,
            0x41200000,
            2,
            7,
            0x40400000,
            0x3E800000,
            0x40E00000,
            0x3F400000,
        )
        event = decoder.decode_event(
            make_event(phase=2, sequence=2, schema=2, hook_id=9, this_ptr=0x5008, values=words, eax=1)
        )
        self.assertEqual(event["object_base_ptr"], "0x00005000")
        self.assertEqual(event["object_snapshot_ptr"], "0x00005000")
        self.assertEqual(event["values"]["pursuit_level"], 1)
        self.assertEqual(event["values"]["busted_timer_task_handle"], 0x2000)
        self.assertEqual(event["values"]["maximum_heat"], 5.0)
        self.assertEqual(event["values"]["breaker_timer"], -1.0)
        self.assertEqual(event["values"]["cooldown_time_remaining"], 7.0)
        self.assertEqual(event["values"]["pursuit_meter"], 0.75)

    def test_rejects_truncated_record(self) -> None:
        with self.assertRaises(decoder.DecodeError):
            decoder.decode_event(bytes(599))

    def test_explicit_legacy_schema_is_not_bypassed_by_schema3_record_size(self) -> None:
        with self.assertRaisesRegex(decoder.DecodeError, "expected 600, got 1024"):
            decoder.decode_event(bytes(1024), expected_schema=2)

    def test_tolerates_only_requested_truncated_tail(self) -> None:
        stream = io.BytesIO(make_event(phase=1, sequence=1, action_name="AIActionRace") + b"tail")
        self.assertEqual(len(list(decoder.iter_events(stream, tolerate_truncated_tail=True))), 1)

    def test_numeric_observation_examples_are_sorted_numerically(self) -> None:
        summary = decoder.summarize_scalar_values([11, 103, 22, 11])
        self.assertEqual(summary["values"], [11, 22, 103])

    def test_task_arguments_are_reported_as_integer_handles(self) -> None:
        event = decoder.decode_event(
            make_event(phase=1, sequence=1, schema=2, hook_id=2)
        )
        self.assertEqual(event["args"]["task_handle"], 0x3C23D70A)

    def test_sample_divisors_match_runtime_decimal_and_range_contract(self) -> None:
        key = decoder.HOOK_SAMPLE_CONFIG_KEYS[1]
        with tempfile.TemporaryDirectory() as directory:
            config = Path(directory) / "NFSMWAITrace.ini"
            for raw, expected in (("0", 0), ("1000000000", 1_000_000_000)):
                config.write_text(f"[trace]\n{key}={raw}\n", encoding="utf-8")
                divisors, _ = decoder.load_sample_divisors(config)
                self.assertEqual(divisors[1], expected)
            for raw in ("-1", "1000000001", "+1", "1_0"):
                with self.subTest(raw=raw):
                    config.write_text(f"[trace]\n{key}={raw}\n", encoding="utf-8")
                    with self.assertRaisesRegex(decoder.DecodeError, "invalid sample divisor"):
                        decoder.load_sample_divisors(config)


if __name__ == "__main__":
    unittest.main()
