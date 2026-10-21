using System;
using System.IO;
using NUnit.Framework;

namespace Gigaduck.AssetSources.Tests
{
    /// <summary>Verifies portable linking, machine relocation and invalid-input handling in isolated temporary folders.</summary>
    public sealed class SourceRegistryTests
    {
        private string temporary, root, guid;
        private SourceRegistry registry;

        [SetUp] public void SetUp()
        {
            temporary = Path.Combine(Path.GetTempPath(), "GigaduckAssetSources-" + Guid.NewGuid().ToString("N"));
            string project = Path.Combine(temporary, "Project");
            root = Path.Combine(temporary, "Resources");
            Directory.CreateDirectory(project);
            Directory.CreateDirectory(Path.Combine(root, "Chair"));
            File.WriteAllText(Path.Combine(root, "Chair/chair.blend"), "test fixture");
            File.WriteAllText(Path.Combine(root, "Chair/chair.gmat"), "test fixture");
            registry = new SourceRegistry(project);
            registry.DefineLibrary("art", "Art");
            registry.SetLibraryRoot("art", root);
            guid = "1234567890abcdef1234567890abcdef";
        }

        [TearDown] public void TearDown()
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }

        private SourceLink Model(string path = "Chair/chair.blend") => new SourceLink
        { id = "model", libraryId = "art", relativePath = path, applicationId = "blender" };

        [Test] public void LinkSurvivesRegistryReloadAndContainsNoMachineRoot()
        {
            registry.Link(guid, Model());
            var fresh = new SourceRegistry(registry.ProjectRoot);
            Assert.That(fresh.Resolve(fresh.GetRecord(guid).sources[0]), Is.EqualTo(Path.GetFullPath(Path.Combine(root, "Chair/chair.blend"))));
            Assert.That(File.ReadAllText(registry.RecordPath(guid)), Does.Not.Contain(root));
            Assert.That(File.ReadAllText(registry.LibrariesPath), Does.Not.Contain(root));
        }

        [Test] public void RelocatingLibraryNeedsOnlyNewLocalRoot()
        {
            registry.Link(guid, Model());
            string before = File.ReadAllText(registry.RecordPath(guid));
            string moved = Path.Combine(temporary, "OtherDrive");
            Directory.Move(root, moved);
            registry.SetLibraryRoot("art", moved);
            Assert.That(registry.Resolve(registry.GetRecord(guid).sources[0]), Does.StartWith(moved));
            Assert.That(File.ReadAllText(registry.RecordPath(guid)), Is.EqualTo(before));
        }

        [Test] public void UpsertPreservesOtherSourcesAndSharedMasters()
        {
            registry.Link(guid, Model());
            registry.Link(guid, new SourceLink { id = "textures", libraryId = "art", relativePath = "Chair/chair.gmat", applicationId = "graphmat" });
            registry.Link(guid, Model());
            string otherGuid = "abcdef1234567890abcdef1234567890";
            registry.Link(otherGuid, Model());
            Assert.That(registry.GetRecord(guid).sources.Count, Is.EqualTo(2));
            Assert.That(registry.GetRecord(otherGuid).sources.Count, Is.EqualTo(1));
            registry.Unlink(guid, "model");
            Assert.That(registry.GetRecord(guid).sources[0].id, Is.EqualTo("textures"));
            Assert.That(File.Exists(Path.Combine(root, "Chair/chair.blend")), Is.True);
        }

        [TestCase("../escape.blend")]
        [TestCase("/absolute.blend")]
        [TestCase("C:/absolute.blend")]
        [TestCase("Chair/../escape.blend")]
        [TestCase("Chair//chair.blend")]
        [TestCase("\\\\server\\share\\source.blend")]
        public void RejectsNonRelativePaths(string path) => Assert.Throws<ArgumentException>(() => registry.Link(guid, Model(path)));

        [Test] public void MissingSourceDoesNotOverwriteExistingRecord()
        {
            registry.Link(guid, Model());
            string before = File.ReadAllText(registry.RecordPath(guid));
            Assert.Throws<FileNotFoundException>(() => registry.Link(guid, Model("Chair/missing.blend")));
            Assert.That(File.ReadAllText(registry.RecordPath(guid)), Is.EqualTo(before));
            File.Delete(Path.Combine(root, "Chair/chair.blend"));
            Assert.That(registry.ValidateAll().Count, Is.EqualTo(1));
        }

        [Test] public void RejectsInternalLibrariesAndRootPrefixSiblings()
        {
            Assert.Throws<ArgumentException>(() => registry.SetLibraryRoot("art", registry.ProjectRoot));
            string sibling = root + "-other";
            Directory.CreateDirectory(sibling);
            Assert.Throws<ArgumentException>(() => registry.RelativePath("art", Path.Combine(sibling, "chair.blend")));
        }

        [Test] public void BrokenJsonIsReportedWithoutOverwriting()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(registry.RecordPath(guid)));
            File.WriteAllText(registry.RecordPath(guid), "{broken");
            Assert.Throws<InvalidDataException>(() => registry.Link(guid, Model()));
            Assert.That(File.ReadAllText(registry.RecordPath(guid)), Is.EqualTo("{broken"));
        }

        [Test] public void EnvironmentRootOverridesLocalSetting()
        {
            string variable = "GIGADUCK_ASSET_SOURCES_TEST_" + Guid.NewGuid().ToString("N");
            string replacement = Path.Combine(temporary, "EnvironmentRoot");
            Directory.CreateDirectory(replacement);
            try
            {
                registry.DefineLibrary("art", "Art", variable);
                Environment.SetEnvironmentVariable(variable, replacement);
                Assert.That(registry.LibraryRoot("art"), Is.EqualTo(replacement));
            }
            finally { Environment.SetEnvironmentVariable(variable, null); }
        }

        [Test] public void UnsupportedSchemaAndGuidMismatchAreRejected()
        {
            registry.Link(guid, Model());
            string file = registry.RecordPath(guid);
            File.WriteAllText(file, "{\"schemaVersion\":2,\"unityGuid\":\"" + guid + "\",\"sources\":[]}");
            registry.Reload();
            Assert.Throws<InvalidDataException>(() => registry.GetRecord(guid));
            File.WriteAllText(file, "{\"schemaVersion\":1,\"unityGuid\":\"wrong\",\"sources\":[]}");
            registry.Reload();
            Assert.Throws<InvalidDataException>(() => registry.GetRecord(guid));
        }
    }
}
