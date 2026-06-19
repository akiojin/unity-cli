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

        private static void AssertError(object result, string expectedSubstring)
        {
            JObject obj = ToJObject(result);
            string error = obj.Value<string>("error");
            Assert.IsNotNull(error, $"expected an error result, got: {obj}");
            StringAssert.Contains(expectedSubstring, error);
        }

        [Test]
        public void Apply_WithUnsupportedOp_ReturnsDescriptiveError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "no_such_op",
                ["assetPath"] = "Assets/Some.vfx"
            }), "Unsupported op");
        }

        [Test]
        public void Apply_AddBlock_WithoutBlockName_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = "Assets/Some.vfx"
            }), "blockName is required");
        }

        [Test]
        public void Apply_AddBlock_WithoutAssetPath_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["blockName"] = "Turbulence"
            }), "assetPath is required");
        }

        [Test]
        public void DescribeGraph_WithoutAssetPath_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.DescribeGraph(new JObject()), "assetPath is required");
        }

        [Test]
        public void Apply_SetBlockSetting_WithoutSetting_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting",
                ["assetPath"] = "Assets/Some.vfx"
            }), "setting is required");
        }

        [Test]
        public void Apply_SetBlockSetting_WithoutValue_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting",
                ["assetPath"] = "Assets/Some.vfx",
                ["setting"] = "NoiseType"
            }), "value is required");
        }

        [Test]
        public void Apply_AddContext_WithoutContextName_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = "Assets/Some.vfx"
            }), "contextName is required");
        }

        [Test]
        public void Apply_AddOperator_WithoutOperatorName_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator",
                ["assetPath"] = "Assets/Some.vfx"
            }), "operatorName is required");
        }

        [Test]
        public void Apply_LinkSlots_WithoutFrom_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = "Assets/Some.vfx",
                ["to"] = new JObject { ["node"] = "operator" }
            }), "from is required");
        }

        [Test]
        public void Apply_LinkSlots_WithoutTo_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = "Assets/Some.vfx",
                ["from"] = new JObject { ["node"] = "operator" }
            }), "to is required");
        }

        [Test]
        public void Apply_AddParameter_WithoutParameterName_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter",
                ["assetPath"] = "Assets/Some.vfx",
                ["type"] = "Float"
            }), "parameterName is required");
        }

        [Test]
        public void Apply_AddParameter_WithoutType_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter",
                ["assetPath"] = "Assets/Some.vfx",
                ["parameterName"] = "Rate"
            }), "type is required");
        }

        [Test]
        public void Apply_LinkFlow_WithoutFrom_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_flow",
                ["assetPath"] = "Assets/Some.vfx",
                ["to"] = new JObject { ["contextType"] = "Spawner" }
            }), "from is required");
        }

        [Test]
        public void Apply_SetBounds_WithoutAnyArgs_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_bounds",
                ["assetPath"] = "Assets/Some.vfx"
            }), "at least one of");
        }

        [Test]
        public void Apply_AddStickyNote_WithoutAssetPath_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_sticky_note",
                ["title"] = "x"
            }), "assetPath is required");
        }

        [Test]
        public void Apply_CreateSubgraph_WithoutSubgraphPath_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "create_subgraph_asset",
                ["kind"] = "block"
            }), "subgraphPath is required");
        }

        [Test]
        public void Apply_CreateSubgraph_WithoutKind_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "create_subgraph_asset",
                ["subgraphPath"] = "Assets/Foo.vfxblock"
            }), "kind is required");
        }

        [Test]
        public void Apply_CreateSubgraph_WithMismatchedExtension_ReturnsDescriptiveError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "create_subgraph_asset",
                ["subgraphPath"] = "Assets/Foo.vfxoperator",
                ["kind"] = "block"
            }), "subgraphPath must end with");
        }

        [Test]
        public void Apply_CreateFromTemplate_WithoutTargetPath_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "create_from_template",
                ["template"] = "01_Minimal_System"
            }), "targetPath is required");
        }

        [Test]
        public void Apply_CreateFromTemplate_WithoutTemplate_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "create_from_template",
                ["targetPath"] = "Assets/New.vfx"
            }), "template is required");
        }

        [Test]
        public void Apply_SetInstancing_WithoutAnyArgs_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_instancing",
                ["assetPath"] = "Assets/Some.vfx"
            }), "at least one of");
        }

        [Test]
        public void Runtime_SetFloat_WithoutGameObject_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Runtime(new JObject
            {
                ["op"] = "set_float",
                ["name"] = "Rate",
                ["value"] = 1.0f
            }), "gameObject is required");
        }

        [Test]
        public void Settings_WithUnsupportedOp_ReturnsDescriptiveError()
        {
            AssertError(VfxGraphHandler.Settings(new JObject
            {
                ["op"] = "no_such_op"
            }), "Unsupported op");
        }

        [Test]
        public void Settings_Set_WithoutSetting_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Settings(new JObject
            {
                ["op"] = "set",
                ["value"] = 0.01f
            }), "setting is required");
        }

        [Test]
        public void Settings_Set_WithoutValue_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Settings(new JObject
            {
                ["op"] = "set",
                ["setting"] = "fixedTimeStep"
            }), "value is required");
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

        [Test]
        public void ApplyAddContextAndLinkFlow_WiresCustomEventIntoSpawn()
        {
            string copy = CopyFixture("event");

            JObject add = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = copy,
                ["contextName"] = "Event",
                ["settings"] = new JObject { ["eventName"] = "Burst" }
            }));
            Assert.AreEqual("VFXBasicEvent", add.Value<string>("addedContext"));
            Assert.IsTrue(((JArray)add["settingsApplied"]).Any(t => t.ToString() == "eventName"),
                "eventName should be reported as applied");

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_flow",
                ["assetPath"] = copy,
                ["from"] = new JObject { ["contextType"] = "Event" },
                ["to"] = new JObject { ["contextType"] = "Spawner" },
                ["toIndex"] = 0
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));

            JToken evt = FindContext(after, "Event");
            Assert.IsNotNull(evt, "Event context should exist");
            Assert.AreEqual("Burst", evt["settings"]?["eventName"]?.ToString());
            // The Event context flows into the Spawn context.
            Assert.AreEqual("Spawner", ((JArray)evt["outputs"])[0]["contextType"].ToString());
            // Spawn reports the reciprocal input edge.
            Assert.AreEqual(1, ((JArray)FindContext(after, "Spawner")["inputs"]).Count);
        }

        [Test]
        public void ApplySetBounds_SwitchesInitToManualAndWritesAABox()
        {
            string copy = CopyFixture("bounds");

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_bounds",
                ["assetPath"] = copy,
                ["mode"] = "Manual",
                ["center"] = new JArray { 1f, 2f, 3f },
                ["size"] = new JArray { 4f, 5f, 6f }
            }));
            Assert.AreEqual("Manual", result.Value<string>("mode"));
            Assert.IsNotNull(result["bounds"], "bounds should be applied");

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));

            JToken init = FindContext(after, "Init");
            Assert.IsNotNull(init, "Init context should exist");
            Assert.AreEqual("Manual", init["settings"]?["boundsMode"]?.ToString(),
                "boundsMode should now read Manual");

            // The Manual mode exposes a single 'bounds' input slot whose value carries the AABox.
            var boundsSlot = ((JArray)init["inputSlots"])
                .FirstOrDefault(s => (string)s["name"] == "bounds");
            Assert.IsNotNull(boundsSlot, "Manual mode should expose a 'bounds' input slot");
            JToken value = boundsSlot["value"];
            Assert.IsNotNull(value, "bounds slot should report its AABox value");
            Assert.AreEqual(1f, value["center"].Value<float>("x"), 0.001f);
            Assert.AreEqual(2f, value["center"].Value<float>("y"), 0.001f);
            Assert.AreEqual(3f, value["center"].Value<float>("z"), 0.001f);
            Assert.AreEqual(4f, value["size"].Value<float>("x"), 0.001f);
            Assert.AreEqual(5f, value["size"].Value<float>("y"), 0.001f);
            Assert.AreEqual(6f, value["size"].Value<float>("z"), 0.001f);
        }

        [Test]
        public void ApplyAddStickyNote_AppendsNoteVisibleInDescribe()
        {
            string copy = CopyFixture("sticky");

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_sticky_note",
                ["assetPath"] = copy,
                ["title"] = "TODO",
                ["contents"] = "Wire up bursts",
                ["position"] = new JArray { 100f, 50f, 240f, 120f },
                ["colorTheme"] = 2,
                ["textSize"] = "Medium"
            }));
            Assert.AreEqual(0, result.Value<int>("stickyNoteIndex"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual(1, after.Value<int>("stickyNoteCount"));
            var note = ((JArray)after["stickyNotes"])[0];
            Assert.AreEqual("TODO", note.Value<string>("title"));
            Assert.AreEqual("Wire up bursts", note.Value<string>("contents"));
            Assert.AreEqual(2, note.Value<int>("colorTheme"));
            Assert.AreEqual("Medium", note.Value<string>("textSize"));
            var pos = note["position"];
            Assert.AreEqual(100f, pos.Value<float>("x"), 0.001f);
            Assert.AreEqual(50f, pos.Value<float>("y"), 0.001f);
            Assert.AreEqual(240f, pos.Value<float>("width"), 0.001f);
            Assert.AreEqual(120f, pos.Value<float>("height"), 0.001f);
        }

        [Test]
        public void ApplySetInstancing_TogglesModeAndDescribesIt()
        {
            string copy = CopyFixture("instancing");

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_instancing",
                ["assetPath"] = copy,
                ["mode"] = "Disabled"
            }));
            Assert.AreEqual("Disabled", result.Value<string>("mode"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            JToken inst = after["instancing"];
            Assert.IsNotNull(inst, "describe should report an instancing block");
            Assert.AreEqual("Disabled", inst.Value<string>("mode"));
        }

        [Test]
        public void ApplyCustomHLSL_AddsBlockAndWritesInlineSource()
        {
            string copy = CopyFixture("customhlsl");
            const string source =
                "void MyHLSL(inout VFXAttributes attributes, in float scale)\n" +
                "{\n  attributes.position *= scale;\n}";

            JObject add = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = copy,
                ["contextType"] = "Update",
                ["blockName"] = "Custom HLSL"
            }));
            Assert.AreEqual("CustomHLSL", add.Value<string>("addedBlock"));

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting",
                ["assetPath"] = copy,
                ["contextType"] = "Update",
                ["blockIndex"] = 0,
                ["setting"] = "m_HLSLCode",
                ["value"] = source
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            JToken update = FindContext(after, "Update");
            var blocks = (JArray)update["blocks"];
            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual("CustomHLSL", blocks[0].Value<string>("type"));
            string storedCode = blocks[0]["settings"]?["m_HLSLCode"]?.ToString();
            Assert.IsNotNull(storedCode, "describe should now report m_HLSLCode (a ReadOnly setting)");
            Assert.IsTrue(storedCode.Contains("MyHLSL"),
                $"m_HLSLCode should contain the inline source; got: {storedCode}");

            // Slot resync: the default block exposes _offset + _speedFactor; our custom MyHLSL
            // declares a single `scale` input, so HLSLParser should reshape the block's input
            // slots to a single `_scale` entry.
            var inputSlots = (JArray)blocks[0]["inputSlots"];
            Assert.AreEqual(1, inputSlots.Count,
                "input slots should resync to match the custom signature");
            Assert.AreEqual("_scale", inputSlots[0].Value<string>("name"),
                "the single input slot should derive from the custom function's `scale` param");

            // Tier-2 oracle: no validator errors registered against any model after the edit.
            var errors = (JArray)after["errors"];
            Assert.IsNotNull(errors, "includeErrors=true should populate the errors array");
            var blockingErrors = errors.Where(e => (string)e["type"] == "Error").ToList();
            Assert.AreEqual(0, blockingErrors.Count,
                $"custom HLSL block should not register Error-tier issues; got: {string.Join(", ", blockingErrors.Select(e => (string)e["description"]))}");
        }

        [Test]
        public void ApplySubgraphBlock_CreatesAssetAndReferencesItFromParent()
        {
            string copy = CopyFixture("subgraph");
            string subPath = $"{TempFolder}/Sub.vfxblock";

            JObject created = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "create_subgraph_asset",
                ["subgraphPath"] = subPath,
                ["kind"] = "block"
            }));
            Assert.AreEqual("VisualEffectSubgraphBlock", created.Value<string>("assetType"));
            Assert.IsTrue(System.IO.File.Exists(subPath),
                $"subgraph asset file should exist on disk: {subPath}");

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = copy,
                ["contextType"] = "Update",
                ["blockName"] = "Empty Subgraph Block"
            });

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting",
                ["assetPath"] = copy,
                ["contextType"] = "Update",
                ["blockIndex"] = 0,
                ["setting"] = "m_Subgraph",
                ["value"] = subPath
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            JToken update = FindContext(after, "Update");
            var blocks = (JArray)update["blocks"];
            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual("VFXSubgraphBlock", blocks[0].Value<string>("type"));
            JToken subRef = blocks[0]["settings"]?["m_Subgraph"];
            Assert.IsNotNull(subRef, "m_Subgraph should be reported in block settings");
            Assert.AreEqual("VisualEffectSubgraphBlock", subRef.Value<string>("type"));
            Assert.AreEqual(subPath, subRef.Value<string>("assetPath"),
                "m_Subgraph.assetPath should resolve to the created subgraph asset");
        }

        [Test]
        public void ApplyAddSystem_BuildsFreshInitUpdateOutputChainSharingNewData()
        {
            string copy = CopyFixture("system");

            // Baseline: capture the existing system's data id.
            JObject baseline = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            int originalInitData = FindContext(baseline, "Init").Value<int>("dataInstanceId");

            // Author a fresh particle system: Init → Update → Output, no linkFrom (linkFrom
            // resolves by first-of-type which is ambiguous when duplicates exist).
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = copy,
                ["contextName"] = "Initialize Particle"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = copy,
                ["contextName"] = "Update Particle"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = copy,
                ["contextName"] = "Output Particle|Unlit|Quad"
            });

            // Wire the second system by index (Minimal has 4 contexts; new ones land at 4, 5, 6).
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_flow",
                ["assetPath"] = copy,
                ["from"] = new JObject { ["index"] = 4 },
                ["to"] = new JObject { ["index"] = 5 }
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_flow",
                ["assetPath"] = copy,
                ["from"] = new JObject { ["index"] = 5 },
                ["to"] = new JObject { ["index"] = 6 }
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual(7, after.Value<int>("contextCount"),
                "graph should now hold both systems' contexts");

            var contexts = (JArray)after["contexts"];
            var init2 = contexts[4];
            var update2 = contexts[5];
            var output2 = contexts[6];

            // Flow chain: Init2 → Update2 → Output2.
            Assert.AreEqual(5, ((JArray)init2["outputs"])[0].Value<int>("index"),
                "Init2 should flow into Update2");
            Assert.AreEqual(6, ((JArray)update2["outputs"])[0].Value<int>("index"),
                "Update2 should flow into Output2");
            Assert.AreEqual(4, ((JArray)update2["inputs"])[0].Value<int>("index"),
                "Update2 should report Init2 as its input");

            // Data sharing: Init2 and Update2 share one VFXData, distinct from the original system.
            int data2 = init2.Value<int>("dataInstanceId");
            Assert.AreEqual(data2, update2.Value<int>("dataInstanceId"),
                "Init2 and Update2 should share a single VFXData (LinkTo auto-merges)");
            Assert.AreEqual(data2, output2.Value<int>("dataInstanceId"),
                "Output2 should share the same VFXData via the Update2 link");
            Assert.AreNotEqual(originalInitData, data2,
                "the new system's VFXData must be distinct from the original system's");

            // Tier-2: no Error-tier validation entries on the freshly-built system.
            var errors = (JArray)after["errors"];
            var blocking = errors.Where(e => (string)e["type"] == "Error").ToList();
            Assert.AreEqual(0, blocking.Count,
                $"a from-scratch particle system should not register Error-tier issues; got: {string.Join(", ", blocking.Select(e => (string)e["description"]))}");
        }

        [Test]
        public void ListLibrary_Templates_ReportsBuiltInVfxTemplates()
        {
            JObject result = ToJObject(VfxGraphHandler.ListLibrary(
                new JObject { ["kind"] = "template" }));
            Assert.AreEqual("template", result.Value<string>("kind"));
            Assert.Greater(result.Value<int>("count"), 0,
                "the VFX package ships built-in templates");
            var names = ((JArray)result["items"]).Select(i => (string)i["name"]).ToList();
            Assert.IsTrue(names.Any(n => n.Contains("Minimal")),
                $"expected a Minimal-System template; got: {string.Join(", ", names)}");
        }

        [Test]
        public void ApplyCreateFromTemplate_InstantiatesVfxAssetWithContexts()
        {
            EnsureFolder("Assets/UnityCliBridgeTests");
            EnsureFolder(TempFolder);
            string targetPath = $"{TempFolder}/FromTemplate.vfx";

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "create_from_template",
                ["targetPath"] = targetPath,
                ["template"] = "01_Minimal_System"
            }));
            Assert.AreEqual("VisualEffectAsset", result.Value<string>("assetType"));
            Assert.IsTrue(System.IO.File.Exists(targetPath),
                $"template-instantiated asset should exist on disk: {targetPath}");

            // The instantiated asset is a real, describable VFX graph with contexts.
            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = targetPath, ["includeErrors"] = true }));
            Assert.Greater(after.Value<int>("contextCount"), 0,
                "the Minimal System template should contain contexts");
            var errors = (JArray)after["errors"];
            var blocking = errors.Where(e => (string)e["type"] == "Error").ToList();
            Assert.AreEqual(0, blocking.Count,
                $"a template-instantiated asset should have no Error-tier issues; got: {string.Join(", ", blocking.Select(e => (string)e["description"]))}");
        }

        [Test]
        public void Runtime_SetFloatOnExposedParameter_RoundTripsViaPublicApi()
        {
            string copy = CopyFixture("runtime");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter", ["assetPath"] = copy,
                ["parameterName"] = "Rate", ["type"] = "Float", ["value"] = 1.0f
            });

            var go = new UnityEngine.GameObject("VfxRigTest");
            try
            {
                go.AddComponent(Type.GetType("UnityEngine.VFX.VisualEffect, UnityEngine.VFXModule"));

                VfxGraphHandler.Runtime(new JObject
                {
                    ["op"] = "set_asset", ["gameObject"] = "VfxRigTest", ["assetPath"] = copy
                });
                JObject set = ToJObject(VfxGraphHandler.Runtime(new JObject
                {
                    ["op"] = "set_float", ["gameObject"] = "VfxRigTest",
                    ["name"] = "Rate", ["value"] = 7.5f
                }));

                Assert.IsTrue(set.Value<bool>("hasAsset"), "asset should be bound");
                Assert.IsTrue(set.Value<bool>("hasFloat"), "exposed Rate should be visible at runtime");
                Assert.AreEqual(7.5f, set.Value<float>("floatValue"), 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Settings_Get_ReportsFixedTimeStepProperty()
        {
            JObject result = ToJObject(VfxGraphHandler.Settings(new JObject { ["op"] = "get" }));
            JToken props = result["properties"];
            Assert.IsNotNull(props, "get should report a 'properties' block");
            Assert.IsNotNull(props["fixedTimeStep"],
                "VFXManager should expose a fixedTimeStep static property");
            Assert.IsNotNull(props["maxDeltaTime"],
                "VFXManager should expose a maxDeltaTime static property");
        }

        [Test]
        public void Settings_SetFixedTimeStep_RoundTripsViaReRead()
        {
            // Capture the original so we can restore it (these are project-global settings).
            JObject before = ToJObject(VfxGraphHandler.Settings(new JObject { ["op"] = "get" }));
            float original = before["properties"].Value<float>("fixedTimeStep");

            try
            {
                float target = original + 0.005f;
                JObject set = ToJObject(VfxGraphHandler.Settings(new JObject
                {
                    ["op"] = "set",
                    ["setting"] = "fixedTimeStep",
                    ["value"] = target
                }));
                Assert.AreEqual("property", set.Value<string>("via"),
                    "fixedTimeStep should write through the public static property");
                Assert.AreEqual(target, set.Value<float>("value"), 0.0001f);

                // Re-read confirms the change persisted on the canonical surface.
                JObject after = ToJObject(VfxGraphHandler.Settings(new JObject { ["op"] = "get" }));
                Assert.AreEqual(target, after["properties"].Value<float>("fixedTimeStep"), 0.0001f,
                    "re-read should reflect the new fixedTimeStep");
            }
            finally
            {
                VfxGraphHandler.Settings(new JObject
                {
                    ["op"] = "set",
                    ["setting"] = "fixedTimeStep",
                    ["value"] = original
                });
            }
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
