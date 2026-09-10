"""Recover vinyl artwork and masks without baking runtime paint composites."""
import sys,json,hashlib,re
from pathlib import Path
from PIL import Image as PillowImage
from prepare import records,GAME,OUT,ROOT,decode_texture
from vinyl_paths import write_unity_catalog

def main():
    target=OUT/'Modifications/Vinyls';target.mkdir(parents=True,exist_ok=True)
    report={'archives':[],'errors':[]}
    for path in sorted((GAME/'CARS').glob('*/VINYLS.BIN'))+sorted((GAME/'CARS').glob('*/PREVINYL.BIN')):
        before=hashlib.sha256(path.read_bytes()).hexdigest()
        entry={'source':str(path.relative_to(GAME)),'sha256':before,'textures':[]}
        car=path.parent.name;dest=target/car/path.stem;dest.mkdir(parents=True,exist_ok=True)
        for record in records(path):
            m=record.metadata
            try:
                if record.error:raise ValueError(record.error)
                key=m['nameHash'];name=re.sub('[^A-Za-z0-9_-]','_',m['name'])
                out=dest/(name+'_'+key+'.png')
                if m['platformFormat']==41:
                    im=PillowImage.frombytes('P',(m['width'],m['height']),record.payload[:m['baseImageSize']])
                    pal=bytes(c for i in range(0,1024,4) for c in (record.palette[i+2],record.palette[i+1],record.palette[i],record.palette[i+3]))
                    im.putpalette(pal,rawmode='RGBA');im=im.convert('RGBA')
                else:
                    decoded=decode_texture(record);im=PillowImage.frombytes('RGBA',(decoded.width,decoded.height),decoded.rgba)
                im.save(out,compress_level=6)
                entry['textures'].append({'name':m['name'],'hash':key,'asset':str(out.relative_to(ROOT)),
                    'width':im.width,'height':im.height,'alphaUsage':m['alphaUsage'],
                    'role':'mask' if name.endswith('_MASK') else 'artwork or source composite','sha256':hashlib.sha256(out.read_bytes()).hexdigest()})
            except Exception as e:report['errors'].append({'source':entry['source'],'texture':m.get('nameHash'),'error':str(e)})
        entry['sourceUnchanged']=hashlib.sha256(path.read_bytes()).hexdigest()==before
        assert entry['sourceUnchanged']
        report['archives'].append(entry)
        (OUT/'Modifications/vinyl-decoding-report.json').write_text(json.dumps(report,indent=2)+'\n')
        print(entry['source'],len(entry['textures']),flush=True)
    (target/'catalog.json').write_text(json.dumps(report,indent=2)+'\n')
    write_unity_catalog(report)
    from finalize_vinyl_catalog import finalize
    finalize()
    print('TOTAL',sum(len(a['textures']) for a in report['archives']),'textures;',len(report['errors']),'errors')
    if report['errors']:raise SystemExit(1)
if __name__=='__main__':main()
