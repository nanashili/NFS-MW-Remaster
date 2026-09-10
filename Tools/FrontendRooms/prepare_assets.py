"""Lossless texture recovery and bounded geometry inventory for four platform packs."""
import sys,json,hashlib
from pathlib import Path
from collections import defaultdict,Counter
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/FrontendAssets'))
from archive_decode import read_chunks
from texture_archive import iter_textures
GAME=Path('/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted/FRONTEND/PLATFORMS')
ROOMS={'SafeHouse':'CAREER_SAFEHOUSE','Showroom':'CAR_LOT','Performance':'CUSTOMIZATION_SHOP_BACKROOM','Visual':'CUSTOMIZATION_SHOP'}
def sha(b):return hashlib.sha256(b).hexdigest()
reports=[]
for room,archive in ROOMS.items():
    source=GAME/(archive+'.BIN');raw=source.read_bytes()
    dest=ROOT/'Art/FrontendRooms/Artwork'/archive;png=dest/'textures-png';png.mkdir(exist_ok=True)
    report={'room':room,'archive':archive,'source':str(source),'sha256':sha(raw),'textures':[],'models':[]}
    for r in iter_textures(raw,read_chunks(raw)):
        if r.error:raise ValueError(r.error)
        m=r.metadata;key='0x'+m['nameHash'].upper();dds=dest/'textures'/(key+'.dds')
        if m['platformFormat']==41:
            assert m['paletteEntries']==256 and len(r.palette)==1024
            assert m['baseImageSize']==m['width']*m['height']
            pal=[bytes((r.palette[i+2],r.palette[i+1],r.palette[i],r.palette[i+3])) for i in range(0,1024,4)]
            im=Image.frombytes('RGBA',(m['width'],m['height']),b''.join(pal[x] for x in r.payload[:m['baseImageSize']]))
            method='Original P8 bytes with BGRA palette recovery'
        else:im=Image.open(dds).convert('RGBA');method='AssetDumper DDS decoded losslessly'
        assert im.size==(m['width'],m['height'])
        file=png/(key+'.png');im.save(file)
        report['textures'].append({'hash':key,'name':m['name'],'width':im.width,'height':im.height,'alphaExtrema':im.getchannel('A').getextrema(),'alphaUsage':m['alphaUsage'],'alphaBlend':m['alphaBlend'],'method':method,'pngSha256':sha(file.read_bytes())})
    for file in sorted((dest/'models').glob('*.obj')):
        verts=[];groups=defaultdict(list);mat=''
        for line in file.read_text().splitlines():
            p=line.split()
            if not p:continue
            if p[0]=='v':verts.append(tuple(map(float,p[1:4])))
            elif p[0]=='usemtl':mat=p[1]
            elif p[0]=='f':groups[mat].append([int(v.split('/')[0])-1 for v in p[1:]])
        info={'file':file.name,'vertices':len(verts),'faces':sum(map(len,groups.values())),'bounds':[[min(v[i] for v in verts),max(v[i] for v in verts)] for i in range(3)] if verts else [],'materials':[]}
        for name,faces in groups.items():
            pts=[verts[v] for f in faces for v in f]
            info['materials'].append({'name':name,'faces':len(faces),'minZ':min(v[2] for v in pts),'maxZ':max(v[2] for v in pts)})
        report['models'].append(info)
    report['sourceUnchanged']=sha(source.read_bytes())==report['sha256'];assert report['sourceUnchanged']
    (dest/'manifest.json').write_text(json.dumps(report,indent=2))
    reports.append({'room':room,'archive':archive,'textures':len(report['textures']),'models':[{'name':m['file'].split(archive+'-')[-1],'faces':m['faces'],'z':m['bounds'][2]} for m in report['models'] if m['faces']>100]})
(ROOT/'Tools/FrontendRooms/inventory.json').write_text(json.dumps(reports,indent=2))
print(json.dumps(reports,indent=2))
