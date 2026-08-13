using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MissingScriptInspectorWindow : EditorWindow
{
    [MenuItem("Tools/Missing Script Inspector")]
    public static void ShowWindow()
    {
        var w = GetWindow<MissingScriptInspectorWindow>();
        w.titleContent = new GUIContent("Missing Scripts");
        w.Show();
    }

    [MenuItem("GameObject/Missing Script Inspector", false, -1)]
    public static void InspectSelected()
    {
        var w = GetWindow<MissingScriptInspectorWindow>("Missing Scripts");
        w.ScanSelection();
    }

    private Vector2 _scrollPos;
    private Vector2 _detailScrollPos;
    private readonly List<GameObject> _results = new();
    private string _detailText = "";
    private bool _scanning;

    private void OnGUI()
    {
        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Scan Selected", GUILayout.Height(30)))
                ScanSelection();
            if (GUILayout.Button("Scan Active Scene", GUILayout.Height(30)))
                SceneScan();
            if (GUILayout.Button("Clear", GUILayout.Height(30)))
                Clear();
            if (GUILayout.Button("Help", GUILayout.Height(30)))
                _detailText = HelpText();
        }

        if (_scanning)
        {
            EditorGUILayout.HelpBox("Scanning scene hierarchy...", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();

        if (_results.Count > 0)
            EditorGUILayout.LabelField($"Found {_results.Count} GameObject(s) with missing scripts:");

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

        for (int i = _results.Count - 1; i >= 0; i--)
        {
            if (_results[i] == null)
            {
                _results.RemoveAt(i);
                continue;
            }
        }

        for (int i = 0; i < _results.Count; i++)
        {
            var go = _results[i];
            if (go == null) continue;

            var rect = EditorGUILayout.GetControlRect();
            if (GUI.Button(rect, $"  {GetPath(go)}", EditorStyles.label))
            {
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
                _detailText = BuildDetails(go);
            }
        }

        if (_results.Count == 0 && string.IsNullOrEmpty(_detailText))
            EditorGUILayout.HelpBox("No GameObjects with missing scripts found.", MessageType.Info);

        EditorGUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(_detailText))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Details", EditorStyles.boldLabel);
            _detailScrollPos = EditorGUILayout.BeginScrollView(_detailScrollPos);
            EditorGUILayout.TextArea(_detailText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

    private static string HelpText()
    {
        return
            "MISSING SCRIPT INSPECTOR\n" +
            "=========================\n\n" +
            "This tool finds GameObjects with missing (deleted) MonoBehaviour scripts\n" +
            "and recovers what the original script was and what data was stored on it.\n\n" +
            "How it works:\n" +
            "  Unity keeps serialized data even when a script is deleted. This tool\n" +
            "  reads that data directly from the scene/prefab YAML files to extract\n" +
            "  the original script GUID and all serialized field values.\n\n" +
            "Usage:\n" +
            "  1. Select GameObjects in the scene/hierarchy and click 'Scan Selected'\n" +
            "  2. Or click 'Scan Active Scene' to search the entire scene\n" +
            "  3. Click any result to see the original script and its data\n" +
            "  4. Use 'Clear' to reset\n\n" +
            "Note: For prefab instances, data is read from the .prefab file.\n" +
            "      Scene overrides on prefab components are not included.";
    }

    private void Clear()
    {
        _results.Clear();
        _detailText = "";
    }

    private void ScanSelection()
    {
        _results.Clear();
        _detailText = "";
        foreach (var go in Selection.gameObjects)
        {
            if (HasMissing(go))
                _results.Add(go);
        }
        if (_results.Count == 0)
            _detailText = "No missing scripts found in current selection.";
    }

    private void SceneScan()
    {
        _results.Clear();
        _detailText = "";
        _scanning = true;
        Repaint();

        EditorApplication.delayCall += () =>
        {
            try
            {
                var roots = SceneManager.GetActiveScene().GetRootGameObjects();
                foreach (var root in roots)
                    Walk(root);
            }
            finally
            {
                _scanning = false;
                Repaint();
            }
        };
    }

    private void Walk(GameObject go)
    {
        if (HasMissing(go))
            _results.Add(go);
        foreach (Transform c in go.transform)
            Walk(c.gameObject);
    }

    private static bool HasMissing(GameObject go) =>
        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0;

    private static string GetPath(GameObject go)
    {
        var sb = new StringBuilder(go.name);
        var t = go.transform.parent;
        while (t != null)
        {
            sb.Insert(0, '/');
            sb.Insert(0, t.name);
            t = t.parent;
        }
        return sb.ToString();
    }

    private static string BuildDetails(GameObject go)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Path: {GetPath(go)}");
        sb.AppendLine();

        var comps = go.GetComponents<Component>();
        int idx = 0;
        for (int i = 0; i < comps.Length; i++)
        {
            if (comps[i] != null) continue;
            idx++;
            sb.AppendLine($"=== Missing Script #{idx} (component slot {i}) ===");
            ExtractData(go, i, sb);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static long GetComponentPathId(GameObject go, int slot)
    {
        var so = new SerializedObject(go);
        var arr = so.FindProperty("m_Component");
        if (arr == null || !arr.isArray || slot >= arr.arraySize)
            return 0;

        var elem = arr.GetArrayElementAtIndex(slot);
        var pptr = elem.FindPropertyRelative("component") ?? elem;
        return GetField<long>(pptr, "m_PathID");
    }

    private static void ExtractData(GameObject go, int slot, StringBuilder sb)
    {
        var prefabSrc = PrefabUtility.GetCorrespondingObjectFromSource(go);

        string srcPath = null;
        long pathId = 0;

        if (prefabSrc != null)
        {
            srcPath = AssetDatabase.GetAssetPath(prefabSrc);
            pathId = GetComponentPathId(prefabSrc, slot);
            sb.AppendLine("  Source: prefab instance");

            if (pathId == 0)
            {
                var sc = go.scene;
                if (sc.IsValid() && !string.IsNullOrEmpty(sc.path))
                {
                    pathId = GetComponentPathId(go, slot);
                    srcPath = sc.path;
                    sb.AppendLine("  (prefab lookup failed, trying scene file)");
                }
            }
        }
        else
        {
            var sc = go.scene;
            if (sc.IsValid() && !string.IsNullOrEmpty(sc.path))
            {
                srcPath = sc.path;
                pathId = GetComponentPathId(go, slot);
                sb.AppendLine("  Source: scene object");
            }
        }

        if (string.IsNullOrEmpty(srcPath))
        {
            sb.AppendLine("  (cannot resolve source file)");
            return;
        }

        sb.AppendLine($"  File: {srcPath}");
        sb.AppendLine($"  YAML pathID: {pathId}");

        if (pathId == 0)
        {
            sb.AppendLine("  (no pathID — component might be dynamically added)");
            return;
        }

        try
        {
            string yaml = File.ReadAllText(srcPath);
            if (!ParseYamlBlock(yaml, pathId, sb))
                sb.AppendLine("  (component block not found in source file)");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Error: {ex.Message}");
        }
    }

    private static T GetField<T>(SerializedProperty prop, string name)
    {
        try
        {
            var f = typeof(SerializedProperty).GetField(name,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) return (T)f.GetValue(prop);
        }
        catch { }
        return default;
    }

    private static bool ParseYamlBlock(string yaml, long targetPathId, StringBuilder sb)
    {
        string escapedId = Regex.Escape(targetPathId.ToString());
        string pattern = $@"---\s+!u!114\s+&{escapedId}(?!\d)\s*\r?\nMonoBehaviour:\r?\n((?:[ \t].*(?:\r?\n|$))*)";
        var match = Regex.Match(yaml, pattern, RegexOptions.Multiline);

        if (!match.Success)
            return false;

        string body = match.Groups[1].Value;

        var guidMatch = Regex.Match(body,
            @"m_Script:\s*\{.*?guid:\s*([a-fA-F0-9]+)",
            RegexOptions.Singleline);

        string guid = guidMatch.Success ? guidMatch.Groups[1].Value : null;
        if (guid != null)
        {
            string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(scriptPath))
            {
                sb.AppendLine($"  Original script: {Path.GetFileNameWithoutExtension(scriptPath)}");
                sb.AppendLine($"  Script path: {scriptPath}");
            }
            else
            {
                sb.AppendLine($"  Original script GUID: {guid}  (script file deleted from project)");
            }
        }
        else
        {
            sb.AppendLine("  (could not extract script GUID)");
        }

        sb.AppendLine();
        sb.AppendLine("  --- Serialized field data ---");
        int fieldCount = 0;

        foreach (string rawLine in body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("m_ObjectHideFlags:", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("m_CorrespondingSourceObject:", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("m_PrefabInstance:", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("m_PrefabAsset:", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("m_GameObject:", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("m_Enabled:", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("m_EditorHideFlags:", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("m_Script:", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("m_Name:", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("m_EditorClassIdentifier:", StringComparison.Ordinal)) continue;

            sb.AppendLine($"    {line}");
            fieldCount++;
        }

        if (fieldCount == 0)
            sb.AppendLine("    (no custom serialized fields)");

        return true;
    }
}
