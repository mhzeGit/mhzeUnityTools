using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace mhze.HierarchyContextMenu
{
    internal readonly struct ItemIconInfo
    {
        public readonly string IconName;
        public readonly Color? TintColor;

        public ItemIconInfo(string iconName, Color? tintColor = null)
        {
            IconName = iconName;
            TintColor = tintColor;
        }
    }

    static class MenuIcons
    {
        public static readonly Dictionary<string, ItemIconInfo> SpecialItemIcons = new()
        {
            { "Cut", new ItemIconInfo("editicon.sml") },
            { "Copy", new ItemIconInfo("SceneLoadIn") },
            { "Paste", new ItemIconInfo("SceneLoadOut") },
            { "Paste Special", new ItemIconInfo("editicon.sml") },
            { "Paste As Child", new ItemIconInfo("editicon.sml") },
            { "Paste As Sibling", new ItemIconInfo("editicon.sml") },
            { "Rename", new ItemIconInfo("editicon.sml") },
            { "Duplicate", new ItemIconInfo("d_TreeEditor.Duplicate") },
            { "Delete", new ItemIconInfo("d_TreeEditor.Trash", new Color(1f, 0.15f, 0.15f)) },
            { "Select All", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Deselect All", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Invert Selection", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Select Children", new ItemIconInfo("GameObject Icon") },
            { "Find References in Scene", new ItemIconInfo("Search Icon") },
            { "Set as Default Parent", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Prefab", new ItemIconInfo("Prefab Icon") },
            { "Open Asset in Context", new ItemIconInfo("Prefab Icon") },
            { "Open Asset in Isolation", new ItemIconInfo("Prefab Icon") },
            { "Select Asset", new ItemIconInfo("Prefab Icon") },
            { "Select Root", new ItemIconInfo("Prefab Icon") },
            { "Replace...", new ItemIconInfo("Prefab Icon") },
            { "Replace and Keep Overrides...", new ItemIconInfo("Prefab Icon") },
            { "Unpack", new ItemIconInfo("Prefab Icon") },
            { "Unpack Completely", new ItemIconInfo("Prefab Icon") },
            { "Remove Unused Overrides...", new ItemIconInfo("Prefab Icon") },

            // Project context menu icons
            { "Copy Path", new ItemIconInfo("d_RectTransform Icon") },
            { "Select Dependencies", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Select Previous", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Reimport All", new ItemIconInfo("Refresh") },
            { "Show in Explorer", new ItemIconInfo("Folder Icon") },
            { "Open", new ItemIconInfo("UnityEditor.SceneView") },
            { "Open Scene Additive", new ItemIconInfo("UnityEditor.SceneView") },
            { "Properties...", new ItemIconInfo("d_Settings") },
            { "Reimport", new ItemIconInfo("Refresh") },
            { "Import New Asset...", new ItemIconInfo("d_Import") },
            { "Import Package", new ItemIconInfo("d_Package Manager") },
            { "Custom Package...", new ItemIconInfo("d_Package Manager") },
            { "Export Package...", new ItemIconInfo("d_Package Manager") },
            { "View in Package Manager", new ItemIconInfo("d_Package Manager") },
            { "Create UPM Package", new ItemIconInfo("d_Package Manager") },
            { "Export As UPM Package", new ItemIconInfo("d_Package Manager") },
            { "Extract Material", new ItemIconInfo("PreMatCube") },
            { "Refresh", new ItemIconInfo("Refresh") },
        };

        private static readonly Dictionary<string, Texture2D> _cache = new();
        private static readonly Dictionary<string, Texture2D> _grayscaleCache = new();

        public static Texture2D Load(string name)
        {
            if (!_cache.TryGetValue(name, out var tex))
            {
                var content = EditorGUIUtility.IconContent(name);
                tex = content?.image as Texture2D;
                _cache[name] = tex;
            }
            return tex;
        }

        public static Texture2D LoadDesaturated(string name)
        {
            if (_grayscaleCache.TryGetValue(name, out var cached))
                return cached;

            var src = Load(name);
            if (src == null)
                return null;

            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            var pixels = src.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                float gray = p.r * 0.299f + p.g * 0.587f + p.b * 0.114f;
                pixels[i] = new Color(gray, gray, gray, p.a);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _grayscaleCache[name] = tex;
            return tex;
        }

        public static string ResolveIcon(string displayName, bool isCategory)
        {
            if (isCategory)
                return ResolveCategoryIcon(displayName);

            var lower = displayName.ToLowerInvariant();

            var lastSlash = lower.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                var lastSegment = lower.Substring(lastSlash + 1);
                switch (lastSegment)
                {
                    case "adaptive probe volume":
                    case "adaptive probe volumes":
                        return "d_LightProbeGroup Icon";
                }
            }

            switch (lower)
            {
                case "create empty":
                case "create empty child":
                case "create empty parent":
                    return "d_Transform Icon";

                case "cube":
                    return "PreMatCube";
                case "sphere":
                    return "PreMatSphere";
                case "capsule":
                case "plane":
                case "quad":
                    return "GameObject Icon";
                case "cylinder":
                    return "PreMatCylinder";

                case "ragdoll":
                case "ragdoll...":
                    return "AvatarMask On Icon";
                case "terrain":
                    return "Terrain Icon";
                case "tree":
                    return "Terrain Icon";
                case "wind zone":
                    return "Terrain Icon";

                case "sprite":
                case "square":
                case "circle":
                case "isometric diamond":
                case "hexagonal tile":
                case "capsule collider 2d":
                case "circle collider 2d":
                case "box collider 2d":
                case "polygon collider 2d":
                case "edge collider 2d":
                case "sprite shape":
                case "sprite shape profile":
                case "sprite atlas":
                    return "Sprite Icon";

                case "directional light":
                    return "DirectionalLight Icon";
                case "point light":
                    return "Light Icon";
                case "area light":
                    return "d_AreaLight Icon";
                case "light probe proxy volume":
                    return "d_LightProbeProxyVolume Icon";
                case "spot light":
                    return "d_Spotlight Icon";
                case "light probe group":
                    return "LightProbeGroup Gizmo";
                case "adaptive probe volume":
                case "adaptive probe volumes":
                    return "d_LightProbeGroup Icon";
                case "reflection probe":
                    return "ReflectionProbeSelector";

                case "audio source":
                case "audio reverb zone":
                case "audio listener":
                case "audio low pass filter":
                case "audio high pass filter":
                case "audio echo filter":
                case "audio distortion filter":
                case "audio reverb filter":
                case "audio chorus filter":
                    return "AudioSource Icon";

                case "video player":
                    return "UnityEditor.GameView";

                case "canvas":
                case "button":
                case "image":
                case "raw image":
                case "text":
                case "input field":
                case "slider":
                case "scrollbar":
                case "toggle":
                case "dropdown":
                case "panel":
                case "scroll view":
                case "event system":
                case "mask":
                case "rect mask 2d":
                case "selectable":
                case "toggle group":
                case "layout element":
                case "horizontal layout group":
                case "vertical layout group":
                case "grid layout group":
                    return "Canvas Icon";

                case "camera":
                case "cinemachine virtual camera":
                case "cinemachine freelook":
                case "cinemachine clear shot":
                case "cinemachine blend list":
                case "cinemachine state-driven":
                case "cinemachine target group":
                case "cinemachine collider":
                case "cinemachine confiner":
                    return "Camera Icon";

                case "particle system":
                    return "ParticleShapeTool";
                case "particle system force field":
                case "visual effect":
                    return "Particle Effect";
                case "trail":
                    return "d_TrailRenderer Icon";
                case "line":
                    return "d_LineRenderer Icon";

                case "timeline":
                case "playable director":
                    return "UnityEditor.AnimationWindow";

                case "post process volume":
                case "post process layer":
                    return "d_Settings";

                case "ui document":
                case "panel settings":
                    return "Canvas Icon";

                case "nav mesh surface":
                case "navmesh surface":
                case "navmeshsurface":
                    return "d_NavMeshData Icon";
                case "nav mesh agent":
                case "navmesh agent":
                case "navmeshagent":
                    return "d_NavMeshAgent Icon";
                case "nav mesh obstacle":
                case "navmesh obstacle":
                case "navmeshobstacle":
                    return "d_NavMeshObstacle Icon";
                case "nav mesh link":
                case "navmesh link":
                case "navmeshlink":
                    return "d_NavMeshAgent Icon";
                case "nav mesh modifier":
                case "navmesh modifier":
                case "navmeshmodifier":
                case "nav mesh modifier volume":
                case "navmesh modifier volume":
                case "navmeshmodifiervolume":
                    return "d_NavMeshObstacle Icon";

                case "rigidbody":
                case "box collider":
                case "sphere collider":
                case "capsule collider":
                case "mesh collider":
                case "wheel collider":
                case "terrain collider":
                case "hinge joint":
                case "fixed joint":
                case "spring joint":
                case "character joint":
                case "configurable joint":
                case "constant force":
                    return "d_editicon.sml";

                case "textmeshpro":
                    return "Font Icon";

                case "audio mixer":
                    return "AudioSource Icon";

                default:
                    return "GameObject Icon";
            }
        }

        private static string ResolveCategoryIcon(string categoryName)
        {
            var lower = categoryName.ToLowerInvariant();
            switch (lower)
            {
                case "3d object":
                case "create":
                case "gameobject":
                    return "GameObject Icon";
                case "2d object":
                case "sprite shape":
                case "physics 2d":
                    return "Sprite Icon";
                case "light":
                case "lights":
                    return "Light Icon";
                case "audio":
                    return "AudioSource Icon";
                case "ui":
                case "ui toolkit":
                    return "d_Canvas Icon";
                case "video":
                    return "UnityEditor.GameView";
                case "effects":
                case "particle systems":
                case "visual effects":
                    return "Particle Effect";
                case "timeline":
                    return "UnityEditor.AnimationWindow";
                case "cinemachine":
                    return "Camera Icon";
                case "post processing":
                case "rendering":
                    return "d_Settings";
                case "textmeshpro":
                    return "Font Icon";
                case "navigation":
                case "navmesh":
                case "ai":
                    return "d_NavMeshData Icon";
                case "physics":
                    return "d_editicon.sml";
                case "camera":
                    return "Camera Icon";
                case "terrain":
                    return "Terrain Icon";
                default:
                    return "Folder Icon";
            }
        }
    }
}
