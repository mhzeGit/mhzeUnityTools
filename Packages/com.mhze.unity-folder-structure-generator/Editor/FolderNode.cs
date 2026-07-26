using System;
using System.Collections.Generic;

namespace MHZE.FolderStructureGenerator
{
    [Serializable]
    public class FolderNode
    {
        public string name;
        public List<FolderNode> children = new List<FolderNode>();

        public FolderNode() { }

        public FolderNode(string name)
        {
            this.name = name;
        }

        public FolderNode Clone()
        {
            var clone = new FolderNode(name);
            foreach (var child in children)
                clone.children.Add(child.Clone());
            return clone;
        }
    }
}
