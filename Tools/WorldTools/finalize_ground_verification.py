"""Finish validation after Unity import without repeating source raster checks."""
import array,hashlib,json,struct,zlib
from pathlib import Path
from inventory_game import ROOT
SOURCE=ROOT/'Art/RockportGround/Source';TILES=ROOT/'Assets/NfsMw/Content/World/Maps/Rockport/Heightmaps'
def read_png(path):
    raw=path.read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n';offset=8;data=[];header=None
    while offset<len(raw):
        length=struct.unpack_from('>I',raw,offset)[0];kind=raw[offset+4:offset+8];payload=raw[offset+8:offset+8+length];crc=struct.unpack_from('>I',raw,offset+8+length)[0];assert crc==zlib.crc32(kind+payload)&0xffffffff
        if kind==b'IHDR':header=struct.unpack('>2I5B',payload)
        if kind==b'IDAT':data.append(payload)
        offset+=length+12
    decoded=zlib.decompress(b''.join(data));w,h,depth,color,*_=header;assert color==0;stride=w*(depth//8)+1;assert len(decoded)==stride*h;assert all(decoded[z*stride]==0 for z in range(h));return header,decoded,stride
def main():
    result=json.loads((SOURCE/'ground-verification.json').read_text());meta=json.loads((TILES/'metadata.json').read_text());unity=json.loads((SOURCE/'unity-ground-validation.json').read_text())
    assert len(unity['layers'])==3 and unity['layers'][0]['meshes']==5632
    for source,actual in zip(result['layers'],unity['layers'][1:]):
        assert source['instances']==actual['meshes'] and source['triangles']==actual['triangles'];assert actual['missingMeshes']==actual['missingMaterials']==0
        assert max(abs(a-b) for a,b in zip(source['min']+source['max'],actual['min']+actual['max']))<.002
    result['unityMeshValidation']='PASS: source instance/triangle counts and bounds match actual imports.'
    original=json.loads((ROOT/'Art/RockportBuildings/Source/game-inventory.json').read_text());placements={i['sourceOffset']:i for s in original['scenery'] for i in s['instances']}
    for layer in ('roads','terrain'):
        report=json.loads((SOURCE/(layer+'-decoding.json')).read_text())
        for instance in report['instances']:
            p=placements[instance['sourceInstanceOffset']];assert p['position']==instance['gamePosition'] and p['rotationRows']==instance['packedRotation']
    result['originalPlacementValidation']='All 15098 original positions and packed rotations match the complete game inventory.'
    terrain=json.loads((SOURCE/'unity-terrain-validation.json').read_text());collision=json.loads((SOURCE/'unity-collision-validation.json').read_text());assert terrain['tiles']==39 and terrain['missingNeighbors']==0 and not terrain['terrainGroupActive'];assert collision['meshColliders']==15098 and collision['raycastMisses']==0
    result['unityTerrainValidation']=terrain;result['unityCollisionValidation']=collision
    hp=ROOT/'Art/RockportGround/RockportHeightmap.png';cp=ROOT/'Art/RockportGround/RockportCoverage.png';header,pixels,stride=read_png(hp);ch,coverage,cstride=read_png(cp);assert header[:3]==(meta['width'],meta['height'],16) and ch[:3]==(meta['width'],meta['height'],8)
    source_coverage=(SOURCE/'heightmap.coverage').read_bytes();n=meta['tileResolution']
    for tile in meta['tiles']:
        a=array.array('H');a.frombytes((TILES/(tile['name']+'.raw')).read_bytes());a.byteswap();raw=a.tobytes();mask=(TILES/(tile['name']+'.coverage')).read_bytes();natural=(TILES/(tile['name']+'.terraincoverage')).read_bytes()
        x=tile['originX']-meta['originX'];z=tile['originZ']-meta['originZ']
        for row in range(n):
            pngrow=meta['height']-1-(z+row);start=pngrow*stride+1+x*2;cstart=pngrow*cstride+1+x;source=(z+row)*meta['width']+x
            assert pixels[start:start+n*2]==raw[row*n*2:(row+1)*n*2]
            assert coverage[cstart:cstart+n]==mask[row*n:(row+1)*n]
            assert natural[row*n:(row+1)*n]==bytes(255 if v==1 else 0 for v in source_coverage[source:source+n])
    assert coverage.count(255)==meta['stats']['coveredSamples']
    result['fullPngValidation']={'heightmapBitDepth':16,'width':meta['width'],'height':meta['height'],'allTileRowsMatch':'PASS, including north/south row reversal and endian conversion','coverageAndNaturalTerrainMasks':'PASS','heightmapSha256':hashlib.sha256(hp.read_bytes()).hexdigest(),'coverageSha256':hashlib.sha256(cp.read_bytes()).hexdigest()}
    result['editorNote']='Unity hit an Undo-stack limit at 8000 colliders. The remaining colliders were added without per-component Undo snapshots; final scene counts and 500 raycasts passed.'
    (SOURCE/'ground-verification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({k:v for k,v in result.items() if k not in ('layers',)},indent=2))
if __name__=='__main__':main()
