using Newtonsoft.Json.Linq;
using UnityEditor;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;

namespace Gigaduck.AgenticTools.Mcp
{
    /// <summary>
    /// MCP custom tool <c>fast_play</c>: enters Play Mode with Domain Reload and Scene Reload off (the Fast Play button's
    /// settings), mirrors the Fast Play toolbar state and restores the original Enter Play Mode settings on exit. Part of the
    /// Agentic Tools package; compiled only when MCP for Unity (com.coplaydev.unity-mcp) is installed.
    /// </summary>
    [McpForUnityTool(
        name: "fast_play",
        Description = "Agents: only while holding the editor Play Mode claim (TESTING.md rule 1: agentplay.py editor claim); otherwise test in your own standalone player via tools/agentplay/agentplay.py. Enter Unity Play Mode using the project's Fast Play settings (Domain Reload and Scene Reload disabled) instead of Unity's default play. Actions: play, stop, status.")]
    public static class FastPlayTool
    {
        /// <summary>Actions advertised to MCP clients.</summary>
        public sealed class Parameters
        {
            [ToolParameter("Play Mode action: play, stop or status.", Required = false, DefaultValue = "play")]
            public string action { get; set; }
        }

        private const string KeyActive = "McpFastPlay.Active";
        private const string KeyOrigEnabled = "McpFastPlay.OrigEnabled";
        private const string KeyOrigOptions = "McpFastPlay.OrigOptions";

        private const string ToolbarKeyPlaying = "FastPlayButton_IsFastPlaying";
        private const string ToolbarKeyOrigEnabled = "FastPlayButton_OrigEnabled";
        private const string ToolbarKeyOrigOptions = "FastPlayButton_OrigOptions";

        private static bool _hooked;

        [InitializeOnLoadMethod]
        private static void Init()
        {
            Hook();
            if (!EditorApplication.isPlaying && IsFastPlayActive())
                RestoreOriginalSettings();
        }

        public static object HandleCommand(JObject @params)
        {
            string action = (@params?["action"]?.ToString() ?? "play").Trim().ToLowerInvariant();

            switch (action)
            {
                case "play":
                    return Play();
                case "stop":
                    return Stop();
                case "status":
                    return Status();
                default:
                    return new ErrorResponse(
                        $"Unknown action '{action}'. Supported actions: play, stop, status."
                    );
            }
        }

        private static object Play()
        {
            Hook();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return new SuccessResponse(
                    SessionState.GetBool(KeyActive, false)
                        ? "Already in Fast Play."
                        : "Already in Play Mode (Fast Play settings were not applied).",
                    Snapshot()
                );
            }

            SaveOriginalSettings();

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions =
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;

            SessionState.SetBool(KeyActive, true);
            SessionState.SetBool(ToolbarKeyPlaying, true);

            EditorApplication.isPlaying = true;

            return new SuccessResponse(
                "Entering Fast Play (Domain and Scene reload disabled).",
                Snapshot()
            );
        }

        private static object Stop()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return new SuccessResponse("Exiting Fast Play.", Snapshot());
            }

            if (IsFastPlayActive())
                RestoreOriginalSettings();

            return new SuccessResponse("Not in Play Mode.", Snapshot());
        }

        private static object Status()
        {
            return new SuccessResponse("Fast Play status.", Snapshot());
        }

        private static void Hook()
        {
            if (_hooked)
                return;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            _hooked = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
                RestoreOriginalSettings();
        }

        private static bool IsFastPlayActive()
        {
            return SessionState.GetBool(KeyActive, false)
                || SessionState.GetBool(ToolbarKeyPlaying, false);
        }

        private static void SaveOriginalSettings()
        {
            if (SessionState.GetBool(KeyActive, false))
                return;

            bool enabled = EditorSettings.enterPlayModeOptionsEnabled;
            int options = (int)EditorSettings.enterPlayModeOptions;

            SessionState.SetBool(KeyOrigEnabled, enabled);
            SessionState.SetInt(KeyOrigOptions, options);

            // Mirror the Fast Play toolbar's session keys so its button shows Stop and restores too.
            SessionState.SetBool(ToolbarKeyOrigEnabled, enabled);
            SessionState.SetInt(ToolbarKeyOrigOptions, options);
        }

        private static void RestoreOriginalSettings()
        {
            if (!IsFastPlayActive())
                return;

            bool enabled = SessionState.GetBool(KeyOrigEnabled, false);
            EnterPlayModeOptions options =
                (EnterPlayModeOptions)SessionState.GetInt(KeyOrigOptions, 0);

            SessionState.SetBool(KeyActive, false);
            SessionState.SetBool(ToolbarKeyPlaying, false);

            EditorSettings.enterPlayModeOptionsEnabled = enabled;
            EditorSettings.enterPlayModeOptions = options;
        }

        private static object Snapshot()
        {
            return new
            {
                playing = EditorApplication.isPlaying,
                playing_or_will_change = EditorApplication.isPlayingOrWillChangePlaymode,
                fast_play_active = SessionState.GetBool(KeyActive, false),
                enter_play_mode_options_enabled = EditorSettings.enterPlayModeOptionsEnabled,
                enter_play_mode_options = EditorSettings.enterPlayModeOptions.ToString(),
            };
        }
    }
}
