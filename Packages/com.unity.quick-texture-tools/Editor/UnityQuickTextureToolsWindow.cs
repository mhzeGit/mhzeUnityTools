using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityQuickTextureTools.Editor
{
    public class UnityQuickTextureToolsWindow : EditorWindow
    {
        private enum Tab { Invert, MaskMap, NormalYFlip, Whiten }

        private Tab _activeTab;
        private Vector2 _scrollPos;

        private Object[] _invertTextures = new Object[1];

        private Texture2D _maskMetallic;
        private Texture2D _maskRoughness;
        private Texture2D _maskAO;
        private Texture2D _maskDetailMask;
        private bool _invertSmoothness = true;
        private string _maskExportName = "MaskMap";

        private Object[] _normalTextures = new Object[1];
        private Object[] _whitenTextures = new Object[1];

        private static readonly string[] TabLabels = { "Invert", "Mask Map", "Normal Y-Flip", "Whiten" };

        [MenuItem("Tools/Unity Quick Texture Tools")]
        public static void Open()
        {
            var win = GetWindow<UnityQuickTextureToolsWindow>("Unity Quick Texture Tools");
            win.minSize = new Vector2(380, 320);
        }

        public static void OpenWithMaskMap(Texture2D texture)
        {
            var win = GetWindow<UnityQuickTextureToolsWindow>("Unity Quick Texture Tools");
            win.minSize = new Vector2(380, 320);
            win._activeTab = Tab.MaskMap;
            win._maskMetallic = texture;
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(texture));
            win._maskExportName = $"{name}_MaskMap";
        }

        public static void OpenWithMaskMapAuto(Texture2D texture)
        {
            var win = GetWindow<UnityQuickTextureToolsWindow>("Unity Quick Texture Tools");
            win.minSize = new Vector2(380, 320);
            win._activeTab = Tab.MaskMap;
            AutoDetectMaskTextures(texture, out win._maskMetallic, out win._maskRoughness, out win._maskAO, out win._maskDetailMask);
            Texture2D reference = win._maskMetallic ?? win._maskRoughness ?? win._maskAO ?? win._maskDetailMask ?? texture;
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(reference));
            // Strip common channel suffixes to build a clean base name
            win._maskExportName = $"{StripChannelSuffix(name)}_MaskMap";
        }

        public static void OpenWithMaskMapAsMetallic(Texture2D texture)
        {
            var win = GetWindow<UnityQuickTextureToolsWindow>("Unity Quick Texture Tools");
            win.minSize = new Vector2(380, 320);
            win._activeTab = Tab.MaskMap;
            win._maskMetallic = texture;
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(texture));
            win._maskExportName = $"{StripChannelSuffix(name)}_MaskMap";
        }

        public static void OpenWithMaskMapAsRoughness(Texture2D texture)
        {
            var win = GetWindow<UnityQuickTextureToolsWindow>("Unity Quick Texture Tools");
            win.minSize = new Vector2(380, 320);
            win._activeTab = Tab.MaskMap;
            win._maskRoughness = texture;
            win._invertSmoothness = true; // roughness → invert to smoothness by default
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(texture));
            win._maskExportName = $"{StripChannelSuffix(name)}_MaskMap";
        }

        public static void OpenWithMaskMapAsAO(Texture2D texture)
        {
            var win = GetWindow<UnityQuickTextureToolsWindow>("Unity Quick Texture Tools");
            win.minSize = new Vector2(380, 320);
            win._activeTab = Tab.MaskMap;
            win._maskAO = texture;
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(texture));
            win._maskExportName = $"{StripChannelSuffix(name)}_MaskMap";
        }

        public static void OpenWithMaskMapAsDetailMask(Texture2D texture)
        {
            var win = GetWindow<UnityQuickTextureToolsWindow>("Unity Quick Texture Tools");
            win.minSize = new Vector2(380, 320);
            win._activeTab = Tab.MaskMap;
            win._maskDetailMask = texture;
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(texture));
            win._maskExportName = $"{StripChannelSuffix(name)}_MaskMap";
        }

        // ─── Auto-detection ──────────────────────────────────────────────────────

        private static readonly string[] MetallicKeywords  = { "metallic", "metalness", "metal", "_met", "_m" };
        private static readonly string[] RoughnessKeywords = { "roughness", "rough", "smoothness", "smooth", "_r", "_s" };
        private static readonly string[] AOKeywords        = { "ambientocclusion", "ambient_occlusion", "occlusion", "_ao", "ao" };
        private static readonly string[] DetailKeywords    = { "detailmask", "detail_mask", "_dm", "_detail", "detail" };

        /// <summary>
        /// Scans the folder containing <paramref name="sourceTexture"/> and tries to fill each mask slot
        /// by matching common naming conventions in the texture file names.
        /// </summary>
        private static void AutoDetectMaskTextures(
            Texture2D sourceTexture,
            out Texture2D metallic,
            out Texture2D roughness,
            out Texture2D ao,
            out Texture2D detailMask)
        {
            metallic    = null;
            roughness   = null;
            ao          = null;
            detailMask  = null;

            string sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
            string folder = Path.GetDirectoryName(sourcePath);

            // Gather all texture assets in the same folder
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            var candidates = guids
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == folder.Replace('\\', '/'))
                .Select(p => (path: p, nameLower: Path.GetFileNameWithoutExtension(p).ToLowerInvariant()))
                .ToList();

            // Score a filename against a keyword list (higher = better match)
            int Score(string nameLower, string[] keywords)
            {
                int best = 0;
                foreach (var kw in keywords)
                {
                    if (nameLower.EndsWith(kw))          best = Mathf.Max(best, 3);
                    else if (nameLower.Contains(kw))     best = Mathf.Max(best, 2);
                    else if (nameLower.EndsWith(kw.TrimStart('_'))) best = Mathf.Max(best, 1);
                }
                return best;
            }

            var used = new System.Collections.Generic.HashSet<string>();

            Texture2D BestMatch(string[] keywords)
            {
                var ranked = candidates
                    .Where(c => !used.Contains(c.path))
                    .Select(c => (c.path, score: Score(c.nameLower, keywords)))
                    .Where(c => c.score > 0)
                    .OrderByDescending(c => c.score)
                    .FirstOrDefault();

                if (ranked.path == null) return null;
                used.Add(ranked.path);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(ranked.path);
            }

            // Fill slots in priority order (most-specific keywords first)
            metallic   = BestMatch(MetallicKeywords);
            ao         = BestMatch(AOKeywords);
            detailMask = BestMatch(DetailKeywords);
            roughness  = BestMatch(RoughnessKeywords);
        }

        /// <summary>Removes common channel suffixes so the export name is clean.</summary>
        private static string StripChannelSuffix(string baseName)
        {
            string[] suffixes = {
                "_Metallic", "_Metal", "_Met", "_M",
                "_Roughness", "_Rough", "_R",
                "_Smoothness", "_Smooth", "_S",
                "_AmbientOcclusion", "_Occlusion", "_AO",
                "_DetailMask", "_Detail", "_DM", "_D"
            };
            foreach (var s in suffixes)
            {
                if (baseName.EndsWith(s, System.StringComparison.OrdinalIgnoreCase))
                    return baseName.Substring(0, baseName.Length - s.Length);
            }
            return baseName;
        }

        private static Texture2D[] GetSelectedTextures()
        {
            return Selection.objects
                .OfType<Texture2D>()
                .Where(t => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(t)))
                .ToArray();
        }

        [MenuItem("Assets/Unity Quick Texture Tools/Invert", true)]
        [MenuItem("Assets/Unity Quick Texture Tools/Normal Y-Flip", true)]
        [MenuItem("Assets/Unity Quick Texture Tools/Whiten", true)]
        private static bool ValidateTextureSelected()
        {
            return GetSelectedTextures().Length > 0;
        }

        // Validate all Mask Map sub-menu items with a single method
        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Auto Set Textures", true)]
        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Set as Metallic", true)]
        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Set as Roughness", true)]
        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Set as Ambient Occlusion", true)]
        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Set as Detail Mask", true)]
        private static bool ValidateMaskMapMenu()
        {
            return GetSelectedTextures().Length > 0;
        }

        [MenuItem("Assets/Unity Quick Texture Tools/Invert")]
        private static void ContextInvert()
        {
            ProcessTextures(GetSelectedTextures().Cast<Object>().ToArray(), InvertPixels, "Inverting", "_Inverted");
        }

        [MenuItem("Assets/Unity Quick Texture Tools/Normal Y-Flip")]
        private static void ContextNormalYFlip()
        {
            ProcessTextures(GetSelectedTextures().Cast<Object>().ToArray(), FlipYPixels, "Flipping Y", "_YFlipped");
        }

        [MenuItem("Assets/Unity Quick Texture Tools/Whiten")]
        private static void ContextWhiten()
        {
            ProcessTextures(GetSelectedTextures().Cast<Object>().ToArray(), WhitenPixels, "Whitening", "_Whitened");
        }

        // ─── Mask Map sub-menu ───────────────────────────────────────────────────

        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Auto Set Textures")]
        private static void ContextMaskMapAuto()
        {
            OpenWithMaskMapAuto(GetSelectedTextures()[0]);
        }

        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Set as Metallic")]
        private static void ContextMaskMapAsMetallic()
        {
            OpenWithMaskMapAsMetallic(GetSelectedTextures()[0]);
        }

        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Set as Roughness")]
        private static void ContextMaskMapAsRoughness()
        {
            OpenWithMaskMapAsRoughness(GetSelectedTextures()[0]);
        }

        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Set as Ambient Occlusion")]
        private static void ContextMaskMapAsAO()
        {
            OpenWithMaskMapAsAO(GetSelectedTextures()[0]);
        }

        [MenuItem("Assets/Unity Quick Texture Tools/Mask Map/Set as Detail Mask")]
        private static void ContextMaskMapAsDetailMask()
        {
            OpenWithMaskMapAsDetailMask(GetSelectedTextures()[0]);
        }

        private void OnGUI()
        {
            DrawTabs();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            GUILayout.Space(6);

            switch (_activeTab)
            {
                case Tab.Invert: DrawInvert(); break;
                case Tab.MaskMap: DrawMaskMap(); break;
                case Tab.NormalYFlip: DrawNormalYFlip(); break;
                case Tab.Whiten: DrawWhiten(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            for (int i = 0; i < TabLabels.Length; i++)
            {
                bool selected = (int)_activeTab == i;
                var style = selected ? CreateActiveTabStyle() : EditorStyles.toolbarButton;
                if (GUILayout.Toggle(selected, TabLabels[i], style, GUILayout.MinWidth(70)))
                {
                    _activeTab = (Tab)i;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static GUIStyle CreateActiveTabStyle()
        {
            var style = new GUIStyle(EditorStyles.toolbarButton)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            return style;
        }

        private void DrawInvert()
        {
            EditorGUILayout.LabelField("Quick Invert", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Invert the RGB channels of one or more textures. Alpha is preserved.", MessageType.Info);
            GUILayout.Space(4);

            DrawTextureList(ref _invertTextures, "Textures");

            GUILayout.Space(6);
            if (GUILayout.Button("Invert Selected Textures", GUILayout.Height(28)))
            {
                ProcessTextures(_invertTextures, InvertPixels, "Inverting", "_Inverted");
            }
        }

        private static Color[] InvertPixels(Color[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i].r = 1f - pixels[i].r;
                pixels[i].g = 1f - pixels[i].g;
                pixels[i].b = 1f - pixels[i].b;
            }
            return pixels;
        }

        private void DrawMaskMap()
        {
            EditorGUILayout.LabelField("PBR to Mask Map Converter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Combine PBR textures into a Unity Mask Map.\n" +
                "R = Metallic  |  G = AO  |  B = Detail Mask  |  A = Smoothness",
                MessageType.Info);
            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _maskMetallic = (Texture2D)EditorGUILayout.ObjectField("Metallic (R)", _maskMetallic, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck() && _maskMetallic != null)
            {
                string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(_maskMetallic));
                _maskExportName = $"{name}_MaskMap";
            }

            _maskAO = (Texture2D)EditorGUILayout.ObjectField("AO (G)", _maskAO, typeof(Texture2D), false);
            _maskDetailMask = (Texture2D)EditorGUILayout.ObjectField("Detail Mask (B)", _maskDetailMask, typeof(Texture2D), false);
            _maskRoughness = (Texture2D)EditorGUILayout.ObjectField("Roughness / Smoothness (A)", _maskRoughness, typeof(Texture2D), false);
            _invertSmoothness = EditorGUILayout.Toggle("Invert to Smoothness", _invertSmoothness);

            GUILayout.Space(4);
            _maskExportName = EditorGUILayout.TextField("Export Name", _maskExportName);

            GUILayout.Space(6);
            if (GUILayout.Button("Generate Mask Map", GUILayout.Height(28)))
            {
                GenerateMaskMap();
            }
        }

        private void GenerateMaskMap()
        {
            if (_maskMetallic == null && _maskRoughness == null && _maskAO == null && _maskDetailMask == null)
            {
                EditorUtility.DisplayDialog("Mask Map", "Assign at least one input texture.", "OK");
                return;
            }

            Texture2D reference = _maskMetallic ?? _maskRoughness ?? _maskAO ?? _maskDetailMask;
            int width = reference.width;
            int height = reference.height;

            Color[] metalPx = ReadOrDefault(_maskMetallic, width, height, Color.black);
            Color[] aoPx = ReadOrDefault(_maskAO, width, height, Color.white);
            Color[] detailPx = ReadOrDefault(_maskDetailMask, width, height, Color.white);
            Color[] roughPx = ReadOrDefault(_maskRoughness, width, height, _invertSmoothness ? Color.white : Color.black);

            var result = new Color[width * height];
            for (int i = 0; i < result.Length; i++)
            {
                float smoothness = roughPx[i].r;
                if (_invertSmoothness)
                {
                    smoothness = 1f - smoothness;
                }

                result[i] = new Color(metalPx[i].r, aoPx[i].r, detailPx[i].r, smoothness);
            }

            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            output.SetPixels(result);
            output.Apply();

            Texture2D sourceReference = _maskMetallic ?? _maskRoughness ?? _maskAO ?? _maskDetailMask;
            string sourcePath = AssetDatabase.GetAssetPath(sourceReference);
            string directory = Path.GetDirectoryName(sourcePath);
            string outputPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{_maskExportName}.png");

            File.WriteAllBytes(outputPath, output.EncodeToPNG());
            DestroyImmediate(output);
            AssetDatabase.Refresh();

            var importer = AssetImporter.GetAtPath(outputPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }

            EditorUtility.DisplayDialog("Mask Map", $"Saved to:\n{outputPath}", "OK");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }

        private void DrawNormalYFlip()
        {
            EditorGUILayout.LabelField("Normal Map Y-Flip", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Flips the green (Y) channel of normal maps.\n" +
                "Use this to convert between DirectX and OpenGL normal map formats.",
                MessageType.Info);
            GUILayout.Space(4);

            DrawTextureList(ref _normalTextures, "Normal Maps");

            GUILayout.Space(6);
            if (GUILayout.Button("Flip Y Channel", GUILayout.Height(28)))
            {
                ProcessTextures(_normalTextures, FlipYPixels, "Flipping Y", "_YFlipped");
            }
        }

        private static Color[] FlipYPixels(Color[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i].g = 1f - pixels[i].g;
            }
            return pixels;
        }

        private void DrawWhiten()
        {
            EditorGUILayout.LabelField("Whiten", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Turns all visible (non-transparent) pixels white while preserving alpha.\n" +
                "Useful for icons you want to tint via material color.",
                MessageType.Info);
            GUILayout.Space(4);

            DrawTextureList(ref _whitenTextures, "Textures");

            GUILayout.Space(6);
            if (GUILayout.Button("Whiten Textures", GUILayout.Height(28)))
            {
                ProcessTextures(_whitenTextures, WhitenPixels, "Whitening", "_Whitened");
            }
        }

        private static Color[] WhitenPixels(Color[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i].r = 1f;
                pixels[i].g = 1f;
                pixels[i].b = 1f;
            }
            return pixels;
        }

        private static void DrawTextureList(ref Object[] list, string label)
        {
            int newCount = Mathf.Max(1, EditorGUILayout.IntField($"{label} Count", list.Length));
            if (newCount != list.Length)
            {
                var resized = new Object[newCount];
                for (int i = 0; i < Mathf.Min(list.Length, newCount); i++)
                {
                    resized[i] = list[i];
                }
                list = resized;
            }

            EditorGUI.indentLevel++;
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = EditorGUILayout.ObjectField($"  [{i}]", list[i], typeof(Texture2D), false);
            }
            EditorGUI.indentLevel--;
        }

        private delegate Color[] PixelProcessor(Color[] pixels);

        private static void ProcessTextures(Object[] textures, PixelProcessor processor, string verb, string suffix)
        {
            int processed = 0;
            foreach (var obj in textures)
            {
                if (obj is not Texture2D texture)
                {
                    continue;
                }

                string sourcePath = AssetDatabase.GetAssetPath(texture);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                bool wasReadable = importer.isReadable;
                if (!wasReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }

                var readable = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                Color[] pixels = readable.GetPixels();
                pixels = processor(pixels);

                var output = new Texture2D(readable.width, readable.height, TextureFormat.RGBA32, false);
                output.SetPixels(pixels);
                output.Apply();

                if (!wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }

                string directory = Path.GetDirectoryName(sourcePath);
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                string outputPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{baseName}{suffix}.png");

                File.WriteAllBytes(outputPath, output.EncodeToPNG());
                DestroyImmediate(output);

                processed++;
            }

            AssetDatabase.Refresh();

            if (processed > 0)
            {
                Debug.Log($"[Unity Quick Texture Tools] {verb}: {processed} duplicate(s) created.");
            }
            else
            {
                EditorUtility.DisplayDialog("Unity Quick Texture Tools", "No valid textures to process.", "OK");
            }
        }

        private static Color[] ReadOrDefault(Texture2D texture, int width, int height, Color defaultColor)
        {
            if (texture == null)
            {
                var fill = new Color[width * height];
                for (int i = 0; i < fill.Length; i++)
                {
                    fill[i] = defaultColor;
                }
                return fill;
            }

            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            bool wasReadable = importer != null && importer.isReadable;

            if (importer != null && !wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            Texture2D source = texture;
            if (texture.width != width || texture.height != height)
            {
                var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(texture, rt);
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                source = new Texture2D(width, height, TextureFormat.RGBA32, false);
                source.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                source.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            var pixels = source.GetPixels();

            if (source != texture)
            {
                DestroyImmediate(source);
            }

            if (importer != null && !wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            return pixels;
        }
    }
}
