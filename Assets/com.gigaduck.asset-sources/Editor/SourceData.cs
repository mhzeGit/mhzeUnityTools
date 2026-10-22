using System;
using System.Collections.Generic;

namespace Gigaduck.AssetSources
{
    /// <summary>A portable external library definition; its physical root belongs to each machine.</summary>
    [Serializable] public sealed class SourceLibrary
    {
        public string id;
        public string displayName;
        public string environmentVariable;
    }

    /// <summary>The versioned set of libraries available to this Unity project.</summary>
    [Serializable] public sealed class SourceLibraries
    {
        public int schemaVersion = 1;
        public List<SourceLibrary> libraries = new List<SourceLibrary>();
    }

    /// <summary>One editable source, identified within its asset by a caller-chosen stable ID.</summary>
    [Serializable] public sealed class SourceLink
    {
        public string id;
        public string libraryId;
        public string relativePath;
        public string applicationId = "default";
        public string label;
        /// <summary>Link for navigation only: opening still works, but saving the master does not export it into Unity.</summary>
        public bool noSync;
    }

    /// <summary>The portable source links for one Unity asset GUID.</summary>
    [Serializable] public sealed class AssetSourceRecord
    {
        public int schemaVersion = 1;
        public string unityGuid;
        public List<SourceLink> sources = new List<SourceLink>();
    }

    /// <summary>A machine-specific library root or application executable location.</summary>
    [Serializable] public sealed class LocalSourcePath
    {
        public string id;
        public string path;
    }

    /// <summary>Machine-specific locations, kept outside the versioned project settings.</summary>
    [Serializable] public sealed class LocalSourceSettings
    {
        public int schemaVersion = 1;
        public List<LocalSourcePath> libraries = new List<LocalSourcePath>();
        public List<LocalSourcePath> applications = new List<LocalSourcePath>();
    }
}
