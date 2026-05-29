#if UNITY_EDITOR && !UNITY_6000_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FastPlayButtonTool.Toolbar
{
	[InitializeOnLoad]
	public static class ToolbarExtender
	{
		public static readonly List<Action> LeftToolbarGUI  = new List<Action>();
		public static readonly List<Action> RightToolbarGUI = new List<Action>();

		static ToolbarExtender()
		{
			ToolbarCallback.OnToolbarGUILeft  = GUILeft;
			ToolbarCallback.OnToolbarGUIRight = GUIRight;
		}

		static void GUILeft()
		{
			GUILayout.BeginHorizontal();
			foreach (var handler in LeftToolbarGUI)
				handler();
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		static void GUIRight()
		{
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			foreach (var handler in RightToolbarGUI)
				handler();
			GUILayout.EndHorizontal();
		}
	}
}
#endif
