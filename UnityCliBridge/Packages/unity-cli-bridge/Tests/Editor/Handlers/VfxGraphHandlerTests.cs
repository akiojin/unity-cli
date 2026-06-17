using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;
#if UNITY_VFX_GRAPH
using System.Linq;
using UnityEditor;
#endif

namespace UnityCliBridge.Tests
{
    /// <summary>
    /// Tests for VfxGraphHandler. Contract tests cover argument validation and op
    /// routing, which run before any reflection into the (internal) VFX Graph API
    /// and need no VFX package. Behavioral tests (UNITY_VFX_GRAPH) author a real
    /// graph against a committed fixture and require com.unity.visualeffectgraph.
    /// </summary>
    [TestFixture]
    public class VfxGraphHandlerTests
    {
        // ---- Contract tests (no VFX package required) ----------------------

        [Test]
        public void Apply_WithUnsupportedOp_ThrowsDescriptiveError()
        {
            var ex = Assert.Throws<Exception>(() =>
                VfxGraphHandler.Apply(new JObject { ["op"] = "no_such_op" }));
            StringAssert.Contains("Unsupported op", ex.Message);
        }

        [Test]
        public void Apply_AddBlock_WithoutBlockName_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = "Assets/Some.vfx"
            }));
            StringAssert.Contains("blockName is required", ex.Message);
        }

        [Test]
        public void Apply_AddBlock_WithoutAssetPath_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["blockName"] = "Turbulence"
            }));
            StringAssert.Contains("assetPath is required", ex.Message);
        }

        [Test]
        public void DescribeGraph_WithoutAssetPath_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() =>
                VfxGraphHandler.DescribeGraph(new JObject()));
            StringAssert.Contains("assetPath is required", ex.Message);
        }

#if UNITY_VFX_GRAPH
        // ---- Behavioral tests (require VFX Graph) --------------------------

        private const string Fixture = "Assets/VfxFixtures/Minimal.vfx";
        private const string TempFolder = "Assets/UnityCliBridgeTests/Vfx";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder("Assets/UnityCliBridgeTests"))
            {
                AssetDatabase.DeleteAsset("Assets/UnityCliBridgeTests");
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void DescribeGraph_OnMinimalFixture_ReportsFourContextsWithEmptyUpdate()
        {
            string copy = CopyFixture("describe");
            JObject result = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));

            Assert.AreEqual(4, result.Value<int>("contextCount"));
            JToken update = FindContext(result, "Update");
            Assert.IsNotNull(update, "Update context should exist");
            Assert.AreEqual(0, ((JArray)update["blocks"]).Count);
        }

        [Test]
        public void ApplyAddBlock_AddsTurbulenceToUpdateContext()
        {
            string copy = CopyFixture("apply");

            JObject applyResult = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = copy,
                ["contextType"] = "Update",
                ["blockName"] = "Turbulence"
            }));
            Assert.AreEqual("Turbulence", applyResult.Value<string>("addedBlock"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var blocks = (JArray)FindContext(after, "Update")["blocks"];
            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual("Turbulence", blocks[0].Value<string>("name"));
        }

        private static string CopyFixture(string suffix)
        {
            if (!System.IO.File.Exists(Fixture))
            {
                Assert.Ignore($"VFX fixture not present: {Fixture}");
            }

            EnsureFolder("Assets/UnityCliBridgeTests");
            EnsureFolder(TempFolder);
            string dest = $"{TempFolder}/Minimal_{suffix}.vfx";
            Assert.IsTrue(AssetDatabase.CopyAsset(Fixture, dest),
                $"Failed to copy fixture to {dest}");
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
            return dest;
        }

        private static JToken FindContext(JObject describeResult, string contextType)
        {
            return ((JArray)describeResult["contexts"])
                .FirstOrDefault(c => (string)c["contextType"] == contextType);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = path.Substring(0, path.LastIndexOf('/'));
            string folderName = path.Substring(path.LastIndexOf('/') + 1);
            AssetDatabase.CreateFolder(parent, folderName);
        }
#endif

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }
    }
}
