"""Inventory all installed front-end, global HUD and language archive textures.
Read-only to the game and historical decoding. PNGs preserve decoded source RGBA.
"""
from pathlib import Path
import sys, json, collections, re, struct
PROJECT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(PROJECT/'Tools/FrontendAssets'))
from archive_decode import ArchiveError, read_chunks, unwrap, sha256
import texture_archive
from texture_archive import iter_textures, decode_texture
from texture_decode import Image, png_bytes
GAME=Path('/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted')
OUT=PROJECT/'Art/UI/Artwork'
original_unwrap=unwrap
def unwrap(data):
    if data[:4]==b'HUFF':
        if len(data)<16:raise ArchiveError('Truncated HUFF header')
        key=sha256(data)
        cached=OUT/'HuffCache/Textures'/(key+'.raw')
        if not cached.exists(): cached=OUT/'HuffCache'/(key+'.raw')
        if not cached.exists():raise ArchiveError('Missing native HUFF cache; run stage_huff.py and DecodeHuff on texture streams')
        raw=cached.read_bytes()
        if len(raw)!=struct.unpack_from('<I',data,8)[0]:raise ArchiveError('Native HUFF length mismatch')
        return raw,{'codec':'HUFF-native-CompLib','storedSha256':key,'uncompressedSha256':sha256(raw),'uncompressedSize':len(raw)}
    return original_unwrap(data)
texture_archive.unwrap=unwrap
def run():
    report={'schemaVersion':1,'gameRoot':str(GAME),'scope':'Top-level FRONTEND, GLOBAL, LANGUAGES binary archives; no 3D platform/track/car archives','archives':[],'textures':[],'errors':[]}
    seen={}
    for directory in ['FRONTEND','GLOBAL','LANGUAGES']:
        for source in sorted((GAME/directory).iterdir()):
            if not source.is_file() or source.suffix.lower() not in ('.bun','.bin','.lzc'):continue
            stored=source.read_bytes(); rel=source.relative_to(GAME).as_posix()
            summary={'source':rel,'bytes':len(stored),'sha256':sha256(stored),'textureRecords':0,'decoded':0}
            report['archives'].append(summary)
            try:
                data,codec=unwrap(stored);chunks=read_chunks(data)
                summary['compression']=codec['codec'];summary['chunkTypes']=dict(collections.Counter(f'{c.kind:08x}' for c in chunks))
                for record in iter_textures(data,chunks):
                    summary['textureRecords']+=1
                    m=dict(record.metadata,source=rel)
                    try:
                        if record.error:raise ValueError(record.error)
                        if m['platformFormat']==41:
                            if m['imageParentHash']!='00000000' or m['paletteEntries']!=256 or len(record.palette)!=1024 or m['baseImageSize']!=m['width']*m['height'] or len(record.payload)<m['baseImageSize']:raise ValueError('Unsupported P8 layout')
                            pal=[bytes((record.palette[i+2],record.palette[i+1],record.palette[i],record.palette[i+3])) for i in range(0,1024,4)]
                            im=Image(m['width'],m['height'],b''.join(pal[i] for i in record.payload[:m['baseImageSize']]))
                            m['decodeMethod']='P8 BGRA verified by existing WarehouseAssets source'
                        else:im=decode_texture(record);m['decodeMethod']=record.metadata.get('format','verified PC decoder')
                        png=png_bytes(im);digest=sha256(png)
                        identity=(m['nameHash'],digest)
                        if identity in seen:
                            seen[identity]['sources'].append(rel)
                        else:
                            slug=re.sub('[^A-Za-z0-9_-]','_',m['name'])
                            dest=OUT/'Textures'/f"{slug}_{m['nameHash']}_{digest[:12]}.png"
                            dest.parent.mkdir(parents=True,exist_ok=True)
                            if dest.exists() and dest.read_bytes()!=png:raise ValueError('Refusing to overwrite different decoding')
                            if not dest.exists():dest.write_bytes(png)
                            alpha=im.rgba[3::4]
                            m.update(path=dest.relative_to(PROJECT).as_posix(),pngSha256=digest,sources=[rel],alphaExtrema=[min(alpha),max(alpha)])
                            report['textures'].append(m);seen[identity]=m
                        summary['decoded']+=1
                    except Exception as error:report['errors'].append(dict(m,error=str(error)))
            except Exception as error:summary['error']=str(error)
            summary['sourceUnchanged']=sha256(source.read_bytes())==summary['sha256']
            print(rel,summary['textureRecords'],summary['decoded'],summary.get('error',''),flush=True)
    report['totals']={'archives':len(report['archives']),'records':sum(a['textureRecords'] for a in report['archives']),'uniqueTextures':len(report['textures']),'recordErrors':len(report['errors']),'archiveErrors':sum('error'in a for a in report['archives'])}
    (OUT/'manifest.json').write_text(json.dumps(report,indent=2)+'\n')
    print(report['totals'])
if __name__=='__main__':run()
