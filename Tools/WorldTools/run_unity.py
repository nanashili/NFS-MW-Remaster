"""Run a local editor command through the configured Unity MCP relay."""
import json,sys
from pathlib import Path
from unity_mcp import call
script=Path(sys.argv[1]);result=call('tools/call',{'name':'Unity_RunCommand','arguments':{'Code':script.read_text(),'Title':script.stem}},timeout=300)
print(json.dumps(result,indent=2))
