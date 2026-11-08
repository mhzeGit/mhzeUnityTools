using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Gigaduck.AgenticTools
{
    /// <summary>A JSON object that keeps its key order (manifest, state file and merged settings are written back in order).</summary>
    public sealed class JsonObject : List<KeyValuePair<string, object>>
    {
        public bool ContainsKey(string key) => IndexOf(key) >= 0;

        public int IndexOf(string key)
        {
            for (int i = 0; i < Count; i++)
                if (this[i].Key == key) return i;
            return -1;
        }

        public object Get(string key)
        {
            int i = IndexOf(key);
            return i < 0 ? null : this[i].Value;
        }

        public string GetString(string key) => Get(key) as string;

        public bool GetBool(string key) => Get(key) is bool b && b;

        public JsonObject GetObject(string key) => Get(key) as JsonObject;

        public List<object> GetList(string key) => Get(key) as List<object>;

        public void Set(string key, object value)
        {
            int i = IndexOf(key);
            if (i < 0) Add(new KeyValuePair<string, object>(key, value));
            else this[i] = new KeyValuePair<string, object>(key, value);
        }
    }

    /// <summary>
    /// Minimal JSON reader and writer with no Unity dependency. <see cref="Write"/> produces the same text as Python's
    /// <c>json.dumps(value, indent=2)</c> (ASCII-only, <c>\uXXXX</c> escapes), so the Unity installer and <c>install.py</c>
    /// write identical files.
    /// </summary>
    public static class AgenticJson
    {
        public static object Parse(string text)
        {
            int at = 0;
            object value = ReadValue(text, ref at);
            SkipSpace(text, ref at);
            if (at != text.Length) throw new FormatException("unexpected text after the JSON value at " + at);
            return value;
        }

        private static void SkipSpace(string s, ref int at)
        {
            while (at < s.Length && (s[at] == ' ' || s[at] == '\t' || s[at] == '\n' || s[at] == '\r')) at++;
        }

        private static object ReadValue(string s, ref int at)
        {
            SkipSpace(s, ref at);
            if (at >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[at];
            if (c == '{')
            {
                var obj = new JsonObject();
                at++;
                SkipSpace(s, ref at);
                if (at < s.Length && s[at] == '}') { at++; return obj; }
                while (true)
                {
                    SkipSpace(s, ref at);
                    if (at >= s.Length || s[at] != '"') throw new FormatException("expected a key at " + at);
                    string key = ReadString(s, ref at);
                    SkipSpace(s, ref at);
                    if (at >= s.Length || s[at] != ':') throw new FormatException("expected ':' at " + at);
                    at++;
                    obj.Set(key, ReadValue(s, ref at));
                    SkipSpace(s, ref at);
                    if (at < s.Length && s[at] == ',') { at++; continue; }
                    if (at < s.Length && s[at] == '}') { at++; return obj; }
                    throw new FormatException("expected ',' or '}' at " + at);
                }
            }

            if (c == '[')
            {
                var list = new List<object>();
                at++;
                SkipSpace(s, ref at);
                if (at < s.Length && s[at] == ']') { at++; return list; }
                while (true)
                {
                    list.Add(ReadValue(s, ref at));
                    SkipSpace(s, ref at);
                    if (at < s.Length && s[at] == ',') { at++; continue; }
                    if (at < s.Length && s[at] == ']') { at++; return list; }
                    throw new FormatException("expected ',' or ']' at " + at);
                }
            }

            if (c == '"') return ReadString(s, ref at);
            if (Word(s, ref at, "true")) return true;
            if (Word(s, ref at, "false")) return false;
            if (Word(s, ref at, "null")) return null;
            int start = at;
            while (at < s.Length && "+-0123456789.eE".IndexOf(s[at]) >= 0) at++;
            if (start == at) throw new FormatException("unexpected character '" + c + "' at " + at);
            string number = s.Substring(start, at - start);
            if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole)) return whole;
            return double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static bool Word(string s, ref int at, string word)
        {
            if (string.CompareOrdinal(s, at, word, 0, word.Length) != 0) return false;
            at += word.Length;
            return true;
        }

        private static string ReadString(string s, ref int at)
        {
            var sb = new StringBuilder();
            at++;
            while (at < s.Length)
            {
                char c = s[at++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (at >= s.Length) break;
                char e = s[at++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        at += 4;
                        break;
                    default: throw new FormatException("bad escape \\" + e);
                }
            }

            throw new FormatException("unterminated string");
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int depth)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteString(sb, s); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d == Math.Floor(d) && Math.Abs(d) < 1e16 ? d.ToString("0.0", CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture)); break;
                case JsonObject obj:
                    if (obj.Count == 0) { sb.Append("{}"); break; }
                    sb.Append("{\n");
                    for (int n = 0; n < obj.Count; n++)
                    {
                        sb.Append(' ', (depth + 1) * 2);
                        WriteString(sb, obj[n].Key);
                        sb.Append(": ");
                        WriteValue(sb, obj[n].Value, depth + 1);
                        sb.Append(n < obj.Count - 1 ? ",\n" : "\n");
                    }

                    sb.Append(' ', depth * 2).Append('}');
                    break;
                case List<object> list:
                    if (list.Count == 0) { sb.Append("[]"); break; }
                    sb.Append("[\n");
                    for (int n = 0; n < list.Count; n++)
                    {
                        sb.Append(' ', (depth + 1) * 2);
                        WriteValue(sb, list[n], depth + 1);
                        sb.Append(n < list.Count - 1 ? ",\n" : "\n");
                    }

                    sb.Append(' ', depth * 2).Append(']');
                    break;
                default: throw new ArgumentException("cannot write " + value.GetType().Name + " as JSON");
            }
        }

        private static void WriteString(StringBuilder sb, string s)
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c > 0x7f) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }
    }
}
