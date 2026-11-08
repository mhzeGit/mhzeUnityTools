#!/usr/bin/env python
"""Compile a scratch C# file inside the running Unity editor (CodeDom) and call a static entry point.

  python ucs.py <source.cs> <Namespace.Type.Method> [arg-string | @argfile] [--emit]

--emit prints the C# body instead of running it: paste it into the execute_code tool of any Unity MCP client (Codex, Kilo, Claude Code
connector) when the HTTP endpoint is not reachable from the shell. @@TOKENS@@ in the source are filled from env.json first.

The entry point must be: public static string Method(string arg). Nothing is written into the
Unity project: the source lives in the scratch directory and is compiled to a temp DLL.
References go through a response file (same trick as the Unity MCP's own execute_code).
"""
import base64
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import mcp  # noqa: E402


def prepare(src):
    """Fill the @@TOKENS@@ of a C# source from env.json and write the result to .utmp/<slug>/unity/<name>.cs (the file the editor compiles)."""
    src = os.path.abspath(src)
    text = open(src, encoding="utf-8").read()
    if "@@" not in text:
        return src
    for tok, val in envcfg.unity_tokens().items():
        text = text.replace(tok, val)
    left = sorted(set(re.findall(r"@@[A-Z_]+@@", text)))
    if left:
        raise SystemExit(f"{os.path.basename(src)}: unresolved tokens {left}: set them in env.json (unity section)")
    out_dir = os.path.join(envcfg.WORK, "unity")
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, os.path.basename(src))
    open(out, "w", encoding="utf-8").write(text)
    return out

BOOT = r'''
var src = System.IO.File.ReadAllText(@"@@SRC@@");
var paths = new System.Collections.Generic.List<string>();
var names = new System.Collections.Generic.HashSet<string>();
bool hasNs = false;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { try { if (!a.IsDynamic && a.GetName().Name == "netstandard") hasNs = true; } catch (System.Exception) { } }
var dup = new System.Collections.Generic.HashSet<string> { "mscorlib", "System.Runtime", "System.Private.CoreLib", "System.Collections" };
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    try
    {
        if (a.IsDynamic) continue;
        var n = a.GetName().Name;
        if (hasNs && dup.Contains(n)) continue;
        var loc = a.Location;
        if (string.IsNullOrEmpty(loc) || !names.Add(n)) continue;
        paths.Add(loc);
    }
    catch (System.Exception) { }
}
var rsp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ucs-" + System.Guid.NewGuid().ToString("N") + ".rsp");
var dll = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ucs-" + System.Guid.NewGuid().ToString("N") + ".dll");
var w = new System.IO.StreamWriter(rsp, false, new System.Text.UTF8Encoding(false));
foreach (var p in paths) w.WriteLine("/r:\"" + p + "\"");
w.Close();
System.Reflection.Assembly asm = null;
try
{
    using (var provider = new Microsoft.CSharp.CSharpCodeProvider())
    {
        var cp = new System.CodeDom.Compiler.CompilerParameters();
        cp.GenerateInMemory = false; cp.OutputAssembly = dll; cp.GenerateExecutable = false; cp.TreatWarningsAsErrors = false;
        cp.CompilerOptions = "@\"" + rsp + "\"";
        var res = provider.CompileAssemblyFromSource(cp, src);
        var sb = new System.Text.StringBuilder();
        foreach (System.CodeDom.Compiler.CompilerError e in res.Errors)
        {
            if (e.IsWarning) continue;
            var t = (e.ErrorText ?? "").Trim('\uFEFF', ' ', '\t', '\r', '\n');
            if (string.IsNullOrEmpty(e.ErrorNumber) && string.IsNullOrEmpty(t)) continue;
            sb.AppendLine("line " + e.Line + ": " + e.ErrorText);
        }
        if (sb.Length > 0) return "COMPILE ERRORS:\n" + sb.ToString();
        if (!System.IO.File.Exists(dll)) return "compile produced no assembly";
        asm = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(dll));
    }
}
finally
{
    try { if (System.IO.File.Exists(dll)) System.IO.File.Delete(dll); } catch (System.Exception) { }
    try { if (System.IO.File.Exists(rsp)) System.IO.File.Delete(rsp); } catch (System.Exception) { }
}
var entry = "@@ENTRY@@";
int dot = entry.LastIndexOf('.');
var type = asm.GetType(entry.Substring(0, dot));
if (type == null) return "type not found: " + entry.Substring(0, dot);
var method = type.GetMethod(entry.Substring(dot + 1), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
if (method == null) return "method not found: " + entry;
var arg = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("@@ARG@@"));
try { return (string)method.Invoke(null, new object[] { arg }); }
catch (System.Reflection.TargetInvocationException tie) { return "EXCEPTION: " + tie.InnerException; }
'''


def boot_code(src, entry, arg=""):
    """The C# method body that compiles <src> inside the editor and invokes entry. Also usable with any MCP client's execute_code (see --emit)."""
    src = prepare(src)
    return (BOOT.replace("@@SRC@@", src.replace('"', '""'))
            .replace("@@ENTRY@@", entry)
            .replace("@@ARG@@", base64.b64encode(arg.encode("utf-8")).decode("ascii")))


def run(src, entry, arg=""):
    code = boot_code(src, entry, arg)
    res = mcp.call("execute_code", {"action": "execute", "code": code, "safety_checks": False})
    if res is None:
        return "(no response)"
    if "error" in res:
        return "MCP ERROR: " + str(res["error"])
    out = []
    for item in res.get("result", {}).get("content", []):
        if item.get("type") != "text":
            continue
        text = item["text"]
        try:
            obj = json.loads(text)
        except ValueError:
            out.append(text)
            continue
        data = obj.get("data") or {}
        if obj.get("success") is False:
            out.append("FAILED: " + str(obj.get("message") or text))
            if isinstance(data, dict) and data.get("errors"):
                out.append(str(data["errors"]))
        else:
            out.append(str(data.get("result", text)) if isinstance(data, dict) else text)
    return "\n".join(out)


EMIT = "--emit" in sys.argv


def main():
    args = [a for a in sys.argv[1:] if a != "--emit"]
    if len(args) < 2:
        print(__doc__)
        return
    sys.argv = [sys.argv[0]] + args
    arg = sys.argv[3] if len(sys.argv) > 3 else ""
    if arg.startswith("@"):
        arg = open(arg[1:], encoding="utf-8").read()
    if "--emit" in os.environ.get("UCS_FLAGS", "") or EMIT:
        print(boot_code(sys.argv[1], sys.argv[2], arg))      # paste into execute_code of an MCP client that is not reachable over HTTP
    else:
        print(run(sys.argv[1], sys.argv[2], arg))


if __name__ == "__main__":
    main()
