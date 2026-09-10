"""Bounded reader for observed Most Wanted PC solid geometry.

Layout reference: NFS-ModTools 897c170 Common/Geometry/MostWantedSolidReader.cs.
No game bytes are modified; unknown vertex layouts fail explicitly.
"""
import struct
from inventory_game import chunks

def aligned(offset, multiple=16):
    return (offset + multiple - 1) & ~(multiple - 1)

def read_solid(data, offset, size, vertices=True):
    end = offset + 8 + size
    solid = {'offset': offset, 'materials': [], 'buffers': []}
    textures = []
    names = []
    for kind, start, length, depth in chunks(data, offset + 8, end):
        p, stop = start + 8, start + 8 + length
        if kind == 0x134011:
            p = aligned(p)
            assert p + 160 <= stop and data[p + 12] == 22
            solid.update(hash=f'{struct.unpack_from("<I",data,p+16)[0]:08x}',
                name=data[p+160:stop].split(b'\0')[0].decode('ascii'),
                boundsMin=list(struct.unpack_from('<3f',data,p+32)),
                boundsMax=list(struct.unpack_from('<3f',data,p+48)),
                pivot=list(struct.unpack_from('<16f',data,p+64)))
        elif kind == 0x134012:
            assert length % 8 == 0
            textures = [f'{struct.unpack_from("<I",data,q)[0]:08x}' for q in range(p, stop, 8)]
        elif kind == 0x134B02:
            p = aligned(p)
            assert (stop - p) % 104 == 0
            stream, last_effect = 0, None
            for q in range(p, stop, 104):
                effect, pointer, flags, count, triangles = struct.unpack_from('<5I',data,q+48)
                if last_effect is not None and effect != last_effect:
                    stream += 1
                ids = list(data[q+24:q+29])
                assert all(i < len(textures) for i in ids[:4])
                solid['materials'].append({'effect': effect, 'flags': flags, 'vertexCount': count,
                    'triangleCount': triangles, 'stream': stream, 'diffuse': textures[ids[0]],
                    'normal': textures[ids[1]] if ids[1] != ids[0] else None,
                    'specular': textures[ids[3]] if ids[3] != ids[0] else None})
                last_effect = effect
        elif kind == 0x134B03:
            p = aligned(p)
            for material in solid['materials']:
                length = material['triangleCount'] * 3 * 2
                assert p + length <= stop
                material['indicesOffset'] = p
                p += length
            assert 0 <= stop-p < 4 and not any(data[p:stop]), (solid.get('name'), 'index padding', stop-p)
        elif kind == 0x134B01:
            p = aligned(p, 128)
            assert p <= stop
            solid['buffers'].append({'offset': p, 'bytes': stop-p})
        elif kind == 0x134C02:
            names.append(data[p:stop].split(b'\0')[0].decode('ascii'))
    assert len(names) == len(solid['materials']), solid.get('name')
    for material, name in zip(solid['materials'], names):
        material['name'] = name
    for index, buffer in enumerate(solid['buffers']):
        materials = [m for m in solid['materials'] if m['stream'] == index]
        count = sum(m['vertexCount'] for m in materials)
        assert count > 0 and buffer['bytes'] % count == 0, solid['name']
        stride = buffer['bytes'] // count
        buffer.update(count=count, stride=stride)
        assert all({0:36,1:60,2:60,3:60,4:36,5:36,6:36,19:44}.get(m['effect']) == stride for m in materials), (solid['name'],stride,[m['effect'] for m in materials])
        if vertices:
            buffer['positions'] = [struct.unpack_from('<3f',data,q) for q in range(buffer['offset'],buffer['offset']+buffer['bytes'],stride)]
            buffer['normals'] = [struct.unpack_from('<3f',data,q+12) for q in range(buffer['offset'],buffer['offset']+buffer['bytes'],stride)]
            buffer['uv'] = [struct.unpack_from('<2f',data,q+28) for q in range(buffer['offset'],buffer['offset']+buffer['bytes'],stride)]
            for material in materials:
                n = material['triangleCount']*3
                material['indices'] = struct.unpack_from('<'+str(n)+'H',data,material['indicesOffset'])
                assert not n or max(material['indices']) < count, solid['name']
    return solid

if __name__ == '__main__':
    import json, mmap
    from inventory_game import GAME, OUT
    with (GAME/'TRACKS/STREAML2RA.BUN').open('rb') as handle, mmap.mmap(handle.fileno(),0,access=mmap.ACCESS_READ) as data:
        solids=[read_solid(data,o,s,False) for k,o,s,d in chunks(data) if k==0x80134010]
    (OUT/'geometry-inventory.json').write_text(json.dumps({'solids':solids},indent=2)+'\n')
    print('Parsed',len(solids),'solid records')
