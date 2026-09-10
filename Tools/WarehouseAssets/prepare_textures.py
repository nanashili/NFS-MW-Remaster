"""Repair AssetDumper 1.0.0.1 texture limitations using the same local archives.

AssetDumper remains the geometry/DDS exporter. Its 2018 P8 DDS output omits
palettes; decode those from the original records. BGRA palette ordering follows
NFSTools/NFS-ModTools TexturePack.GenerateImage and DDSHeader RGB masks.
Existing frontend parsers are reused without changing their UI-only decoder.
"""
import sys, json, hashlib
from pathlib import Path
from collections import Counter
from PIL import Image

PROJECT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(PROJECT / 'Tools/FrontendAssets'))
from archive_decode import read_chunks, unwrap
from texture_archive import iter_textures

GAME = Path('/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted')
OUT = PROJECT / 'Art/FrontendCourtyard/Artwork/PlatformCrib'
PNG = OUT / 'textures-png'
PNG.mkdir(exist_ok=True)
SOURCE = GAME / 'FRONTEND/PLATFORMS/PlatformCrib.BIN'

def digest(data):
    return hashlib.sha256(data).hexdigest()

source = SOURCE.read_bytes()
records = list(iter_textures(source, read_chunks(source)))
report = {'archive': str(SOURCE), 'archiveSha256': digest(source),
          'exporter': 'AssetDumper v1.0.0.1 via CrossOver NFS MW',
          'exporterSource': 'https://nfsmods.xyz/mod/684',
          'paletteReference': 'https://github.com/NFSTools/NFS-ModTools/blob/master/Common/Textures/Data/DDSHeader.cs',
          'textures': []}

for r in records:
    if r.error:
        raise ValueError(r.error)
    m=r.metadata
    key='0x'+m['nameHash'].upper()
    dds=OUT/'textures'/(key+'.dds')
    if m['platformFormat']==41:
        assert m['paletteEntries']==256 and len(r.palette)==1024
        assert m['baseImageSize']==m['width']*m['height']
        pal=[bytes((r.palette[i+2],r.palette[i+1],r.palette[i],r.palette[i+3])) for i in range(0,1024,4)]
        pixels=b''.join(pal[index] for index in r.payload[:m['baseImageSize']])
        im=Image.frombytes('RGBA',(m['width'],m['height']),pixels)
        method='P8 BGRA palette from original archive; exporter DDS lacks palette'
    else:
        im=Image.open(dds).convert('RGBA')
        method='AssetDumper DDS decoded by Pillow'
    assert im.size==(m['width'],m['height'])
    dest=PNG/(key+'.png');im.save(dest)
    report['textures'].append({'hash':key,'name':m['name'],'width':im.width,'height':im.height,
        'sourceFormat':m['platformFormat'],'alphaUsage':m['alphaUsage'],'alphaBlend':m['alphaBlend'],
        'alphaExtrema':im.getchannel('A').getextrema(),'method':method,
        'sourceImageSha256':m['sourceImageSha256'],'exporterDdsSha256':digest(dds.read_bytes()),
        'pngSha256':digest(dest.read_bytes()),'decodedPixelsSha256':digest(im.tobytes())})

# Missing material keys are searched only in the game's common texture packs.
needed={'00163c76','578bc664'}
report['missingMaterialKeys']=sorted(needed)
report['commonPackSearch']=[]
for rel in ['GLOBAL/GLOBALA.BUN','GLOBAL/GLOBALB.BUN','GLOBAL/DYNTEX.BIN','GLOBAL/InGameA.bun']:
    d=(GAME/rel).read_bytes();raw,info=unwrap(d)
    rs=list(iter_textures(raw,read_chunks(raw)))
    found=[r.metadata for r in rs if r.metadata.get('nameHash') in needed]
    report['commonPackSearch'].append({'archive':rel,'sha256':digest(d),'matches':found})
report['sourceUnchanged']=digest(SOURCE.read_bytes())==report['archiveSha256']
assert report['sourceUnchanged']
(PROJECT/'Tools/WarehouseAssets/decoding-manifest.json').write_text(json.dumps(report,indent=2)+'\n')
print('Converted',len(report['textures']),'textures;',Counter(t['method'] for t in report['textures']))
print('Common pack matches',[(v['archive'],[m['name'] for m in v['matches']]) for v in report['commonPackSearch']])
