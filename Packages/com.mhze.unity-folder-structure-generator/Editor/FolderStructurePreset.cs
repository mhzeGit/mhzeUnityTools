using System.Collections.Generic;
using UnityEngine;

namespace MHZE.FolderStructureGenerator
{
    [CreateAssetMenu(menuName = "MHZE/Folder Structure Preset", fileName = "NewFolderStructurePreset.asset")]
    public class FolderStructurePreset : ScriptableObject
    {
        public string presetName;
        public List<FolderNode> rootFolders = new List<FolderNode>();

        public static List<FolderStructurePreset> GetBuiltInPresets()
        {
            return new List<FolderStructurePreset>
            {
                CreateStandardUnity(),
                CreateCleanArchitecture(),
                CreateModularGame()
            };
        }

        static FolderStructurePreset CreateStandardUnity()
        {
            var preset = ScriptableObject.CreateInstance<FolderStructurePreset>();
            preset.presetName = "Standard Unity Project";
            preset.rootFolders = new List<FolderNode>
            {
                new FolderNode("_Scripts")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Managers"),
                        new FolderNode("Mediators"),
                        new FolderNode("Player"),
                        new FolderNode("Enemies"),
                        new FolderNode("UI"),
                        new FolderNode("Utils")
                    }
                },
                new FolderNode("_Art")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Materials"),
                        new FolderNode("Shaders"),
                        new FolderNode("Textures"),
                        new FolderNode("Models")
                        {
                            children = new List<FolderNode>
                            {
                                new FolderNode("Environment"),
                                new FolderNode("Props")
                            }
                        },
                        new FolderNode("Sprites")
                    }
                },
                new FolderNode("_Audio")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Music"),
                        new FolderNode("SFX"),
                        new FolderNode("Voice")
                    }
                },
                new FolderNode("_Prefabs")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Player"),
                        new FolderNode("Environment"),
                        new FolderNode("Props"),
                        new FolderNode("UI")
                    }
                },
                new FolderNode("_Scenes"),
                new FolderNode("_Animations")
            };
            return preset;
        }

        static FolderStructurePreset CreateCleanArchitecture()
        {
            var preset = ScriptableObject.CreateInstance<FolderStructurePreset>();
            preset.presetName = "Clean Architecture";
            preset.rootFolders = new List<FolderNode>
            {
                new FolderNode("_Scripts")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Domain")
                        {
                            children = new List<FolderNode>
                            {
                                new FolderNode("Entities"),
                                new FolderNode("ValueObjects"),
                                new FolderNode("Interfaces")
                            }
                        },
                        new FolderNode("Application")
                        {
                            children = new List<FolderNode>
                            {
                                new FolderNode("Services"),
                                new FolderNode("DTOs"),
                                new FolderNode("Ports")
                            }
                        },
                        new FolderNode("Infrastructure")
                        {
                            children = new List<FolderNode>
                            {
                                new FolderNode("Persistence"),
                                new FolderNode("Networking"),
                                new FolderNode("Audio")
                            }
                        },
                        new FolderNode("Presentation")
                        {
                            children = new List<FolderNode>
                            {
                                new FolderNode("Controllers"),
                                new FolderNode("Views"),
                                new FolderNode("UI")
                            }
                        }
                    }
                }
            };
            return preset;
        }

        static FolderStructurePreset CreateModularGame()
        {
            var preset = ScriptableObject.CreateInstance<FolderStructurePreset>();
            preset.presetName = "Modular Game";
            preset.rootFolders = new List<FolderNode>
            {
                new FolderNode("Core")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Singletons"),
                        new FolderNode("Events"),
                        new FolderNode("Pooling"),
                        new FolderNode("SaveSystem")
                    }
                },
                new FolderNode("Gameplay")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Combat"),
                        new FolderNode("Inventory"),
                        new FolderNode("Quests"),
                        new FolderNode("Dialogues")
                    }
                },
                new FolderNode("UI")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Screens"),
                        new FolderNode("Widgets"),
                        new FolderNode("Popups"),
                        new FolderNode("HUD")
                    }
                },
                new FolderNode("Audio")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Music"),
                        new FolderNode("SFX")
                    }
                },
                new FolderNode("Art")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Materials"),
                        new FolderNode("Textures"),
                        new FolderNode("Models"),
                        new FolderNode("Animations")
                    }
                },
                new FolderNode("Config"),
                new FolderNode("Tests")
                {
                    children = new List<FolderNode>
                    {
                        new FolderNode("Unit"),
                        new FolderNode("PlayMode")
                    }
                }
            };
            return preset;
        }

        public FolderStructurePreset Clone()
        {
            var clone = ScriptableObject.CreateInstance<FolderStructurePreset>();
            clone.presetName = presetName;
            foreach (var root in rootFolders)
                clone.rootFolders.Add(root.Clone());
            return clone;
        }
    }
}
