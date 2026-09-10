"""Faithful 2K artwork packaging; run with the system Python on macOS.

Uses native sips resampling, with explicitly approved masters in reconstructions.json.
Original decoding files and glTF geometry/material contracts stay untouched.
"""
import hashlib
import json
import re
import shutil
import struct
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/FrontendRooms/Artwork2K'
ART = re.compile(r'GRAF|SIGN|BANNER|POSTER|OFFICE|PIZZABOX|NEWSPAPER|SOCIALS|CASTROL')

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def dimensions(path):
    with path.open('rb') as stream:
        header = stream.read(26)
    assert header[:8] == b'\x89PNG\r\n\x1a\n', path
    width, height = struct.unpack('>II', header[16:24])
    return width, height, header[25] in (4, 6)

def run():
    OUT.mkdir(parents=True, exist_ok=True)
    reconstruction_file = OUT / 'reconstructions.json'
    reconstructions = json.loads(reconstruction_file.read_text()) if reconstruction_file.exists() else {}
    manifests = sorted((ROOT / 'Art/FrontendRooms/Artwork').glob('*/manifest.json'))
    sources = {}
    for manifest in manifests:
        for entry in json.loads(manifest.read_text())['textures']:
            path = manifest.parent / 'textures-png' / (entry['hash'] + '.png')
            if path.exists():
                sources.setdefault(entry['hash'], []).append(path)
    for path in (ROOT / 'Art/FrontendCourtyard/Artwork/PlatformCrib/textures-png').glob('*.png'):
        sources.setdefault(path.stem, []).append(path)
    models = sorted((ROOT / 'Assets/NfsMw/Content/World/Models').glob('Frontend*/*.gltf'))
    entries = {}
    geometry = {}
    for model in models:
        data = json.loads(model.read_text())
        geometry[str(model.relative_to(ROOT))] = {'gltfSha256': sha(model), 'binSha256': sha(model.with_suffix('.bin'))}
        for material in data.get('materials', []):
            texture = material.get('pbrMetallicRoughness', {}).get('baseColorTexture')
            if not texture or not ART.search(material['name']):
                continue
            image = data['images'][data['textures'][texture['index']]['source']]
            path = model.parent / image['uri']
            key = path.stem
            assert re.fullmatch(r'0x[0-9A-Fa-f]{8}', key), key
            entry = entries.setdefault(key, {'hash': key, 'materials': [], 'destinations': [], 'candidates': []})
            entry['materials'].append(material['name'])
            entry['destinations'].append(str(path.relative_to(ROOT)))
            entry['candidates'].append(path)
    for key, entry in entries.items():
        candidates = sources.get(key, []) or entry['candidates']
        source = max(candidates, key=lambda path: dimensions(path)[0] * dimensions(path)[1])
        width, height, alpha = dimensions(source)
        target_size = (round(width * 2048 / max(width, height)), round(height * 2048 / max(width, height)))
        output = OUT / (key + '_2K.png')
        reconstruction = reconstructions.get(key)
        master = ROOT / reconstruction['master'] if reconstruction else source
        output_alpha = dimensions(master)[2]
        subprocess.run(['sips', '-z', str(target_size[1]), str(target_size[0]), str(master), '--out', str(output)], check=True, stdout=subprocess.DEVNULL)
        assert dimensions(output) == (*target_size, output_alpha), (source, dimensions(output))
        entry.update(source=str(source.relative_to(ROOT)), sourceSha256=sha(source), sourceSize=[width, height], size=list(target_size), alpha=alpha, output=str(output.relative_to(ROOT)), outputSha256=sha(output))
        if reconstruction:
            entry.update(reconstruction=reconstruction, alpha=output_alpha, masterSize=list(dimensions(master)[:2]))
        del entry['candidates']
        entry['destinations'] = sorted(set(entry['destinations']))
        entry['materials'] = sorted(set(entry['materials']))
        for destination in entry['destinations']:
            shutil.copy2(output, ROOT / destination)
    for name, hashes in geometry.items():
        model = ROOT / name
        assert sha(model) == hashes['gltfSha256'] and sha(model.with_suffix('.bin')) == hashes['binSha256']
    reconstructed = sum('reconstruction' in entry for entry in entries.values())
    method = 'Native macOS sips faithful resampling; no AI redrawing or sharpening'
    if reconstructed:
        method += '; except ' + str(reconstructed) + ' explicitly approved image-generation reconstruction(s), recorded per artwork'
    report = {'method': method, 'resolution': '2048 pixels on the longer edge; original aspect ratio retained', 'originalDecodingFilesUnchanged': True, 'geometryAndMaterialContractsUnchanged': True, 'assetCount': len(entries), 'reconstructedCount': reconstructed, 'textureCopies': sum(len(e['destinations']) for e in entries.values()), 'models': geometry, 'artworks': list(entries.values())}
    (OUT / 'artwork-report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({'assetCount': report['assetCount'], 'textureCopies': report['textureCopies'], 'models': len(geometry)}))

if __name__ == '__main__':
    run()
