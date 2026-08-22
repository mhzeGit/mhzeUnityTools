using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_6000_0_OR_NEWER
using PhysicsMaterialAsset = UnityEngine.PhysicsMaterial;
#else
using PhysicsMaterialAsset = UnityEngine.PhysicMaterial;
#endif

namespace Gigaduck.AutoBoxCollider
{
    internal sealed class AutoBoxColliderWindow : EditorWindow
    {
        private const string WindowTitle = "Auto Box Collider";

        [SerializeField] private GameObject _target;
        [SerializeField] private ColliderAnalysisSettings _settings = new ColliderAnalysisSettings();
        [SerializeField] private bool _replaceExisting = true;
        [SerializeField] private bool _isTrigger;
        [SerializeField] private PhysicsMaterialAsset _physicsMaterial;
        [SerializeField] private bool _xRayPreview = true;
        [SerializeField] private bool _showBoxNumbers;
        [SerializeField] private Vector2 _scrollPosition;

        [NonSerialized] private ColliderAnalysisResult _preview;
        [NonSerialized] private string _status;
        [NonSerialized] private MessageType _statusType = MessageType.Info;

        [MenuItem("Tools/Advanced Box Collider Generator")]
        private static void Open()
        {
            OpenFor(Selection.activeGameObject);
        }

        [MenuItem("CONTEXT/MeshFilter/Advanced Box Collider Generator")]
        private static void OpenFromMeshFilter(MenuCommand command)
        {
            var filter = command.context as MeshFilter;
            OpenFor(filter != null ? filter.gameObject : null);
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/Advanced Box Collider Generator")]
        private static void OpenFromSkinnedMesh(MenuCommand command)
        {
            var renderer = command.context as SkinnedMeshRenderer;
            OpenFor(renderer != null ? renderer.gameObject : null);
        }

        private static void OpenFor(GameObject target)
        {
            AutoBoxColliderWindow window = GetWindow<AutoBoxColliderWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(390f, 560f);
            if (target != null)
            {
                window._target = target;
                window.ClearPreview();
            }
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            SceneView.duringSceneGui -= DrawScenePreview;
            SceneView.duringSceneGui += DrawScenePreview;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawScenePreview;
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawHeader();
            DrawTarget();
            DrawQualityPresets();
            DrawAnalysisSettings();
            DrawPreviewControls();
            DrawResult();
            DrawOutputSettings();
            DrawActions();
            DrawStatus();
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Advanced Auto Box Collider", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Voxelizes real mesh triangles, fills closed volume, recursively fits boxes, and optimizes their count before anything is added to the scene.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8f);
        }

        private void DrawTarget()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            GameObject nextTarget = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Target", "The GameObject that will own the generated collider hierarchy."),
                _target,
                typeof(GameObject),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                _target = nextTarget;
                ClearPreview();
            }

            if (GUILayout.Button("Use Selection", GUILayout.Width(100f)))
            {
                _target = Selection.activeGameObject;
                ClearPreview();
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            _settings.includeChildMeshes = EditorGUILayout.Toggle(
                new GUIContent(
                    "Include Child Meshes",
                    "Combines all MeshFilters and SkinnedMeshRenderers below the target into one collider analysis."),
                _settings.includeChildMeshes);
            if (EditorGUI.EndChangeCheck())
                ClearPreview();

            if (_target != null && !MeshColliderAnalyzer.HasMeshSource(_target, _settings.includeChildMeshes))
            {
                EditorGUILayout.HelpBox(
                    _settings.includeChildMeshes
                        ? "No mesh source was found on this target or its children."
                        : "No mesh source was found on this target. Enable Include Child Meshes for a model root.",
                    MessageType.Warning);
            }
            EditorGUILayout.Space(8f);
        }

        private void DrawQualityPresets()
        {
            EditorGUILayout.LabelField("Quality Presets", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Low"))
                ApplyPreset(32, 0.18f, 32);
            if (GUILayout.Button("Balanced"))
                ApplyPreset(64, 0.08f, 96);
            if (GUILayout.Button("High"))
                ApplyPreset(128, 0.025f, 192);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5f);
        }

        private void DrawAnalysisSettings()
        {
            EditorGUILayout.LabelField("Mesh Analysis", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _settings.voxelResolution = EditorGUILayout.IntSlider(
                new GUIContent(
                    "Detail Resolution",
                    "Voxel cells along the mesh's longest axis. Higher values preserve smaller features but cost more analysis time."),
                _settings.voxelResolution,
                12,
                160);
            _settings.emptySpaceTolerance = EditorGUILayout.Slider(
                new GUIContent(
                    "Shape Tolerance",
                    "Maximum empty-space fraction accepted inside a fitted box. Lower values follow concavity with more colliders."),
                _settings.emptySpaceTolerance,
                0f,
                0.5f);
            _settings.maximumColliders = EditorGUILayout.IntSlider(
                new GUIContent(
                    "Collider Budget",
                    "Hard maximum number of BoxColliders. The preview reports when this budget prevents the requested tolerance."),
                _settings.maximumColliders,
                1,
                256);
            _settings.alignment = (ColliderAlignment)EditorGUILayout.EnumPopup(
                new GUIContent(
                    "Box Alignment",
                    "Auto compares object-space boxes against an area-weighted principal-axis fit and keeps the better result."),
                _settings.alignment);
            _settings.fillEnclosedVolume = EditorGUILayout.Toggle(
                new GUIContent(
                    "Fill Closed Volume",
                    "Flood fills watertight meshes so boxes model their solid interior. Disable this for intentionally open shells."),
                _settings.fillEnclosedVolume);
            _settings.preserveHolesAndOpenings = EditorGUILayout.Toggle(
                new GUIContent(
                    "Preserve Openings",
                    "Treats bracketed exterior voids such as doors, windows, tunnels, and concave channels as hard constraints that tolerance cannot fill."),
                _settings.preserveHolesAndOpenings);
            _settings.detectAngledSurfaces = EditorGUILayout.Toggle(
                new GUIContent(
                    "Detect Angled Surfaces",
                    "Fits independently rotated boxes to connected planar triangle patches and rotated mesh components before voxel decomposition."),
                _settings.detectAngledSurfaces);
            using (new EditorGUI.DisabledScope(!_settings.detectAngledSurfaces))
            {
                _settings.planarAngleTolerance = EditorGUILayout.Slider(
                    new GUIContent(
                        "Planar Angle Tolerance",
                        "Maximum normal-angle difference used to combine adjacent triangles into one rotated planar collider candidate."),
                    _settings.planarAngleTolerance,
                    0.5f,
                    15f);
            }
            _settings.paddingInVoxels = EditorGUILayout.Slider(
                new GUIContent(
                    "Collider Padding",
                    "Expands or contracts every fitted box by this fraction of a voxel."),
                _settings.paddingInVoxels,
                -0.25f,
                0.5f);
            if (EditorGUI.EndChangeCheck())
            {
                _settings.Validate();
                ClearPreview();
            }

            EditorGUILayout.HelpBox(
                $"Effective grid: up to {_settings.voxelResolution} cells on the longest axis. "
                + $"Each fitted box may contain at most {_settings.emptySpaceTolerance:P0} empty volume.",
                MessageType.None);
            EditorGUILayout.Space(8f);
        }

        private void DrawPreviewControls()
        {
            EditorGUILayout.LabelField("Scene Preview", EditorStyles.boldLabel);
            _xRayPreview = EditorGUILayout.Toggle(
                new GUIContent("X-Ray Bounds", "Draw collider bounds through the mesh."),
                _xRayPreview);
            _showBoxNumbers = EditorGUILayout.Toggle(
                new GUIContent("Show Box Numbers", "Labels each proposed collider in the Scene view."),
                _showBoxNumbers);
            EditorGUILayout.Space(8f);
        }

        private void DrawResult()
        {
            if (_preview == null)
                return;

            EditorGUILayout.LabelField("Preview Result", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Alignment", _preview.Frame.Name);
            EditorGUILayout.LabelField("Mesh", $"{_preview.VertexCount:N0} vertices, {_preview.TriangleCount:N0} triangles");
            EditorGUILayout.LabelField("Sources", _preview.SourceDescription);
            EditorGUILayout.LabelField(
                "Topology",
                _preview.IsWatertight
                    ? $"{_preview.ConnectedComponentCount} watertight component(s)"
                    : $"{_preview.ConnectedComponentCount} component(s), {_preview.BoundaryEdgeCount:N0} boundary edges");
            if (_preview.NonManifoldEdgeCount > 0)
                EditorGUILayout.LabelField("Non-Manifold Edges", _preview.NonManifoldEdgeCount.ToString("N0"));
            EditorGUILayout.LabelField("Voxel Grid", $"{_preview.GridX} x {_preview.GridY} x {_preview.GridZ}");
            EditorGUILayout.LabelField("Occupied Voxels", $"{_preview.OccupiedVoxelCount:N0} ({_preview.SurfaceVoxelCount:N0} surface)");
            EditorGUILayout.LabelField("Protected Void Voxels", _preview.ProtectedVoidVoxelCount.ToString("N0"));
            EditorGUILayout.LabelField("Proposed Colliders", _preview.Boxes.Count.ToString("N0"));
            EditorGUILayout.LabelField("Rotated Surface Fits", _preview.OrientedBoxCount.ToString("N0"));
            EditorGUILayout.LabelField("Occupied Coverage", _preview.OccupiedCoverage.ToString("P1"));
            EditorGUILayout.LabelField("Approx. Empty Volume", _preview.EmptyVolumeFraction.ToString("P1"));

            if (_preview.UnresolvedProtectedVoidCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"The current resolution produced {_preview.UnresolvedProtectedVoidCount:N0} protected opening voxels that could not be carved safely. "
                    + "Generation is disabled. Raise the collider budget, lower detail, or disable Preserve Openings only if filling those voids is intentional.",
                    MessageType.Error);
            }
            else if (_preview.ColliderBudgetExceeded)
            {
                EditorGUILayout.HelpBox(
                    $"The optimizer used {_preview.Boxes.Count:N0} colliders, above the requested budget, because preserving doors and through-holes has priority over collider count.",
                    MessageType.Warning);
            }
            else if (_preview.BudgetLimited)
            {
                EditorGUILayout.HelpBox(
                    $"The collider budget was reached. Worst box error is {_preview.WorstBoxError:P1}, "
                    + $"above the requested {_settings.emptySpaceTolerance:P1}. Raise the budget, increase tolerance, or lower detail.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Preview only: no collider objects have been created. Occupied coverage is preserved and protected negative space is not crossed by any proposed box.",
                    MessageType.Info);
            }
            if (_settings.fillEnclosedVolume && !_preview.IsWatertight)
            {
                EditorGUILayout.HelpBox(
                    "The mesh has open or non-manifold topology, so enclosed-volume filling was disabled for safety. Triangle shell coverage is still generated.",
                    MessageType.Info);
            }
            EditorGUILayout.Space(8f);
        }

        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField("Generated Colliders", EditorStyles.boldLabel);
            _replaceExisting = EditorGUILayout.Toggle(
                new GUIContent(
                    "Replace Previous",
                    "Removes child hierarchies named 'Generated Box Colliders' before generating the confirmed result."),
                _replaceExisting);
            _isTrigger = EditorGUILayout.Toggle("Is Trigger", _isTrigger);
            _physicsMaterial = (PhysicsMaterialAsset)EditorGUILayout.ObjectField(
                "Physics Material",
                _physicsMaterial,
                typeof(PhysicsMaterialAsset),
                false);
            EditorGUILayout.Space(8f);
        }

        private void DrawActions()
        {
            bool hasSource = MeshColliderAnalyzer.HasMeshSource(_target, _settings.includeChildMeshes);
            using (new EditorGUI.DisabledScope(!hasSource))
            {
                if (GUILayout.Button("Analyze and Preview Bounds", GUILayout.Height(30f)))
                    AnalyzePreview();
            }

            using (new EditorGUI.DisabledScope(
                       _preview == null
                       || _preview.Target != _target
                       || _preview.UnresolvedProtectedVoidCount > 0))
            {
                if (GUILayout.Button("Confirm and Generate Box Colliders", GUILayout.Height(34f)))
                    ConfirmAndGenerate();
            }
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(_status, _statusType);
            }
            EditorGUILayout.Space(8f);
        }

        private void ApplyPreset(int resolution, float tolerance, int budget)
        {
            _settings.voxelResolution = resolution;
            _settings.emptySpaceTolerance = tolerance;
            _settings.maximumColliders = budget;
            ClearPreview();
            GUI.FocusControl(null);
        }

        private void AnalyzePreview()
        {
            ClearPreview();
            if (!MeshColliderAnalyzer.TryBuildGeometry(
                    _target,
                    _settings.includeChildMeshes,
                    out MeshGeometry geometry,
                    out string error))
            {
                SetStatus(error, MessageType.Error);
                return;
            }

            try
            {
                _settings.Validate();
                _preview = MeshColliderAnalyzer.Analyze(
                    _target,
                    geometry,
                    _settings,
                    (progress, stage) => EditorUtility.DisplayCancelableProgressBar(
                        "Analyzing Mesh Colliders",
                        stage,
                        Mathf.Clamp01(progress)));
                SetStatus(
                    $"Preview ready: {_preview.Boxes.Count} optimized box collider(s). Confirm to create them.",
                    MessageType.Info);
                SceneView.RepaintAll();
            }
            catch (OperationCanceledException)
            {
                ClearPreview();
                SetStatus("Mesh analysis was canceled. No scene objects were changed.", MessageType.Warning);
            }
            catch (Exception exception)
            {
                ClearPreview();
                SetStatus("Analysis failed: " + exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ConfirmAndGenerate()
        {
            if (_preview == null || _preview.Target != _target)
                return;

            if (_preview.UnresolvedProtectedVoidCount > 0)
            {
                SetStatus("Generation is blocked because protected openings are unresolved in this preview.", MessageType.Error);
                return;
            }

            if (_preview.SettingsHash != _settings.GetStableHash())
            {
                ClearPreview();
                SetStatus("Analysis settings changed. Run the preview again before generating.", MessageType.Warning);
                return;
            }

            if (!MeshColliderAnalyzer.TryBuildGeometry(
                    _target,
                    _settings.includeChildMeshes,
                    out MeshGeometry currentGeometry,
                    out string error))
            {
                ClearPreview();
                SetStatus(error, MessageType.Error);
                return;
            }

            if (currentGeometry.SourceHash != _preview.SourceHash)
            {
                ClearPreview();
                SetStatus("The source mesh or child transforms changed. Run the preview again before generating.", MessageType.Warning);
                return;
            }

            GameObject root = BoxColliderBaker.Bake(
                _preview,
                _replaceExisting,
                _isTrigger,
                _physicsMaterial);
            SetStatus(
                $"Generated {_preview.Boxes.Count} BoxCollider object(s) under '{root.name}'.",
                MessageType.Info);
        }

        private void DrawScenePreview(SceneView sceneView)
        {
            if (_preview == null || _preview.Target == null || _preview.Boxes == null)
                return;

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            CompareFunction previousZTest = Handles.zTest;
            try
            {
                Handles.zTest = _xRayPreview ? CompareFunction.Always : CompareFunction.LessEqual;
                Color defaultColor = _preview.BudgetLimited
                    ? new Color(1f, 0.55f, 0.12f, 0.95f)
                    : new Color(0.1f, 0.85f, 1f, 0.95f);

                for (int i = 0; i < _preview.Boxes.Count; i++)
                {
                    ColliderBox box = _preview.Boxes[i];
                    Handles.color = box.IsOrientedFit
                        ? new Color(0.45f, 1f, 0.25f, 0.95f)
                        : defaultColor;
                    Handles.matrix = _preview.Target.transform.localToWorldMatrix
                        * Matrix4x4.TRS(box.Center, box.Rotation, Vector3.one);
                    Handles.DrawWireCube(Vector3.zero, box.Size);
                    if (_showBoxNumbers)
                        Handles.Label(Vector3.zero, (i + 1).ToString());
                }
            }
            finally
            {
                Handles.matrix = previousMatrix;
                Handles.color = previousColor;
                Handles.zTest = previousZTest;
            }
        }

        private void ClearPreview()
        {
            _preview = null;
            _status = null;
            SceneView.RepaintAll();
            Repaint();
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            Repaint();
        }
    }

    internal static class BoxColliderBaker
    {
        private const string RootName = "Generated Box Colliders";

        public static GameObject Bake(
            ColliderAnalysisResult result,
            bool replaceExisting,
            bool isTrigger,
            PhysicsMaterialAsset material)
        {
            if (result == null || result.Target == null || result.Boxes == null)
                throw new ArgumentException("A valid preview result is required.", nameof(result));

            GameObject target = result.Target;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate Box Colliders");
            try
            {
                if (replaceExisting)
                {
                    for (int i = target.transform.childCount - 1; i >= 0; i--)
                    {
                        Transform child = target.transform.GetChild(i);
                        if (child.name == RootName)
                            Undo.DestroyObjectImmediate(child.gameObject);
                    }
                }

                string generatedRootName = replaceExisting
                    ? RootName
                    : GameObjectUtility.GetUniqueNameForSibling(target.transform, RootName);
                var root = new GameObject(generatedRootName);
                Undo.RegisterCreatedObjectUndo(root, "Create Collider Root");
                Undo.SetTransformParent(root.transform, target.transform, "Parent Collider Root");
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                root.layer = target.layer;
                for (int i = 0; i < result.Boxes.Count; i++)
                {
                    ColliderBox box = result.Boxes[i];
                    var child = new GameObject($"Box Collider {i + 1:000}");
                    Undo.RegisterCreatedObjectUndo(child, "Create Box Collider");
                    Undo.SetTransformParent(child.transform, root.transform, "Parent Box Collider");
                    child.layer = target.layer;
                    child.transform.localPosition = box.Center;
                    child.transform.localRotation = box.Rotation;
                    child.transform.localScale = Vector3.one;

                    BoxCollider collider = Undo.AddComponent<BoxCollider>(child);
                    collider.center = Vector3.zero;
                    collider.size = box.Size;
                    collider.isTrigger = isTrigger;
                    collider.sharedMaterial = material;
                }

                if (target.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(target.scene);
                Selection.activeGameObject = root;
                return root;
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }
    }
}
