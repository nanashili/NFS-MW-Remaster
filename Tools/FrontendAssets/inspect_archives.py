"""Read-only inventory of the explicitly staged frontend archive copies."""

from __future__ import annotations

import collections
from pathlib import Path

from archive_decode import ArchiveError, MAX_ARCHIVE_BYTES, read_chunks, sha256, unwrap

SOURCE = Path(__file__).resolve().parent / "Source"


def main() -> None:
    for name in ("FRONTEND/FrontB.lzc", "GLOBAL/GLOBALA.BUN", "GLOBAL/GLOBALB.BUN",
                 "GLOBAL/RIVALS.BIN", "GLOBAL/DYNTEX.BIN", "GLOBAL/FE_ATTRIB.bin"):
        path = SOURCE / name
        if path.is_symlink() or not path.resolve().is_relative_to(SOURCE.resolve()):
            raise ValueError("Source must be an ordinary in-root file")
        if not path.is_file():
            print(name, "MISSING")
            continue
        if path.stat().st_size > MAX_ARCHIVE_BYTES:
            raise ValueError("Source size exceeds limit")
        raw = path.read_bytes()
        print("\nSOURCE", name, len(raw), sha256(raw), raw[:16].hex())
        try:
            data, compression = unwrap(raw)
            print("COMPRESSION", compression)
            chunks = read_chunks(data)
            print("CHUNKS", len(chunks), {f"{kind:08x}": count for kind, count in
                                           collections.Counter(c.kind for c in chunks).most_common(30)})
            for c in chunks:
                if c.kind in (0x33310001, 0x33310002, 0x33310003, 0x33310004, 0x33310005,
                              0x33320001, 0x33320002, 0x00030201, 0x00030203, 0x00030210):
                    print(f"  {c.offset:08x} {c.kind:08x} size={c.size} parent={c.parent}",
                          data[c.data_offset:min(c.end, c.data_offset + 64)].hex())
        except ArchiveError as error:
            print("UNSUPPORTED", str(error))
        if sha256(path.read_bytes()) != sha256(raw):
            raise RuntimeError("Source changed during inventory")


if __name__ == "__main__":
    main()
