using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Gigaduck.PlayFromView
{
    /// <summary>
    /// Enters Play Mode with the player at the Scene view camera, like Unreal's "Play From Camera".
    /// The Scene view pose is captured when the shortcut is pressed, kept in SessionState across the
    /// domain reload, and applied to the player as soon as the scene has loaded (before the first
    /// frame), so the Game view opens on the same picture the Scene view showed. Nothing is saved
    /// to the scene: only the running player moves.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromView
    {
        private const string LogTag = "<b>[Play From View]</b>";
        private const string PendingKey = "Gigaduck.PlayFromView.Pending";
        private const double PendingLifetimeSeconds = 60.0;
        private const string FastActiveKey = "Gigaduck.PlayFromView.FastActive";
        private const string OriginalEnabledKey = "Gigaduck.PlayFromView.OriginalOptionsEnabled";
        private const string OriginalOptionsKey = "Gigaduck.PlayFromView.OriginalOptions";

        private static readonly List<IPlayFromViewPlacer> s_Placers = new List<IPlayFromViewPlacer>();

        private static bool s_Active;
        private static ViewPose s_Request;
        private static double s_StartedAt;

        static PlayFromView()
        {
            RegisterPlacer(new GenericPlayerPlacer());
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting -= RestoreEnterPlayModeOptions;
            EditorApplication.quitting += RestoreEnterPlayModeOptions;

            // A reload while not playing means a previous fast Play never got to restore.
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RestoreEnterPlayModeOptions();
            }
        }

        // --- Fast enter -------------------------------------------------------------------

        // Same idea as a "fast play" button: skip the domain and scene reload for this one Play
        // and put the project's own Enter Play Mode settings back afterwards.
        private static void EnterFastOptions()
        {
            if (SessionState.GetBool(FastActiveKey, false))
            {
                return;
            }

            SessionState.SetBool(OriginalEnabledKey, EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(OriginalOptionsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(FastActiveKey, true);

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions |= EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        }

        private static void RestoreEnterPlayModeOptions()
        {
            if (!SessionState.GetBool(FastActiveKey, false))
            {
                return;
            }

            SessionState.SetBool(FastActiveKey, false);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(OriginalEnabledKey, false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OriginalOptionsKey, 0);
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RestoreEnterPlayModeOptions();
            }
        }

        // --- Public API -------------------------------------------------------------------

        /// <summary>
        /// Registers a placer for a kind of player rig. A placer with the same
        /// <see cref="IPlayFromViewPlacer.Name"/> is replaced. Call it from an
        /// <c>[InitializeOnLoad]</c> constructor.
        /// </summary>
        public static void RegisterPlacer(IPlayFromViewPlacer placer)
        {
            if (placer == null)
            {
                throw new ArgumentNullException(nameof(placer));
            }

            UnregisterPlacer(placer.Name);
            s_Placers.Add(placer);
        }

        public static bool UnregisterPlacer(string name)
        {
            return s_Placers.RemoveAll(p => p.Name == name) > 0;
        }

        /// <summary>Plays from the Scene view camera, or stops if the game is already running.</summary>
        public static void PlayFromSceneView()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (EditorUtility.scriptCompilationFailed)
            {
                Debug.LogWarning($"{LogTag} Fix the script compile errors first; Play Mode cannot start.");
                return;
            }

            if (TryCaptureSceneView(out ViewPose pose, out string problem))
            {
                SessionState.SetString(PendingKey, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + pose.Serialize());
            }
            else
            {
                SessionState.EraseString(PendingKey);
                Debug.LogWarning($"{LogTag} {problem} Entering Play Mode normally.");
            }

            if (PlayFromViewSettings.FastEnter)
            {
                EnterFastOptions();
            }

            EditorApplication.isPlaying = true;
        }

        /// <summary>The Scene view camera's pose, or why there is none to use.</summary>
        public static bool TryCaptureSceneView(out ViewPose pose, out string problem)
        {
            pose = default;
            problem = null;

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null && SceneView.sceneViews.Count > 0)
            {
                view = SceneView.sceneViews[0] as SceneView;
            }

            if (view == null || view.camera == null)
            {
                problem = "No Scene view is open.";
                return false;
            }

            if (view.in2DMode)
            {
                problem = "The Scene view is in 2D mode.";
                return false;
            }

            if (view.orthographic)
            {
                problem = "The Scene view is orthographic (isometric), so its camera has no useful eye position.";
                return false;
            }

            if (StageUtility.GetCurrentStageHandle() != StageUtility.GetMainStageHandle())
            {
                problem = "A prefab stage is open, so the Scene view is not looking at the game scene.";
                return false;
            }

            Transform camera = view.camera.transform;
            pose = new ViewPose(camera.position, camera.rotation);
            return true;
        }

        /// <summary>
        /// Moves the player to <paramref name="pose"/> right now using the registered placers, highest
        /// priority first. Returns false when no placer found a player.
        /// </summary>
        public static bool TryPlace(in ViewPose pose, out PlacementReport report, out string placerName)
        {
            report = default;
            placerName = null;

            List<IPlayFromViewPlacer> ordered = new List<IPlayFromViewPlacer>(s_Placers);
            ordered.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            foreach (IPlayFromViewPlacer placer in ordered)
            {
                try
                {
                    if (placer.TryPlace(pose, out report))
                    {
                        placerName = placer.Name;
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError($"{LogTag} Placer \"{placer.Name}\" failed: {exception.Message}");
                    Debug.LogException(exception);
                }
            }

            return false;
        }

        // --- Entry points -----------------------------------------------------------------

        [Shortcut("Gigaduck/Play From Scene View", KeyCode.P, ShortcutModifiers.Alt)]
        private static void PlayShortcut()
        {
            PlayFromSceneView();
        }

        [MenuItem("Tools/Gigaduck/Play From Scene View")]
        private static void PlayMenu()
        {
            PlayFromSceneView();
        }

        // --- Applying the pose once play mode is running ---------------------------------

        // Runs after the scene's Awake/OnEnable and before any Start, so the first rendered frame
        // is already at the right place. Not reliable across every Unity setup, hence the
        // playModeStateChanged fallback below; Begin is safe to call twice.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnAfterSceneLoad()
        {
            Begin();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    Begin();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    End();
                    RestoreEnterPlayModeOptions();
                    break;
            }
        }

        private static void Begin()
        {
            if (s_Active || !Application.isPlaying)
            {
                return;
            }

            string raw = SessionState.GetString(PendingKey, string.Empty);
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            // Consume at once: a later plain Play must never reuse an old Scene view pose.
            SessionState.EraseString(PendingKey);

            if (!TryDecode(raw, out ViewPose pose))
            {
                return;
            }

            s_Request = pose;
            s_Active = true;
            s_StartedAt = EditorApplication.timeSinceStartup;

            if (TryPlaceAndReport())
            {
                End();
                return;
            }

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!s_Active || !Application.isPlaying)
            {
                End();
                return;
            }

            if (TryPlaceAndReport())
            {
                End();
                return;
            }

            if (EditorApplication.timeSinceStartup - s_StartedAt > PlayFromViewSettings.WaitSeconds)
            {
                Debug.LogWarning($"{LogTag} No player was found within {PlayFromViewSettings.WaitSeconds:0.#} s, so nothing was moved.");
                End();
            }
        }

        private static bool TryPlaceAndReport()
        {
            if (!TryPlace(s_Request, out PlacementReport report, out string placerName))
            {
                return false;
            }

            if (PlayFromViewSettings.LogPlacement)
            {
                Debug.Log($"{LogTag} Placed \"{report.Target}\" at the Scene view camera via {placerName} " +
                          $"(off by {report.PositionError:0.000} m, {report.AngleError:0.0}°).");
            }

            return true;
        }

        private static void End()
        {
            s_Active = false;
            EditorApplication.update -= Tick;
        }

        private static bool TryDecode(string raw, out ViewPose pose)
        {
            pose = default;
            int separator = raw.IndexOf('|');
            if (separator < 0 ||
                !long.TryParse(raw.Substring(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
            {
                return false;
            }

            double age = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            if (age > PendingLifetimeSeconds)
            {
                return false;
            }

            return ViewPose.TryParse(raw.Substring(separator + 1), out pose);
        }
    }
}
