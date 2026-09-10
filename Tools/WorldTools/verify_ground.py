"""Validate source placements, native raster sampling, tile seams and Unity data."""
import array,collections,hashlib,json,math,random,struct,subprocess,tempfile
from pathlib import Path
from inventory_game import ROOT
SOURCE=ROOT/'Art/RockportGround/Source';TILES=ROOT/'Assets/NfsMw/Content/World/Maps/Rockport/Heightmaps'
def main():
    result={'status':'PASS','layers':[]};meta=json.loads((TILES/'metadata.json').read_text())
    for layer,name in (('roads','RockportRoads'),('terrain','RockportTerrainSource')):
        folder=ROOT/'Assets/NfsMw/Content/World/Models'/name;doc=json.loads((folder/(name+'.gltf')).read_text());data=(folder/(name+'.bin')).read_bytes();report=json.loads((SOURCE/(layer+'-decoding.json')).read_text());triangles=0;lo=[math.inf]*3;hi=[-math.inf]*3
        assert len(data)==doc['buffers'][0]['byteLength'] and len(doc['nodes'])==len(report['instances'])
        for node,original in zip(doc['nodes'],report['instances']):
            p=original['gamePosition'];assert node['translation']==[p[0],p[2],-p[1]]
            for prim in doc['meshes'][node['mesh']]['primitives']:
                a=doc['accessors'][prim['attributes']['POSITION']];triangles+=doc['accessors'][prim['indices']]['count']//3
                mn=[-(a['max'][0]+p[0]),a['min'][1]+p[2],a['min'][2]-p[1]];mx=[-(a['min'][0]+p[0]),a['max'][1]+p[2],a['max'][2]-p[1]]
                for q in range(3):lo[q]=min(lo[q],mn[q]);hi[q]=max(hi[q],mx[q])
        for v in doc['bufferViews']:assert 0<=v['byteOffset']<len(data) and v['byteOffset']+v['byteLength']<=len(data)
        for a in doc['accessors']:
            if a['componentType']==5126:
                v=doc['bufferViews'][a['bufferView']];assert all(math.isfinite(x[0]) for x in struct.iter_unpack('<f',data[v['byteOffset']:v['byteOffset']+v['byteLength']]))
        for key,texture in report['textures'].items():
            if 'pngSha256' in texture:assert hashlib.sha256((folder/'Textures'/(key+'.png')).read_bytes()).hexdigest()==texture['pngSha256']
        assert not report['missingSolids'] and not report['invalidPlacements'] and report['counts']['sourceUnchanged']
        result['layers'].append({'layer':layer,'instances':len(doc['nodes']),'triangles':triangles,'min':lo,'max':hi,'missingTextures':[k for k,v in report['textures'].items() if 'status' in v],'gltfSha256':hashlib.sha256((folder/(name+'.gltf')).read_bytes()).hexdigest(),'binarySha256':hashlib.sha256(data).hexdigest()})
    # A plane with an overlaid raised road and a second natural layer checks
    # axis orientation, barycentric interpolation, coverage and overlap policy.
    with tempfile.TemporaryDirectory(prefix='rockport-raster-check-') as temp:
        p=Path(temp);triangles=[(0,0,2,0,4,6,0,0,10,4),(1,0,20,0,4,20,0,0,20,4),(0,0,4,0,1,4,0,0,4,1),(1,3,30,3,4,30,3,3,30,4)]
        (p/'input').write_bytes(b''.join(struct.pack('<I9f',*t) for t in triangles));subprocess.run([str(SOURCE/'raster_heightmap'),str(p/'input'),str(p/'out'),'5','5','0','0','1'],check=True,capture_output=True)
        a=array.array('f');a.frombytes((p/'out.f32').read_bytes());mask=(p/'out.coverage').read_bytes();assert a[0]==4 and a[2]==4 and a[2*5]==6 and a[3*5+3]==30 and mask[4*5+4]==0
    # Independent point-in-triangle evaluation at deterministic world samples.
    tris=list(struct.iter_unpack('<I9f',(SOURCE/'heightmap-triangles.bin').read_bytes()));bins=collections.defaultdict(list);rng=random.Random(2005);points=set()
    for i,t in enumerate(tris):
        xs=t[1::3];zs=t[3::3]
        for bx in range(math.floor(min(xs)/64),math.floor(max(xs)/64)+1):
            for bz in range(math.floor(min(zs)/64),math.floor(max(zs)/64)+1):bins[(bx,bz)].append(i)
    for i in rng.sample(range(len(tris)),2000):
        t=tris[i];points.add((round(sum(t[1::3])/3),round(sum(t[3::3])/3)))
    heights=array.array('f');heights.frombytes((SOURCE/'heightmap.f32').read_bytes());coverage=(SOURCE/'heightmap.coverage').read_bytes();multi=(SOURCE/'heightmap.multilevel').read_bytes();max_error=0;checked=0
    for x,z in points:
        layers=[[],[]]
        for i in bins[(math.floor(x/64),math.floor(z/64))]:
            t=tris[i];x0,y0,z0,x1,y1,z1,x2,y2,z2=t[1:];den=(z1-z2)*(x0-x2)+(x2-x1)*(z0-z2)
            if abs(den)<1e-8:continue
            a=((z1-z2)*(x-x2)+(x2-x1)*(z-z2))/den;b=((z2-z0)*(x-x2)+(x0-x2)*(z-z2))/den;c=1-a-b
            if min(a,b,c)>=-1e-7:layers[t[0]].append(a*y0+b*y1+c*y2)
        index=(z-meta['originZ'])*meta['width']+x-meta['originX'];expected=max(layers[0]) if layers[0] else (min(layers[1]) if layers[1] else None)
        assert bool(coverage[index])==(expected is not None)
        if expected is not None:
            error=abs(heights[index]-expected);max_error=max(error,max_error);assert error<.0001,(x,z,error);checked+=1
    result['rasterValidation']={'independentSampleLocations':len(points),'coveredSamplesChecked':checked,'maximumInterpolationErrorMetres':max_error,'syntheticLayersAndCoverage':'PASS'}
    # Full raw checks, quantization, and shared border equality on every tile.
    edges={};total=0;maximum=0
    for tile in meta['tiles']:
        p=TILES/(tile['name']+'.raw');assert hashlib.sha256(p.read_bytes()).hexdigest()==tile['rawSha256'];h=array.array('H');h.frombytes(p.read_bytes());mask=(TILES/(tile['name']+'.coverage')).read_bytes();n=meta['tileResolution'];assert len(h)==len(mask)==n*n
        valid=0
        for iz in range(n):
            start=(tile['originZ']-meta['originZ']+iz)*meta['width']+tile['originX']-meta['originX']
            for ix in range(n):
                k=iz*n+ix;j=start+ix;assert bool(mask[k])==bool(coverage[j])
                if mask[k]:
                    error=abs(meta['minHeight']+h[k]/65535*meta['heightRange']-heights[j]);maximum=max(maximum,error);valid+=1
                else:assert h[k]==0
        assert valid==tile['validSamples'];total+=valid
        edges[(tile['xIndex'],tile['zIndex'])]=(h[::n],h[n-1::n],h[:n],h[-n:])
    for (x,z),(left,right,bottom,top) in edges.items():
        if (x+1,z) in edges:assert right==edges[(x+1,z)][0]
        if (x,z+1) in edges:assert top==edges[(x,z+1)][2]
    assert maximum<=meta['quantizationMaxErrorMetres']+1e-7
    result['tileValidation']={'tiles':len(edges),'coveredSamplesIncludingSharedBorders':total,'maxQuantizationErrorMetres':maximum,'allSharedEdges':'bit-identical','coverage':'Every tile sample matches source coverage; empty samples stay zero and masked.'}
    unity=SOURCE/'unity-ground-validation.json'
    if unity.exists():
        u=json.loads(unity.read_text())
        if 'layers' in u:
            for r,ur in zip(result['layers'],u['layers'][1:]):
                assert r['instances']==ur['meshes'] and r['triangles']==ur['triangles'];assert ur['missingMeshes']==ur['missingMaterials']==0
                assert max(abs(a-b) for a,b in zip(r['min']+r['max'],ur['min']+ur['max']))<.002
            result['unityMeshValidation']='PASS'
    (SOURCE/'ground-verification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
if __name__=='__main__':main()
