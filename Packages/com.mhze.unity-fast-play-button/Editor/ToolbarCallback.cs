#if UNITY_EDITOR && !UNITY_6000_3_OR_NEWER
using System;
using UnityEngine;
using UnityEditor;
using System.Reflection;
using UnityEngine.UIElements;

namespace FastPlayButtonTool.Toolbar
{
	public static class ToolbarCallback
	{
		static readonly Type s_ToolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
		static ScriptableObject s_CurrentToolbar;

		public static Action OnToolbarGUILeft;
		public static Action OnToolbarGUIRight;

		static ToolbarCallback()
		{
			EditorApplication.update -= OnUpdate;
			EditorApplication.update += OnUpdate;
		}

		static void OnUpdate()
		{
			if (s_CurrentToolbar != null)
				return;

			var toolbars = Resources.FindObjectsOfTypeAll(s_ToolbarType);
			s_CurrentToolbar = toolbars.Length > 0 ? (ScriptableObject)toolbars[0] : null;
			if (s_CurrentToolbar == null)
				return;

			var rootField = s_CurrentToolbar.GetType()
				.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
			var mRoot = rootField?.GetValue(s_CurrentToolbar) as VisualElement;
			if (mRoot == null)
				return;

			AddZoneCallback(mRoot, "ToolbarZoneLeftAlign",  () => OnToolbarGUILeft?.Invoke());
			AddZoneCallback(mRoot, "ToolbarZoneRightAlign", () => OnToolbarGUIRight?.Invoke());
		}

		static void AddZoneCallback(VisualElement root, string zoneName, Action callback)
		{
			var zone = root.Q(zoneName);
			if (zone == null) return;

			var parent = new VisualElement
			{
				style = { flexGrow = 1, flexDirection = FlexDirection.Row }
			};
			var container = new IMGUIContainer { style = { flexGrow = 1 } };
			container.onGUIHandler += callback;
			parent.Add(container);
			zone.Add(parent);
		}
	}
}
#endif
