"""Import decoded images into their curated UI, world, and vehicle locations."""
from decode_ui import PROJECT, OUT, json, sha256
from decode_frontend import ASSETS, immutable_write, texture_meta, preserve_or_create_meta, publish_manifest, json_bytes
from image_library import locate_texture

source = json.loads((OUT / 'manifest.json').read_text())
catalog = json.loads((ASSETS / 'Catalog.json').read_text())
boundary = PROJECT / 'Assets/NfsMw/Content'
report = {'newTextures': 0, 'reusedTextures': 0, 'uiTextures': 0, 'worldAndVehicleTextures': 0}
records = {t['assetPath']: t for t in catalog['textures']}

for texture in source['textures']:
    location = locate_texture(texture['nameHash'], texture['pngSha256'])
    path = PROJECT / location['assetPath']
    content = (PROJECT / texture['path']).read_bytes()
    assert sha256(content) == texture['pngSha256'], texture['name']
    status = immutable_write(path, content, boundary)
    preserve_or_create_meta(path.with_suffix('.png.meta'),
                            texture_meta(location['assetPath'], location['guid']), boundary)
    report['newTextures' if status == 'created' else 'reusedTextures'] += 1
    if location['resourcePath']:
        report['uiTextures'] += 1
        # Preserve existing alias order, blend metadata and capture provenance.
        if location['assetPath'] not in records:
            record = {key: texture[key] for key in ('name', 'nameHash', 'width', 'height', 'pngSha256', 'sources', 'alphaExtrema')}
            record.update(assetPath=location['assetPath'], resourcePath=location['resourcePath'])
            records[location['assetPath']] = record
    else:
        report['worldAndVehicleTextures'] += 1

catalog['textures'] = list(records.values())
publish_manifest(json_bytes(catalog))
print(json.dumps(report))
