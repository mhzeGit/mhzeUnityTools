#if DEVELOPMENT_BUILD || UNITY_EDITOR || AGENT_PLAY
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>
    /// Tiny JSON reader/writer for the agent command files: objects become Dictionary&lt;string, object&gt;, arrays
    /// List&lt;object&gt;, numbers double, plus string, bool and null. No dependency on a JSON package, so the harness
    /// stays a single self-contained pair of files that real release builds leave out.
    /// </summary>
    internal static class AgentPlayJson
    {
        public static List<Dictionary<string, object>> ParseArray(string json)
        {
            int i = 0;
            object root = Value(json, ref i);
            var list = new List<Dictionary<string, object>>();
            if (root is List<object> items)
                foreach (object item in items)
                    if (item is Dictionary<string, object> d) list.Add(d);
            return list;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Emit(sb, value);
            return sb.ToString();
        }

        private static void Emit(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: Str(sb, s); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> map:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in map)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Str(sb, kv.Key);
                        sb.Append(':');
                        Emit(sb, kv.Value);
                    }

                    sb.Append('}');
                    break;
                case IEnumerable seq:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (object item in seq)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        Emit(sb, item);
                    }

                    sb.Append(']');
                    break;
                default: Str(sb, Convert.ToString(v, CultureInfo.InvariantCulture)); break;
            }
        }

        private static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var map = new Dictionary<string, object>();
                Skip(s, ref i);
                if (s[i] == '}') { i++; return map; }
                while (true)
                {
                    Skip(s, ref i);
                    string key = (string)Value(s, ref i);
                    Skip(s, ref i);
                    if (s[i++] != ':') throw new FormatException("expected ':' at " + i);
                    map[key] = Value(s, ref i);
                    Skip(s, ref i);
                    char next = s[i++];
                    if (next == '}') return map;
                    if (next != ',') throw new FormatException("expected ',' or '}' at " + i);
                }
            }

            if (c == '[')
            {
                i++;
                var list = new List<object>();
                Skip(s, ref i);
                if (s[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Add(Value(s, ref i));
                    Skip(s, ref i);
                    char next = s[i++];
                    if (next == ']') return list;
                    if (next != ',') throw new FormatException("expected ',' or ']' at " + i);
                }
            }

            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (s[i] != '"')
                {
                    char ch = s[i++];
                    if (ch != '\\') { sb.Append(ch); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }

                i++;
                return sb.ToString();
            }

            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-.eE0123456789".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("unexpected character '" + c + "' at " + i);
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }
    }
}
#endif
