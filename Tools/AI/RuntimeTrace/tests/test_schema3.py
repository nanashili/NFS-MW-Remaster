from __future__ import annotations

import hashlib
import io
import json
import struct
import subprocess
import sys
import tempfile
import unittest
from copy import deepcopy
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import decoder  # noqa: E402
import generate_catalogue  # noqa: E402
import validate_target  # noqa: E402


def fixed_text(value: str, size: int) -> bytes:
    raw = value.encode("ascii")
    if len(raw) >= size:
        raise ValueError(value)
    return raw.ljust(size, b"\0")


def seal_header_core(core: bytes) -> bytes:
    if len(core) != decoder.SCHEMA3_HEADER_CORE_SIZE:
        raise AssertionError(len(core))
    result = bytearray(core)
    at = decoder.SCHEMA3_HEADER_DIGEST_OFFSET
    result[at : at + 32] = bytes(32)
    result[at : at + 32] = hashlib.sha256(result).digest()
    return bytes(result)


def seal_record(raw: bytes) -> bytes:
    if len(raw) != decoder.SCHEMA3_EVENT_SIZE:
        raise AssertionError(len(raw))
    result = bytearray(raw)
    at = decoder.SCHEMA3_EVENT_DIGEST_OFFSET
    result[at : at + 16] = bytes(16)
    result[at : at + 16] = hashlib.sha256(result).digest()[:16]
    return bytes(result)


def build_manifest(profile: str, *, hook_count: int = 48) -> tuple[bytes, list[int]]:
    catalogue = generate_catalogue.load_catalogue()
    profile_mask = int(catalogue["_profile_by_name"][profile]["_mask"])
    entries: list[bytes] = []
    divisors: list[int] = []
    for hook in catalogue["hooks"][:hook_count]:
        divisor = int(hook["_default_divisor"]) if int(hook["_profile_mask"]) & profile_mask else 0
        divisors.append(divisor)
        stolen = bytes(hook["_stolen"]).ljust(16, b"\0")
        lengths = bytes(hook["_instruction_lengths"]).ljust(8, b"\0")
        prefix = decoder.SCHEMA3_MANIFEST_PREFIX.pack(
            int(hook["_id"]),
            int(hook["_address"]),
            int(hook["_rva"]),
            int(hook["_profile_mask"]),
            int(divisor != 0),
            divisor,
            len(hook["args"]),
            int(hook["_this_adjust"]),
            int(hook["_flags"]),
            int(hook["_return_code"]),
            int(hook["_arg_type_bits"]),
            int(hook["_relocation_code"]),
            int(hook["_relocation_offset"]),
            int(hook["_relocation_target"]),
            len(hook["_stolen"]),
            len(hook["_instruction_lengths"]),
            stolen,
            lengths,
            fixed_text(str(hook["_name"]), 72),
            fixed_text(str(hook["_config_key"]), 48),
            fixed_text(str(hook["_recovery"]), 16),
        )
        regions = list(hook["_regions"])
        while len(regions) < decoder.SCHEMA3_REGION_COUNT:
            regions.append({"source_code": 0, "offset": 0, "length": 0, "label": ""})
        encoded_regions = b"".join(
            decoder.SCHEMA3_MANIFEST_REGION.pack(
                int(region["source_code"]),
                int(region["offset"]) if int(region["offset"]) < (1 << 31) else int(region["offset"]) - (1 << 32),
                int(region["length"]),
                int(int(region["length"]) != 0),
                fixed_text(str(region["label"]), 16),
            )
            for region in regions
        )
        entry = prefix + encoded_regions
        if len(entry) != decoder.SCHEMA3_MANIFEST_SIZE:
            raise AssertionError(len(entry))
        entries.append(entry)
    return b"".join(entries), divisors


def make_header(*, profile: str = "survey", state: int = 6, max_records: int = 2) -> bytes:
    catalogue = generate_catalogue.load_catalogue()
    manifest, divisors = build_manifest(profile)
    profile_definition = catalogue["_profile_by_name"][profile]
    config_digest = hashlib.sha256(b"schema3-test-config").digest()
    core = decoder.SCHEMA3_HEADER_CORE.pack(
        b"NMWAITR3",
        3,
        decoder.SCHEMA3_HEADER_SIZE,
        decoder.SCHEMA3_EVENT_SIZE,
        decoder.SCHEMA3_MANIFEST_SIZE,
        bytes.fromhex(decoder.EXPECTED_TARGET_SHA256),
        config_digest,
        hashlib.sha256(b"schema3-historical-catalogue").digest(),
        hashlib.sha256(manifest).digest(),
        0x00400000,
        0x00400000,
        0x00678E4E,
        42,
        1337,
        10_000_000,
        1000,
        bytes.fromhex("00112233445566778899aabbccddeeff"),
        48,
        sum(divisor != 0 for divisor in divisors),
        8 if profile == "all" else int(profile_definition["_id"]),
        0xF,
        max_records,
        8,
        3,
        256,
        12,
        256,
        1000,
        state,
        fixed_text("0.3.4", 32),
        fixed_text("speed.exe PC 1.3", 32),
        fixed_text(profile, 32),
        fixed_text("NFSMWAITrace.ini", 64),
        bytes(32),
        bytes(572),
    )
    result = seal_header_core(core) + manifest
    if len(result) != decoder.SCHEMA3_HEADER_SIZE:
        raise AssertionError(len(result))
    return result


def parse_header(raw: bytes) -> dict[str, object]:
    return decoder.read_header(io.BytesIO(raw))


def counter_tuple(
    *,
    admitted: int,
    enqueued: int,
    queue_losses: int = 0,
    budget_rejections: int = 0,
    read_faults: int = 0,
    read_budget: int = 0,
    write_failures: int = 0,
    written: int = 0,
) -> tuple[int, ...]:
    return (
        admitted,
        enqueued,
        queue_losses,
        budget_rejections,
        read_faults,
        read_budget,
        write_failures,
        written,
    )


def make_entry(
    header: dict[str, object],
    *,
    sequence: int,
    hook_id: int = 1,
    call_index: int = 1,
    sample_index: int = 1,
    counters: tuple[int, ...] | None = None,
    schema_version: int = 3,
    extra_event_flags: int = 0,
) -> bytes:
    hook = header["manifest"][hook_id - 1]
    raw_this = 0x10000000
    normalized_this = raw_this + int(hook["this_adjust"])
    arguments = [0] * 8
    arg_count = int(hook["arg_count"])
    if arg_count:
        arguments[0] = 0x3C23D70A
    arg_valid_mask = (1 << arg_count) - 1 if arg_count else 0
    region_addresses: list[int] = []
    region_valid_masks: list[int] = []
    region_requested: list[int] = []
    region_captured: list[int] = []
    region_payloads: list[bytes] = []
    for index, region in enumerate(hook["regions"]):
        requested = int(region["length"])
        address = decoder.schema3_expected_region_address(
            region,
            raw_this=raw_this,
            normalized_this=normalized_this,
            normalized_valid=True,
            args=tuple(arguments),
            arg_valid_mask=arg_valid_mask,
            image_base=int(str(header["image_base"]), 16),
            preferred_base=int(str(header["preferred_base"]), 16),
        )
        region_addresses.append(address or 0)
        region_requested.append(requested)
        region_captured.append(requested)
        region_valid_masks.append((1 << ((requested + 15) // 16)) - 1 if requested else 0)
        region_payloads.append(bytes([index + 1]) * requested + bytes(256 - requested))
    event_flags = 1 | 2 | extra_event_flags
    prefix = decoder.SCHEMA3_EVENT_PREFIX.pack(
        decoder.SCHEMA3_EVENT_MAGIC,
        decoder.SCHEMA3_EVENT_SIZE,
        schema_version,
        1,
        sequence,
        1000 + sequence,
        call_index,
        sample_index,
        5000 + sequence,
        7,
        hook_id,
        int(hook["address_value"]),
        raw_this,
        normalized_this,
        0x00401234,
        0x11111111,
        0x22222222,
        0x202,
        arg_valid_mask,
        *arguments,
        *region_addresses,
        *region_valid_masks,
        *region_requested,
        *region_captured,
        event_flags,
        1 + sum(requested != 0 for requested in region_requested),
        *(counters or counter_tuple(admitted=sequence, enqueued=sequence)),
        min(sequence, int(header["queue_capacity"])),
        0,
        bytes(16),
        bytes(8),
    )
    return seal_record(prefix + b"".join(region_payloads))


def make_terminal(
    *,
    sequence: int,
    counters: tuple[int, ...],
    reason: int = 1,
) -> bytes:
    raw = decoder.SCHEMA3_EVENT_PREFIX.pack(
        decoder.SCHEMA3_EVENT_MAGIC,
        decoder.SCHEMA3_EVENT_SIZE,
        3,
        3,
        sequence,
        1000 + sequence,
        0,
        0,
        5000 + sequence,
        7,
        *([0] * 9),
        *([0] * 8),
        *([0] * 3),
        *([0] * 3),
        *([0] * 3),
        *([0] * 3),
        0,
        0,
        *counters,
        min(sequence - 1, 8),
        reason,
        bytes(16),
        bytes(8),
    ) + bytes(3 * 256)
    return seal_record(raw)


def decode_events(header: dict[str, object], payload: bytes) -> list[dict[str, object]]:
    return list(
        decoder.iter_events(
            io.BytesIO(payload),
            expected_schema=3,
            manifest=header["manifest"],
            image_base=int(str(header["image_base"]), 16),
            preferred_base=int(str(header["preferred_base"]), 16),
            read_call_budget=int(header["read_call_budget"]),
        )
    )


class Schema3DecoderTests(unittest.TestCase):
    def setUp(self) -> None:
        self.header_raw = make_header()
        self.header = parse_header(self.header_raw)

    def test_header_manifest_and_local_catalogue_identity(self) -> None:
        self.assertEqual(self.header["schema_version"], 3)
        self.assertEqual(self.header["hook_count"], 48)
        self.assertEqual(self.header["enabled_hook_count"], 12)
        self.assertFalse(self.header["catalogue_matches_local"])
        self.assertEqual(self.header["manifest"][43]["relocation"]["target"], "0x00409e60")
        self.assertEqual(self.header["manifest"][44]["relocation"]["target"], "0x00409e60")

    def test_manifest_hash_corruption_is_rejected(self) -> None:
        damaged = bytearray(self.header_raw)
        damaged[decoder.SCHEMA3_HEADER_CORE_SIZE + 100] ^= 0x01
        with self.assertRaisesRegex(decoder.DecodeError, "manifest SHA-256"):
            parse_header(bytes(damaged))

    def test_manifest_rejects_out_of_image_relocation_without_local_catalogue(self) -> None:
        damaged = bytearray(self.header_raw)
        entry_at = decoder.SCHEMA3_HEADER_CORE_SIZE + 43 * decoder.SCHEMA3_MANIFEST_SIZE
        fields = list(decoder.SCHEMA3_MANIFEST_PREFIX.unpack_from(damaged, entry_at))
        address = int(fields[1])
        relocation_offset = int(fields[12])
        relocation_target = 0x10000000
        displacement = (relocation_target - (address + relocation_offset + 5)) & 0xFFFFFFFF
        stolen = bytearray(fields[16])
        struct.pack_into("<I", stolen, relocation_offset + 1, displacement)
        fields[13] = relocation_target
        fields[16] = bytes(stolen)
        damaged[entry_at : entry_at + decoder.SCHEMA3_MANIFEST_PREFIX.size] = (
            decoder.SCHEMA3_MANIFEST_PREFIX.pack(*fields)
        )
        manifest = bytes(damaged[decoder.SCHEMA3_HEADER_CORE_SIZE :])
        core = list(decoder.SCHEMA3_HEADER_CORE.unpack_from(damaged))
        core[7] = hashlib.sha256(b"non-local-catalogue").digest()
        core[8] = hashlib.sha256(manifest).digest()
        core[33] = bytes(32)
        damaged[: decoder.SCHEMA3_HEADER_CORE_SIZE] = seal_header_core(
            decoder.SCHEMA3_HEADER_CORE.pack(*core)
        )
        with self.assertRaisesRegex(decoder.DecodeError, "relocation target is outside"):
            parse_header(bytes(damaged))

    def test_header_rejects_unknown_profile_without_local_catalogue(self) -> None:
        damaged = bytearray(self.header_raw)
        core = list(decoder.SCHEMA3_HEADER_CORE.unpack_from(damaged))
        core[7] = hashlib.sha256(b"non-local-catalogue").digest()
        core[19] = 999
        core[31] = fixed_text("invented", 32)
        core[33] = bytes(32)
        damaged[: decoder.SCHEMA3_HEADER_CORE_SIZE] = seal_header_core(
            decoder.SCHEMA3_HEADER_CORE.pack(*core)
        )
        with self.assertRaisesRegex(decoder.DecodeError, "profile identity"):
            parse_header(bytes(damaged))

    def test_header_and_event_truncation_are_rejected(self) -> None:
        with self.assertRaisesRegex(decoder.DecodeError, "truncated schema-3 header"):
            parse_header(self.header_raw[:-1])
        event = make_entry(self.header, sequence=1)
        with self.assertRaisesRegex(decoder.DecodeError, "truncated schema-3 event"):
            decode_events(self.header, event[:-1])

    def test_header_core_and_record_payload_corruption_are_rejected(self) -> None:
        damaged_header = bytearray(self.header_raw)
        damaged_header[200] ^= 0x01
        with self.assertRaisesRegex(decoder.DecodeError, "header-core SHA-256"):
            parse_header(bytes(damaged_header))

        damaged_record = bytearray(make_entry(self.header, sequence=1))
        damaged_record[decoder.SCHEMA3_EVENT_PREFIX.size] ^= 0x01
        with self.assertRaisesRegex(decoder.DecodeError, "record SHA-256/128"):
            decode_events(self.header, bytes(damaged_record))

    def test_exact_indices_and_closed_terminal_integrity(self) -> None:
        events = decode_events(
            self.header,
            make_entry(self.header, sequence=1, call_index=1, sample_index=1, counters=counter_tuple(admitted=1, enqueued=1))
            + make_entry(self.header, sequence=2, call_index=129, sample_index=2, counters=counter_tuple(admitted=2, enqueued=2, written=1))
            + make_terminal(sequence=3, counters=counter_tuple(admitted=2, enqueued=2, written=2)),
        )
        summary = decoder.build_summary(self.header, events)
        observed = summary["hook_reachability"]["AIVehicle::OnTaskSimulate"]
        self.assertEqual(observed["call_index_max"], 129)
        self.assertEqual(observed["sample_index_max"], 2)
        self.assertEqual(observed["unobserved_sample_slots_within_observed_span"], 0)
        self.assertTrue(summary["integrity"]["complete_closed_capture"])
        self.assertTrue(summary["integrity"]["counter_equations"]["admitted_equals_enqueued_plus_queue_losses"])

    def test_invalid_sampling_index_is_rejected(self) -> None:
        with self.assertRaisesRegex(decoder.DecodeError, "invalid call/sample index"):
            decode_events(self.header, make_entry(self.header, sequence=1, call_index=2, sample_index=1))
        with self.assertRaisesRegex(decoder.DecodeError, "sample index"):
            decode_events(self.header, make_entry(self.header, sequence=1, call_index=129, sample_index=3))

    def test_physical_sequence_reordering_is_not_hidden(self) -> None:
        events = decode_events(
            self.header,
            make_entry(self.header, sequence=2, call_index=129, sample_index=2, counters=counter_tuple(admitted=2, enqueued=2))
            + make_entry(self.header, sequence=1, call_index=1, sample_index=1, counters=counter_tuple(admitted=1, enqueued=1)),
        )
        integrity = decoder.build_summary(self.header, events)["integrity"]
        self.assertFalse(integrity["on_disk_strictly_increasing"])
        self.assertFalse(integrity["on_disk_contiguous"])
        self.assertTrue(integrity["counter_monotonicity_failures"])

    def test_queue_loss_equation_and_missing_sample_slot(self) -> None:
        events = decode_events(
            self.header,
            make_entry(self.header, sequence=1, call_index=1, sample_index=1, counters=counter_tuple(admitted=1, enqueued=1))
            + make_entry(self.header, sequence=2, call_index=257, sample_index=3, counters=counter_tuple(admitted=3, enqueued=2, queue_losses=1))
            + make_terminal(sequence=3, counters=counter_tuple(admitted=3, enqueued=2, queue_losses=1, written=2)),
        )
        summary = decoder.build_summary(self.header, events)
        observed = summary["hook_reachability"]["AIVehicle::OnTaskSimulate"]
        self.assertEqual(observed["unobserved_sample_slots_within_observed_span"], 1)
        self.assertTrue(summary["integrity"]["counter_equations"]["admitted_equals_enqueued_plus_queue_losses"])
        self.assertFalse(summary["integrity"]["complete_closed_capture"])

    def test_all_profile_resolves_every_catalogue_hook(self) -> None:
        header = parse_header(make_header(profile="all"))
        self.assertEqual(header["enabled_hook_count"], 48)
        self.assertTrue(all(hook["enabled"] and int(hook["divisor"]) > 0 for hook in header["manifest"]))

    def test_stream_summary_matches_complete_small_capture(self) -> None:
        events = decode_events(
            self.header,
            make_entry(self.header, sequence=1, counters=counter_tuple(admitted=1, enqueued=1))
            + make_entry(
                self.header,
                sequence=2,
                call_index=129,
                sample_index=2,
                counters=counter_tuple(admitted=2, enqueued=2, written=1),
            )
            + make_terminal(sequence=3, counters=counter_tuple(admitted=2, enqueued=2, written=2), reason=5),
        )
        self.header["file_layout"] = {
            "size_bytes": decoder.SCHEMA3_HEADER_SIZE + 3 * decoder.SCHEMA3_EVENT_SIZE,
            "complete_record_count": 3,
            "truncated_tail_bytes": 0,
        }
        streamed, arguments = decoder.build_schema3_stream_summary(self.header, iter(events))
        self.assertEqual(streamed["event_count"], 3)
        self.assertEqual(streamed["integrity"]["terminal_reason"], "stop_request")
        self.assertTrue(streamed["integrity"]["structurally_complete_closed_capture"])
        self.assertTrue(streamed["integrity"]["lossless_admitted_capture"])
        self.assertEqual(
            streamed["hook_reachability"]["AIVehicle::OnTaskSimulate"]["sample_index_max"],
            2,
        )
        self.assertIn("arg0", arguments["AIVehicle::OnTaskSimulate"])

    def test_tail_is_reported_even_when_explicitly_tolerated(self) -> None:
        payload = (
            make_entry(self.header, sequence=1, counters=counter_tuple(admitted=1, enqueued=1))
            + make_terminal(sequence=2, counters=counter_tuple(admitted=1, enqueued=1, written=1))
        )
        with tempfile.TemporaryDirectory() as directory:
            capture = Path(directory) / "tail.bin"
            capture.write_bytes(self.header_raw + payload + b"x")
            header, events = decoder.read_capture(capture, tolerate_truncated_tail=True)
        summary = decoder.build_summary(header, events)
        self.assertEqual(summary["integrity"]["truncated_tail_bytes"], 1)
        self.assertFalse(summary["integrity"]["structurally_complete_closed_capture"])

    def test_cli_output_replaces_only_after_complete_decode(self) -> None:
        entry = make_entry(self.header, sequence=1, counters=counter_tuple(admitted=1, enqueued=1))
        corrupt_terminal = bytearray(
            make_terminal(sequence=2, counters=counter_tuple(admitted=1, enqueued=1, written=1))
        )
        corrupt_terminal[0] ^= 0xFF
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            capture = root / "corrupt.bin"
            output = root / "existing.jsonl"
            capture.write_bytes(self.header_raw + entry + corrupt_terminal)
            output.write_text("preserve-me\n", encoding="utf-8")
            result = subprocess.run(
                [
                    sys.executable,
                    str(ROOT / "decoder.py"),
                    str(capture),
                    "--format",
                    "jsonl",
                    "--output",
                    str(output),
                ],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("invalid schema-3 event", result.stderr)
            self.assertEqual(output.read_text(encoding="utf-8"), "preserve-me\n")
            self.assertEqual(list(root.glob(".existing.jsonl.*.tmp")), [])

    def test_valid_blocks_cannot_exceed_captured_byte_count(self) -> None:
        raw = bytearray(make_entry(self.header, sequence=1))
        fields = list(decoder.SCHEMA3_EVENT_PREFIX.unpack_from(raw))
        fields[36] = 1
        raw[: decoder.SCHEMA3_EVENT_PREFIX.size] = decoder.SCHEMA3_EVENT_PREFIX.pack(*fields)
        with self.assertRaisesRegex(decoder.DecodeError, "smaller than valid blocks"):
            decode_events(self.header, seal_record(bytes(raw)))

    def test_closed_capture_distinguishes_queue_loss_from_structure(self) -> None:
        header = parse_header(make_header(max_records=3))
        events = decode_events(
            header,
            make_entry(header, sequence=1, counters=counter_tuple(admitted=1, enqueued=1))
            + make_entry(
                header,
                sequence=2,
                call_index=257,
                sample_index=3,
                counters=counter_tuple(admitted=3, enqueued=2, queue_losses=1, written=1),
            )
            + make_terminal(
                sequence=3,
                counters=counter_tuple(admitted=3, enqueued=2, queue_losses=1, written=2),
            ),
        )
        integrity = decoder.build_summary(header, events)["integrity"]
        self.assertTrue(integrity["structurally_complete_closed_capture"])
        self.assertFalse(integrity["lossless_admitted_capture"])

    def test_recovered_terminal_write_failure_prevents_complete_certification(self) -> None:
        events = decode_events(
            self.header,
            make_entry(self.header, sequence=1, counters=counter_tuple(admitted=1, enqueued=1))
            + make_terminal(
                sequence=2,
                counters=counter_tuple(admitted=1, enqueued=1, write_failures=1, written=1),
            ),
        )
        integrity = decoder.build_summary(self.header, events)["integrity"]
        self.assertFalse(integrity["no_reported_write_failures"])
        self.assertFalse(integrity["structurally_complete_closed_capture"])


class CatalogueContractTests(unittest.TestCase):
    def test_generated_header_is_current_and_relocations_are_exact(self) -> None:
        catalogue = generate_catalogue.load_catalogue()
        generated = generate_catalogue.render_header(catalogue)
        self.assertEqual(generated, generate_catalogue.GENERATED_PATH.read_text(encoding="utf-8"))
        for hook_id in (44, 45):
            hook = catalogue["hooks"][hook_id - 1]
            offset = int(hook["_relocation_offset"])
            displacement = struct.unpack_from("<i", bytes(hook["_stolen"]), offset + 1)[0]
            resolved = (int(hook["_address"]) + offset + 5 + displacement) & 0xFFFFFFFF
            self.assertEqual(resolved, int(hook["_relocation_target"]))
        self.assertTrue(all(int(hook["_address"]) % 8 == 0 for hook in catalogue["hooks"]))

    def test_generated_target_identity_matches_decoder_contract(self) -> None:
        catalogue = generate_catalogue.load_catalogue()
        generated = generate_catalogue.render_header(catalogue)
        self.assertEqual(catalogue["target"]["sha256"], decoder.EXPECTED_TARGET_SHA256)
        self.assertIn("#define TRACE_CATALOGUE_IMAGE_BASE 0x00400000u", generated)
        self.assertIn("#define TRACE_CATALOGUE_IMAGE_SIZE 0x00678e4eu", generated)
        self.assertIn('#define TRACE_CATALOGUE_TARGET_NAME "speed.exe PC 1.3"', generated)
        expected_bytes = ", ".join(f"0x{byte:02x}u" for byte in bytes.fromhex(decoder.EXPECTED_TARGET_SHA256))
        self.assertIn(f"static const uint8_t kExpectedTargetSha256[32] = {{{expected_bytes}}};", generated)

    def test_catalogue_rejects_duplicate_keys_relative_branches_and_bad_regions(self) -> None:
        original_text = generate_catalogue.CATALOGUE_PATH.read_text(encoding="utf-8")
        original = json.loads(original_text)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "catalogue.json"
            path.write_text(original_text.replace('"catalogue_version": 1,', '"catalogue_version": 1,\n  "catalogue_version": 1,'), encoding="utf-8")
            with self.assertRaisesRegex(generate_catalogue.CatalogueError, "duplicate JSON object key"):
                generate_catalogue.load_catalogue(path)

            uppercase = deepcopy(original)
            uppercase["target"]["sha256"] = uppercase["target"]["sha256"].upper()
            path.write_text(json.dumps(uppercase), encoding="utf-8")
            with self.assertRaisesRegex(generate_catalogue.CatalogueError, "canonical lowercase"):
                generate_catalogue.load_catalogue(path)

            relative = deepcopy(original)
            relative["hooks"][0]["stolen"] = "e9 00 00 00 00"
            relative["hooks"][0]["instruction_lengths"] = [5]
            path.write_text(json.dumps(relative), encoding="utf-8")
            with self.assertRaisesRegex(generate_catalogue.CatalogueError, "relative instructions"):
                generate_catalogue.load_catalogue(path)

            misaligned = deepcopy(original)
            misaligned["hooks"][0]["va"] = "0x004081e1"
            path.write_text(json.dumps(misaligned), encoding="utf-8")
            with self.assertRaisesRegex(generate_catalogue.CatalogueError, "not 8-byte aligned"):
                generate_catalogue.load_catalogue(path)

            atomic_outside = deepcopy(original)
            atomic_outside["hooks"][0]["va"] = "0x00a78e48"
            path.write_text(json.dumps(atomic_outside), encoding="utf-8")
            with self.assertRaisesRegex(generate_catalogue.CatalogueError, "atomic patch extends outside"):
                generate_catalogue.load_catalogue(path)

            outside = deepcopy(original)
            outside["hooks"][0].pop("region_template")
            outside["hooks"][0]["regions"] = [
                {"source": "absolute", "offset": "0x10000000", "length": 16, "label": "outside"}
            ]
            path.write_text(json.dumps(outside), encoding="utf-8")
            with self.assertRaisesRegex(generate_catalogue.CatalogueError, "outside the exact target image"):
                generate_catalogue.load_catalogue(path)

    def test_reference_budget_and_queue_accounting(self) -> None:
        # Deterministic contract model: every admitted record is eventually
        # enqueued or explicitly lost; the limit closes admission exactly once.
        maximum = 4
        queue_capacity = 2
        queue: list[int] = []
        admitted = enqueued = losses = rejected = 0
        for call in range(1, 7):
            if admitted >= maximum:
                rejected += 1
                continue
            admitted += 1
            if len(queue) < queue_capacity:
                queue.append(call)
                enqueued += 1
            else:
                losses += 1
            if call == 3:
                queue.pop(0)
        self.assertEqual(admitted, maximum)
        self.assertEqual(admitted, enqueued + losses)
        self.assertEqual(rejected, 2)


class LoaderConfigurationTests(unittest.TestCase):
    def test_loader_identity_is_exact_digest_gated(self) -> None:
        changed_loader = b"structurally plausible but unaudited loader"
        with mock.patch.object(
            validate_target,
            "read_pe",
            return_value=(
                changed_loader,
                {"machine": 0x14C, "characteristics": 0x2000},
                [],
            ),
        ):
            with self.assertRaisesRegex(
                validate_target.ValidationError,
                r"SHA-256 .* != audited Ultimate ASI Loader",
            ):
                validate_target.validate_loader(Path("dinput8.dll"))

    def test_defaults_are_deferred_and_enabled(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            result = validate_target.validate_loader_configuration(Path(directory), "dinput8.dll")
        self.assertTrue(result["deferred_plugin_loading"])
        self.assertTrue(result["plugin_loading_enabled"])
        self.assertEqual(result["files_read"], [])

    def test_rejects_dllmain_plugin_loading(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "dinput8.ini").write_text(
                "[globalsets]\ndontloadfromdllmain=0\nforceentrypointhook=0\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(validate_target.ValidationError, "load plugins from DllMain"):
                validate_target.validate_loader_configuration(root, "dinput8.dll")

    def test_later_config_can_restore_deferred_loading(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "dinput8.ini").write_text(
                "[globalsets]\ndontloadfromdllmain=0\n",
                encoding="utf-8",
            )
            (root / "plugins").mkdir()
            (root / "plugins/global.ini").write_text(
                "[globalsets]\nforceentrypointhook=1\n",
                encoding="utf-8",
            )
            result = validate_target.validate_loader_configuration(root, "dinput8.dll")
        self.assertTrue(result["deferred_plugin_loading"])
        self.assertEqual(result["effective"]["forceentrypointhook"], 1)

    def test_rejects_disabled_or_ambiguous_loader_settings(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "global.ini").write_text("[globalsets]\nloadplugins=0\n", encoding="utf-8")
            with self.assertRaisesRegex(validate_target.ValidationError, "plugin loading is disabled"):
                validate_target.validate_loader_configuration(root, "dinput8.dll")
            (root / "global.ini").write_text(
                "[globalsets]\nloadplugins=1\nloadplugins=1\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(validate_target.ValidationError, "duplicate"):
                validate_target.validate_loader_configuration(root, "dinput8.dll")


if __name__ == "__main__":
    unittest.main()
