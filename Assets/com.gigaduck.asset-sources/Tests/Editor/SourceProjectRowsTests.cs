using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Gigaduck.AssetSources.Tests
{
    /// <summary>Checks source grouping, missing-file handling and right-aligned row button placement.</summary>
    public sealed class SourceProjectRowsTests
    {
        [Test] public void RowButtonsGroupApplicationsAndKeepDisabledSyncLinksClickable()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SourceRows-" + Guid.NewGuid().ToString("N"));
            var registry = new SourceRegistry(Path.Combine(folder, "Project"));
            const string guid = "11111111111111111111111111111111";
            try
            {
                string library = Path.Combine(folder, "Art");
                Directory.CreateDirectory(library);
                File.WriteAllText(Path.Combine(library, "one.blend"), "fixture");
                File.WriteAllText(Path.Combine(library, "two.blend"), "fixture");
                File.WriteAllText(Path.Combine(library, "texture.gmat"), "fixture");
                registry.DefineLibrary("art"); registry.SetLibraryRoot("art", library);
                registry.Link(guid, new SourceLink { id = "one", libraryId = "art", relativePath = "one.blend", applicationId = "blender", noSync = true });
                registry.Link(guid, new SourceLink { id = "two", libraryId = "art", relativePath = "two.blend", applicationId = "blender" });
                registry.Link(guid, new SourceLink { id = "texture", libraryId = "art", relativePath = "texture.gmat", applicationId = "graphmat" });
                File.Delete(Path.Combine(library, "texture.gmat"));
                var groups = SourceProjectRows.ReadGroups(registry, guid);
                Assert.That(groups.Select(x => x.application), Is.EqualTo(new[] { "blender", "graphmat" }));
                Assert.That(groups[0].available, Is.True);
                Assert.That(groups[0].content.tooltip, Does.Contain("one.blend").And.Contain("two.blend"));
                Assert.That(groups[1].available, Is.False);
                Assert.That(SourceProjectRows.ReadGroups(registry, "22222222222222222222222222222222"), Is.Empty);
                registry.Unlink(guid, "one"); registry.Unlink(guid, "two");
                Assert.That(SourceProjectRows.ReadGroups(registry, guid).Select(x => x.application), Is.EqualTo(new[] { "graphmat" }));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [TestCase(0)]
        [TestCase(75)]
        public void ButtonsStayAtTheFarRightAndDoNotOverlap(float left)
        {
            var row = new Rect(left, 30, 300, 20);
            var folder = SourceProjectRows.ButtonRect(row, 0);
            var source = SourceProjectRows.ButtonRect(row, 1);
            Assert.That(folder.xMax, Is.EqualTo(row.xMax - 4));
            Assert.That(source.xMax, Is.LessThan(folder.xMin));
            Assert.That(folder.center.y, Is.EqualTo(row.center.y));
        }

        [TestCase("blender", "Bl")]
        [TestCase("graphmat", "Gm")]
        [TestCase("painter", "Pt")]
        [TestCase("designer", "Ds")]
        [TestCase("photoshop", "Ps")]
        [TestCase("affinity", "Af")]
        [TestCase("code", "co")]
        [TestCase("x", "x")]
        public void ApplicationBadgesHaveDistinctLabelsAndActionTooltips(string application, string label)
        {
            string folder = Path.Combine(Path.GetTempPath(), "SourceBadge-" + Guid.NewGuid().ToString("N"));
            var registry = new SourceRegistry(Path.Combine(folder, "Project"));
            const string guid = "11111111111111111111111111111111";
            try
            {
                string library = Path.Combine(folder, "Art");
                Directory.CreateDirectory(library);
                registry.DefineLibrary("art"); registry.SetLibraryRoot("art", library);
                registry.Link(guid, new SourceLink { id = "source", libraryId = "art", relativePath = "missing.source", applicationId = application }, false);
                var badge = SourceProjectRows.ReadGroups(registry, guid).Single();
                Assert.That(badge.content.text, Is.EqualTo(label));
                Assert.That(badge.content.tooltip, Does.Contain(application).And.Contain("enable live sync"));
                Assert.That(badge.available, Is.False);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test] public void FolderButtonUsesAnExistingNativeIcon()
        {
            Assert.That(SourceProjectRows.FolderContent.image, Is.Not.Null);
            Assert.That(SourceProjectRows.FolderContent.image.width, Is.GreaterThan(0));
            Assert.That(SourceProjectRows.FolderContent.tooltip, Does.Contain("Show linked source"));
        }
    }
}
