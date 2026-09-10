"""Validate exported GLB buffers, mesh attributes, texture embedding and roster coverage."""
import json
import math
import struct
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/Cars'

def validate(path):
    raw=path.read_bytes()
    magic,version,length=struct.unpack_from('<4sII',raw)
    assert magic==b'glTF' and version==2 and length==len(raw), 'GLB header'
    n,kind=struct.unpack_from('<II',raw,12)
    assert kind==0x4e4f534a
    doc=json.loads(raw[20:20+n]);start=20+n
    size,kind=struct.unpack_from('<II',raw,start)
    assert kind==0x004e4942
    binary=raw[start+8:start+8+size]
    for view in doc['bufferViews']:
        assert view.get('byteOffset',0)+view['byteLength']<=len(binary), 'buffer bounds'
    for a in doc['accessors']:
        if a['componentType']==5126:
            v=doc['bufferViews'][a['bufferView']]
            components={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
            offset=v.get('byteOffset',0)+a.get('byteOffset',0)
            stride=v.get('byteStride',4*components)
            for i in range(a['count']):
                assert all(math.isfinite(x) for x in struct.unpack_from('<'+'f'*components,binary,offset+i*stride)), 'nonfinite vertex data'
    for mesh in doc['meshes']:
        for p in mesh['primitives']:
            assert p.get('mode',4)==4 and 'POSITION' in p['attributes'] and 'NORMAL' in p['attributes']
            count=doc['accessors'][p['attributes']['POSITION']]['count']
            a=doc['accessors'][p['indices']];v=doc['bufferViews'][a['bufferView']]
            code,width={5121:('B',1),5123:('H',2),5125:('I',4)}[a['componentType']]
            offset=v.get('byteOffset',0)+a.get('byteOffset',0)
            assert a['count']%3==0
            assert max(struct.unpack_from('<'+code*a['count'],binary,offset))<count, 'index out of range'
            assert p['material']<len(doc['materials'])
    for im in doc.get('images',[]):
        assert 'bufferView' in im and im['mimeType']=='image/png', 'unembedded texture'
        v=doc['bufferViews'][im['bufferView']];offset=v.get('byteOffset',0)
        assert binary[offset:offset+8]==b'\x89PNG\r\n\x1a\n'
    return {'path':str(path.relative_to(ROOT)),'bytes':len(raw),'meshes':len(doc['meshes']),
            'materials':len(doc['materials']),'textures':len(doc.get('images',[]))}

def main():
    decoding=json.loads((OUT/'decoding-manifest.json').read_text())
    report={'cars':[],'errors':[]}
    for car,folder in decoding['carFolders'].items():
        try:
            entry=validate(ROOT/folder/'Remastered.glb');entry['car']=car
            meta=json.loads((OUT/'Remastered'/car/'report.json').read_text())
            assert len(meta['lods'])==3
            assert meta['lods'][0]['triangles']>meta['lods'][1]['triangles']>meta['lods'][2]['triangles']>0
            assert (OUT/'Remastered'/car/(car+'.blend')).exists()
            report['cars'].append(entry)
        except Exception as e:report['errors'].append({'car':car,'error':repr(e)})
    assert not decoding['errors']
    assert all(e['sourceUnchanged'] for e in decoding['archives'])
    (OUT/'validation-report.json').write_text(json.dumps(report,indent=2)+'\n')
    print(len(report['cars']),'valid cars;',len(report['errors']),'errors')
    if report['errors']:print(json.dumps(report['errors'],indent=2));raise SystemExit(1)

if __name__=='__main__':main()
