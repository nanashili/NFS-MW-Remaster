"""Validate modular GLBs, complete archive coverage and recovered PNGs."""
import json,hashlib,sys,collections,struct
from pathlib import Path
from PIL import Image
from validate import validate
from prepare import ROOT,OUT,GAME

def main():
    report={'archives':[],'categories':{},'sourceSolids':0,'parts':0,'vinylTextures':0,'errors':[]}
    counts=collections.Counter()
    expected={p.parent.parent.parent.name for p in (OUT/'Models').glob('*/GEOMETRY/models/*.obj')}
    # Each prepared archive must correspond to exactly one library, including shared and special objects.
    for archive in sorted(expected):
        try:
            source=json.loads((OUT/'Prepared'/archive/'source.json').read_text())
            catalog=json.loads((OUT/'Modifications'/archive/'report.json').read_text())
            source_hash=hashlib.sha256((GAME/'CARS'/archive/'GEOMETRY.BIN').read_bytes()).hexdigest()
            assert source_hash==source['sourceSha256']==catalog['sourceSha256'],'source hash mismatch'
            assert catalog.get('layout')=='content-first-v2','missing content-first catalog layout'
            assert catalog['asset'].startswith('Assets/NfsMw/Content/Customization/Sources/')
            assert catalog.get('sourceCatalog')=='Assets/NfsMw/Content/Customization/Sources/'+archive+'/Metadata/catalog.json'
            entries=catalog['entries']
            assert len({e['id'] for e in entries})==len(entries),'duplicate part IDs'
            assert set(source['solids'])=={n for e in entries+catalog.get('placeholders',[]) for n in e['sourceSolids']},'missing source solids'
            assert all(len(e['lods'])==3 and all(l['triangles']>0 for l in e['lods']) for e in entries),'empty LOD'
            assert (OUT/'Modifications'/archive/(archive+'.blend')).exists(),'missing editable Blender file'
            binary=validate(ROOT/catalog['asset'])
            raw=(ROOT/catalog['asset']).read_bytes();n=struct.unpack_from('<I',raw,12)[0];g=json.loads(raw[20:20+n])
            assert len(g['scenes'])==1 and g.get('scene',0)==0,'unexpected additional scene'
            roots=g['scenes'][0]['nodes']
            assert {g['nodes'][i]['name'] for i in roots}=={e['node'] for e in entries},'unexpected root objects'
            reached=set()
            def visit(i):
                reached.add(i)
                for c in g['nodes'][i].get('children',[]):visit(c)
            for i in roots:visit(i)
            assert reached==set(range(len(g['nodes']))),'unreferenced scene nodes'
            binary.update(archive=archive,parts=len(entries),sourceSolids=len(source['solids']))
            report['archives'].append(binary);report['sourceSolids']+=len(source['solids']);report['parts']+=len(entries)
            counts.update(e['category'] for e in entries)
        except Exception as e:report['errors'].append({'archive':archive,'error':repr(e)})
    vinyls=json.loads((OUT/'Modifications/vinyl-decoding-report.json').read_text())
    assert not vinyls['errors']
    from finalize_vinyl_catalog import name_hash
    for a in vinyls['archives']:
        assert hashlib.sha256((GAME/a['source']).read_bytes()).hexdigest()==a['sha256']
        for t in a['textures']:
            try:
                assert name_hash(t['name'])==t['hash'],'vinyl name hash mismatch'
                assert (t['role']=='mask')==t['name'].endswith('_MASK'),'vinyl mask role mismatch'
                path=ROOT/t['asset'];assert hashlib.sha256(path.read_bytes()).hexdigest()==t['sha256']
                with Image.open(path) as im:
                    assert im.size==(t['width'],t['height']);im.verify()
                report['vinylTextures']+=1
            except Exception as e:report['errors'].append({'texture':t['asset'],'error':repr(e)})
    unity_vinyls=json.loads((ROOT/'Assets/NfsMw/Content/Customization/Vinyls/catalog.json').read_text())
    unity_textures=[t for a in unity_vinyls['archives'] for t in a['textures']]
    assert len(unity_textures)==report['vinylTextures'],'Unity vinyl catalog coverage mismatch'
    for texture in unity_textures:
        try:
            path=ROOT/texture['asset'];assert path.is_file(),texture['asset']
            assert path.parent.parent.name in ('Artwork','Masks','PreVinyl'),'non-canonical Unity vinyl folder'
            with Image.open(path) as im:assert im.size==(texture['width'],texture['height'])
        except Exception as e:report['errors'].append({'texture':texture.get('asset'),'error':repr(e)})
    report['categories']=dict(counts)
    (OUT/'Modifications/validation-report.json').write_text(json.dumps(report,indent=2)+'\n')
    print(len(report['archives']),'libraries;',report['parts'],'parts;',report['sourceSolids'],'source solids;',report['vinylTextures'],'vinyl textures;',len(report['errors']),'errors')
    if report['errors']:print(json.dumps(report['errors'],indent=2));raise SystemExit(1)
if __name__=='__main__':main()
