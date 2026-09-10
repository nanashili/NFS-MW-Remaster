"""Inventory original world textures using the verified PC texture reader."""
import json,mmap,sys,collections
from pathlib import Path
from inventory_game import chunks,GAME,OUT,ROOT
sys.path.insert(0,str(ROOT/'Tools/FrontendAssets'))
from archive_decode import Chunk
from texture_archive import iter_textures,decode_texture
from texture_decode import png_bytes

def chunk_records(data):
    parents={-1:None}
    for k,o,s,d in chunks(data):
        yield Chunk(o,k,s,d,parents[d-1])
        parents[d]=o

def run():
    target=ROOT/'Art/RockportBuildings/Textures';target.mkdir(exist_ok=True)
    records={};errors=[]
    for relative in ['TRACKS/STREAML2RA.BUN','TRACKS/LOC2DYNTEX.BIN','GLOBAL/GLOBALA.BUN','GLOBAL/GLOBALB.BUN','GLOBAL/DYNTEX.BIN','GLOBAL/InGameA.bun']:
        path=GAME/relative
        if not path.exists() or not path.stat().st_size:continue
        with path.open('rb') as f,mmap.mmap(f.fileno(),0,access=mmap.ACCESS_READ) as data:
            for t in iter_textures(data,list(chunk_records(data))):
                key=t.metadata.get('nameHash','')
                if key in records:continue
                if t.error:errors.append(dict(t.metadata,error=t.error));continue
                meta=dict(t.metadata,source=relative)
                records[key]=meta
                # Cache original compressed pixel payload; decode only retained building textures.
                (target/(key+'.pixels')).write_bytes(t.payload)
                if t.palette:(target/(key+'.palette')).write_bytes(t.palette)
        print(relative,len(records),'textures',len(errors),'errors',flush=True)
    (OUT/'textures.json').write_text(json.dumps({'textures':records,'errors':errors},indent=2))
if __name__=='__main__':run()
