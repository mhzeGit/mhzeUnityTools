using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Gigaduck.AssetSources.Tests
{
    /// <summary>Checks safe output mapping, shared sources, atomic publication and failed exports in isolated folders.</summary>
    public sealed class SourceLiveLinkTests
    {
        private string folder;
        [SetUp] public void SetUp() { folder = Path.Combine(Path.GetTempPath(), "AssetSourcesLive-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); }
        [TearDown] public void TearDown() { Directory.Delete(folder, true); }

        [Test] public void LegacyLinksKeepLiveSync()
        {
            var link = JsonUtility.FromJson<SourceLink>("{ \"id\": \"x\", \"relativePath\": \"a.blend\" }");
            Assert.That(link.noSync, Is.False);
            Assert.That(SourceLiveLink.Status(link), Is.Not.EqualTo("Linked for navigation only (live sync off)"));
        }

        [Test] public void NoSyncLinksAreNavigationOnly() =>
            Assert.That(SourceLiveLink.Status(new SourceLink { id = "x", noSync = true }), Is.EqualTo("Linked for navigation only (live sync off)"));

        [TestCase("Chair_BaseColor", "basecolor")]
        [TestCase("Chair_Normal", "normal")]
        [TestCase("Chair_AO", "ao")]
        [TestCase("Chair_roughness", "roughness")]
        [TestCase("Chair_MetallicSmoothness", null)]
        [TestCase("Chair_RoughnessMetallic", null)]
        [TestCase("Chair_ORM", null)]
        [TestCase("Chair_MaskMap", null)]
        [TestCase("Chair_unknown", null)]
        public void ChannelMappingRefusesPackedOrUnknownOutputs(string name, string channel) => Assert.That(SourceExport.Channel(name), Is.EqualTo(channel));

        [Test] public void MaterialMappingRefusesAmbiguity()
        {
            Assert.That(SourceExport.SelectMaterial(new[] { "Wood", "Metal" }, "Wood_BaseColor"), Is.EqualTo("Wood"));
            Assert.Throws<InvalidOperationException>(() => SourceExport.SelectMaterial(new[] { "Wood", "Metal" }, "Chair_BaseColor"));
            Assert.Throws<InvalidOperationException>(() => SourceExport.SelectMaterial(new[] { "Wood", "Wood_Dark" }, "Wood_Dark_BaseColor"));
        }

        [Test] public void SharedMasterFindsEveryConfirmedLinkButNoNeighbours()
        {
            var registry = new SourceRegistry(Path.Combine(folder, "Project"));
            string library = Path.Combine(folder, "Library");
            Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(library, "master.txt"), "master");
            File.WriteAllText(Path.Combine(library, "other.txt"), "other");
            registry.DefineLibrary("art"); registry.SetLibraryRoot("art", library);
            var source = new SourceLink { id = "source", libraryId = "art", relativePath = "master.txt" };
            registry.Link("11111111111111111111111111111111", source);
            registry.Link("22222222222222222222222222222222", source);
            registry.Link("33333333333333333333333333333333", new SourceLink { id = "source", libraryId = "art", relativePath = "other.txt" });
            registry.Link("44444444444444444444444444444444", new SourceLink { id = "source", libraryId = "art", relativePath = "master.txt", noSync = true });
            Assert.That(SourceLiveLink.FindTargets(registry, source).Count, Is.EqualTo(2));
        }

        [Test] public void DisablingALinkAlsoPreventsPublicationFromAnExistingSession()
        {
            var source = new SourceLink { libraryId = "art", relativePath = "master.blend" };
            Assert.That(SourceLiveLink.Matches(new SourceLink { libraryId = "art", relativePath = "master.blend", noSync = true }, source), Is.False);
            Assert.That(SourceLiveLink.Matches(new SourceLink { libraryId = "art", relativePath = "other.blend" }, source), Is.False);
        }

        [Test] public void BlenderConnectionsIsolateMastersAndSurviveScriptReload()
        {
            var first = new BlenderLiveConnection(Path.Combine(folder, "first.blend"), folder);
            var second = new BlenderLiveConnection(Path.Combine(folder, "second.blend"), folder);
            Assert.That(first.Config, Is.Not.EqualTo(second.Config));
            first.Configure(new List<SourceExport.Job>());
            var resumed = new BlenderLiveConnection(Path.Combine(folder, "first.blend"), folder);
            Assert.That(resumed.Config, Is.EqualTo(first.Config));
            Assert.That(File.Exists(resumed.Config), Is.True);
            first.Stop(); second.Stop();
            Assert.That(File.Exists(first.Config), Is.False);
        }

        [Test] public void BlenderRejectsOutputsOutsideItsSession()
        {
            var connection = new BlenderLiveConnection(Path.Combine(folder, "master.blend"), folder);
            connection.Configure(new List<SourceExport.Job> { new SourceExport.Job { guid = "test", assetPath = "Assets/test.fbx", target = Path.Combine(folder, "test.fbx") } });
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(connection.Config), "ready.json"),
                "{\"results\":[{\"guid\":\"test\",\"assetPath\":\"Assets/test.fbx\",\"output\":\"escape.fbx\"}]}");
            Assert.Throws<InvalidDataException>(() => connection.Receive());
            connection.Stop();
        }

        [Test] public void PublicationPreservesMetadataAndSkipsIdenticalBytes()
        {
            string source = Path.Combine(folder, "source.txt"), target = Path.Combine(folder, "target.txt");
            File.WriteAllText(source, "new"); File.WriteAllText(target, "old"); File.WriteAllText(target + ".meta", "guid: keep-me");
            Assert.That(SourceExport.Publish(source, target), Is.True);
            Assert.That(File.ReadAllText(target), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(target + ".meta"), Is.EqualTo("guid: keep-me"));
            Assert.That(SourceExport.Publish(source, target), Is.False);
            Assert.That(Directory.GetFiles(folder, "*.tmp"), Is.Empty);
        }

        [Test] public void UnsupportedNativeExportLeavesTargetUntouched()
        {
            string source = Path.Combine(folder, "master.afphoto"), target = Path.Combine(folder, "target.png");
            File.WriteAllText(source, "native"); File.WriteAllText(target, "keep");
            var job = new SourceExport.Job { source = source, target = target, assetPath = "Assets/target.png" };
            using (var result = SourceExport.Export(job))
            {
                Assert.That(result.error, Does.Contain("No automatic native exporter"));
                Assert.That(File.ReadAllText(target), Is.EqualTo("keep"));
            }
        }

        [Test] public void CopyExporterStagesUntilExplicitPublication()
        {
            string source = Path.Combine(folder, "source.txt"), target = Path.Combine(folder, "target.txt");
            File.WriteAllText(source, "new"); File.WriteAllText(target, "old");
            using (var result = SourceExport.Export(new SourceExport.Job { source = source, target = target, assetPath = "Assets/target.txt" }))
            {
                Assert.That(result.error, Is.Null);
                Assert.That(File.ReadAllText(result.output), Is.EqualTo("new"));
                Assert.That(File.ReadAllText(target), Is.EqualTo("old"));
                Assert.That(result.previousHash, Is.EqualTo(SourceExport.Hash(target)));
            }
        }

        [TestCase("master.blend", "Assets/Bin/Bin.fbx", true)]
        [TestCase("master.blend", "Assets/Bin/BinColor.png", true)]
        [TestCase("master.blend", "Assets/Bin/_M_BinMat.mat", false)]
        [TestCase("master.blend", "Assets/Bin/Bin.gltf", false)]
        [TestCase("master.gmat", "Assets/Textures/Bin_BaseColor.png", true)]
        [TestCase("master.gmat", "Assets/Textures/Bin_BaseColor.png.meta", false)]
        [TestCase("master.afphoto", "Assets/Textures/Bin.png", false)]
        [TestCase("master.png", "Assets/Textures/Bin.png", true)]
        public void OnlySupportedExporterPairsAreQueued(string source, string target, bool supported) =>
            Assert.That(SourceExport.CanExport(source, target), Is.EqualTo(supported));

        [Test] public void BatchCopiesSameExtensionMastersWithoutNativeTools()
        {
            string source = Path.Combine(folder, "master.gmat");
            string first = Path.Combine(folder, "first.gmat"), second = Path.Combine(folder, "second.gmat");
            File.WriteAllText(source, "master"); File.WriteAllText(first, "old"); File.WriteAllText(second, "old");
            var jobs = new[] { first, second }.Select(target => new SourceExport.Job { source = source, target = target, assetPath = "Assets/" + Path.GetFileName(target) }).ToList();
            var results = SourceExport.ExportMany(jobs);
            Assert.That(results.Count, Is.EqualTo(2));
            foreach (var result in results)
                using (result)
                {
                    Assert.That(result.error, Is.Null);
                    Assert.That(File.ReadAllText(result.output), Is.EqualTo("master"));
                }
        }

        [TestCase("C:\\art files\\master.blend", "\"C:\\art files\\master.blend\"")]
        [TestCase("C:\\art\\", "\"C:\\art\\\\\"")]
        [TestCase("a\"b", "\"a\\\"b\"")]
        public void ProcessArgumentsKeepPathsAndQuotesIntact(string value, string expected) => Assert.That(SourceExport.Quote(value), Is.EqualTo(expected));
    }
}
