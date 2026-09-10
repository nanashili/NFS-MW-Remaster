"""Select original tree models and authored tree groups, excluding low vegetation."""
import re
LOW_VEGETATION=re.compile(r'BUSH|HEDGE|GRASS|FERN|PLANTER')
def category_for(name):
    name=name.upper()
    is_tree=name.startswith('XT_') or name.startswith(('XO_CT_CAMPUSSMACKTREE','XO_CT_CYPRESS'))
    return 'tree' if is_tree and not LOW_VEGETATION.search(name) else 'excluded-object'
def decision(name,material,texture):
    return (category_for(name)=='tree','original tree, canopy, trunk or tree-line geometry' if category_for(name)=='tree' else 'not a tree; shrubs, grass, hedges and planter props excluded')
