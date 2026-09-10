"""Complement the building decoding with roads, shortcuts and natural ground.

Decisions are primitive-level and recorded by export_map. Grass/dirt belongs to
terrain, including drivable golf-course/field shortcuts; paved/dirt paths and
road infrastructure belong to roads. No navigation spline is inferred.
"""
import re
from classification import object_category, material_decision
EXCLUDED=re.compile(r'^SHD|^SHADOW|^PAN|^RFL|^XT_|^XV_|^XS_|_SFRAG|_FRAG|_DMG')
NATURAL=re.compile(r'GRASS|CLIFF|HILLS|MOUNT|SANDA|GRAVELA|DIRTREPEAT|^ORG_DIRT$|ROCKCLIFF|BEACHTRANS|SOUTHFIELDS|DIRT(?!Y|PATH|RD)|FIELD')
WATER_VEGETATION=re.compile(r'WATERSEA|WATERFALL|^ANM_WATER|POND|LILYPAD|EVERGREEN|FRINGE|FALLEN|BRANCH|ORG_IVY|PAN_')
SURFACE=re.compile(r'PAVEMENT|SIDEWALK|COBBLE|BOARDWALK|DIRTPATH|DIRTRD|GRAVEL|RO_GEN|RO_DETAIL|RDP_|RDT_|PARKING|PKD_(LOT|STALL|ACCESS|HELIPAD)|COURTGD|TENNIS|SEWERGRND|PAINTEDCONCRETE|OLDCONCRETE|CONC_WALK|TRAINTRACK|GRUNGA|CONSTRUCTIONSITE|PARKTRAIL|CARWASHROAD|CONCRETERAMP|STADIUMGRASSRD|SHIPYARDGRND')
INFRA=re.compile(r'OVERPASS|FREEWAY|HIGHWAY|FWY|BRIDGE|TUNNEL|TUNL_|TUN_|TUNROOF|TUNWALL|TUNTILE|SEWER|BARRIER|RETAIN|MEDIAN|JUMP|RAMP|DOCK|PIER|SHIPHULL|SHIPWRECK')
def category_for(name):
    return 'excluded-object' if EXCLUDED.search(name.upper()) else 'ground-candidate'
def decision(layer,name,material,texture):
    n=name.upper();v=(material+' '+texture).upper();t=texture.upper()
    if category_for(name)=='excluded-object':return False,'non-ground object or distant panorama'
    if re.search(r'TRACKBARRIER|GRASSCLUMP|HEDGE|TREEBOX|PLANTER|TREESUPPORT',n):return False,'race-only blocker or vegetation dressing'
    if n.startswith('XO_') and not re.search(r'BRIDGE|OVERPASS|TUNNEL|MEDIAN|RAMP|DOCK|PIER|SEWER|JUMP',n):return False,'loose prop rather than road structure'
    if n.startswith(('XW_','XWU_')) and not re.search(r'BARR|GUARDRAIL|HIGHWAY|RETAIN',n):return False,'fence rather than road structure'
    if WATER_VEGETATION.search(v):return False,'water or vegetation'
    # The same authored primitive must not be duplicated in the building layer.
    if material_decision(object_category(name),material,texture)[0]:return False,'already retained by building decoding'
    natural=bool(NATURAL.search(t))
    path=bool(SURFACE.search(v))
    if natural and not path:
        return (layer=='terrain' and not n.startswith(('XW_','XWU_'))),'natural ground including off-road shortcuts'
    if path:return (layer=='roads'),'paved or unpaved ground surface'
    if INFRA.search(n+' '+v):return (layer=='roads'),'road structure, tunnel, bridge, retaining wall or shortcut ramp'
    return False,'not a ground surface or road structure'
