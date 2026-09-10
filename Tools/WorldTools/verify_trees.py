"""Compare native tree instances against every original exported tree vertex."""
import json,math,struct,hashlib
from pathlib import Path
from inventory_game import ROOT
BASE=ROOT/'Assets/NfsMw/Content/World/Models/RockportTrees';SOURCE=ROOT/'Art/RockportTrees/Source'
def read(name):
    doc=json.loads((BASE/(name+'.gltf')).read_text());blob=(BASE/(name+'.bin')).read_bytes()
    def positions(index):
        a=doc['accessors'][index];v=doc['bufferViews'][a['bufferView']];start=v.get('byteOffset',0)+a.get('byteOffset',0);return [(-x,y,z) for x,y,z in struct.iter_unpack('<3f',blob[start:start+a['count']*12])]
    return doc,positions

def main():
    raw,getraw=read('RockportTrees');native,getnative=read('NativeTreePrototypes');manifest=json.loads((SOURCE/'terrain-trees.json').read_text());cache={};rawcache={};worst=0;vertices=0;triangles=0
    assert len(raw['nodes'])==len(manifest['instances'])
    source_nodes={node['extras']['sourceInstanceOffset']:node for node in raw['nodes']}
    for instance in manifest['instances']:
        node=source_nodes[instance['sourceOffset']]
        assert node['extras']['sourceInstanceOffset']==instance['sourceOffset'];r=instance['rotation'];cs=math.cos(r);sn=math.sin(r);width=instance['widthScale'];height=instance['heightScale'];world=instance['worldPosition'];translation=node['translation'];translation=(-translation[0],translation[1],translation[2]);proto=instance['prototype']
        if proto not in cache:cache[proto]=[getnative(p['attributes']['POSITION']) for p in native['meshes'][proto]['primitives']]
        mesh=node['mesh']
        if mesh not in rawcache:rawcache[mesh]=[getraw(p['attributes']['POSITION']) for p in raw['meshes'][mesh]['primitives']]
        assert len(cache[proto])==len(rawcache[mesh])
        for expected,points in zip(rawcache[mesh],cache[proto]):
            assert len(expected)==len(points)
            for p,q in zip(expected,points):
                actual=(world[0]+width*(cs*q[0]+sn*q[2]),world[1]+height*q[1],world[2]+width*(-sn*q[0]+cs*q[2]));error=math.sqrt(sum((actual[k]-p[k]-translation[k])**2 for k in range(3)));worst=max(worst,error);vertices+=1
        for p in raw['meshes'][mesh]['primitives']:triangles+=raw['accessors'][p['indices']]['count']//3
    assert worst<.011,worst
    report={'status':'PASS','instances':len(manifest['instances']),'prototypes':len(manifest['prototypes']),'comparedVertices':vertices,'sourceInstancedTriangles':triangles,'maxWorldVertexDifferenceMetres':worst,'nativeGltfSha256':hashlib.sha256((BASE/'NativeTreePrototypes.gltf').read_bytes()).hexdigest(),'nativeBinSha256':hashlib.sha256((BASE/'NativeTreePrototypes.bin').read_bytes()).hexdigest()}
    (SOURCE/'tree-source-verification.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
if __name__=='__main__':main()
