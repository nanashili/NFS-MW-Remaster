"""Independent checks of serialized ocean geometry and original footprint containment."""
import json,math,struct
from pathlib import Path
from prepare_ocean import DEST,REPORT,clip,area

def cross(a,b,p):return (b[0]-a[0])*(p[2]-a[2])-(b[2]-a[2])*(p[0]-a[0])
def contains(p,tri):
    values=[cross(tri[n],tri[(n+1)%3],p) for n in range(3)]
    return min(values)>=-1e-3 or max(values)<=1e-3
def main():
    square=[(0,0),(2,0),(2,2),(0,2)];assert area(clip(square,0,1,True))==2
    assert not clip(square,0,3,True)
    raw=(DEST/'OceanSurface.bytes').read_bytes();nv,ni=struct.unpack_from('<II',raw);assert len(raw)==8+12*nv+4*ni
    vertices=list(struct.iter_unpack('<3f',raw[8:8+12*nv]));indices=struct.unpack_from('<'+str(ni)+'I',raw,8+12*nv)
    assert max(indices)<nv and all(math.isfinite(c) for v in vertices for c in v) and all(v[1]==0 for v in vertices)
    footprint=(DEST/'OceanFootprint.bytes').read_bytes();count=struct.unpack_from('<I',footprint)[0];points=list(struct.iter_unpack('<3f',footprint[4:]));assert count==len(points)
    original=[points[i:i+3] for i in range(0,count,3)]
    triangles=ni//3;total_area=0;max_edge=0
    for i in range(0,ni,3):
        a,b,c=(vertices[indices[i+k]] for k in range(3));signed=cross(a,b,c);assert signed<=.001
        total_area+=abs(signed)*.5
        max_edge=max(max_edge,math.dist(a,b),math.dist(b,c),math.dist(c,a))
    checks=0
    for t in range(0,triangles,max(1,triangles//4096)):
        tri=[vertices[indices[t*3+k]] for k in range(3)];p=tuple(sum(v[k] for v in tri)/3 for k in range(3))
        assert any(contains(p,o) for o in original),(t,p);checks+=1
    assert not any(contains((0,0,0),o) for o in original)
    report=json.loads((REPORT/'ocean-source-verification.json').read_text());relative=abs(total_area-report['sourceAreaSquareMetres'])/total_area
    assert relative<1e-6 and max_edge<=32*math.sqrt(2)+.01
    result={'status':'PASS','serializedVertices':nv,'serializedTriangles':triangles,'checkedOceanCentroids':checks,'landOriginExcluded':True,'float32AreaRelativeError':relative,'maxTriangleEdgeMetres':max_edge,'allNormalsFaceUp':True,'meanSeaLevel':0}
    (REPORT/'ocean-mesh-verification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
if __name__=='__main__':main()
