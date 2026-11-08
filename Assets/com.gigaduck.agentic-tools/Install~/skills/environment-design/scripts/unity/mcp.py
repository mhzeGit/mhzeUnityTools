#!/usr/bin/env python
"""Tiny JSON-RPC client for the Unity MCP HTTP endpoint (env.json unity.mcp_url, default http://127.0.0.1:8080/mcp).

Usage:
  python mcp.py tools                       # list tool names
  python mcp.py schema <tool>               # print one tool's schema
  python mcp.py call <tool> '<json args>'   # call a tool
  python mcp.py exec <file.cs>              # run C# 6 method body through execute_code
  python mcp.py custom <tool_name> '<json>' # execute_custom_tool wrapper
The Mcp-Session-Id is cached in mcp_session.txt next to this script.
"""
import json
import os
import sys
import urllib.request
import urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..")); sys.path.insert(0, HERE)
import envcfg  # noqa: E402
URL = envcfg.CFG["unity"]["mcp_url"]
os.makedirs(envcfg.WORK, exist_ok=True)
SESSION_FILE = os.path.join(envcfg.WORK, "mcp_session.txt")


def _post(payload, session=None, timeout=600):
    data = json.dumps(payload).encode("utf-8")
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
    }
    if session:
        headers["Mcp-Session-Id"] = session
    req = urllib.request.Request(URL, data=data, headers=headers, method="POST")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        body = resp.read().decode("utf-8", "replace")
        sid = resp.headers.get("Mcp-Session-Id")
        return body, sid


def _parse(body):
    body = body.strip()
    if not body:
        return None
    if body.startswith("{") or body.startswith("["):
        return json.loads(body)
    out = None
    for line in body.splitlines():
        if line.startswith("data:"):
            chunk = line[5:].strip()
            if chunk:
                try:
                    out = json.loads(chunk)
                except ValueError:
                    pass
    return out


def init_session():
    body, sid = _post({
        "jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": {"protocolVersion": "2025-03-26", "capabilities": {},
                   "clientInfo": {"name": "claude-code-helper", "version": "1.0"}},
    })
    _post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid)
    with open(SESSION_FILE, "w") as f:
        f.write(sid or "")
    return sid


def session():
    if os.path.exists(SESSION_FILE):
        sid = open(SESSION_FILE).read().strip()
        if sid:
            return sid
    return init_session()


def rpc(method, params=None, _retry=True):
    sid = session()
    payload = {"jsonrpc": "2.0", "id": 2, "method": method}
    if params is not None:
        payload["params"] = params
    try:
        body, _ = _post(payload, sid)
    except urllib.error.HTTPError as e:
        if _retry and e.code in (400, 404, 409):
            if os.path.exists(SESSION_FILE):
                os.remove(SESSION_FILE)
            return rpc(method, params, _retry=False)
        raise
    return _parse(body)


def call(tool, args):
    res = rpc("tools/call", {"name": tool, "arguments": args})
    return res


def show(res):
    if res is None:
        print("(no response)")
        return
    if "error" in res:
        print("ERROR:", json.dumps(res["error"], indent=2))
        return
    result = res.get("result", res)
    content = result.get("content") if isinstance(result, dict) else None
    if content:
        for item in content:
            if item.get("type") == "text":
                print(item["text"])
            else:
                print(json.dumps(item)[:500])
        if result.get("isError"):
            print("[isError=true]")
    else:
        print(json.dumps(result, indent=2))


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return
    cmd = sys.argv[1]
    if cmd == "tools":
        res = rpc("tools/list", {})
        for t in res["result"]["tools"]:
            print(t["name"], "-", (t.get("description") or "").split("\n")[0][:110])
    elif cmd == "schema":
        res = rpc("tools/list", {})
        for t in res["result"]["tools"]:
            if t["name"] == sys.argv[2]:
                print(json.dumps(t, indent=2))
    elif cmd == "call":
        args = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
        show(call(sys.argv[2], args))
    elif cmd == "exec":
        code = open(sys.argv[2], encoding="utf-8").read()
        show(call("execute_code", {"action": "execute", "code": code}))
    elif cmd == "custom":
        params = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
        show(call("execute_custom_tool", {"tool_name": sys.argv[2], "parameters": params}))
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
