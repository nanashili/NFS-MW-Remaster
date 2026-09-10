"""Read map bchunks and original placement records without modifying game files."""
from pathlib import Path
import collections, hashlib, json, mmap, struct

ROOT = Path(__file__).resolve().parents[2]
GAME = Path('/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted')
OUT = ROOT / 'Art/RockportBuildings/Source'

def chunks(data, start=0, end=None, depth=0):
    end = len(data) if end is None else end
    assert depth < 32
    while start < end:
        assert start + 8 <= end, (start, end)
        kind, size = struct.unpack_from('<II', data, start)
        stop = start + 8 + size
        assert stop <= end, (hex(kind), start, size, end)
        yield kind, start, size, depth
        if kind & 0x80000000:
            yield from chunks(data, start + 8, stop, depth + 1)
        start = stop

def run():
    OUT.mkdir(parents=True, exist_ok=True)
    report = {'archives': [], 'scenery': [], 'solidHeaders': []}
    for name in ('L2RA.BUN', 'STREAML2RA.BUN'):
        path = GAME / 'TRACKS' / name
        with path.open('rb') as handle, mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ) as data:
            digest = hashlib.sha256(data).hexdigest()
            entries = list(chunks(data))
            archive = {'source': 'TRACKS/' + name, 'bytes': len(data), 'sha256': digest,
                       'chunkCounts': dict(collections.Counter(f'{c[0]:08x}' for c in entries))}
            report['archives'].append(archive)
            for kind, offset, size, depth in entries:
                if kind == 0x80034100:
                    section = {'source': archive['source'], 'offset': offset, 'infos': [], 'instances': []}
                    for ck, co, cs, cd in chunks(data, offset + 8, offset + 8 + size):
                        p = co + 8
                        if ck == 0x34101:
                            section['sectionNumber'] = struct.unpack_from('<i', data, p + 12)[0]
                        elif ck == 0x34102:
                            assert cs % 72 == 0
                            for pos in range(p, p + cs, 72):
                                section['infos'].append({'name': data[pos:pos+24].split(b'\0')[0].decode('ascii'),
                                    'solidKeys': [f'{v:08x}' for v in struct.unpack_from('<4I', data, pos + 24)]})
                        elif ck == 0x34103:
                            start = (p + 15) & ~15
                            assert (p + cs - start) % 64 == 0
                            for pos in range(start, p + cs, 64):
                                section['instances'].append({'sourceOffset': pos,
                                    'bounds': list(struct.unpack_from('<6f', data, pos)),
                                    'excludeFlags': struct.unpack_from('<I', data, pos + 24)[0],
                                    'position': list(struct.unpack_from('<3f', data, pos + 32)),
                                    'rotationRows': [v / 8192 for v in struct.unpack_from('<9h', data, pos + 44)],
                                    'infoIndex': struct.unpack_from('<h', data, pos + 62)[0]})
                    assert all(0 <= i['infoIndex'] < len(section['infos']) for i in section['instances'])
                    report['scenery'].append(section)
                elif kind == 0x134011:
                    p = (offset + 8 + 15) & ~15
                    assert p + 160 <= offset + 8 + size
                    report['solidHeaders'].append({'source': archive['source'], 'offset': offset,
                        'hash': f'{struct.unpack_from("<I", data, p+16)[0]:08x}',
                        'name': data[p+160:offset+8+size].split(b'\0')[0].decode('ascii', errors='replace')})
            assert hashlib.sha256(data).hexdigest() == digest
            print(name, 'chunks', len(entries), 'sections', len(report['scenery']), flush=True)
    report['totals'] = {'scenerySections': len(report['scenery']),
        'instances': sum(len(s['instances']) for s in report['scenery']),
        'infoRecords': sum(len(s['infos']) for s in report['scenery']), 'visibleSolidHeaders': len(report['solidHeaders'])}
    (OUT / 'game-inventory.json').write_text(json.dumps(report, indent=2) + '\n')
    print(report['totals'])

if __name__ == '__main__':
    run()
