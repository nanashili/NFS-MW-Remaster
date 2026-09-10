"""Resolve original textures, source pivots, markers and stock chassis dimensions."""
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

from decode import ROOT, GAME, OUT
sys.path.insert(0, str(ROOT / 'Tools/WorldTools'))
from inventory_game import chunks
sys.path.insert(0, str(ROOT / 'Tools/FrontendAssets'))
from archive_decode import read_chunks, unwrap, ArchiveError
import texture_archive
from texture_archive import iter_textures, decode_texture
from texture_decode import Image, png_bytes

def cached_unwrap(data):
    if data[:4] != b'HUFF':
        return unwrap(data)
    key = hashlib.sha256(data).hexdigest()
    path = OUT / 'HuffCache' / (key + '.raw')
    if not path.exists():
        raise ArchiveError('Missing HUFF cache')
    raw = path.read_bytes()
    if len(raw) != struct.unpack_from('<I',data,8)[0]:
        raise ArchiveError('HUFF size mismatch')
    return raw, {'codec':'HUFF-native-CompLib','sha256':key}

texture_archive.unwrap = cached_unwrap

def records(path):
    raw, _ = unwrap(path.read_bytes())
    return list(iter_textures(raw, read_chunks(raw)))

def prepare(selected):
    report = json.loads((OUT / 'decoding-manifest.json').read_text())
    report['carFolders'].setdefault('BMWM3', 'Assets/NfsMw/Content/Vehicles/Street/BMW/M3')
    if '--all-parts' in selected:
        selected = selected - {'--all-parts'}
        for folder in sorted((OUT / 'Models').iterdir()):
            if (folder / 'GEOMETRY/models').is_dir():
                report['carFolders'].setdefault(folder.name, 'Assets/NfsMw/Content/Customization/Sources/' + folder.name + '/Geometry')
    visual = {}
    for row in json.loads((OUT / 'visual-attributes.json').read_text()):
        fields = {f['name']: f for f in row['fields']}
        name = fields.get('CollectionName', {}).get('values', [{}])[0].get('text')
        if name:
            visual[name.upper()] = {'record': row['key'], 'tireOffsets': fields.get('TireOffsets', {}).get('vector4'),
                                    'extraRearTireOffset': fields.get('ExtraRearTireOffset', {}).get('values', [{}])[0].get('numericValue', 0)}
    handling = json.loads((ROOT / 'Library/DrivingMechanics/car-remaster-handling.json').read_text())
    record_map = {r['id']: r for r in handling['records']}
    chassis = {}
    for vehicle in handling['vehicles']:
        link = next((l for l in vehicle['links'] if l['fieldName'] == 'chassis' and l['status'] == 'resolved'), None)
        if not link:
            continue
        fields = {}
        for f in record_map[link['targetRecordId']]['fields']:
            if f['name'] not in ('WHEEL_BASE', 'FRONT_AXLE', 'TRACK_WIDTH', 'RIDE_HEIGHT'):
                continue
            v = f['values'][0]
            fields[f['name']] = [c['numericValue'] for c in v['components']] if v['components'] else v['numericValue']
        if vehicle['model'] not in chassis or vehicle['name'].upper() == vehicle['model']:
            chassis[vehicle['model']] = dict(fields, vehicle=vehicle['name'], record=link['targetRecordId'])
    common = {}
    if 'BMWM3' not in chassis:
        chassis['BMWM3'] = dict(chassis['BMWM3GTRE46'], assemblyFallback='BMWM3GTRE46; no BMWM3 pvehicle record')
    for rel in ('CARS/TEXTURES.BIN', 'GLOBAL/GLOBALA.BUN', 'GLOBAL/GLOBALB.BUN'):
        for t in records(GAME / rel):
            common.setdefault(t.metadata.get('nameHash'), (t, rel))
    for car, asset_dir in report['carFolders'].items():
        if selected and car not in selected:
            continue
        raw_dir = OUT / 'Models' / car / 'GEOMETRY/models'
        if not raw_dir.exists():
            continue
        source = GAME / 'CARS' / car / 'GEOMETRY.BIN'
        data = source.read_bytes()
        solids = {}
        current = None
        for kind, offset, size, depth in chunks(data):
            p = (offset + 23) & ~15
            end = offset + 8 + size
            if kind == 0x134011:
                name = data[p+160:end].split(b'\0')[0].decode('ascii')
                current = {'pivot': list(struct.unpack_from('<16f', data, p+64)), 'markers': [],
                           'boundsMin': list(struct.unpack_from('<3f', data, p+32)),
                           'boundsMax': list(struct.unpack_from('<3f', data, p+48))}
                solids[name] = current
            elif kind == 0x13401a:
                assert current is not None and (end-p) % 80 == 0
                for q in range(p, end, 80):
                    current['markers'].append({'hash': f'{struct.unpack_from("<I",data,q)[0]:08x}',
                                               'matrix': list(struct.unpack_from('<16f',data,q+16))})
        textures = dict(common)
        own_keys = set()
        texture_path = GAME / 'CARS' / car / 'TEXTURES.BIN'
        for t in records(texture_path) if texture_path.exists() else []:
            textures[t.metadata.get('nameHash')] = (t, f'CARS/{car}/TEXTURES.BIN')
            if t.metadata.get('nameHash'):
                own_keys.add(t.metadata['nameHash'])
        target = OUT / 'Prepared' / car
        (target / 'Textures').mkdir(parents=True, exist_ok=True)
        mats = {}
        needed = set(own_keys)
        for mtl in raw_dir.glob('*.mtl'):
            material = None
            for line in mtl.read_text().splitlines():
                if line.startswith('newmtl '):
                    material = line[7:]
                elif line.startswith('map_Kd '):
                    key = re.search(r'0x([0-9a-fA-F]{8})\.dds', line).group(1).lower()
                    mats[mtl.stem + '/' + material] = key
                    needed.add(key)
        decoded = {}
        for key in sorted(needed):
            if key not in textures:
                decoded[key] = {'status': 'unresolved'}
                continue
            t, rel = textures[key]
            m = t.metadata
            try:
                if t.error:
                    raise ValueError(t.error)
                if m['platformFormat'] == 41:
                    assert len(t.palette) == 1024 and m['baseImageSize'] == m['width'] * m['height']
                    pal = [bytes((t.palette[i+2], t.palette[i+1], t.palette[i], t.palette[i+3])) for i in range(0,1024,4)]
                    im = Image(m['width'],m['height'],b''.join(pal[i] for i in t.payload[:m['baseImageSize']]))
                else:
                    im = decode_texture(t)
                path = target / 'Textures' / (key + '.png')
                path.write_bytes(png_bytes(im))
                decoded[key] = {'name':m['name'],'width':im.width,'height':im.height,'source':rel,
                                'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'alphaUsage':m['alphaUsage']}
            except (ValueError, AssertionError) as error:
                decoded[key] = {'status':'decode error','reason':str(error),'name':m['name']}
        output = {'car':car,'assetFolder':asset_dir,'sourceSha256':hashlib.sha256(data).hexdigest(),
                  'solids':solids,'materials':mats,'textures':decoded,'chassis':chassis.get(car),
                  'visual':visual.get(car) or visual.get(chassis.get(car, {}).get('vehicle', '').upper())}
        (target / 'source.json').write_text(json.dumps(output,indent=2)+'\n')
        print(car,len(solids),'parts;',sum('status' not in t for t in decoded.values()),'textures;',
              sum('status' in t for t in decoded.values()),'unresolved;',chassis.get(car),flush=True)

if __name__ == '__main__':
    prepare(set(sys.argv[1:]))
