using UnityEditor;
using UnityEngine;

namespace Gigaduck.PlayFromView
{
    /// <summary>Per-user options, kept in EditorPrefs, plus the Preferences page that edits them.</summary>
    public static class PlayFromViewSettings
    {
        private const string Prefix = "Gigaduck.PlayFromView.";
        private const string FastKey = Prefix + "FastEnter";
        private const string LogKey = Prefix + "LogPlacement";
        private const string WaitKey = Prefix + "WaitSeconds";
        private const string TagKey = Prefix + "PlayerTag";
        private const string CameraKey = Prefix + "FallbackToMainCamera";

        /// <summary>
        /// Enter Play Mode with Domain Reload and Scene Reload switched off for this one Play (like a
        /// "fast play" button), then put the project's Enter Play Mode settings back.
        /// </summary>
        public static bool FastEnter
        {
            get => EditorPrefs.GetBool(FastKey, true);
            set => EditorPrefs.SetBool(FastKey, value);
        }

        /// <summary>Print one console line saying where the player was placed.</summary>
        public static bool LogPlacement
        {
            get => EditorPrefs.GetBool(LogKey, true);
            set => EditorPrefs.SetBool(LogKey, value);
        }

        /// <summary>How long to keep looking for a player that is spawned after the scene loads.</summary>
        public static float WaitSeconds
        {
            get => EditorPrefs.GetFloat(WaitKey, 5f);
            set => EditorPrefs.SetFloat(WaitKey, Mathf.Clamp(value, 0f, 60f));
        }

        /// <summary>Tag the generic placer looks for.</summary>
        public static string PlayerTag
        {
            get => EditorPrefs.GetString(TagKey, "Player");
            set => EditorPrefs.SetString(TagKey, value ?? string.Empty);
        }

        /// <summary>With no player in the scene, move the main camera instead.</summary>
        public static bool FallbackToMainCamera
        {
            get => EditorPrefs.GetBool(CameraKey, false);
            set => EditorPrefs.SetBool(CameraKey, value);
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Gigaduck/Play From Scene View", SettingsScope.User)
            {
                label = "Play From Scene View",
                keywords = new[] { "play", "scene view", "camera", "player", "start" },
                guiHandler = _ =>
                {
                    EditorGUILayout.HelpBox(
                        "Press Alt+P (Tools > Gigaduck > Play From Scene View) to play with the player at the Scene view camera.",
                        MessageType.None);

                    FastEnter = EditorGUILayout.Toggle(
                        new GUIContent("Fast enter", "Skip Domain Reload and Scene Reload for this Play, then restore the project's Enter Play Mode settings."),
                        FastEnter);
                    LogPlacement = EditorGUILayout.Toggle(
                        new GUIContent("Log placement", "Print one console line with where the player was placed."),
                        LogPlacement);
                    WaitSeconds = EditorGUILayout.Slider(
                        new GUIContent("Wait for player (s)", "How long to keep looking for a player that appears after the scene loads."),
                        WaitSeconds, 0f, 60f);
                    PlayerTag = EditorGUILayout.TagField(
                        new GUIContent("Player tag", "Tag the generic placer looks for when no rig-specific placer applies."),
                        PlayerTag);
                    FallbackToMainCamera = EditorGUILayout.Toggle(
                        new GUIContent("Move main camera if no player", "Without a player, put the main camera at the Scene view pose."),
                        FallbackToMainCamera);

                    EditorGUILayout.Space();
                    if (GUILayout.Button("Change the shortcut...", GUILayout.Width(180f)))
                    {
                        EditorApplication.ExecuteMenuItem("Edit/Shortcuts...");
                    }
                }
            };
        }
    }
}
