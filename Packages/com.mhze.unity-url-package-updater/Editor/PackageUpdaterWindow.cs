using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace mhze.UrlPackageUpdater
{
    public class PackageUpdaterWindow : EditorWindow
    {
        [MenuItem("Tools/URL Package Updater", priority = 100)]
        private static void OpenWindow()
        {
            var w = GetWindow<PackageUpdaterWindow>(true, "URL Package Updater");
            w.minSize = new Vector2(420, 250);
        }

        private List<PackageInfo> _packages = new();
        private readonly HashSet<string> _selected = new();
        private Vector2 _scrollPos;
        private string _status = "";
        private bool _busy;
        private int _updateIndex;
        private List<string> _queue = new();
        private ListRequest _listReq;
        private AddRequest _addReq;

        private void OnEnable() => Refresh();

        private void Refresh()
        {
            if (_busy) return;
            _status = "Loading packages...";
            _listReq = Client.List();
            EditorApplication.update += PollList;
        }

        private void PollList()
        {
            if (!_listReq.IsCompleted) return;
            EditorApplication.update -= PollList;

            if (_listReq.Status == StatusCode.Success)
            {
                _packages = _listReq.Result
                    .Where(p => p.source == PackageSource.Git || p.source == PackageSource.LocalTarball)
                    .OrderBy(p => p.name)
                    .ToList();
                _selected.Clear();
                foreach (var p in _packages)
                    _selected.Add(p.packageId);
                _status = $"{_packages.Count} URL package(s) found.";
            }
            else
            {
                _packages.Clear();
                _selected.Clear();
                _status = $"Error: {_listReq.Error?.message}";
            }
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            GUILayout.Label("URL Package Updater", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
            EditorGUILayout.Space();

            if (_packages.Count == 0)
            {
                if (!_busy)
                    EditorGUILayout.HelpBox("No Git or Local Tarball packages found.", MessageType.Info);
                DrawButtons();
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            foreach (var pkg in _packages)
            {
                bool was = _selected.Contains(pkg.packageId);
                bool now = EditorGUILayout.ToggleLeft($"  {pkg.name}  [{pkg.source}]", was);
                if (now != was)
                {
                    if (now) _selected.Add(pkg.packageId);
                    else _selected.Remove(pkg.packageId);
                }
            }

            EditorGUILayout.EndScrollView();
            DrawButtons();
        }

        private void DrawButtons()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledGroupScope(_busy))
            {
                if (GUILayout.Button("Refresh", GUILayout.Width(80)))
                    Refresh();
            }

            using (new EditorGUI.DisabledGroupScope(_busy || _selected.Count == 0))
            {
                if (GUILayout.Button($"Update Selected ({_selected.Count})"))
                    StartUpdate();
            }

            if (_busy && _queue.Count > 0)
                EditorGUILayout.LabelField($"({_updateIndex}/{_queue.Count})", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void StartUpdate()
        {
            _queue = _packages.Where(p => _selected.Contains(p.packageId))
                .Select(p => p.packageId).ToList();
            if (_queue.Count == 0) return;
            _busy = true;
            _updateIndex = 0;
            _status = $"Updating {_queue.Count} package(s)...";
            ProcessNext();
        }

        private void ProcessNext()
        {
            if (_updateIndex >= _queue.Count)
            {
                _busy = false;
                _status = "Update complete.";
                Repaint();
                Refresh();
                return;
            }

            var id = _queue[_updateIndex];
            _status = $"Updating: {id}";
            _updateIndex++;
            _addReq = Client.Add(id);
            EditorApplication.update += PollAdd;
            Repaint();
        }

        private void PollAdd()
        {
            if (!_addReq.IsCompleted) return;
            EditorApplication.update -= PollAdd;
            if (_addReq.Status != StatusCode.Success)
                Debug.LogWarning($"[PackageUpdater] Failed: {_addReq.Error?.message}");
            ProcessNext();
        }

        private void OnDestroy()
        {
            EditorApplication.update -= PollList;
            EditorApplication.update -= PollAdd;
        }
    }
}
