"""Select authored smash-tree families, with original damaged/fragment solids as evidence."""
import json
from pathlib import Path
from inventory_game import ROOT,OUT
def main():
    report=json.loads((ROOT/'Art/RockportTrees/Source/terrain-trees.json').read_text())
    solids={g['name']:g for g in json.loads((OUT/'geometry-inventory.json').read_text())['solids']}
    families={
        'XO_CT_CAMPUSSMACKTREE_1B_JG_00':['CAMPUSSMACKTREE_DMG01']+[f'CAMPUSSMACKTREE_FRAG0{i}' for i in range(1,4)],
        'XO_CT_CAMPUSSMACKTREEB_1B_JG_00':['CAMPUSSMACKTREEB_DMG']+[f'CAMPUSSMACKTREEB_FRAG0{i}' for i in range(1,5)]}
    rows=[];counts={name:0 for name in families}
    for placement in report['instances']:
        if placement['name'] not in families:continue
        proto=report['prototypes'][placement['prototype']];counts[placement['name']]+=1
        rows.append('\t'.join(map(str,[placement['sourceOffset'],placement['name'],proto['name'],*placement['worldPosition'],placement['rotation'],placement['widthScale'],placement['heightScale'],*proto['anchor'],proto['boundsMax'][1]-proto['boundsMin'][1]])))
    assert len(rows)==481
    for pieces in families.values():assert all(piece in solids for piece in pieces)
    (ROOT/'Assets/NfsMw/Content/World/Maps/Rockport/Trees/breakable-placements.tsv').write_text('\n'.join(rows)+'\n')
    evidence={'status':'PASS','instances':len(rows),'families':counts,'originalDamageSolids':{name:[{'name':piece,'hash':solids[piece]['hash']} for piece in pieces] for name,pieces in families.items()},'behavior':'Unity whole-body physical break/fall. Original damage solids establish family classification; they are not used as runtime fragments.'}
    (ROOT/'Art/RockportTrees/Source/breakable-tree-source.json').write_text(json.dumps(evidence,indent=2)+'\n');print(json.dumps(counts))
if __name__=='__main__':main()
