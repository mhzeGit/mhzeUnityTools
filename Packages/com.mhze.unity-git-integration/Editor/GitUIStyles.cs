using UnityEditor;
using UnityEngine;

namespace GitIntegration
{
    public static class GitUIStyles
    {
        private const string PackagePath  = "Packages/com.mhze.unity-git-integration";
        private const string IconsPath    = PackagePath + "/ToolAssets/Icons/AssetChangesIcon/";


        public static readonly Color AccentBlue     = EditorGUIUtility.isProSkin ? new Color(0.35f, 0.60f, 1.0f) : new Color(0.15f, 0.40f, 0.85f);
        public static readonly Color AccentGreen    = EditorGUIUtility.isProSkin ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.15f, 0.65f, 0.25f);
        public static readonly Color AccentRed      = EditorGUIUtility.isProSkin ? new Color(0.95f, 0.35f, 0.35f) : new Color(0.80f, 0.20f, 0.20f);
        public static readonly Color AccentYellow   = EditorGUIUtility.isProSkin ? new Color(0.95f, 0.85f, 0.30f) : new Color(0.75f, 0.65f, 0.10f);
        public static readonly Color MutedText      = EditorGUIUtility.isProSkin ? new Color(0.60f, 0.60f, 0.60f) : new Color(0.45f, 0.45f, 0.45f);
        public static readonly Color CardBg         = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.88f, 0.88f, 0.88f);
        public static readonly Color DiffAddBg      = EditorGUIUtility.isProSkin ? new Color(0.15f, 0.30f, 0.15f, 0.5f) : new Color(0.75f, 0.95f, 0.75f, 0.5f);
        public static readonly Color DiffRemoveBg   = EditorGUIUtility.isProSkin ? new Color(0.35f, 0.12f, 0.12f, 0.5f) : new Color(0.95f, 0.75f, 0.75f, 0.5f);
        public static readonly Color DiffHunkBg     = EditorGUIUtility.isProSkin ? new Color(0.20f, 0.25f, 0.35f, 0.5f) : new Color(0.80f, 0.85f, 0.95f, 0.5f);
        public static readonly Color SeparatorColor = EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.70f, 0.70f, 0.70f);
        public static readonly Color HeaderBg       = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.82f, 0.82f, 0.82f);
        public static readonly Color SelectedRowBg  = EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.24f, 0.48f, 0.90f, 0.3f);
        public static readonly Color HoverRowBg     = EditorGUIUtility.isProSkin ? new Color(0.26f, 0.26f, 0.28f) : new Color(0.84f, 0.84f, 0.88f);
        public static readonly Color SubtleBorder   = EditorGUIUtility.isProSkin ? new Color(0.20f, 0.20f, 0.20f) : new Color(0.72f, 0.72f, 0.72f);


        private static GUIStyle _header;
        public static GUIStyle Header
        {
            get
            {
                if (_header == null)
                {
                    _header = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 16,
                        padding = new RectOffset(8, 8, 6, 6),
                    };
                }
                return _header;
            }
        }

        private static GUIStyle _subHeader;
        public static GUIStyle SubHeader
        {
            get
            {
                if (_subHeader == null)
                {
                    _subHeader = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 13,
                        padding = new RectOffset(4, 4, 4, 4),
                    };
                }
                return _subHeader;
            }
        }

        private static GUIStyle _sectionLabel;
        public static GUIStyle SectionLabel
        {
            get
            {
                if (_sectionLabel == null)
                {
                    _sectionLabel = new GUIStyle(EditorStyles.label)
                    {
                        fontSize = 11,
                        fontStyle = FontStyle.Bold,
                        padding = new RectOffset(4, 4, 6, 2),
                    };
                    _sectionLabel.normal.textColor = AccentBlue;
                }
                return _sectionLabel;
            }
        }

        private static GUIStyle _mutedLabel;
        public static GUIStyle MutedLabel
        {
            get
            {
                if (_mutedLabel == null)
                {
                    _mutedLabel = new GUIStyle(EditorStyles.miniLabel)
                    {
                        padding = new RectOffset(4, 4, 0, 0),
                    };
                    _mutedLabel.normal.textColor = MutedText;
                }
                return _mutedLabel;
            }
        }

        private static GUIStyle _commitMessage;
        public static GUIStyle CommitMessage
        {
            get
            {
                if (_commitMessage == null)
                {
                    _commitMessage = new GUIStyle(EditorStyles.label)
                    {
                        fontSize = 12,
                        wordWrap = true,
                        richText = true,
                        padding = new RectOffset(4, 4, 2, 2),
                    };
                }
                return _commitMessage;
            }
        }

        private static GUIStyle _card;
        public static GUIStyle Card
        {
            get
            {
                if (_card == null)
                {
                    _card = new GUIStyle("HelpBox")
                    {
                        padding = new RectOffset(10, 10, 8, 8),
                        margin = new RectOffset(4, 4, 2, 2),
                    };
                }
                return _card;
            }
        }

        private static GUIStyle _monoLabel;
        public static GUIStyle MonoLabel
        {
            get
            {
                if (_monoLabel == null)
                {
                    _monoLabel = new GUIStyle(EditorStyles.label)
                    {
                        font = Font.CreateDynamicFontFromOSFont("Consolas", 11),
                        fontSize = 11,
                        richText = true,
                        wordWrap = false,
                        padding = new RectOffset(6, 6, 1, 1),
                    };
                }
                return _monoLabel;
            }
        }

        private static GUIStyle _toolbarButton;
        public static GUIStyle ToolbarButton
        {
            get
            {
                if (_toolbarButton == null)
                {
                    _toolbarButton = new GUIStyle(EditorStyles.toolbarButton)
                    {
                        fixedHeight = 28,
                        fontSize = 12,
                        fontStyle = FontStyle.Bold,
                        padding = new RectOffset(12, 12, 4, 4),
                    };
                }
                return _toolbarButton;
            }
        }

        private static GUIStyle _tag;
        public static GUIStyle Tag
        {
            get
            {
                if (_tag == null)
                {
                    _tag = new GUIStyle("CN StatusInfo")
                    {
                        fontSize = 10,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(6, 6, 2, 2),
                        margin = new RectOffset(2, 2, 2, 2),
                        fixedHeight = 20,
                    };
                }
                return _tag;
            }
        }

        private static GUIStyle _centeredGreyMini;
        public static GUIStyle CenteredGreyMini
        {
            get
            {
                if (_centeredGreyMini == null)
                {
                    _centeredGreyMini = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    {
                        fontSize = 12,
                        wordWrap = true,
                    };
                }
                return _centeredGreyMini;
            }
        }


        public static void DrawSeparator(float thickness = 1f, float topSpacing = 4f, float bottomSpacing = 4f)
        {
            GUILayout.Space(topSpacing);
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(thickness));
            EditorGUI.DrawRect(rect, SeparatorColor);
            GUILayout.Space(bottomSpacing);
        }

        public static void DrawColoredRect(Rect rect, Color color)
        {
            EditorGUI.DrawRect(rect, color);
        }

        public static bool DrawIconButton(string tooltip, string iconName, float size = 24f)
        {
            var content = EditorGUIUtility.IconContent(iconName);
            content.tooltip = tooltip;
            return GUILayout.Button(content, GUIStyle.none, GUILayout.Width(size), GUILayout.Height(size));
        }

        public static void DrawStatusBadge(string status)
        {
            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = GitOperations.StatusToColor(status);
            GUILayout.Label(GitOperations.StatusToLabel(status), Tag, GUILayout.Width(72));
            GUI.backgroundColor = prevColor;
        }

        public static void DrawCardHeader(string icon, string title, string subtitle = null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(icon, Header, GUILayout.Width(28));
            EditorGUILayout.BeginVertical();
            GUILayout.Label(title, SubHeader);
            if (!string.IsNullOrEmpty(subtitle))
                GUILayout.Label(subtitle, MutedLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        public static void BeginCard()
        {
            EditorGUILayout.BeginVertical(Card);
        }

        public static void EndCard()
        {
            EditorGUILayout.EndVertical();
        }


        private static Texture2D _pillTex;
        private static Texture2D _circleTex;
        private static Texture2D _roundedRectTex;

        public static Texture2D RoundedRectTexture
        {
            get
            {
                if (_roundedRectTex == null)
                {
                    int w = 16, h = 8;
                    float r = 3f;  // Corner radius
                    _roundedRectTex = new Texture2D(w, h, TextureFormat.ARGB32, false);
                    _roundedRectTex.hideFlags = HideFlags.HideAndDontSave;
                    _roundedRectTex.filterMode = FilterMode.Bilinear;
                    
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            float px = x + 0.5f;
                            float py = y + 0.5f;
                            
                            float dx = Mathf.Max(0, Mathf.Max(r - px, px - (w - r)));
                            float dy = Mathf.Max(0, Mathf.Max(r - py, py - (h - r)));
                            float dist = Mathf.Sqrt(dx * dx + dy * dy);
                            
                            float a = Mathf.Clamp01(r - dist + 0.5f);
                            _roundedRectTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                        }
                    }
                    _roundedRectTex.Apply();
                }
                return _roundedRectTex;
            }
        }

        public static Texture2D PillTexture
        {
            get
            {
                if (_pillTex == null)
                {
                    int w = 64, h = 32;
                    float r = h * 0.5f; // radius = 16
                    float lx = r, rx = w - r; // left/right cap centres x
                    float cy = r;             // cap centre y

                    _pillTex = new Texture2D(w, h, TextureFormat.ARGB32, false);
                    _pillTex.hideFlags = HideFlags.HideAndDontSave;
                    _pillTex.filterMode = FilterMode.Bilinear;
                    _pillTex.wrapMode  = TextureWrapMode.Clamp;

                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            float px = x + 0.5f, py = y + 0.5f;
                            float sdf;
                            if (px < lx)
                            {
                                float dx = px - lx, dy = py - cy;
                                sdf = r - Mathf.Sqrt(dx * dx + dy * dy);
                            }
                            else if (px > rx)
                            {
                                float dx = px - rx, dy = py - cy;
                                sdf = r - Mathf.Sqrt(dx * dx + dy * dy);
                            }
                            else
                            {
                                sdf = Mathf.Min(py, h - py); // distance from top/bottom edge
                            }
                            float a = Mathf.Clamp01(sdf + 0.5f);
                            _pillTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                        }
                    }
                    _pillTex.Apply();
                }
                return _pillTex;
            }
        }

        public static Texture2D CircleTexture
        {
            get
            {
                if (_circleTex == null)
                {
                    int sz = 32;  // Higher resolution for smoothness
                    float r = sz * 0.5f - 1f;  // Radius with 1px border
                    _circleTex = new Texture2D(sz, sz, TextureFormat.ARGB32, false);
                    _circleTex.hideFlags = HideFlags.HideAndDontSave;
                    _circleTex.filterMode = FilterMode.Bilinear;
                    float center = sz * 0.5f - 0.5f;
                    for (int y = 0; y < sz; y++)
                        for (int x = 0; x < sz; x++)
                        {
                            float dx = x - center;
                            float dy = y - center;
                            float dist = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.Clamp01(r - dist + 0.7f);  // Smooth antialiasing
                            _circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                        }
                    _circleTex.Apply();
                }
                return _circleTex;
            }
        }

        private static Texture2D _addedIcon;
        private static Texture2D _modifyIcon;
        private static Texture2D _removedIcon;

        private static Texture2D GetStatusIcon(string status)
        {
            switch (status.ToUpper())
            {
                case "A":
                case "??":
                    if (_addedIcon == null)
                        _addedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconsPath + "ChangeIcon_Added.png");
                    return _addedIcon;
                
                case "M":
                case "MM":
                case "AM":
                    if (_modifyIcon == null)
                        _modifyIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconsPath + "ChangeIcon_Modify.png");
                    return _modifyIcon;
                
                case "D":
                    if (_removedIcon == null)
                        _removedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconsPath + "ChangeIcon_Removed.png");
                    return _removedIcon;
                
                default:
                    if (_addedIcon == null)
                        _addedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconsPath + "ChangeIcon_Added.png");
                    return _addedIcon;
            }
        }

        public static void DrawStatusCircle(Rect rect, string status)
        {
            Texture2D icon = GetStatusIcon(status);
            if (icon != null)
            {
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
            }
            else
            {
                Color bg = GitOperations.StatusToColor(status);
                var prev = GUI.color;
                GUI.color = bg;
                GUI.DrawTexture(rect, CircleTexture, ScaleMode.StretchToFill);
                GUI.color = prev;
            }
        }

        public static void DrawRoundedBadge(Rect rect, string label, Color color)
        {
            DrawRoundedRect(rect, new Color(color.r, color.g, color.b, 0.22f), 4f);

            if (_badgeStyle == null)
            {
                _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize  = 10,
                    padding   = new RectOffset(2, 2, 0, 0),
                };
            }
            _badgeStyle.normal.textColor = color;
            GUI.Label(rect, label, _badgeStyle);
        }

        private static GUIStyle _badgeStyle;

        public static void DrawRefPill(Rect rect, string label, Color color)
        {
            DrawRoundedRect(rect, new Color(color.r, color.g, color.b, 0.20f), 4f);

            if (_pillStyle == null)
            {
                _pillStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize  = 10,
                    padding   = new RectOffset(4, 4, 0, 0),
                };
            }
            _pillStyle.normal.textColor = color;
            GUI.Label(rect, label, _pillStyle);
        }

        private static GUIStyle _pillStyle;

        public static void DrawRoundedRect(Rect rect, Color color, float radius = 4f)
        {
            Texture2D tex = PillTexture;
            float capW = Mathf.Min(rect.height * 0.5f, rect.width * 0.5f);

            var prev = GUI.color;
            GUI.color = color;

            if (rect.width <= capW * 2f)
            {
                GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);
            }
            else
            {
                GUI.DrawTextureWithTexCoords(
                    new Rect(rect.x, rect.y, capW, rect.height),
                    tex, new Rect(0f, 0f, 0.25f, 1f));
                GUI.DrawTextureWithTexCoords(
                    new Rect(rect.x + capW, rect.y, rect.width - capW * 2f, rect.height),
                    tex, new Rect(0.25f, 0f, 0.5f, 1f));
                GUI.DrawTextureWithTexCoords(
                    new Rect(rect.xMax - capW, rect.y, capW, rect.height),
                    tex, new Rect(0.75f, 0f, 0.25f, 1f));
            }

            GUI.color = prev;
        }

        public static void DrawAuthorCircle(Rect rect, string author, string email)
        {
            int hash = (email ?? author ?? "").GetHashCode();
            float hue = (Mathf.Abs(hash) % 360) / 360f;
            Color bg = Color.HSVToRGB(hue, 0.50f, 0.60f);


            var prev = GUI.color;
            GUI.color = bg;
            GUI.DrawTexture(rect, CircleTexture, ScaleMode.ScaleToFit);
            GUI.color = prev;

            string initial = string.IsNullOrEmpty(author) ? "?" : author[0].ToString().ToUpper();
            if (_authorInitStyle == null)
            {
                _authorInitStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize  = 10,
                };
                _authorInitStyle.normal.textColor = Color.white;
            }
            GUI.Label(rect, initial, _authorInitStyle);
        }

        private static GUIStyle _authorInitStyle;


        private static GUIStyle _colHeader;
        public static GUIStyle ColumnHeader
        {
            get
            {
                if (_colHeader == null)
                {
                    _colHeader = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontStyle = FontStyle.Bold,
                    };
                    _colHeader.normal.textColor = MutedText;
                }
                return _colHeader;
            }
        }
    }
}
