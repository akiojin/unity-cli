using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    public class UnityPackageHandlerTests
    {
        private string temporary;
        private const string AssetPath = "Assets/Scenes/Generated/E2E/PackageImport/probe.txt";

        [SetUp]
        public void SetUp() => temporary = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "unitypackage-tests-" + Guid.NewGuid().ToString("N"))).FullName;

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset("Assets/Scenes/Generated/E2E/PackageImport");
            Directory.Delete(temporary, true);
        }

        private static JObject Call(string action, JObject parameters) =>
            JObject.FromObject(PackageManagerHandler.HandleCommand(action, parameters));

        [Test]
        public void ImportMissingFileReturnsStableError()
        {
            var result = Call("import", new JObject { ["path"] = Path.Combine(temporary, "missing.unitypackage") });
            Assert.AreEqual("PACKAGE_NOT_FOUND", result.Value<string>("code"));
        }

        [Test]
        public void ImportCorruptArchiveReturnsStableError()
        {
            var path = Path.Combine(temporary, "broken.unitypackage");
            File.WriteAllText(path, "not a gzip archive");
            var result = Call("import", new JObject { ["path"] = path });
            Assert.AreEqual("INVALID_UNITYPACKAGE", result.Value<string>("code"));
        }

        [Test]
        public void ImportRejectsInteractiveAndMissingPath()
        {
            Assert.AreEqual("INVALID_ARGUMENT", Call("import", new JObject()).Value<string>("code"));
            Assert.AreEqual("INVALID_ARGUMENT", Call("import", new JObject
            {
                ["path"] = "unused.unitypackage", ["interactive"] = true
            }).Value<string>("code"));
        }

        [Test]
        public void CacheListReturnsPublisherStableIdPathAndSize()
        {
            var folder = Directory.CreateDirectory(Path.Combine(temporary, "Publisher", "Category"));
            var path = Path.Combine(folder.FullName, "Small.unitypackage");
            File.WriteAllText(path, "cached");
            var first = Call("list_cached", new JObject { ["cachePath"] = temporary });
            var second = Call("list_cached", new JObject { ["cachePath"] = temporary });
            Assert.IsTrue(first.Value<bool>("success"), first.ToString());
            var item = first["packages"].Single();
            Assert.AreEqual("Publisher", item.Value<string>("publisher"));
            Assert.AreEqual("Small", item.Value<string>("name"));
            Assert.AreEqual(6, item.Value<long>("size"));
            Assert.AreEqual(Path.GetFullPath(path).Replace('\\', '/'), item.Value<string>("path"));
            Assert.IsNotEmpty(item.Value<string>("id"));
            Assert.AreEqual(item["id"], second["packages"][0]["id"]);
            Assert.IsNotNull(item["version"]);
        }

        [Test]
        public void CacheListReadsAssetStoreHeaderMetadata()
        {
            var path = Path.Combine(temporary, "download.unitypackage");
            var json = Encoding.UTF8.GetBytes("{\"id\":\"12345\",\"title\":\"Small Package\",\"version\":\"2.8\",\"publisher\":{\"label\":\"Publisher\"}}");
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new byte[] { 0x1f, 0x8b, 8, 4, 0, 0, 0, 0, 0, 3 });
                writer.Write((ushort)(json.Length + 4));
                writer.Write((byte)'A'); writer.Write((byte)'$'); writer.Write((ushort)json.Length); writer.Write(json);
            }
            var item = Call("list_cached", new JObject { ["cachePath"] = temporary })["packages"].Single();
            Assert.AreEqual("12345", item.Value<string>("id"));
            Assert.AreEqual("Small Package", item.Value<string>("name"));
            Assert.AreEqual("2.8", item.Value<string>("version"));
            Assert.AreEqual("Publisher", item.Value<string>("publisher"));
        }

        [TestCase("Assets/../outside.txt")]
        [TestCase("Packages/unsafe.txt")]
        [TestCase("Assets/sub/../../outside.txt")]
        public void ImportRejectsUnsafeArchivePaths(string pathname)
        {
            var path = Path.Combine(temporary, "unsafe.unitypackage");
            using (var gzip = new System.IO.Compression.GZipStream(File.Create(path), System.IO.Compression.CompressionMode.Compress))
            {
                var bytes = Encoding.UTF8.GetBytes(pathname);
                var header = new byte[512];
                Encoding.ASCII.GetBytes(new string('a', 32) + "/pathname").CopyTo(header, 0);
                Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 8).PadLeft(11, '0')).CopyTo(header, 124);
                for (var i = 148; i < 156; i++) header[i] = 32;
                Encoding.ASCII.GetBytes(Convert.ToString(header.Sum(value => (int)value), 8).PadLeft(6, '0') + "\0 ").CopyTo(header, 148);
                gzip.Write(header, 0, header.Length);
                gzip.Write(bytes, 0, bytes.Length);
                gzip.Write(new byte[512 - bytes.Length + 1024], 0, 512 - bytes.Length + 1024);
            }
            Assert.AreEqual("INVALID_UNITYPACKAGE", Call("import", new JObject { ["path"] = path }).Value<string>("code"));
        }

        [Test]
        public void CacheImportRejectsAmbiguousAndUnknownNames()
        {
            foreach (var publisher in new[] { "First", "Second" })
            {
                var folder = Directory.CreateDirectory(Path.Combine(temporary, publisher));
                File.WriteAllText(Path.Combine(folder.FullName, "Small.unitypackage"), "cached");
            }
            Assert.AreEqual("PACKAGE_AMBIGUOUS", Call("import_cached", new JObject
            {
                ["cachePath"] = temporary, ["nameOrId"] = "Small"
            }).Value<string>("code"));
            Assert.AreEqual("PACKAGE_NOT_FOUND", Call("import_cached", new JObject
            {
                ["cachePath"] = temporary, ["nameOrId"] = "Unknown"
            }).Value<string>("code"));
        }

        [Test]
        public void UnknownStatusAndUnsafeRequestIdFailClearly()
        {
            Assert.AreEqual("PACKAGE_IMPORT_NOT_FOUND", Call("import_status", new JObject
            {
                ["requestId"] = "unknown"
            }).Value<string>("code"));
            Assert.AreEqual("INVALID_ARGUMENT", Call("import_status", new JObject
            {
                ["requestId"] = "../unsafe"
            }).Value<string>("code"));
        }

        [UnityTest]
        public IEnumerator ImportRealExportedPackageReportsImportedPathsAndIdleState()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            File.WriteAllText(AssetPath, "package import probe");
            AssetDatabase.ImportAsset(AssetPath);
            var path = Path.Combine(temporary, "Small.unitypackage");
            AssetDatabase.ExportPackage(AssetPath, path);
            AssetDatabase.DeleteAsset(AssetPath);
            var request = new JObject { ["path"] = path, ["requestId"] = Guid.NewGuid().ToString("N") };
            var start = Call("import", request);
            Assert.IsTrue(start.Value<bool>("success"), start.ToString());
            // An explicit request ID makes a repeated dispatch idempotent.
            Assert.AreEqual(start["requestId"], Call("import", request)["requestId"]);
            Assert.AreEqual("PACKAGE_IMPORT_BUSY", Call("import", new JObject { ["path"] = path }).Value<string>("code"));
            var deadline = DateTime.UtcNow.AddSeconds(30);
            JObject result;
            do
            {
                yield return null;
                result = Call("import_status", new JObject { ["requestId"] = start["requestId"] });
            } while (result.Value<string>("status") != "completed" && result["error"] == null && DateTime.UtcNow < deadline);
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("completed", result.Value<string>("status"), result.ToString());
            Assert.Contains(AssetPath, result["importedAssets"].Values<string>().ToArray());
            Assert.IsFalse(result["compileState"].Value<bool>("isCompiling"));
            Assert.IsFalse(result["compileState"].Value<bool>("isUpdating"));
            Assert.AreEqual("package import probe", File.ReadAllText(AssetPath));
        }
    }
}
