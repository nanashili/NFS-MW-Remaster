"""Original ocean footprint and grid-clipped render mesh; never modify game files."""
import hashlib,json,math,mmap,struct
from pathlib import Path
from inventory_game import ROOT,GAME,OUT
from geometry import read_solid

DEST=ROOT/'Assets/NfsMw/Content/World/Maps/Rockport/Ocean'
REPORT=ROOT/'Art/RockportOcean/Source'
GRID=32

def clip(poly,axis,value,greater):
    result=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        ina=(a[axis]>=value) if greater else (a[axis]<=value)
        inb=(b[axis]>=value) if greater else (b[axis]<=value)
        if ina:result.append(a)
        if ina!=inb:
            t=(value-a[axis])/(b[axis]-a[axis]);result.append(tuple(a[k]+t*(b[k]-a[k]) for k in range(2)))
    return result

def area(poly):
    return abs(sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(poly,poly[1:]+poly[:1])))*.5

def main():
    DEST.mkdir(parents=True,exist_ok=True);REPORT.mkdir(parents=True,exist_ok=True)
    inv=json.loads((OUT/'game-inventory.json').read_text());geo={g['hash']:g for g in json.loads((OUT/'geometry-inventory.json').read_text())['solids']}
    source=[];placements=[]
    with (GAME/'TRACKS/STREAML2RA.BUN').open('rb') as handle,mmap.mmap(handle.fileno(),0,access=mmap.ACCESS_READ) as data:
        digest=hashlib.sha256(data).hexdigest();assert digest==inv['archives'][1]['sha256']
        for section in inv['scenery']:
            for ins in section['instances']:
                info=section['infos'][ins['infoIndex']]
                if info['name'].upper() not in ('TRN_OCEAN_A','TRN_OCEAN_B'):continue
                g=geo[info['solidKeys'][0]];solid=read_solid(data,g['offset'],struct.unpack_from('<I',data,g['offset']+4)[0]);r=ins['rotationRows'];p=ins['position']
                placements.append({'name':g['name'],'sourceOffset':ins['sourceOffset'],'position':p,'rotation':r})
                for mat in solid['materials']:
                    verts=solid['buffers'][mat['stream']]['positions'];idx=mat['indices']
                    for n in range(0,len(idx),3):
                        points=[]
                        for i in idx[n:n+3]:
                            v=verts[i];w=[p[q]+sum(r[k*3+q]*v[k] for k in range(3)) for q in range(3)]
                            assert abs(w[2])<1e-5
                            points.append((-w[0],-w[1]))
                        if area(points)>1e-8:source.append(points)
        assert hashlib.sha256(data).hexdigest()==digest
    assert len(placements)==2
    vertices=[];indices=[];lookup={};source_area=sum(map(area,source));render_area=0
    def vertex(p):
        key=tuple(round(v,5) for v in p)
        if key not in lookup:lookup[key]=len(vertices);vertices.append((p[0],0,p[1]))
        return lookup[key]
    for tri in source:
        for x in range(math.floor(min(p[0] for p in tri)/GRID),math.ceil(max(p[0] for p in tri)/GRID)):
            strip=clip(clip(tri,0,x*GRID,True),0,(x+1)*GRID,False)
            if len(strip)<3:continue
            for z in range(math.floor(min(p[1] for p in strip)/GRID),math.ceil(max(p[1] for p in strip)/GRID)):
                poly=clip(clip(strip,1,z*GRID,True),1,(z+1)*GRID,False)
                if len(poly)<3 or area(poly)<1e-7:continue
                for n in range(1,len(poly)-1):
                    a,b,c=poly[0],poly[n],poly[n+1]
                    if area([a,b,c])<1e-7:continue
                    # Clockwise in X/Z has upward Unity normals.
                    if (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])>0:b,c=c,b
                    indices.extend(vertex(p) for p in (a,b,c));render_area+=area([a,b,c])
    assert abs(render_area-source_area)/source_area<1e-8
    blob=bytearray(struct.pack('<II',len(vertices),len(indices)))
    for v in vertices:blob.extend(struct.pack('<3f',*v))
    blob.extend(struct.pack('<'+str(len(indices))+'I',*indices));(DEST/'OceanSurface.bytes').write_bytes(blob)
    points=[(x,0,z) for tri in source for x,z in tri]
    (DEST/'OceanFootprint.bytes').write_bytes(struct.pack('<I',len(points))+b''.join(struct.pack('<3f',*v) for v in points))
    report={'status':'PASS','sourceArchiveSha256':digest,'placements':placements,'sourceTriangles':len(source),'renderVertices':len(vertices),'renderTriangles':len(indices)//3,'gridMetres':GRID,'meanSeaLevel':0,'sourceAreaSquareMetres':source_area,'renderAreaSquareMetres':render_area,'areaRelativeError':abs(render_area-source_area)/source_area,'boundsMin':[min(v[k] for v in vertices) for k in range(3)],'boundsMax':[max(v[k] for v in vertices) for k in range(3)],'policy':'Original ocean polygons clipped to a regular render grid. Shoreline and sea elevation retained; wave shape is new HDRP simulation.'}
    (REPORT/'ocean-source-verification.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))

if __name__=='__main__':main()
