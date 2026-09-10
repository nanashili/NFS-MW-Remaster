from decode_ui import *
import struct
manifest=json.loads((OUT/'manifest.json').read_text())
cache=OUT/'HuffCache';cache.mkdir(exist_ok=True)
texture_cache=cache/'Textures';texture_cache.mkdir(exist_ok=True)
blocks=[]
def stage(stored,source,offset,kind):
    if stored[:4]!=b'HUFF':return
    key=sha256(stored);dest=cache/(key+'.huff')
    if not dest.exists():dest.write_bytes(stored)
    if kind=='texture':
        texture_dest=texture_cache/(key+'.huff')
        if not texture_dest.exists():texture_dest.write_bytes(stored)
    blocks.append({'source':source,'offset':offset,'kind':kind,'sha256':key,'storedBytes':len(stored),'expectedBytes':struct.unpack_from('<I',stored,8)[0]})
for archive in manifest['archives']:
    if 'error' in archive:continue
    data,_=unwrap((GAME/archive['source']).read_bytes());chunks=read_chunks(data)
    for record in iter_textures(data,chunks):
        m=record.metadata
        if 'streamFileOffset' in m:
            start=m['streamFileOffset'];stage(data[start:start+m['streamStoredSize']],archive['source'],start,'texture')
    for c in chunks:
        if c.kind==0x30210:
            payload=data[c.data_offset:c.end]
            if len(payload)>=20:
                length=struct.unpack_from('<I',payload,16)[0]
                if 4+length<=len(payload):stage(payload[4:4+length],archive['source'],c.data_offset+4,'frontend-package')
(cache/'manifest.json').write_text(json.dumps(blocks,indent=2)+'\n')
(texture_cache/'texture-streams.txt').write_text(''.join(key+'.huff\n' for key in sorted({b['sha256'] for b in blocks if b['kind']=='texture'})))
print('Staged',len(blocks),'references,',len(list(cache.glob('*.huff'))),'unique HUFF streams')
