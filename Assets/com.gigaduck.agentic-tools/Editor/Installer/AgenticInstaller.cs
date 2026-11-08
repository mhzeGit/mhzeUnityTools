using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Gigaduck.AgenticTools
{
    /// <summary>One line of an install plan: ADD, UPDATE, REMOVE, CONFLICT, KEEP or same.</summary>
    public sealed class InstallAction
    {
        public string Verb, Path, Detail, Group;
        public override string ToString() => Verb + " " + Path + (string.IsNullOrEmpty(Detail) ? "" : "  (" + Detail + ")");
    }

    /// <summary>
    /// Installs the package's agent rules, tools and skills into a project (the C# twin of <c>Install~/install.py</c>; both
    /// follow <c>Install~/manifest.json</c> and must stay identical, <c>Install~/test_install.py</c> checks that). It never
    /// overrides a project's own work: tools are copied only where nothing different is in the way, rules are injected as
    /// marked blocks (<c>&lt;!-- agentic-tools:begin id --&gt;</c>) whose surroundings stay untouched, JSON settings only gain
    /// missing keys, and anything edited by hand since it was installed is reported as a conflict instead of being replaced
    /// (unless forced). <c>.agentic-tools.json</c> in the project root records what was installed. No Unity dependency.
    /// </summary>
    public sealed class AgenticInstaller
    {
        public const string StateFile = ".agentic-tools.json";
        private const string Mark = "agentic-tools";

        public readonly string PackageRoot, InstallDir, ProjectRoot;
        public readonly JsonObject Manifest, Package;
        public JsonObject State { get; private set; }

        public AgenticInstaller(string packageRoot, string projectRoot)
        {
            PackageRoot = Path.GetFullPath(packageRoot);
            InstallDir = Path.Combine(PackageRoot, "Install~");
            ProjectRoot = Path.GetFullPath(projectRoot);
            Manifest = (JsonObject)AgenticJson.Parse(ReadTextLf(Path.Combine(InstallDir, "manifest.json")));
            Package = (JsonObject)AgenticJson.Parse(ReadTextLf(Path.Combine(PackageRoot, "package.json")));
            string statePath = Path.Combine(ProjectRoot, StateFile);
            State = File.Exists(statePath) ? (JsonObject)AgenticJson.Parse(ReadTextLf(statePath)) : new JsonObject();
        }

        public string Version => Package.GetString("version");
        public string PackageName => Package.GetString("name");
        public string InstalledVersion => State.GetString("version");
        public bool IsInstalled => State.Count > 0;

        public IEnumerable<JsonObject> Modules => Manifest.GetList("modules").Cast<JsonObject>();

        public IEnumerable<string> VariableNames => Manifest.GetObject("vars").Select(p => p.Key);

        /// <summary>The modules an update installs: the recorded ones, or the defaults on a first install, plus the required ones.</summary>
        public List<string> CurrentModules()
        {
            List<object> recorded = State.GetList("modules");
            var known = Modules.Select(m => m.GetString("id")).ToList();
            var chosen = recorded != null ? recorded.Cast<string>().Where(known.Contains).ToList()
                : Modules.Where(m => m.GetBool("default")).Select(m => m.GetString("id")).ToList();
            return Normalize(chosen);
        }

        /// <summary>Known module ids in manifest order, with the required ones added.</summary>
        public List<string> Normalize(IEnumerable<string> modules)
        {
            var set = new HashSet<string>(modules);
            foreach (JsonObject m in Modules)
                if (m.GetBool("required")) set.Add(m.GetString("id"));
            foreach (string id in set)
                if (!Modules.Any(m => m.GetString("id") == id)) throw new ArgumentException("unknown module '" + id + "'");
            return Modules.Select(m => m.GetString("id")).Where(set.Contains).ToList();
        }

        /// <summary>The recorded path variables (defaults from the manifest).</summary>
        public Dictionary<string, string> CurrentVariables()
        {
            var values = new Dictionary<string, string>();
            JsonObject recorded = State.GetObject("vars") ?? new JsonObject();
            foreach (var pair in Manifest.GetObject("vars"))
                values[pair.Key] = recorded.GetString(pair.Key) ?? (string)pair.Value;
            return values;
        }

        // ------------------------------------------------------------------ text, hashes, variables

        public static string Sha(byte[] data)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        private static bool IsText(byte[] data) => Array.IndexOf(data, (byte)0) < 0;

        private static byte[] Normalized(byte[] data)
        {
            if (!IsText(data)) return data;
            var output = new List<byte>(data.Length);
            for (int i = 0; i < data.Length; i++)
                if (!(data[i] == '\r' && i + 1 < data.Length && data[i + 1] == '\n')) output.Add(data[i]);
            return output.ToArray();
        }

        private static string FileHash(byte[] data) => Sha(Normalized(data));

        private static bool HasCrlf(byte[] data)
        {
            for (int i = 0; i + 1 < data.Length; i++)
                if (data[i] == '\r' && data[i + 1] == '\n') return true;
            return false;
        }

        private static byte[] InStyleOf(byte[] data, byte[] existing)
        {
            if (existing == null || !IsText(data) || !HasCrlf(existing)) return data;
            var output = new List<byte>(data.Length + 64);
            foreach (byte b in Normalized(data))
            {
                if (b == '\n') output.Add((byte)'\r');
                output.Add(b);
            }

            return output.ToArray();
        }

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private static string Decode(byte[] data)
        {
            int skip = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            return Utf8.GetString(data, skip, data.Length - skip).Replace("\r\n", "\n");
        }

        private static string ReadTextLf(string path) => Decode(File.ReadAllBytes(path));

        private static string RelPath(string target, string start)
        {
            var t = target.Split('/').Where(p => p.Length > 0 && p != ".").ToList();
            var s = start.Split('/').Where(p => p.Length > 0 && p != ".").ToList();
            while (t.Count > 0 && s.Count > 0 && t[0] == s[0]) { t.RemoveAt(0); s.RemoveAt(0); }
            string result = string.Join("/", Enumerable.Repeat("..", s.Count).Concat(t));
            return result.Length > 0 ? result : ".";
        }

        private Dictionary<string, string> AllVariables(Dictionary<string, string> variables)
        {
            var values = new Dictionary<string, string>();
            foreach (var pair in Manifest.GetObject("vars"))
                values[pair.Key] = variables != null && variables.TryGetValue(pair.Key, out string v) ? v : (string)pair.Value;
            values["PROJECT"] = Path.GetFileName(ProjectRoot.TrimEnd('/', '\\'));
            values["RULES_TO_ROOT"] = RelPath("", values["RULES_DIR"]);
            values["RULES_TO_DOCS"] = RelPath(values["DOCS_DIR"], values["RULES_DIR"]);
            values["DOCS_TO_ROOT"] = RelPath("", values["DOCS_DIR"]);
            values["DOCS_TO_RULES"] = RelPath(values["RULES_DIR"], values["DOCS_DIR"]);
            return values;
        }

        private static string Render(string text, Dictionary<string, string> values, string where) =>
            Regex.Replace(text, @"\{\{([A-Z_]+)\}\}", m =>
            {
                if (!values.TryGetValue(m.Groups[1].Value, out string value))
                    throw new InvalidOperationException(where + " uses an unknown variable {{" + m.Groups[1].Value + "}}");
                return value;
            });

        // ------------------------------------------------------------------ blocks

        private static string MarkerStyle(string dst)
        {
            string lower = dst.ToLowerInvariant();
            return lower.EndsWith(".md") || lower.EndsWith(".mdc") || lower.EndsWith(".html") ? "html" : "hash";
        }

        private static void Markers(string style, string id, out string begin, out string end)
        {
            if (style == "html") { begin = "<!-- " + Mark + ":begin " + id + " -->"; end = "<!-- " + Mark + ":end " + id + " -->"; }
            else { begin = "# " + Mark + ":begin " + id; end = "# " + Mark + ":end " + id; }
        }

        /// <summary>Position after optional spaces/tabs and a line break (or the end of the text); -1 when another character follows.</summary>
        private static int LineEnd(string text, int at)
        {
            while (at < text.Length && (text[at] == ' ' || text[at] == '\t')) at++;
            if (at == text.Length) return at;
            return text[at] == '\n' ? at + 1 : -1;
        }

        private static bool FindBlock(string text, string style, string id, out int start, out int end, out string body)
        {
            Markers(style, id, out string begin, out string close);
            start = end = -1;
            body = null;
            int bodyStart = -1;
            for (int from = 0; ;)
            {
                int at = text.IndexOf(begin, from, StringComparison.Ordinal);
                if (at < 0) return false;
                int after = LineEnd(text, at + begin.Length);
                if ((at == 0 || text[at - 1] == '\n') && after >= 0) { start = at; bodyStart = after; break; }
                from = at + 1;
            }

            for (int from = bodyStart - 1; ;)
            {
                int at = text.IndexOf("\n" + close, from, StringComparison.Ordinal);
                if (at < 0) throw new InvalidOperationException("block '" + id + "' has a begin marker but no end marker");
                int after = LineEnd(text, at + 1 + close.Length);
                if (after >= 0) { end = after; body = text.Substring(bodyStart, at + 1 - bodyStart); return true; }
                from = at + 1;
            }
        }

        private static string BlockText(string style, string id, string body)
        {
            Markers(style, id, out string begin, out string end);
            return begin + "\n" + body + end + "\n";
        }

        private static string BodyOf(string text) => text.TrimEnd('\n') + "\n";

        // ------------------------------------------------------------------ plan

        /// <summary>What an install would do. Nothing is written until <see cref="Apply"/>.</summary>
        public sealed class Plan
        {
            internal AgenticInstaller Owner;
            public List<string> Modules;
            public Dictionary<string, string> Values;
            public bool Force;
            public readonly List<InstallAction> Actions = new List<InstallAction>();
            internal readonly Dictionary<string, byte[]> Writes = new Dictionary<string, byte[]>();
            internal readonly Dictionary<string, string> NewFiles = new Dictionary<string, string>();
            internal readonly Dictionary<string, string> NewBlocks = new Dictionary<string, string>();
            internal readonly Dictionary<string, string> Texts = new Dictionary<string, string>();
            internal readonly List<string> TextOrder = new List<string>();
            private readonly Dictionary<string, byte[]> _originals = new Dictionary<string, byte[]>();

            public IEnumerable<InstallAction> Conflicts => Actions.Where(a => a.Verb == "CONFLICT");
            public int Changes => Actions.Count(a => a.Verb == "ADD" || a.Verb == "UPDATE" || a.Verb == "REMOVE");

            internal void Note(string verb, string path, string detail = "", string group = null) =>
                Actions.Add(new InstallAction { Verb = verb, Path = path, Detail = detail, Group = group });

            internal byte[] Original(string rel)
            {
                if (!_originals.TryGetValue(rel, out byte[] data))
                {
                    string path = Path.Combine(Owner.ProjectRoot, rel);
                    _originals[rel] = data = File.Exists(path) ? File.ReadAllBytes(path) : null;
                }

                return data;
            }

            internal string TextOf(string rel)
            {
                if (!Texts.TryGetValue(rel, out string text))
                {
                    byte[] data = Original(rel);
                    SetText(rel, text = data == null ? null : Decode(data));
                }

                return text;
            }

            internal void SetText(string rel, string text)
            {
                if (!Texts.ContainsKey(rel)) TextOrder.Add(rel);
                Texts[rel] = text;
            }

            /// <summary>The plan as lines, folders of copied files counted per verb unless <paramref name="verbose"/>.</summary>
            public List<string> Describe(bool verbose)
            {
                var lines = new List<string>();
                var counted = new List<KeyValuePair<string, int>>();
                foreach (InstallAction a in Actions)
                {
                    if (a.Group != null && !verbose && (a.Verb == "same" || a.Verb == "ADD" || a.Verb == "UPDATE"))
                    {
                        string key = a.Group + "\n" + a.Verb;
                        int i = counted.FindIndex(p => p.Key == key);
                        if (i < 0) counted.Add(new KeyValuePair<string, int>(key, 1));
                        else counted[i] = new KeyValuePair<string, int>(key, counted[i].Value + 1);
                        continue;
                    }

                    if (a.Verb == "same" && !verbose) continue;
                    lines.Add(string.Format("  {0,-8} {1}{2}", a.Verb, a.Path, string.IsNullOrEmpty(a.Detail) ? "" : "  (" + a.Detail + ")"));
                }

                foreach (var pair in counted)
                {
                    string[] parts = pair.Key.Split('\n');
                    if (parts[1] != "same")
                        lines.Add(string.Format("  {0,-8} {1} ({2} file{3})", parts[1], parts[0], pair.Value, pair.Value == 1 ? "" : "s"));
                }

                return lines;
            }
        }

        private void PlanCopy(Plan plan, string dst, string src, string group)
        {
            byte[] data = File.ReadAllBytes(src);
            string want = FileHash(data);
            byte[] current = plan.Original(dst);
            string recorded = State.GetObject("files")?.GetString(dst);
            if (current != null && FileHash(current) == want)
            {
                plan.NewFiles[dst] = want;
                plan.Note("same", dst, "", group);
            }
            else if (current == null)
            {
                plan.NewFiles[dst] = want;
                plan.Writes[dst] = data;
                plan.Note("ADD", dst, "", group);
            }
            else if (recorded != null && FileHash(current) == recorded)
            {
                plan.NewFiles[dst] = want;
                plan.Writes[dst] = InStyleOf(data, current);
                plan.Note("UPDATE", dst, "", group);
            }
            else if (plan.Force)
            {
                plan.NewFiles[dst] = want;
                plan.Writes[dst] = InStyleOf(data, current);
                plan.Note("UPDATE", dst, "forced over a local edit");
            }
            else
            {
                if (recorded != null) plan.NewFiles[dst] = recorded;
                plan.Note("CONFLICT", dst, recorded != null ? "edited by hand since it was installed" : "a file nobody installed is already there");
            }
        }

        private void PlanBlock(Plan plan, JsonObject item)
        {
            string src = item.GetString("src");
            string dst = Render(item.GetString("dst"), plan.Values, src).Trim('/');
            string id = item.GetString("id");
            string style = MarkerStyle(dst);
            string body = BodyOf(Render(ReadTextLf(Path.Combine(InstallDir, src)), plan.Values, src));
            string key = dst + "#" + id;
            string want = Sha(Utf8.GetBytes(body));
            string text = plan.TextOf(dst);
            if (text == null)
            {
                string headerSrc = item.GetString("header");
                string header = headerSrc != null ? Render(ReadTextLf(Path.Combine(InstallDir, headerSrc)), plan.Values, headerSrc) : "";
                plan.SetText(dst, header + BlockText(style, id, body));
                plan.NewBlocks[key] = want;
                plan.Note("ADD", key, "new file");
                return;
            }

            if (!FindBlock(text, style, id, out int start, out int end, out string old))
            {
                string after = item.GetString("after");
                if (after != null && FindBlock(text, style, after, out _, out int anchorEnd, out _))
                {
                    plan.SetText(dst, text.Substring(0, anchorEnd) + "\n" + BlockText(style, id, body) + text.Substring(anchorEnd));
                }
                else
                {
                    if (text.Length > 0 && !text.EndsWith("\n")) text += "\n";
                    plan.SetText(dst, text + (text.Length > 0 ? "\n" : "") + BlockText(style, id, body));
                }

                plan.NewBlocks[key] = want;
                plan.Note("ADD", key, "injected");
                return;
            }

            string recorded = State.GetObject("blocks")?.GetString(key);
            bool untouched = recorded != null && Sha(Utf8.GetBytes(old)) == recorded;
            if (old == body)
            {
                plan.NewBlocks[key] = want;
                plan.Note("same", key);
            }
            else if (untouched || plan.Force)
            {
                plan.SetText(dst, text.Substring(0, start) + BlockText(style, id, body) + text.Substring(end));
                plan.NewBlocks[key] = want;
                plan.Note("UPDATE", key, untouched ? "" : "forced over a local edit");
            }
            else
            {
                if (recorded != null) plan.NewBlocks[key] = recorded;
                plan.Note("CONFLICT", key, "the block was edited by hand");
            }
        }

        /// <summary>Adds the keys of <paramref name="source"/> that <paramref name="target"/> lacks, <paramref name="depth"/> levels
        /// deep: with 2 a missing server is added to "mcpServers", but a server the project already defines stays as it is.</summary>
        private static bool MergeMissing(JsonObject target, JsonObject source, int depth)
        {
            bool changed = false;
            foreach (var pair in source)
            {
                if (!target.ContainsKey(pair.Key)) { target.Set(pair.Key, pair.Value); changed = true; }
                else if (depth > 1 && target.Get(pair.Key) is JsonObject inner && pair.Value is JsonObject wanted)
                    changed |= MergeMissing(inner, wanted, depth - 1);
            }

            return changed;
        }

        private void PlanJson(Plan plan, JsonObject item)
        {
            string src = item.GetString("src");
            string dst = Render(item.GetString("dst"), plan.Values, src).Trim('/');
            var wanted = (JsonObject)AgenticJson.Parse(ReadTextLf(Path.Combine(InstallDir, src)));
            byte[] current = plan.Original(dst);
            string recorded = State.GetObject("files")?.GetString(dst);
            if (current == null)
            {
                byte[] created = Utf8.GetBytes(AgenticJson.Write(wanted) + "\n");
                plan.Writes[dst] = created;
                plan.NewFiles[dst] = FileHash(created); // created here, so an uninstall may remove it again
                plan.Note("ADD", dst, "new file");
                return;
            }

            bool ours = recorded != null && FileHash(current) == recorded;

            JsonObject data;
            try { data = AgenticJson.Parse(Decode(current)) as JsonObject ?? throw new FormatException("not an object"); }
            catch (FormatException)
            {
                plan.Note("CONFLICT", dst, "not plain JSON, so the keys cannot be merged: add them by hand");
                return;
            }

            if (MergeMissing(data, wanted, 2))
            {
                byte[] merged = InStyleOf(Utf8.GetBytes(AgenticJson.Write(data) + "\n"), current);
                plan.Writes[dst] = merged;
                if (ours) plan.NewFiles[dst] = FileHash(merged);
                plan.Note("UPDATE", dst, "added missing keys");
            }
            else
            {
                if (ours) plan.NewFiles[dst] = recorded;
                plan.Note("same", dst);
            }
        }

        private Dictionary<string, string> HeadersByFile(Plan plan)
        {
            var headers = new Dictionary<string, string>();
            foreach (JsonObject module in Modules)
                foreach (JsonObject item in module.GetList("items").Cast<JsonObject>())
                {
                    string header = item.GetString("header");
                    if (item.GetString("kind") != "block" || header == null) continue;
                    string dst = Render(item.GetString("dst"), plan.Values, item.GetString("src")).Trim('/');
                    headers[dst] = Render(ReadTextLf(Path.Combine(InstallDir, header)), plan.Values, header);
                }

            return headers;
        }

        private static string Strip(string s) => s.Trim(' ', '\t', '\n', '\r', '\f', '\v');

        private void PlanRemovals(Plan plan)
        {
            Dictionary<string, string> headers = HeadersByFile(plan);
            JsonObject files = State.GetObject("files") ?? new JsonObject();
            foreach (var pair in files.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (plan.NewFiles.ContainsKey(pair.Key)) continue;
                byte[] current = plan.Original(pair.Key);
                if (current == null) continue;
                if (FileHash(current) == (string)pair.Value || plan.Force)
                {
                    plan.Writes[pair.Key] = null;
                    plan.Note("REMOVE", pair.Key);
                }
                else plan.Note("KEEP", pair.Key, "no longer installed, but edited by hand: left in place");
            }

            JsonObject blocks = State.GetObject("blocks") ?? new JsonObject();
            foreach (var pair in blocks.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (plan.NewBlocks.ContainsKey(pair.Key)) continue;
                int hash = pair.Key.LastIndexOf('#');
                string rel = pair.Key.Substring(0, hash), id = pair.Key.Substring(hash + 1);
                string text = plan.TextOf(rel);
                if (string.IsNullOrEmpty(text) || !FindBlock(text, MarkerStyle(rel), id, out int start, out int end, out string old)) continue;
                if (Sha(Utf8.GetBytes(old)) == (string)pair.Value || plan.Force)
                {
                    string before = text.Substring(0, start), after = text.Substring(end);
                    if (before.EndsWith("\n\n") && (after.StartsWith("\n") || after.Length == 0)) before = before.Substring(0, before.Length - 1);
                    string rest = before + after;
                    if (Strip(rest) == Strip(headers.TryGetValue(rel, out string header) ? header : "")) rest = "";
                    plan.SetText(rel, rest);
                    plan.Note("REMOVE", pair.Key);
                }
                else plan.Note("KEEP", pair.Key, "no longer installed, but edited by hand: left in place");
            }
        }

        private List<KeyValuePair<string, string>> ExpandCopy(JsonObject item, Dictionary<string, string> values)
        {
            string srcRel = item.GetString("src");
            string src = Path.Combine(InstallDir, srcRel);
            string dst = Render(item.GetString("dst"), values, srcRel).Trim('/');
            var pairs = new List<KeyValuePair<string, string>>();
            if (File.Exists(src)) { pairs.Add(new KeyValuePair<string, string>(dst, src)); return pairs; }
            if (!Directory.Exists(src)) throw new InvalidOperationException("package file missing: " + srcRel);
            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(src.Length).Replace('\\', '/').TrimStart('/');
                if (rel.Split('/').Contains("__pycache__") || rel.EndsWith(".pyc")) continue;
                pairs.Add(new KeyValuePair<string, string>(dst + "/" + rel, file));
            }

            pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return pairs;
        }

        /// <summary>Works out what installing <paramref name="modules"/> with <paramref name="variables"/> would change.</summary>
        public Plan MakePlan(IEnumerable<string> modules, Dictionary<string, string> variables, bool force)
        {
            var plan = new Plan { Owner = this, Modules = modules.ToList(), Values = AllVariables(variables), Force = force };
            foreach (JsonObject module in Modules)
            {
                if (!plan.Modules.Contains(module.GetString("id"))) continue;
                foreach (JsonObject item in module.GetList("items").Cast<JsonObject>())
                {
                    switch (item.GetString("kind"))
                    {
                        case "copy":
                            List<KeyValuePair<string, string>> pairs = ExpandCopy(item, plan.Values);
                            string group = pairs.Count > 1 ? Render(item.GetString("dst"), plan.Values, item.GetString("src")).Trim('/') + "/" : null;
                            foreach (var pair in pairs) PlanCopy(plan, pair.Key, pair.Value, group);
                            break;
                        case "block": PlanBlock(plan, item); break;
                        case "json-merge": PlanJson(plan, item); break;
                        default: throw new InvalidOperationException("unknown item kind '" + item.GetString("kind") + "' in manifest.json");
                    }
                }
            }

            PlanRemovals(plan);
            foreach (string rel in plan.TextOrder)
            {
                string text = plan.Texts[rel];
                if (text == null) continue;
                byte[] original = plan.Original(rel);
                byte[] data = InStyleOf(Utf8.GetBytes(text), original);
                if (original != null && original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF)
                    data = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(data).ToArray();
                if (original == null || !data.SequenceEqual(original))
                    plan.Writes[rel] = Strip(text).Length > 0 ? data : null;
            }

            return plan;
        }

        /// <summary>Writes the plan and the state file. Returns the number of files written or deleted.</summary>
        public int Apply(Plan plan)
        {
            int count = 0;
            foreach (var pair in plan.Writes.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string path = Path.Combine(ProjectRoot, pair.Key);
                if (pair.Value == null)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        RemoveEmptyFolders(Path.GetDirectoryName(path));
                        count++;
                    }

                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".agentic-tmp";
                File.WriteAllBytes(temp, pair.Value);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                count++;
            }

            string statePath = Path.Combine(ProjectRoot, StateFile);
            if (plan.Modules.Count == 0)
            {
                if (File.Exists(statePath)) File.Delete(statePath);
                State = new JsonObject();
                return count;
            }

            var state = new JsonObject();
            state.Set("package", PackageName);
            state.Set("version", Version);
            state.Set("modules", Modules.Select(m => m.GetString("id")).Where(plan.Modules.Contains).Cast<object>().ToList());
            var vars = new JsonObject();
            foreach (string name in VariableNames) vars.Set(name, plan.Values[name]);
            state.Set("vars", vars);
            state.Set("files", Sorted(plan.NewFiles));
            state.Set("blocks", Sorted(plan.NewBlocks));
            byte[] existing = File.Exists(statePath) ? File.ReadAllBytes(statePath) : null;
            File.WriteAllBytes(statePath, InStyleOf(Utf8.GetBytes(AgenticJson.Write(state) + "\n"), existing));
            State = state;
            return count;
        }

        private static JsonObject Sorted(Dictionary<string, string> values)
        {
            var obj = new JsonObject();
            foreach (var pair in values.OrderBy(p => p.Key, StringComparer.Ordinal)) obj.Set(pair.Key, pair.Value);
            return obj;
        }

        private void RemoveEmptyFolders(string folder)
        {
            string root = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            while (folder != null && folder.Length > root.Length && folder.StartsWith(root, StringComparison.Ordinal))
            {
                if (Directory.EnumerateFileSystemEntries(folder).Any()) return;
                Directory.Delete(folder);
                folder = Path.GetDirectoryName(folder);
            }
        }

        // ------------------------------------------------------------------ personal skills

        /// <summary>The skill folders of Codex, Claude Code, Kilo and Gemini in this user's home.</summary>
        public static List<string> PersonalSkillRoots()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            return new List<string>
            {
                string.IsNullOrEmpty(codexHome) ? Path.Combine(home, ".codex", "skills") : Path.Combine(codexHome, "skills"),
                Path.Combine(home, ".claude", "skills"),
                Path.Combine(home, ".config", "kilo", "skills"),
                Path.Combine(home, ".gemini", "config", "skills"),
            };
        }

        /// <summary>Copies every skill to <see cref="PersonalSkillRoots"/> (overwriting older copies). Returns the folders written.</summary>
        public List<string> InstallPersonalSkills()
        {
            var written = new List<string>();
            string skills = Path.Combine(InstallDir, "skills");
            foreach (string root in PersonalSkillRoots())
            {
                foreach (string skill in Directory.GetDirectories(skills))
                {
                    if (!File.Exists(Path.Combine(skill, "SKILL.md"))) continue;
                    foreach (string file in Directory.GetFiles(skill, "*", SearchOption.AllDirectories))
                    {
                        string rel = file.Substring(skill.Length).TrimStart('/', '\\');
                        if (rel.Contains("__pycache__") || rel.EndsWith(".pyc")) continue;
                        string target = Path.Combine(root, Path.GetFileName(skill), rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(file, target, true);
                    }
                }

                written.Add(root);
            }

            return written;
        }
    }
}
