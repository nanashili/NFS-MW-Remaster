#!/usr/bin/env python3
"""Print bounded structural GIN table observations; no semantic labels are inferred."""
import struct
import sys

def inspect(path):
    data = open(path, "rb").read()
    count_a, count_b, frames, rate = struct.unpack_from("<4I", data, 16)
    def table(offset, count):
        return [struct.unpack_from("<I", data, offset + i * 4)[0] for i in range(count + 1)]
    a = table(0x20, count_a)
    b = table(0x20 + 4 * (count_a + 1), count_b)
    def direction(values):
        if all(x <= y for x, y in zip(values, values[1:])): return "ascending"
        if all(x >= y for x, y in zip(values, values[1:])): return "descending"
        return "non-monotonic"
    e0, e1 = struct.unpack_from("<2f", data, 8)
    print(path)
    print(" bytes=%d endpoints=%g,%g counts=%d,%d frames=%d rate=%d" % (len(data), e0, e1, len(a), len(b), frames, rate))
    print(" table_a first=%d last=%d direction=%s step=%d..%d" % (a[0], a[-1], direction(a), min(y-x for x,y in zip(a,a[1:])), max(y-x for x,y in zip(a,a[1:]))))
    print(" table_b first=%d last=%d direction=%s step=%d..%d" % (b[0], b[-1], direction(b), min(y-x for x,y in zip(b,b[1:])), max(y-x for x,y in zip(b,b[1:]))))

if __name__ == "__main__":
    for path in sys.argv[1:]: inspect(path)
