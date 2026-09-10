"""Paint editable Unity Terrain from highest original natural-ground materials."""
import array, collections, hashlib, json, math, statistics, struct, subprocess
from pathlib import Path
from inventory_game import ROOT
SOURCE=ROOT/'Art/RockportGround/Source'
OUT=ROOT/'Assets/NfsMw/Content/World/Maps/Rockport/PaintMaps'
GROUPS=[('Grass','156e7175'),('Dark grass','8beffd79'),('Golf rough','0319b1a2'),('Golf fairway','074bc2d0'),('Rock','761cfb13'),('Cliff','761ea036'),('Sand','fc21a2e1'),('Dirt','c8779df9')]
# Explicit material-index mapping recorded alongside the generated paint maps.
MAPPING=[0,0,0,5,0,5,1,5,5,0,5,5,4,4,4,3,0,0,2,2,3,3,2,6,7,3,3,0,0,0,0,4,6,6,1,6,0,4,4,7,7]
def main():
    OUT.mkdir(parents=True,exist_ok=True)
    folder=ROOT/'Assets/NfsMw/Content/World/Models/RockportTerrainSource';doc=json.loads((folder/'RockportTerrainSource.gltf').read_text());blob=(folder/'RockportTerrainSource.bin').read_bytes()
    def values(index,fmt):
        a=doc['accessors'][index];v=doc['bufferViews'][a['bufferView']];return list(struct.iter_unpack('<'+fmt,blob[v.get('byteOffset',0)+a.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']]))
    cache={};scales=[[] for _ in GROUPS];count=0
    with (SOURCE/'paint-triangles.bin').open('wb') as output:
        for node in doc['nodes']:
            mesh=node['mesh']
            if mesh not in cache:
                prims=[]
                for p in doc['meshes'][mesh]['primitives']:
                    group=MAPPING[p['material']];pos=values(p['attributes']['POSITION'],'3f');uv=values(p['attributes']['TEXCOORD_0'],'2f');idx=[v[0] for v in values(p['indices'],'I')];prims.append((group,pos,idx))
                    for i in range(0,len(idx),3):
                        a,b,c=[pos[k] for k in idx[i:i+3]];u,v,w=[uv[k] for k in idx[i:i+3]]
                        e=[b[k]-a[k] for k in range(3)];f=[c[k]-a[k] for k in range(3)]
                        area=math.sqrt(sum((e[(k+1)%3]*f[(k+2)%3]-e[(k+2)%3]*f[(k+1)%3])**2 for k in range(3)))
                        ua=abs((v[0]-u[0])*(w[1]-u[1])-(v[1]-u[1])*(w[0]-u[0]))
                        if area>1 and ua>1e-6:scales[group].append(math.sqrt(area/ua))
                cache[mesh]=prims
            t=node['translation']
            for group,pos,idx in cache[mesh]:
                world=[(-(v[0]+t[0]),v[1]+t[1],v[2]+t[2]) for v in pos]
                for i in range(0,len(idx),3):output.write(struct.pack('<I9f',group,*(v for k in idx[i:i+3] for v in world[k])));count+=1
    meta=json.loads((ROOT/'Assets/NfsMw/Content/World/Maps/Rockport/Heightmaps/metadata.json').read_text());w=(meta['width']-1)//2;h=(meta['height']-1)//2
    exe=SOURCE/'raster_materials';subprocess.run(['clang++','-O3','-std=c++17',str(ROOT/'Tools/WorldTools/raster_materials.cpp'),'-o',str(exe)],check=True)
    subprocess.run([str(exe),str(SOURCE/'paint-triangles.bin'),str(SOURCE/'paint-labels.bin'),str(w),str(h),str(meta['originX']+1),str(meta['originZ']+1),'2'],check=True)
    labels=(SOURCE/'paint-labels.bin').read_bytes();assert len(labels)==w*h
    tiles=[]
    for tile in meta['tiles']:
        data=b''.join(labels[(tile['zIndex']*512+z)*w+tile['xIndex']*512:(tile['zIndex']*512+z)*w+tile['xIndex']*512+512] for z in range(512))
        (OUT/(tile['name']+'.paint')).write_bytes(data);tiles.append({'name':tile['name'],'counts':dict(collections.Counter(data)),'sha256':hashlib.sha256(data).hexdigest()})
    groups=[{'name':name,'texture':f'Assets/NfsMw/Content/World/Models/RockportTerrainSource/Textures/{key}.png','tileSize':1024 / round(1024 / max(2,min(128,statistics.median(scales[i]))))} for i,(name,key) in enumerate(GROUPS)]
    (OUT/'layers.tsv').write_text(''.join(f"{g['name']}\t{g['texture']}\t{g['tileSize']}\n" for g in groups))
    (OUT/'metadata.json').write_text(json.dumps({'resolution':512,'spacingMetres':2,'rowOrder':'Minimum Unity Z first','policy':'Highest original natural-ground triangle at each pixel centre. Eight representative original textures; original UV layout and transition textures are approximated. Unsampled pixels use grass; terrain holes remain unchanged.','groups':groups,'materialMapping':[{'material':m['name'],'group':MAPPING[i]} for i,m in enumerate(doc['materials'])],'triangles':count,'tiles':tiles},indent=2)+'\n')
    print(json.dumps({'tiles':len(tiles),'triangles':count,'groups':groups},indent=2))
if __name__=='__main__':main()
