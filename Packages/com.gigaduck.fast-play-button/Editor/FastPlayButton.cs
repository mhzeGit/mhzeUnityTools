#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#else
using FastPlayButtonTool.Toolbar;
#endif

namespace FastPlayButtonTool
{
	[InitializeOnLoad]
	public static class FastPlayButton
	{
		private const string KEY_FAST_PLAYING = "FastPlayButton_IsFastPlaying";
		private const string KEY_ORIG_ENABLED = "FastPlayButton_OrigEnabled";
		private const string KEY_ORIG_OPTIONS = "FastPlayButton_OrigOptions";

#if UNITY_6000_3_OR_NEWER
		private const string k_ElementName = "FastPlayButton/FastPlay";
#else
		private static GUIContent _playContent;
		private static GUIContent _stopContent;
		private static GUIStyle _buttonStyle;
#endif

		static FastPlayButton()
		{
#if !UNITY_6000_3_OR_NEWER
			ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI);
#endif
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			EditorApplication.quitting -= OnEditorQuitting;
			EditorApplication.quitting += OnEditorQuitting;

			if (!EditorApplication.isPlaying && SessionState.GetBool(KEY_FAST_PLAYING, false))
				RestoreOriginalSettings();
		}

		[DidReloadScripts]
		private static void OnScriptsReloaded()
		{
			if (!EditorApplication.isPlaying && SessionState.GetBool(KEY_FAST_PLAYING, false))
				RestoreOriginalSettings();
		}

		private static void OnEditorQuitting() => RestoreOriginalSettings();

#if UNITY_6000_3_OR_NEWER

		[MainToolbarElement(k_ElementName, defaultDockPosition = MainToolbarDockPosition.Right)]
		public static MainToolbarElement CreateFastPlayButton()
		{
			bool isFastPlaying = SessionState.GetBool(KEY_FAST_PLAYING, false);
			bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;

			MainToolbarContent content = (isFastPlaying && isPlaying)
				? new MainToolbarContent(" Stop", "Stop Fast Play\nOriginal Enter Play Mode settings will be restored.")
				: new MainToolbarContent("Fast", "Fast Play\nEnters Play Mode with Domain & Scene reload disabled.\nOriginal settings are restored automatically on exit.");

			return new MainToolbarButton(content, OnButtonClicked)
			{
				enabled = !(isPlaying && !isFastPlaying)
			};
		}

		static void OnButtonClicked()
		{
			bool isFastPlaying = SessionState.GetBool(KEY_FAST_PLAYING, false);
			bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;

			if (isFastPlaying && isPlaying) EditorApplication.isPlaying = false;
			else if (!isPlaying) EnterFastPlay();
		}

#else

		static void OnToolbarGUI()
		{
			bool isFastPlaying = SessionState.GetBool(KEY_FAST_PLAYING, false);
			bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;

			GUILayout.BeginVertical();
			GUILayout.Space(2);

			Color originalBg = GUI.backgroundColor;
			if (isFastPlaying && isPlaying)
				GUI.backgroundColor = new Color(0.35f, 0.9f, 0.35f, 1f);

			EditorGUI.BeginDisabledGroup(isPlaying && !isFastPlaying);

			if (GUILayout.Button((isFastPlaying && isPlaying) ? GetStopContent() : GetPlayContent(), GetButtonStyle()))
			{
				if (isFastPlaying && isPlaying) EditorApplication.isPlaying = false;
				else if (!isPlaying) EnterFastPlay();
			}

			EditorGUI.EndDisabledGroup();
			GUI.backgroundColor = originalBg;
			GUILayout.EndVertical();
		}

		private static GUIContent GetPlayContent()
		{
			if (_playContent == null)
			{
				Texture icon = LoadIcon("d_PlayButton", "PlayButton", "Animation.Play");
				string tooltip = "Fast Play\nEnters Play Mode with Domain & Scene reload disabled.\nOriginal settings are restored automatically on exit.";
				_playContent = icon != null
					? new GUIContent(" Fast", icon, tooltip)
					: new GUIContent("Fast", tooltip);
			}
			return _playContent;
		}

		private static GUIContent GetStopContent()
		{
			if (_stopContent == null)
			{
				Texture icon = LoadIcon("d_PlayButton On", "PlayButton On", "d_PlayButton", "PlayButton");
				string tooltip = "Stop Fast Play\nOriginal Enter Play Mode settings will be restored.";
				_stopContent = icon != null
					? new GUIContent(" Stop", icon, tooltip)
					: new GUIContent("Stop", tooltip);
			}
			return _stopContent;
		}

		private static Texture LoadIcon(params string[] names)
		{
			foreach (string name in names)
			{
				try
				{
					GUIContent c = EditorGUIUtility.IconContent(name);
					if (c?.image != null) return c.image;
				}
				catch { }
			}
			return null;
		}

		private static GUIStyle GetButtonStyle()
		{
			if (_buttonStyle == null)
			{
				try
				{
					_buttonStyle = new GUIStyle("Command")
					{
						fontSize = 11,
						alignment = TextAnchor.MiddleCenter,
						imagePosition = ImagePosition.ImageLeft,
						fontStyle = FontStyle.Bold,
						fixedWidth = 0,
						padding = new RectOffset(4, 6, 0, 0)
					};
				}
				catch
				{
					_buttonStyle = new GUIStyle(EditorStyles.toolbarButton)
					{
						fontStyle = FontStyle.Bold,
						padding = new RectOffset(4, 6, 2, 2)
					};
				}
			}
			return _buttonStyle;
		}

#endif


		private static void EnterFastPlay()
		{
			SessionState.SetBool(KEY_ORIG_ENABLED, EditorSettings.enterPlayModeOptionsEnabled);
			SessionState.SetInt(KEY_ORIG_OPTIONS, (int)EditorSettings.enterPlayModeOptions);

			EditorSettings.enterPlayModeOptionsEnabled = true;
			EditorSettings.enterPlayModeOptions =
				EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;

			SessionState.SetBool(KEY_FAST_PLAYING, true);
			Debug.Log("<b>[Fast Play]</b> Entering Play Mode (Domain & Scene reload disabled).");
			EditorApplication.isPlaying = true;
		}

		private static void RestoreOriginalSettings()
		{
			if (!SessionState.GetBool(KEY_FAST_PLAYING, false))
				return;

			bool origEnabled = SessionState.GetBool(KEY_ORIG_ENABLED, false);
			EnterPlayModeOptions origOptions = (EnterPlayModeOptions)SessionState.GetInt(KEY_ORIG_OPTIONS, 0);

			SessionState.SetBool(KEY_FAST_PLAYING, false);

			EditorSettings.enterPlayModeOptionsEnabled = origEnabled;
			EditorSettings.enterPlayModeOptions = origOptions;

			Debug.Log($"<b>[Fast Play]</b> Settings restored (enterPlayModeOptionsEnabled={origEnabled}, options={origOptions}).");
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
#if UNITY_6000_3_OR_NEWER
			MainToolbar.Refresh(k_ElementName);
#endif
			if (state == PlayModeStateChange.ExitingPlayMode)
			{
				RestoreOriginalSettings();
			}
			else if (state == PlayModeStateChange.EnteredEditMode)
			{
				RestoreOriginalSettings();
				EditorApplication.delayCall += VerifySettingsRestored;
			}
		}

		private static void VerifySettingsRestored()
		{
			if (SessionState.GetBool(KEY_FAST_PLAYING, false))
			{
				Debug.LogWarning("<b>[Fast Play]</b> Settings were not properly restored - forcing restoration now.");
				RestoreOriginalSettings();
			}
		}
	}
}
#endif
