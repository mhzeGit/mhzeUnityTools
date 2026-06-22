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
        private ListRequest _listReq;
        private AddAndRemoveRequest _addRemoveReq;
        private int _completedCount;
        private int _totalCount;

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

            if (_busy)
                EditorGUILayout.LabelField($"({_completedCount}/{_totalCount})", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void StartUpdate()
        {
            var toUpdate = _packages.Where(p => _selected.Contains(p.packageId)).ToList();
            if (toUpdate.Count == 0) return;

            _busy = true;
            _totalCount = toUpdate.Count;
            _completedCount = 0;
            _status = $"Updating {_totalCount} package(s)...";

            var toAdd = toUpdate.Select(p => p.packageId).ToArray();
            var toRemove = toUpdate.Select(p => p.name).ToArray();

            var param = new AddAndRemoveParameters(toAdd, toRemove);
            _addRemoveReq = Client.AddAndRemove(param);
            EditorApplication.update += PollAddRemove;
            Repaint();
        }

        private void PollAddRemove()
        {
            if (!_addRemoveReq.IsCompleted) return;
            EditorApplication.update -= PollAddRemove;

            _completedCount = _totalCount;

            if (_addRemoveReq.Status != StatusCode.Success)
                Debug.LogWarning($"[PackageUpdater] Failed: {_addRemoveReq.Error?.message}");

            _busy = false;
            _status = "Update complete. Re-listing...";
            Repaint();
            Refresh();
        }

        private void OnDestroy()
        {
            EditorApplication.update -= PollList;
            EditorApplication.update -= PollAddRemove;
        }
    }
}
