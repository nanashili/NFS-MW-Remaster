"""Recover truncated vinyl names only when the original name hash agrees."""
import json
from pathlib import Path
from prepare import ROOT,OUT
from vinyl_paths import write_unity_catalog

def name_hash(s):
    value=0xffffffff
    for c in s:value=(value*33+ord(c))&0xffffffff
    return f'{value:08x}'

def finalize():
    path=OUT/'Modifications/vinyl-decoding-report.json';report=json.loads(path.read_text())
    textures=[t for a in report['archives'] for t in a['textures']]
    known=set()
    for t in textures:
        original=t.get('sourceDebugName',t['name']);t['sourceDebugName']=original
        candidates=[original+suffix for suffix in ('','_MASK','MASK','ASK','SK','K','O','O_MASK','NT','NT_MASK','AN','AN_MASK')]
        matches=[n for n in candidates if name_hash(n)==t['hash']]
        if len(matches)==1:known.add(matches[0]);t['name']=matches[0]
    suffixes={n.partition('_')[2] for n in known if '_' in n}
    for t in textures:
        if name_hash(t['name'])!=t['hash']:
            prefix=t['sourceDebugName'].partition('_')[0]
            matches=[prefix+'_'+s for s in suffixes if (prefix+'_'+s).startswith(t['sourceDebugName']) and name_hash(prefix+'_'+s)==t['hash']]
            if len(matches)==1:t['name']=matches[0]
        t['nameHashVerified']=name_hash(t['name'])==t['hash']
        t['role']='mask' if t['name'].endswith('_MASK') else 'artwork or source composite'
    report['nameRecovery']={'verified':sum(t['nameHashVerified'] for t in textures),'truncatedNamesRecovered':sum(t['name']!=t['sourceDebugName'] for t in textures)}
    for target in [path,OUT/'Modifications/Vinyls/catalog.json']:
        target.write_text(json.dumps(report,indent=2)+'\n')
    write_unity_catalog(report)
    print(report['nameRecovery'])
if __name__=='__main__':finalize()
