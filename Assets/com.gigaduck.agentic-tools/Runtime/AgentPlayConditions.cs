#if DEVELOPMENT_BUILD || UNITY_EDITOR || AGENT_PLAY
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>What a condition is checked against: the status now, the status when the wait began, the log position then.</summary>
    public sealed class AgentPlayConditionContext
    {
        public Dictionary<string, object> Status = new Dictionary<string, object>();
        public Dictionary<string, object> Start = new Dictionary<string, object>();
        public int LogFrom;
    }

    /// <summary>
    /// The small condition language of <c>until</c>, route expectations and learning goals (Docs: AGENT_PLAY.md). A condition
    /// is a JSON object; several keys in one object must all hold.
    /// <list type="bullet">
    /// <item><c>{"field":"stage","gt":4}</c>: a status field (dot path) compared with <c>eq ne gt gte lt lte contains in</c>,
    /// <c>"changed":true</c> (differs from when the wait began) or <c>"exists":true</c>; no comparator = truthy.</item>
    /// <item><c>{"near":"Kettle","within":1.5}</c> (an object name or [x,y,z]), <c>{"exists":"Name"}</c> / <c>{"active":"Name"}</c>,
    /// <c>{"gone":"Name"}</c>, <c>{"crosshair":"Name"}</c>, <c>{"text":"Press E"}</c> (on screen), <c>{"log":"substring"}</c>
    /// (a console line since the wait began).</item>
    /// <item><c>{"all":[...]}</c>, <c>{"any":[...]}</c>, <c>{"not":{...}}</c>.</item>
    /// </list>
    /// Projects add keys with <see cref="Register"/> (for example <c>{"holding":"Cup"}</c>).
    /// </summary>
    public static class AgentPlayConditions
    {
        /// <summary>A project condition: <paramref name="arg"/> is the key's value, <paramref name="cond"/> the whole object.</summary>
        public delegate bool Check(object arg, Dictionary<string, object> cond, AgentPlayConditionContext ctx);

        private static readonly Dictionary<string, Check> Custom = new Dictionary<string, Check>(StringComparer.OrdinalIgnoreCase);

        // Keys that only modify another key; never checked on their own.
        private static readonly HashSet<string> Modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "eq", "ne", "gt", "gte", "lt", "lte", "contains", "in", "changed", "within", "why", "label",
        };

        public static void Register(string key, Check check) => Custom[key] = check;

        /// <summary>True when the condition holds. Throws <see cref="ArgumentException"/> for a malformed one.</summary>
        public static bool Evaluate(object cond, AgentPlayConditionContext ctx)
        {
            if (cond is bool b) return b;
            if (cond is List<object> list) return All(list, ctx);
            if (!(cond is Dictionary<string, object> c)) throw new ArgumentException("a condition must be an object, got " + Describe(cond));
            if (c.Count == 0) throw new ArgumentException("empty condition");
            foreach (var kv in c)
            {
                if (Modifiers.Contains(kv.Key)) continue;
                if (kv.Key == "exists" && c.ContainsKey("field")) continue; // {"field":..,"exists":true} checks the field
                if (!One(kv.Key, kv.Value, c, ctx)) return false;
            }

            return true;
        }

        public static string Describe(object cond)
        {
            string text = AgentPlayJson.Write(cond);
            return text.Length > 200 ? text.Substring(0, 200) + "..." : text;
        }

        private static bool All(List<object> items, AgentPlayConditionContext ctx)
        {
            foreach (object item in items)
                if (!Evaluate(item, ctx)) return false;
            return true;
        }

        private static bool One(string key, object arg, Dictionary<string, object> c, AgentPlayConditionContext ctx)
        {
            switch (key)
            {
                case "all": return arg is List<object> all ? All(all, ctx) : throw new ArgumentException("'all' needs a list");
                case "any":
                    if (!(arg is List<object> any)) throw new ArgumentException("'any' needs a list");
                    foreach (object item in any)
                        if (Evaluate(item, ctx)) return true;
                    return false;
                case "not": return !Evaluate(arg, ctx);
                case "field": return Field(Convert.ToString(arg, CultureInfo.InvariantCulture), c, ctx);
                case "exists":
                case "active": return AgentPlayOps.FindObject(Str(arg), false) != null;
                case "gone": return AgentPlayOps.FindObject(Str(arg), false) == null;
                case "near": return Near(arg, c);
                case "crosshair":
                    GameObject under = AgentPlayOps.UnderCrosshair();
                    return under != null && under.name.IndexOf(Str(arg), StringComparison.OrdinalIgnoreCase) >= 0;
                case "text": return AgentPlayOps.ScreenTextLine().IndexOf(Str(arg), StringComparison.OrdinalIgnoreCase) >= 0;
                case "log": return AgentPlayOps.LogSince(ctx.LogFrom, Str(arg));
            }

            if (Custom.TryGetValue(key, out Check check)) return check(arg, c, ctx);
            throw new ArgumentException("unknown condition key '" + key + "' (field, near, exists, active, gone, crosshair, text, log, all, any, not"
                                        + (Custom.Count > 0 ? ", " + string.Join(", ", Custom.Keys) : "") + ")");
        }

        private static bool Near(object arg, Dictionary<string, object> c)
        {
            Transform player = AgentPlayOps.PlayerTransform();
            if (player == null) return false;
            Vector3 point;
            if (arg is List<object> xyz && xyz.Count >= 3) point = new Vector3(Num(xyz[0]), Num(xyz[1]), Num(xyz[2]));
            else
            {
                GameObject go = AgentPlayOps.FindObject(Str(arg), true);
                if (go == null) return false;
                point = AgentPlayOps.Focus(go);
            }

            float within = c.TryGetValue("within", out object w) ? Num(w) : 1.5f;
            Vector3 d = point - player.position;
            d.y = 0f;
            return d.magnitude <= within;
        }

        private static bool Field(string path, Dictionary<string, object> c, AgentPlayConditionContext ctx)
        {
            object v = Lookup(ctx.Status, path);
            bool any = false;
            if (c.TryGetValue("eq", out object eq)) { any = true; if (!Same(v, eq)) return false; }
            if (c.TryGetValue("ne", out object ne)) { any = true; if (Same(v, ne)) return false; }
            if (c.TryGetValue("gt", out object gt)) { any = true; if (!(IsNum(v) && Num(v) > Num(gt))) return false; }
            if (c.TryGetValue("gte", out object gte)) { any = true; if (!(IsNum(v) && Num(v) >= Num(gte))) return false; }
            if (c.TryGetValue("lt", out object lt)) { any = true; if (!(IsNum(v) && Num(v) < Num(lt))) return false; }
            if (c.TryGetValue("lte", out object lte)) { any = true; if (!(IsNum(v) && Num(v) <= Num(lte))) return false; }
            if (c.TryGetValue("contains", out object needle)) { any = true; if (!Contains(v, needle)) return false; }
            if (c.TryGetValue("in", out object set))
            {
                any = true;
                if (!(set is List<object> options) || !options.Exists(o => Same(v, o))) return false;
            }

            if (c.TryGetValue("changed", out object changed) && changed is bool wantChange)
            {
                any = true;
                if (Same(v, Lookup(ctx.Start, path)) == wantChange) return false;
            }

            if (c.TryGetValue("exists", out object exists) && exists is bool wantExists)
            {
                any = true;
                if ((v != null) != wantExists) return false;
            }

            return any || Truthy(v);
        }

        /// <summary>A status value by dot path ("stage", "held.name").</summary>
        public static object Lookup(Dictionary<string, object> map, string path)
        {
            object current = map;
            foreach (string part in path.Split('.'))
            {
                if (!(current is Dictionary<string, object> d) || !d.TryGetValue(part, out current)) return null;
            }

            return current;
        }

        private static bool Contains(object v, object needle)
        {
            switch (v)
            {
                case null: return false;
                case string s: return s.IndexOf(Str(needle), StringComparison.OrdinalIgnoreCase) >= 0;
                case IEnumerable seq:
                    foreach (object item in seq)
                        if (Same(item, needle) || (item is string text && text.IndexOf(Str(needle), StringComparison.OrdinalIgnoreCase) >= 0))
                            return true;
                    return false;
                default: return Same(v, needle);
            }
        }

        private static bool Same(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (IsNum(a) && IsNum(b)) return Math.Abs(Num(a) - Num(b)) < 1e-6;
            if (a is bool ab && b is bool bb) return ab == bb;
            if (a is IList la && b is IList lb)
            {
                if (la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++)
                    if (!Same(la[i], lb[i])) return false;
                return true;
            }

            return string.Equals(Str(a), Str(b), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Truthy(object v) => v switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0 && s != "none",
            ICollection list => list.Count > 0,
            _ => !IsNum(v) || Math.Abs(Num(v)) > 1e-9,
        };

        private static bool IsNum(object v) => v is double || v is float || v is int || v is long;

        private static float Num(object v) => v switch
        {
            double d => (float)d,
            float f => f,
            int i => i,
            long l => l,
            string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) => parsed,
            _ => throw new ArgumentException("expected a number, got " + Describe(v)),
        };

        private static string Str(object v) => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
    }
}
#endif
