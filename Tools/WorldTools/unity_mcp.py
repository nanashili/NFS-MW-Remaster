"""Call the user's configured Unity MCP relay over its stdio transport."""
import json, os, selectors, subprocess, sys, time
RELAY=os.path.expanduser('~/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64')
def call(method, params=None, timeout=90):
    p=subprocess.Popen([RELAY,'--mcp'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,bufsize=1)
    sel=selectors.DefaultSelector(); sel.register(p.stdout,selectors.EVENT_READ)
    def send(v): p.stdin.write(json.dumps(v)+'\n'); p.stdin.flush()
    def receive(i):
        end=time.monotonic()+timeout
        while time.monotonic()<end:
            if sel.select(1):
                line=p.stdout.readline()
                if not line: raise RuntimeError('Unity MCP relay exited')
                try: v=json.loads(line)
                except ValueError: continue
                if v.get('id')==i: return v
        raise TimeoutError('Unity MCP response timed out')
    try:
        send({'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':'2024-11-05','capabilities':{},'clientInfo':{'name':'Codex-map-decoding','version':'1.0'}}})
        receive(1)
        send({'jsonrpc':'2.0','method':'notifications/initialized'})
        send({'jsonrpc':'2.0','id':2,'method':method,'params':params or {}})
        return receive(2)
    finally: p.terminate(); p.wait(timeout=5)
if __name__=='__main__': print(json.dumps(call(sys.argv[1] if len(sys.argv)>1 else 'tools/list',json.loads(sys.argv[2]) if len(sys.argv)>2 else {}),indent=2))
