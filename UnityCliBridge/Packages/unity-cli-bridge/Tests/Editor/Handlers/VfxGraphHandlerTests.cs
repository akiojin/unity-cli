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

        [Test]
        public void Apply_SetBlockSetting_WithoutSetting_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting",
                ["assetPath"] = "Assets/Some.vfx"
            }));
            StringAssert.Contains("setting is required", ex.Message);
        }

        [Test]
        public void Apply_SetBlockSetting_WithoutValue_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting",
                ["assetPath"] = "Assets/Some.vfx",
                ["setting"] = "NoiseType"
            }));
            StringAssert.Contains("value is required", ex.Message);
        }

        [Test]
        public void Apply_AddContext_WithoutContextName_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = "Assets/Some.vfx"
            }));
            StringAssert.Contains("contextName is required", ex.Message);
        }

        [Test]
        public void Apply_AddOperator_WithoutOperatorName_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator",
                ["assetPath"] = "Assets/Some.vfx"
            }));
            StringAssert.Contains("operatorName is required", ex.Message);
        }

        [Test]
        public void Apply_LinkSlots_WithoutFrom_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = "Assets/Some.vfx",
                ["to"] = new JObject { ["node"] = "operator" }
            }));
            StringAssert.Contains("from is required", ex.Message);
        }

        [Test]
        public void Apply_LinkSlots_WithoutTo_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = "Assets/Some.vfx",
                ["from"] = new JObject { ["node"] = "operator" }
            }));
            StringAssert.Contains("to is required", ex.Message);
        }

        [Test]
        public void Apply_AddParameter_WithoutParameterName_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter",
                ["assetPath"] = "Assets/Some.vfx",
                ["type"] = "Float"
            }));
            StringAssert.Contains("parameterName is required", ex.Message);
        }

        [Test]
        public void Apply_AddParameter_WithoutType_ThrowsRequiredError()
        {
            var ex = Assert.Throws<Exception>(() => VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter",
                ["assetPath"] = "Assets/Some.vfx",
                ["parameterName"] = "Rate"
            }));
            StringAssert.Contains("type is required", ex.Message);
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

        [Test]
        public void ApplySetBlockSetting_ChangesTurbulenceNoiseType()
        {
            string copy = CopyFixture("setsetting");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = copy,
                ["contextType"] = "Update",
                ["blockName"] = "Turbulence"
            });

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting",
                ["assetPath"] = copy,
                ["contextType"] = "Update",
                ["blockIndex"] = 0,
                ["setting"] = "NoiseType",
                ["value"] = "Perlin"
            }));
            Assert.AreEqual("Perlin", result.Value<string>("value"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            JToken settings = ((JArray)FindContext(after, "Update")["blocks"])[0]["settings"];
            Assert.AreEqual("Perlin", settings?["NoiseType"]?.ToString());
        }

        [Test]
        public void ApplyAddContext_AddsOutputLinkedFromUpdate()
        {
            string copy = CopyFixture("addcontext");

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = copy,
                ["contextName"] = "Output Particle|Point",
                ["linkFrom"] = "Update"
            }));
            Assert.AreEqual("VFXPointOutput", result.Value<string>("addedContext"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual(5, after.Value<int>("contextCount"));
            // Update now flows into two outputs (the original quad + the new point output).
            Assert.AreEqual(2, ((JArray)FindContext(after, "Update")["outputs"]).Count);
        }

        [Test]
        public void ApplyAddOperator_AddsOperatorToGraph()
        {
            string copy = CopyFixture("addop");

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator",
                ["assetPath"] = copy,
                ["operatorName"] = "Add"
            }));
            Assert.AreEqual("Add", result.Value<string>("addedOperator"));
            Assert.AreEqual(0, result.Value<int>("operatorIndex"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual(1, after.Value<int>("operatorCount"));
            Assert.AreEqual("Add", ((JArray)after["operators"])[0].Value<string>("type"));
        }

        [Test]
        public void ApplyLinkSlots_LinksOperatorOutputToOperatorInput()
        {
            string copy = CopyFixture("linkslots");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator", ["assetPath"] = copy, ["operatorName"] = "Add"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator", ["assetPath"] = copy, ["operatorName"] = "Add"
            });

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = copy,
                ["from"] = new JObject { ["node"] = "operator", ["operatorIndex"] = 0, ["slot"] = 0 },
                ["to"] = new JObject { ["node"] = "operator", ["operatorIndex"] = 1, ["slot"] = 0 }
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var operators = (JArray)after["operators"];

            // Operator 0's output slot 0 now links to operator 1.
            var outLinks = (JArray)operators[0]["outputSlots"][0]["links"];
            Assert.AreEqual(1, outLinks.Count, "operator 0 output should have one link");
            Assert.AreEqual("operator", outLinks[0]["node"].Value<string>("kind"));
            Assert.AreEqual(1, outLinks[0]["node"].Value<int>("operatorIndex"));

            // Operator 1's input slot 0 reports the reciprocal link.
            Assert.IsTrue(operators[1]["inputSlots"][0].Value<bool>("hasLink"),
                "operator 1 input slot should report a link");
        }

        [Test]
        public void ApplyAddParameter_CreatesExposedFloatReportedByDescribe()
        {
            string copy = CopyFixture("addparam");

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter",
                ["assetPath"] = copy,
                ["parameterName"] = "Rate",
                ["type"] = "Float",
                ["value"] = 42.5f,
                ["category"] = "Tuning"
            }));
            Assert.AreEqual("Rate", result.Value<string>("parameterName"));
            Assert.IsTrue(result.Value<bool>("exposed"));
            Assert.AreEqual(0, result.Value<int>("parameterIndex"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual(1, after.Value<int>("parameterCount"));
            var param = ((JArray)after["parameters"])[0];
            Assert.AreEqual("Rate", param.Value<string>("exposedName"));
            Assert.IsTrue(param.Value<bool>("exposed"));
            Assert.AreEqual("Tuning", param.Value<string>("category"));
            Assert.AreEqual(42.5f, param.Value<float>("value"), 0.001f);
        }

        [Test]
        public void ApplyLinkSlots_LinksParameterIntoSpawnRateBlock()
        {
            string copy = CopyFixture("paramlink");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter", ["assetPath"] = copy,
                ["parameterName"] = "Rate", ["type"] = "Float"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Spawner", ["blockName"] = "Constant Spawn Rate"
            });

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = copy,
                ["from"] = new JObject { ["node"] = "parameter", ["parameterIndex"] = 0, ["slot"] = 0 },
                ["to"] = new JObject
                {
                    ["node"] = "block", ["contextType"] = "Spawner", ["blockIndex"] = 0, ["slot"] = 0
                }
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));

            // The parameter's output slot now drives the block's Rate input.
            var paramOut = (JArray)((JArray)after["parameters"])[0]["outputSlots"][0]["links"];
            Assert.AreEqual(1, paramOut.Count, "parameter output should drive one slot");
            Assert.AreEqual("block", paramOut[0]["node"].Value<string>("kind"));

            var spawner = FindContext(after, "Spawner");
            var rateInput = spawner["blocks"][0]["inputSlots"][0];
            Assert.IsTrue(rateInput.Value<bool>("hasLink"), "Rate input slot should report a link");
            Assert.AreEqual("parameter", ((JArray)rateInput["links"])[0]["node"].Value<string>("kind"));
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
