"""Verify the exported layout and independent Blender/Unity import measurements."""
import hashlib,json,math,struct
from inventory_game import ROOT,OUT
from classification import object_category,material_decision

def main():
    asset=ROOT/'Assets/NfsMw/Content/World/Models/RockportBuildings';g=json.loads((asset/'RockportBuildings.gltf').read_text());data=(asset/'RockportBuildings.bin').read_bytes();r=json.loads((OUT/'decoding-report.json').read_text());b=json.loads((OUT/'blender-validation.json').read_text());u=json.loads((OUT/'unity-validation.json').read_text())
    assert len(data)==g['buffers'][0]['byteLength']
    assert len(g['nodes'])==len(r['instances'])==b['meshObjects']==u['meshObjects']
    triangles=0
    for node,source in zip(g['nodes'],r['instances']):
        assert node['translation']==[source['gamePosition'][0],source['gamePosition'][2],-source['gamePosition'][1]]
        assert object_category(source['name'])!='excluded-object'
        triangles+=sum(g['accessors'][p['indices']]['count']//3 for p in g['meshes'][node['mesh']]['primitives'])
    for v in g['bufferViews']:assert 0<=v['byteOffset']<len(data) and v['byteOffset']+v['byteLength']<=len(data)
    for a in g['accessors']:
        v=g['bufferViews'][a['bufferView']]
        if a['componentType']==5126:
            assert all(math.isfinite(n[0]) for n in struct.iter_unpack('<f',data[v['byteOffset']:v['byteOffset']+v['byteLength']]))
    for name,t in r['textures'].items():
        if 'pngSha256' in t:assert hashlib.sha256((asset/'Textures'/(name+'.png')).read_bytes()).hexdigest()==t['pngSha256']
    assert u['boundsMin'][0]==-b['boundsMax'][0] and u['boundsMin'][2]==-b['boundsMax'][1]
    assert u['boundsMax'][0]==-b['boundsMin'][0] and u['boundsMax'][2]==-b['boundsMin'][1]
    assert abs(u['boundsMin'][1]-b['boundsMin'][2])<.0001 and abs(u['boundsMax'][1]-b['boundsMax'][2])<.0001
    assert triangles==u['triangles'];assert u['missingMeshes']==u['missingMaterials']==u['terrains']==u['nonBuildingObjects']==0
    assert not r['missingSolids'] and r['counts']['sourceUnchanged']
    result={'status':'PASS','instances':len(g['nodes']),'sourceAndUnityTriangles':triangles,'blenderTriangles':b['triangles'],'lodFallbacks':len(r['lodFallbacks']),'invalidSourcePlacementsExcluded':len(r['invalidPlacements']),'unresolvedSourceTextures':[key for key,value in r['textures'].items() if 'status' in value],'gltfSha256':hashlib.sha256((asset/'RockportBuildings.gltf').read_bytes()).hexdigest(),'binarySha256':hashlib.sha256(data).hexdigest(),'layout':'All translations match source records; Blender and Unity bounds match after axis conversion; all binary values finite.'}
    (OUT/'verification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
if __name__=='__main__':main()
