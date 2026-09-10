"""Stage only bounded texture HUFF streams for the existing native decoder."""
import hashlib
import json
import struct
from prepare import GAME, OUT, records

cache = OUT / 'HuffCache'
cache.mkdir(parents=True, exist_ok=True)
entries = []
paths = sorted((GAME / 'CARS').glob('*/TEXTURES.BIN')) + sorted((GAME / 'CARS').glob('*/VINYLS.BIN')) + sorted((GAME / 'CARS').glob('*/PREVINYL.BIN')) + [GAME / 'CARS/TEXTURES.BIN', GAME / 'GLOBAL/GLOBALA.BUN', GAME / 'GLOBAL/GLOBALB.BUN']
for path in paths:
    data = path.read_bytes()
    for t in records(path):
        m = t.metadata
        if 'streamFileOffset' not in m:
            continue
        stored = data[m['streamFileOffset']:m['streamFileOffset']+m['streamStoredSize']]
        if stored[:4] != b'HUFF':
            continue
        assert 16 <= len(stored) <= 67108864 and 0 < struct.unpack_from('<I',stored,8)[0] <= 67108864
        key = hashlib.sha256(stored).hexdigest()
        (cache / (key + '.huff')).write_bytes(stored)
        entries.append({'source':str(path.relative_to(GAME)),'texture':m['nameHash'],'sha256':key})
(cache / 'texture-streams.txt').write_text(''.join(k+'.huff\n' for k in sorted({e['sha256'] for e in entries})))
(cache / 'manifest.json').write_text(json.dumps(entries,indent=2)+'\n')
print('Staged',len(entries),'texture streams')
