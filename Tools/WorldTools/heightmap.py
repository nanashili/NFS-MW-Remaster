"""Create one-metre 16-bit Unity height tiles from exported source geometry."""
import array,hashlib,json,math,struct,subprocess,sys,zlib
from pathlib import Path
from inventory_game import ROOT
from ground_classification import SURFACE
OUT=ROOT/'Art/RockportGround';SOURCE=OUT/'Source';TILES=ROOT/'Assets/NfsMw/Content/World/Maps/Rockport/Heightmaps'
def png(path,w,h,rows,depth=16):
    def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
    compressor=zlib.compressobj(6);parts=[]
    for row in rows:parts.append(compressor.compress(b'\0'+row))
    parts.append(compressor.flush());path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>2I5B',w,h,depth,0,0,0,0))+chunk(b'IDAT',b''.join(parts))+chunk(b'IEND',b''))
def triangles():
    bounds=[[math.inf]*3,[-math.inf]*3];count=[0,0]
    with (SOURCE/'heightmap-triangles.bin').open('wb') as output:
        for layer,name in enumerate(('RockportTerrainSource','RockportRoads')):
            folder=ROOT/'Assets/NfsMw/Content/World/Models'/name;doc=json.loads((folder/(name+'.gltf')).read_text());blob=(folder/(name+'.bin')).read_bytes();cache={}
            def values(index,fmt):
                a=doc['accessors'][index];v=doc['bufferViews'][a['bufferView']];return list(struct.iter_unpack('<'+fmt,blob[v['byteOffset']:v['byteOffset']+v['byteLength']]))
            for node in doc['nodes']:
                if node['mesh'] not in cache:
                    prims=[]
                    for p in doc['meshes'][node['mesh']]['primitives']:
                        if layer and not SURFACE.search(doc['materials'][p['material']]['name'].upper()):continue
                        pos=values(p['attributes']['POSITION'],'3f');idx=values(p['indices'],'I');prims.append((pos,[i[0] for i in idx]))
                    cache[node['mesh']]=prims
                p=node['translation']
                for positions,indices in cache[node['mesh']]:
                    world=[(-(v[0]+p[0]),v[1]+p[1],v[2]+p[2]) for v in positions]
                    for i in range(0,len(indices),3):
                        points=[world[j] for j in indices[i:i+3]];output.write(struct.pack('<I9f',layer,*(x for v in points for x in v)));count[layer]+=1
                    for v in world:
                        for q in range(3):bounds[0][q]=min(bounds[0][q],v[q]);bounds[1][q]=max(bounds[1][q],v[q])
    return bounds,count
def main():
    SOURCE.mkdir(parents=True,exist_ok=True);TILES.mkdir(parents=True,exist_ok=True)
    bounds,count=triangles();tile_size=1024;ox=math.floor(bounds[0][0]/tile_size)*tile_size;oz=math.floor(bounds[0][2]/tile_size)*tile_size
    nx=math.ceil((bounds[1][0]-ox)/tile_size);nz=math.ceil((bounds[1][2]-oz)/tile_size);width=nx*tile_size+1;height=nz*tile_size+1
    print('Rasterizing',count,'triangles',width,height,'grid',flush=True)
    executable=SOURCE/'raster_heightmap';subprocess.run(['clang++','-O3','-std=c++17',str(ROOT/'Tools/WorldTools/raster_heightmap.cpp'),'-o',str(executable)],check=True)
    raw=subprocess.check_output([str(executable),str(SOURCE/'heightmap-triangles.bin'),str(SOURCE/'heightmap'),str(width),str(height),str(ox),str(oz),'1'],text=True);stats=json.loads(raw);print(raw,flush=True)
    low=math.floor(stats['heightMin'])-1;high=math.ceil(stats['heightMax'])+1;span=high-low
    h=array.array('f');h.frombytes((SOURCE/'heightmap.f32').read_bytes());coverage=(SOURCE/'heightmap.coverage').read_bytes();multi=(SOURCE/'heightmap.multilevel').read_bytes()
    metadata={'coordinateSystem':'Unity world X,Y,Z; original game -> (-X,Z,-Y)','width':width,'height':height,'originX':ox,'originZ':oz,'sampleSpacing':1,'tileSize':tile_size,'tileResolution':tile_size+1,'minHeight':low,'heightRange':span,'sourceBounds':bounds,'stats':stats,'quantizationMaxErrorMetres':span/65535/2,'rowOrder':'RAW row zero is minimum Unity Z; PNG row zero is maximum Unity Z. Little-endian unsigned 16-bit RAW.','heightPolicy':'Highest natural-ground intersection; if absent, lowest road-surface intersection. No gap filling. Multi-level conflicts flagged above 0.5 m. Original meshes remain the exact geometry reference.','tiles':[]}
    for tz in range(nz):
        for tx in range(nx):
            samples=array.array('H');mask=bytearray();terrain_mask=bytearray();conflicts=bytearray();valid_count=0
            for z in range(tile_size+1):
                start=(tz*tile_size+z)*width+tx*tile_size
                for k in range(start,start+tile_size+1):
                    valid=coverage[k];mask.append(255 if valid else 0);terrain_mask.append(255 if valid==1 else 0);conflicts.append(255 if multi[k] else 0);valid_count+=bool(valid)
                    samples.append(round((h[k]-low)/span*65535) if valid else 0)
            if not valid_count:continue
            name=f'height_x{tx:02}_z{tz:02}';path=TILES/(name+'.raw');path.write_bytes(samples.tobytes());(TILES/(name+'.coverage')).write_bytes(mask);(TILES/(name+'.terraincoverage')).write_bytes(terrain_mask)
            rowlen=tile_size+1
            png(TILES/(name+'.png'),rowlen,rowlen,(struct.pack('>'+str(rowlen)+'H',*samples[z*rowlen:(z+1)*rowlen]) for z in range(tile_size,-1,-1)))
            metadata['tiles'].append({'name':name,'xIndex':tx,'zIndex':tz,'originX':ox+tx*tile_size,'originZ':oz+tz*tile_size,'validSamples':valid_count,'rawSha256':hashlib.sha256(path.read_bytes()).hexdigest()})
        print('Wrote tile row',tz+1,'of',nz,flush=True)
    # Compact full-map previews expose sparse coverage and multiple elevations.
    stride=4;pw=(width-1)//stride+1;ph=(height-1)//stride+1
    png(OUT/'heightmap-preview.png',pw,ph,(bytes(round((h[z*width+x]-low)/span*255) if coverage[z*width+x] else 0 for x in range(0,width,stride)) for z in range(height-1,-1,-stride)),8)
    png(OUT/'coverage-preview.png',pw,ph,(bytes(255 if coverage[z*width+x] else 0 for x in range(0,width,stride)) for z in range(height-1,-1,-stride)),8)
    png(OUT/'multilevel-preview.png',pw,ph,(bytes(255 if multi[z*width+x] else 0 for x in range(0,width,stride)) for z in range(height-1,-1,-stride)),8)
    def full_rows():
        for z in range(height-1,-1,-1):
            start=z*width
            yield struct.pack('>'+str(width)+'H',*(round((h[k]-low)/span*65535) if coverage[k] else 0 for k in range(start,start+width)))
    png(OUT/'RockportHeightmap.png',width,height,full_rows())
    png(OUT/'RockportCoverage.png',width,height,(bytes(255 if v else 0 for v in coverage[z*width:(z+1)*width]) for z in range(height-1,-1,-1)),8)
    metadata['unityTerrainHolesPolicy']='Keep only cells with four natural-ground samples. Road-only and unobserved cells are holes; original road meshes remain the road surfaces. Full surface elevations remain available in RAW/PNG.'
    (TILES/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')
    (TILES/'tiles.tsv').write_text(''.join('\t'.join(str(t[k]) for k in ('name','xIndex','zIndex','originX','originZ'))+'\n' for t in metadata['tiles']))
    print('DONE',len(metadata['tiles']),'tiles',flush=True)
if __name__=='__main__':main()
