"""Bounded readers for explicitly supplied, local Most Wanted archive copies.

Format evidence: cached nfsmw revision 13189413c4c6e447c2225052c66b55d76863b985,
LZCompress.hpp/.cpp and bChunk.hpp. This original Python implementation does not
execute, import, compile or modify the reconstruction or any original-game file.
"""

from __future__ import annotations

import hashlib
import struct
from dataclasses import dataclass

MAX_ARCHIVE_BYTES = 128 * 1024 * 1024
MAX_CHUNKS = 100_000
MAX_DEPTH = 32


class ArchiveError(ValueError):
    """Malformed, unsupported, or unreasonably large archive data."""


def sha256(data: bytes | memoryview) -> str:
    return hashlib.sha256(data).hexdigest()


def decompress_jdlz(data: bytes) -> tuple[bytes, dict]:
    """Read one JDLZ v2 stream, including overlapping back-references.

    The reference compressor's Size >= 0 loop can emit a spare terminal literal
    and up to two flag bytes. At most three trailing bytes are retained in the
    report; they are not treated as decoded data or silently concatenated.
    """
    if len(data) < 18 or data[:4] != b"JDLZ":
        raise ArchiveError("Missing or truncated JDLZ header/control bytes")
    version, header_size, flags, expected, compressed = struct.unpack_from("<BBHII", data, 4)
    if (version, header_size, flags) != (2, 16, 0):
        raise ArchiveError("Only JDLZ version 2, header size 16, flags 0 is supported")
    if not 0 < expected <= MAX_ARCHIVE_BYTES:
        raise ArchiveError("JDLZ output size exceeds bounds")
    if compressed != len(data) or not 18 <= compressed <= MAX_ARCHIVE_BYTES:
        raise ArchiveError("JDLZ compressed size does not match the supplied stream")
    position = 18
    control, run_type = data[16] | 0x100, data[17] | 0x100
    result = bytearray()

    def take(count: int) -> bytes:
        nonlocal position
        if position + count > compressed:
            raise ArchiveError("Truncated JDLZ token or control byte")
        value = data[position:position + count]
        position += count
        return value

    while len(result) < expected:
        if control & 1:
            first, second = take(2)
            if run_type & 1:
                length = ((first >> 4) << 8 | second) + 3
                distance = (first & 15) + 1
            else:
                length = (first & 31) + 3
                distance = ((first >> 5) << 8 | second) + 17
            if distance > len(result) or len(result) + length > expected:
                raise ArchiveError("JDLZ back-reference precedes output or exceeds declared length")
            pattern = result[-distance:]
            result.extend((pattern * ((length + distance - 1) // distance))[:length])
            run_type >>= 1
        else:
            result.extend(take(1))
        control >>= 1
        if len(result) == expected:
            break
        if control == 1:
            control = take(1)[0] | 0x100
        if run_type == 1:
            run_type = take(1)[0] | 0x100
    tail = data[position:]
    if len(tail) > 3:
        raise ArchiveError("Unexpected data following the declared JDLZ output")
    output = bytes(result)
    return output, {
        "codec": "JDLZ-v2", "compressedSize": compressed,
        "uncompressedSize": expected, "consumedBytes": position,
        "terminalBytesHex": tail.hex(), "uncompressedSha256": sha256(output),
    }


def unwrap(data: bytes) -> tuple[bytes, dict]:
    if len(data) > MAX_ARCHIVE_BYTES:
        raise ArchiveError("Input archive exceeds size limit")
    if data[:4] == b"JDLZ":
        return decompress_jdlz(data)
    if data[:4] in (b"HUFF", b"COMP", b"RAWW"):
        raise ArchiveError(f"Unsupported archive compression {data[:4]!r}")
    if data[:4] == b"\x22\x11\x44\x55":
        raise ArchiveError("Compress-in-place archive layout is not supported")
    return data, {"codec": "none", "uncompressedSize": len(data), "uncompressedSha256": sha256(data)}


@dataclass(frozen=True)
class Chunk:
    offset: int
    kind: int
    size: int
    depth: int
    parent: int | None

    @property
    def data_offset(self) -> int:
        return self.offset + 8

    @property
    def end(self) -> int:
        return self.data_offset + self.size

    def aligned_offset(self, alignment: int) -> int:
        if alignment < 1 or alignment & (alignment - 1):
            raise ArchiveError("Alignment must be a positive power of two")
        value = (self.data_offset + alignment - 1) & ~(alignment - 1)
        if value > self.end:
            raise ArchiveError("Aligned payload lies outside its chunk")
        return value


def read_chunks(data: bytes) -> list[Chunk]:
    """Walk actual chunk boundaries. No carving for signatures inside payloads."""
    if len(data) > MAX_ARCHIVE_BYTES:
        raise ArchiveError("Chunk input exceeds size limit")
    result: list[Chunk] = []

    def visit(start: int, end: int, depth: int, parent: int | None) -> None:
        if depth > MAX_DEPTH:
            raise ArchiveError("Chunk nesting exceeds limit")
        position = start
        while position < end:
            if end - position < 8:
                raise ArchiveError(f"Truncated chunk header at 0x{position:x}")
            kind, size = struct.unpack_from("<Ii", data, position)
            if size < 0 or position + 8 + size > end:
                raise ArchiveError(f"Chunk 0x{kind:08x} at 0x{position:x} exceeds its parent")
            chunk = Chunk(position, kind, size, depth, parent)
            result.append(chunk)
            if len(result) > MAX_CHUNKS:
                raise ArchiveError("Chunk count exceeds limit")
            if kind & 0x80000000:
                visit(chunk.data_offset, chunk.end, depth + 1, position)
            position = chunk.end

    visit(0, len(data), 0, None)
    return result
