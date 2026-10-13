from __future__ import annotations

import hashlib
import io
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(Path(__file__).resolve().parent))

import decoder  # noqa: E402
import generate_catalogue  # noqa: E402
import test_schema3  # noqa: E402


def make_schema4_header(*, profile: str = "zbware") -> bytes:
    catalogue = generate_catalogue.load_catalogue()
    manifest, divisors = test_schema3.build_manifest(profile, hook_count=64)
    padding = bytes(decoder.SCHEMA4_HEADER_SIZE - decoder.SCHEMA3_HEADER_CORE_SIZE - len(manifest))
    manifest_area = manifest + padding
    profile_definition = catalogue["_profile_by_name"][profile]
    core = decoder.SCHEMA3_HEADER_CORE.pack(
        b"NMWAITR4",
        4,
        decoder.SCHEMA4_HEADER_SIZE,
        decoder.SCHEMA3_EVENT_SIZE,
        decoder.SCHEMA3_MANIFEST_SIZE,
        bytes.fromhex(decoder.EXPECTED_TARGET_SHA256),
        hashlib.sha256(b"schema4-test-config").digest(),
        bytes(catalogue["_raw_sha256"]),
        hashlib.sha256(manifest_area).digest(),
        0x00400000,
        0x00400000,
        0x00678E4E,
        42,
        1337,
        10_000_000,
        1000,
        bytes.fromhex("00112233445566778899aabbccddeeff"),
        64,
        sum(divisor != 0 for divisor in divisors),
        int(profile_definition["_id"]),
        0xF,
        100,
        2048,
        3,
        256,
        12,
        4096,
        1000,
        6,
        test_schema3.fixed_text("0.4.2", 32),
        test_schema3.fixed_text("speed.exe PC 1.3", 32),
        test_schema3.fixed_text(profile, 32),
        test_schema3.fixed_text("NFSMWAITrace.ini", 64),
        bytes(32),
        bytes(572),
    )
    return test_schema3.seal_header_core(core) + manifest_area


class Schema4ZBWareTests(unittest.TestCase):
    def test_schema4_catalogue_exposes_the_zbware_profile(self) -> None:
        catalogue = generate_catalogue.load_catalogue()

        self.assertEqual(decoder.CURRENT_SCHEMA_VERSION, 4)
        self.assertEqual(decoder.SUPPORTED_SCHEMAS, frozenset({1, 2, 3, 4}))
        self.assertEqual(len(catalogue["hooks"]), 64)
        self.assertEqual(catalogue["_profile_by_name"]["zbware"]["_id"], 8)
        self.assertEqual(catalogue["_profile_by_name"]["zbware"]["_mask"], 0x80)
        self.assertEqual(catalogue["_profile_by_name"]["all"]["_id"], 9)

        expected = [
            (49, "bReleasePutString", 0x0045CC70),
            (50, "bLaunchWindows", 0x0045CD70),
            (51, "bInitTicker", 0x0045CDD0),
            (52, "bGetTicker", 0x0045CE40),
            (53, "bGetTickerDifference", 0x0045CE70),
            (54, "bHasTickerExpired", 0x0045CF30),
            (55, "MemoryPool::TraceNewPool", 0x00463F00),
            (56, "MemoryPool::AddFreeMemory", 0x00464510),
            (57, "MemoryPool::Init", 0x00464D80),
            (58, "MemoryPool::Close", 0x00464E10),
            (59, "MemoryPool::AllocateMemory", 0x00464E60),
            (60, "MemoryPool::VerifyPoolIntegrity", 0x00465110),
            (61, "bInitMemoryPool", 0x00465240),
            (62, "bCloseMemoryPool", 0x00465280),
            (63, "bMemoryInit", 0x004652A0),
            (64, "bWareMalloc", 0x004653D0),
        ]
        actual = [
            (hook["_id"], hook["_name"], hook["_address"])
            for hook in catalogue["hooks"][48:]
        ]
        self.assertEqual(actual, expected)
        self.assertNotIn("bFunkCallASync", {hook["_name"] for hook in catalogue["hooks"]})

        zbware_mask = catalogue["_profile_by_name"]["zbware"]["_mask"]
        self.assertTrue(all(hook["_profile_mask"] & zbware_mask for hook in catalogue["hooks"][48:]))
        self.assertTrue(all(not hook["_profile_mask"] & zbware_mask for hook in catalogue["hooks"][:48]))

    def test_schema4_header_and_entry_event_round_trip(self) -> None:
        header = decoder.read_header(io.BytesIO(make_schema4_header()))

        self.assertEqual(header["schema_version"], 4)
        self.assertEqual(header["header_size"], decoder.SCHEMA4_HEADER_SIZE)
        self.assertEqual(header["hook_count"], 64)
        self.assertEqual(header["enabled_hook_count"], 16)
        self.assertEqual(header["profile_name"], "zbware")
        self.assertTrue(header["catalogue_matches_local"])

        event = decoder.decode_event(
            test_schema3.make_entry(header, sequence=1, hook_id=55, schema_version=4),
            expected_schema=4,
            manifest=header["manifest"],
            image_base=0x00400000,
            preferred_base=0x00400000,
            read_call_budget=12,
            queue_capacity=2048,
            max_data_records=100,
        )
        self.assertEqual(event["schema_version"], 4)
        self.assertEqual(event["hook"], "MemoryPool::TraceNewPool")
        self.assertEqual(event["regions"][0]["label"], "pool_000")

        probe_event = decoder.decode_event(
            test_schema3.make_entry(
                header,
                sequence=2,
                hook_id=55,
                schema_version=4,
                extra_event_flags=32,
            ),
            expected_schema=4,
            manifest=header["manifest"],
            image_base=0x00400000,
            preferred_base=0x00400000,
            read_call_budget=12,
            queue_capacity=2048,
            max_data_records=100,
        )
        self.assertIn("probe_assisted", probe_event["event_flag_names"])

    def test_schema3_rejects_schema4_probe_flags(self) -> None:
        header = test_schema3.parse_header(test_schema3.make_header())
        with self.assertRaisesRegex(decoder.DecodeError, "unknown event flags"):
            decoder.decode_event(
                test_schema3.make_entry(header, sequence=1, extra_event_flags=16),
                expected_schema=3,
                manifest=header["manifest"],
                image_base=0x00400000,
                preferred_base=0x00400000,
                read_call_budget=12,
                queue_capacity=2048,
                max_data_records=100,
            )

    def test_schema4_rejects_nonzero_header_padding(self) -> None:
        damaged = bytearray(make_schema4_header())
        damaged[-1] = 1
        manifest_area = bytes(damaged[decoder.SCHEMA3_HEADER_CORE_SIZE :])
        core = list(decoder.SCHEMA3_HEADER_CORE.unpack_from(damaged))
        core[8] = hashlib.sha256(manifest_area).digest()
        core[33] = bytes(32)
        damaged[: decoder.SCHEMA3_HEADER_CORE_SIZE] = test_schema3.seal_header_core(
            decoder.SCHEMA3_HEADER_CORE.pack(*core)
        )

        with self.assertRaisesRegex(decoder.DecodeError, "manifest padding is nonzero"):
            decoder.read_header(io.BytesIO(damaged))


if __name__ == "__main__":
    unittest.main()
