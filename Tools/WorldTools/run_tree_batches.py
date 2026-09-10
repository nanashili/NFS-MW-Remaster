"""Run bounded tree import batches through the configured editor relay."""
import json,sys
from pathlib import Path
from unity_mcp import call
script=Path(sys.argv[1]);count=int(sys.argv[2]);prefix=sys.argv[3]
for i in range(count):
    result=call('tools/call',{'name':'Unity_RunCommand','arguments':{'Code':script.read_text(),'Title':script.stem}},timeout=300)
    Path(f'Art/RockportTrees/Source/{prefix}-{i+1:02}.json').write_text(json.dumps(result,indent=2)+'\n')
    body=json.loads(result['result']['content'][0]['text'])
    print(body.get('success'),body.get('data',{}).get('executionLogs'),flush=True)
    if not body.get('success'):raise RuntimeError(body.get('message'))
