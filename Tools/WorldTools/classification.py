"""Building-only selection, with every primitive decision exported for review.

Object prefixes alone cannot classify Rockport: TRN meshes contain architecture.
Use the source diffuse texture's debug name alongside the material name.
"""
import re
NATURE=re.compile(r'GRASS|CLIFF|HILLS|MOUNTAIN|FOLIAGE|EVERGREEN|FRINGE|FALLEN.?TREE|BRANCH|LILYPAD|WATERSEA|WATERFALL|FOREST|SANDA_|DIRT(?!Y)|GRAVEL|POND|OCEAN|ORG_|CT_MOUNT|SOUTHFIELDS|ROCKY.?BANK|PAN_')
GROUND=re.compile(r'PAVEMENT|SIDEWALK|ASPHALT|COBBLE|BOARDWALK|PARKINGLOT|PARKING_LOT|PKD_LOT|PKD_STALL|PKD_HELIPAD|TENNIS|COURTGD|STADIUMGRASS|SHIPYARDGRND|SEWERGRND|PARKTRAIL|TRAINTRACK|RO_GEN|RDP_|RDT_|RO_DETAIL|ROAD|PAINTEDCONCRETE|OLDCONCRETE|CONC_WALK')
INFRA=re.compile(r'OVERPASS|FREEWAY|FWY|TUNNEL|TUN_ROOF|TUNL_|BRIDGE|BARRIER|FENCE|CHAINLINK|PICKET|RETAIN|PARKWALL|DOCK|SEWER|WATERBREAK|PLANTER|MEDIAN|SHIPWRECK')
DETAIL=re.compile(r'ROOF|SHACK|GAZEBO|WATERTOWER|ROSEWATER|RADIOTOWER|TRAINYARDSILO|OILPIPEBUILD|OILREF_SUPPORT|SMOKEVENT|PIPEVENT|VENTSCOOP|FIREESCAPE|CHIMNEY|GARAGEDOOR|BOARDEDWINDOW|BROKENWINDOW|CEILING_DUCT')
ARCH=re.compile(r'ARC_|GLSY_ARC_|\bXB_|BUILD|BLD|ROOF|DOOR|WINDOW|WALL|BRICK|STAIR|STADIUM|PILLAR|TOPLEDGE|WHITEGLAVANISE|PARKADE')
def object_category(name):
    n=name.upper()
    if re.search(r'_FRAG|_SFRAG|_DMG|SHADOW|^SHD|^PAN|^RFL|^XT_|^XV_|^XS_|^XW_',n): return 'excluded-object'
    if n.startswith('XB_'):
        if re.search(r'TRAINBRIDGE|SHIPHULL|STADIUM_TUNNEL',n):return 'excluded-object'
        return 'building'
    if n.startswith('TRN_'):
        if re.search(r'ROAD|HIGHWAY|FREEWAY|FENCE|DOCK|OVERPASS|TUNNEL|SEWER|BRIDGE|OCEAN|WATERBREAK|SHIPWRECK|JUMP|PKG',n):return 'excluded-object'
        return 'mixed-map'
    if n.startswith(('STAIRS','GAZEBOSUPPORT')):return 'building-detail'
    if DETAIL.search(n) and not re.search(r'TOWERCRANE|BUSSHELTER|TUNNEL|BRIDGE',n):return 'building-detail'
    return 'excluded-object'
def material_decision(category, name, texture):
    value=(name+' '+texture).upper()
    if category=='excluded-object':return False,'non-building object'
    if NATURE.search(value):return False,'vegetation or natural terrain'
    if GROUND.search(value):return False,'road or ground surface'
    if category in ('building','building-detail'):return True,'building surface'
    if INFRA.search(value):return False,'road infrastructure or landscape dressing'
    if ARCH.search(value):return True,'architecture embedded in mixed map mesh'
    return False,'non-architectural mixed-map surface'
