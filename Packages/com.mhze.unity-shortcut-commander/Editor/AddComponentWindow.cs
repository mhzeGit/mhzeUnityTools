using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace mhze.ShortcutCommander
{
    static class AddComponentWindow
    {
        public static void Open()
        {
            if (TryOpenBuiltin())
                return;

            CustomAddComponentWindow.Open();
        }

        private static bool TryOpenBuiltin()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.GetName().Name.StartsWith("UnityEditor", StringComparison.Ordinal))
                        continue;

                    Type windowType = null;

                    try
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.IsSubclassOf(typeof(EditorWindow)) &&
                                t.FullName != null &&
                                t.FullName.IndexOf("AddComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                windowType = t;
                                break;
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException) { continue; }

                    if (windowType == null)
                        continue;

                    var method = windowType
                        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0);

                    if (method != null)
                    {
                        method.Invoke(null, null);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }
    }

    class CustomAddComponentWindow : EditorWindow
    {
        private string _searchText = "";
        private Vector2 _scrollPosition;
        private List<Type> _allComponentTypes;
        private List<Type> _filteredTypes;
        private int _selectedIndex;

        private const float ItemHeight = 20f;
        private const float Width = 280f;
        private const float MaxHeight = 350f;

        internal static void Open()
        {
            var inspector = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(w => w.GetType().Name == "InspectorWindow");

            var screenPos = inspector != null
                ? new Vector2(inspector.position.x + inspector.position.width * 0.5f - Width * 0.5f,
                    inspector.position.y + inspector.position.height - 40f)
                : GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);

            var window = CreateInstance<CustomAddComponentWindow>();
            window.titleContent = new GUIContent("Add Component");
            var buttonRect = new Rect(screenPos.x, screenPos.y, Width, 1);
            window.ShowAsDropDown(buttonRect, new Vector2(Width, 300f));
        }

        private void OnEnable()
        {
            _allComponentTypes = TypeCache.GetTypesDerivedFrom<Component>()
                .Where(t => !t.IsAbstract && !t.IsGenericType && t.IsPublic && !t.IsSubclassOf(typeof(Transform)))
                .OrderBy(t => t.Name)
                .ToList();
            _filteredTypes = new List<Type>(_allComponentTypes);
            _selectedIndex = 0;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(2);
            GUI.SetNextControlName("SearchField");
            var newSearch = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField);
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                FilterComponents();
            }

            if (Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != "SearchField")
            {
                GUI.FocusControl("SearchField");
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            for (int i = 0; i < _filteredTypes.Count; i++)
            {
                var type = _filteredTypes[i];
                var rect = GUILayoutUtility.GetRect(Width, ItemHeight);

                var isSelected = i == _selectedIndex;
                var content = EditorGUIUtility.ObjectContent(null, type);

                if (Event.current.type == EventType.Repaint)
                {
                    if (isSelected)
                        EditorGUI.DrawRect(rect, new Color(0.22f, 0.42f, 0.75f));
                    else if (i % 2 == 1)
                        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.1f));

                    var iconRect = new Rect(rect.x + 2, rect.y + 2, 16, 16);
                    if (content.image != null)
                        GUI.DrawTexture(iconRect, content.image);

                    var labelRect = new Rect(rect.x + 20, rect.y, rect.width - 22, rect.height);
                    var style = isSelected ? EditorStyles.whiteLabel : EditorStyles.label;
                    GUI.Label(labelRect, type.Name, style);
                }

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    _selectedIndex = i;
                    Event.current.Use();
                    AddComponentAndClose(type);
                }
            }

            if (_filteredTypes.Count == 0)
            {
                EditorGUILayout.LabelField("No components found", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            HandleKeyboard();
        }

        private void HandleKeyboard()
        {
            if (Event.current.type != EventType.KeyDown) return;

            switch (Event.current.keyCode)
            {
                case KeyCode.DownArrow:
                    _selectedIndex = Mathf.Min(_selectedIndex + 1, _filteredTypes.Count - 1);
                    Event.current.Use(); Repaint();
                    break;
                case KeyCode.UpArrow:
                    _selectedIndex = Mathf.Max(_selectedIndex - 1, 0);
                    Event.current.Use(); Repaint();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Event.current.Use();
                    if (_selectedIndex >= 0 && _selectedIndex < _filteredTypes.Count)
                        AddComponentAndClose(_filteredTypes[_selectedIndex]);
                    break;
                case KeyCode.Escape:
                    Event.current.Use();
                    Close();
                    break;
            }
        }

        private void FilterComponents()
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                _filteredTypes = new List<Type>(_allComponentTypes);
            }
            else
            {
                var search = _searchText.ToLower();
                _filteredTypes = _allComponentTypes
                    .Where(t => t.Name.ToLower().Contains(search))
                    .ToList();
            }

            _selectedIndex = _filteredTypes.Count > 0 ? 0 : -1;

            var height = Mathf.Min(24f + _filteredTypes.Count * ItemHeight, MaxHeight);
            position = new Rect(position.x, position.y, Width, Mathf.Max(height, 50f));
        }

        private void AddComponentAndClose(Type componentType)
        {
            var gameObjects = Selection.gameObjects;
            if (gameObjects.Length == 0) { Close(); return; }

            Undo.RecordObjects(gameObjects, $"Add {componentType.Name}");

            foreach (var go in gameObjects)
            {
                if (go == null || go.GetComponent(componentType) != null) continue;
                go.AddComponent(componentType);
            }

            Close();
        }
    }
}
