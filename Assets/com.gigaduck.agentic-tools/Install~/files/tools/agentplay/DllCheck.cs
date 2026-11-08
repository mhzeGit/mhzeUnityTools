// DllCheck: compares two builds of a player assembly and says whether the new one can replace the old one inside an
// already built player (agentplay.py patch). Built and run by agentplay.py with the .NET SDK bundled with Unity.
//
//   DllCheck <old.dll> <new.dll>   prints one JSON object:
//     layout      types whose serialized field list (name, type, order) changed: scene and prefab data in the build
//                 was written with the old layout, so the player would read it wrong
//     added       new types that may be Unity objects (MonoBehaviour, ScriptableObject...): the build has no MonoScript for them
//     removed     Unity-object types that are gone: scene objects using them would have a missing script
//     runtimeInit the [RuntimeInitializeOnLoadMethod] methods in the new assembly (the build lists them in
//                 RuntimeInitializeOnLoads.json, so a changed list must be written there)
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

static class DllCheck
{
    sealed class Names : ISignatureTypeProvider<string, object>
    {
        readonly MetadataReader r;
        public Names(MetadataReader reader) { r = reader; }
        public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
        public string GetTypeFromDefinition(MetadataReader m, TypeDefinitionHandle h, byte k) => FullName(m, h);
        public string GetTypeFromReference(MetadataReader m, TypeReferenceHandle h, byte k) => RefName(m, h);
        public string GetTypeFromSpecification(MetadataReader m, object c, TypeSpecificationHandle h, byte k) => m.GetTypeSpecification(h).DecodeSignature(this, c);
        public string GetSZArrayType(string e) => e + "[]";
        public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
        public string GetByReferenceType(string e) => e + "&";
        public string GetPointerType(string e) => e + "*";
        public string GetPinnedType(string e) => e;
        public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
        public string GetGenericTypeParameter(object c, int i) => "!" + i;
        public string GetGenericMethodParameter(object c, int i) => "!!" + i;
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
        public string GetModifiedType(string m, string u, bool req) => u;
    }

    static string FullName(MetadataReader m, TypeDefinitionHandle h)
    {
        var t = m.GetTypeDefinition(h);
        var name = m.GetString(t.Name);
        var outer = t.GetDeclaringType();
        if (!outer.IsNil) return FullName(m, outer) + "/" + name;
        var ns = m.GetString(t.Namespace);
        return ns.Length > 0 ? ns + "." + name : name;
    }

    static string RefName(MetadataReader m, TypeReferenceHandle h)
    {
        var t = m.GetTypeReference(h);
        var name = m.GetString(t.Name);
        if (t.ResolutionScope.Kind == HandleKind.TypeReference) return RefName(m, (TypeReferenceHandle)t.ResolutionScope) + "/" + name;
        var ns = m.GetString(t.Namespace);
        return ns.Length > 0 ? ns + "." + name : name;
    }

    static string AttributeName(MetadataReader m, CustomAttribute a)
    {
        if (a.Constructor.Kind == HandleKind.MemberReference)
        {
            var parent = m.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent;
            if (parent.Kind == HandleKind.TypeReference) return RefName(m, (TypeReferenceHandle)parent);
            if (parent.Kind == HandleKind.TypeDefinition) return FullName(m, (TypeDefinitionHandle)parent);
        }
        else if (a.Constructor.Kind == HandleKind.MethodDefinition)
        {
            return FullName(m, m.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType());
        }
        return "";
    }

    static bool HasAttribute(MetadataReader m, CustomAttributeHandleCollection list, params string[] names)
    {
        foreach (var h in list)
            if (Array.IndexOf(names, AttributeName(m, m.GetCustomAttribute(h))) >= 0) return true;
        return false;
    }

    sealed class Info
    {
        public Dictionary<string, string> Layout = new Dictionary<string, string>();
        public HashSet<string> UnityTypes = new HashSet<string>();
        public List<string> RuntimeInit = new List<string>();
    }

    static Info Read(string path)
    {
        var info = new Info();
        using var pe = new PEReader(File.OpenRead(path));
        var m = pe.GetMetadataReader();
        var names = new Names(m);
        foreach (var h in m.TypeDefinitions)
        {
            var t = m.GetTypeDefinition(h);
            var full = FullName(m, h);
            if (full.Contains('<')) continue; // compiler generated
            var fields = new List<string>();
            foreach (var fh in t.GetFields())
            {
                var f = m.GetFieldDefinition(fh);
                var a = f.Attributes;
                if ((a & (FieldAttributes.Static | FieldAttributes.Literal)) != 0) continue;
                bool explicitField = HasAttribute(m, f.GetCustomAttributes(), "UnityEngine.SerializeField", "UnityEngine.SerializeReference");
                bool publicField = (a & FieldAttributes.FieldAccessMask) == FieldAttributes.Public
                                   && (a & (FieldAttributes.InitOnly | FieldAttributes.NotSerialized)) == 0;
                if (explicitField || publicField)
                    fields.Add(m.GetString(f.Name) + ":" + f.DecodeSignature(names, null) + (explicitField && (a & FieldAttributes.NotSerialized) != 0 ? "!ns" : ""));
            }
            info.Layout[full] = string.Join(";", fields);
            if (MaybeUnityObject(m, h)) info.UnityTypes.Add(full);
            foreach (var mh in t.GetMethods())
            {
                var method = m.GetMethodDefinition(mh);
                foreach (var ah in method.GetCustomAttributes())
                {
                    var attribute = m.GetCustomAttribute(ah);
                    if (AttributeName(m, attribute) != "UnityEngine.RuntimeInitializeOnLoadMethodAttribute") continue;
                    var blob = m.GetBlobBytes(attribute.Value);
                    int load = blob.Length >= 6 ? BitConverter.ToInt32(blob, 2) : 0; // default: AfterSceneLoad
                    var ns = t.GetDeclaringType().IsNil ? m.GetString(t.Namespace) : "";
                    info.RuntimeInit.Add(Json(ns) + "|" + Json(TypeOnlyName(m, h)) + "|" + Json(m.GetString(method.Name)) + "|" + load);
                }
            }
        }
        info.RuntimeInit.Sort(StringComparer.Ordinal);
        return info;
    }

    static string TypeOnlyName(MetadataReader m, TypeDefinitionHandle h)
    {
        var t = m.GetTypeDefinition(h);
        var outer = t.GetDeclaringType();
        return outer.IsNil ? m.GetString(t.Name) : TypeOnlyName(m, outer) + "/" + m.GetString(t.Name);
    }

    /// True when the base chain leaves this assembly at a non-System type: it may be a Unity object
    /// (MonoBehaviour, ScriptableObject or a package's base class). Erring on the side of "maybe" only costs a full build.
    static bool MaybeUnityObject(MetadataReader m, TypeDefinitionHandle h)
    {
        for (int guard = 0; guard < 64; guard++)
        {
            var t = m.GetTypeDefinition(h);
            var b = t.BaseType;
            if (b.IsNil) return false; // interface or System.Object itself
            if (b.Kind == HandleKind.TypeDefinition) { h = (TypeDefinitionHandle)b; continue; }
            string name = b.Kind == HandleKind.TypeReference ? RefName(m, (TypeReferenceHandle)b)
                        : m.GetTypeSpecification((TypeSpecificationHandle)b).DecodeSignature(new Names(m), null);
            if (name.StartsWith("System.", StringComparison.Ordinal)) return false;
            return true;
        }
        return true;
    }

    static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    static string List(IEnumerable<string> items) => "[" + string.Join(",", items.Select(Json)) + "]";

    static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("usage: DllCheck <old.dll> <new.dll>"); return 2; }
        var old = Read(args[0]);
        var now = Read(args[1]);
        var layout = now.Layout.Where(p => old.Layout.TryGetValue(p.Key, out var before) && before != p.Value)
                               .Select(p => p.Key).OrderBy(x => x, StringComparer.Ordinal);
        var added = now.UnityTypes.Where(x => !old.UnityTypes.Contains(x)).OrderBy(x => x, StringComparer.Ordinal);
        var removed = old.UnityTypes.Where(x => !now.UnityTypes.Contains(x)).OrderBy(x => x, StringComparer.Ordinal);
        var init = now.RuntimeInit.Select(x =>
        {
            var p = x.Split('|');
            return "{\"nameSpace\":" + p[0] + ",\"className\":" + p[1] + ",\"methodName\":" + p[2] + ",\"loadTypes\":" + p[3] + "}";
        });
        Console.WriteLine("{\"layout\":" + List(layout) + ",\"added\":" + List(added) + ",\"removed\":" + List(removed)
                          + ",\"runtimeInitChanged\":" + (old.RuntimeInit.SequenceEqual(now.RuntimeInit) ? "false" : "true")
                          + ",\"runtimeInit\":[" + string.Join(",", init) + "]}");
        return 0;
    }
}
