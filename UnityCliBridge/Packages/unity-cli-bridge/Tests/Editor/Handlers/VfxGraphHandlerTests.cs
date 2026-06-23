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
        public void Apply_SetSlotValue_WithoutTarget_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_slot_value",
                ["assetPath"] = "Assets/Some.vfx",
                ["value"] = 1.0
            }), "target is required");
        }

        [Test]
        public void Apply_SetSlotValue_WithoutValue_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_slot_value",
                ["assetPath"] = "Assets/Some.vfx",
                ["target"] = new JObject { ["node"] = "context", ["contextType"] = "Init" }
            }), "value is required");
        }

        [Test]
        public void Apply_RemoveBlock_WithoutContextType_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "remove_block",
                ["assetPath"] = "Assets/Some.vfx",
                ["blockIndex"] = 0
            }), "contextType is required");
        }

        [Test]
        public void Apply_RemoveContext_WithoutContextTypeOrIndex_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "remove_context",
                ["assetPath"] = "Assets/Some.vfx"
            }), "contextType (or index) is required");
        }

        [Test]
        public void Apply_DeleteSystem_WithoutContextTypeOrIndex_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "delete_system",
                ["assetPath"] = "Assets/Some.vfx"
            }), "contextType (or index) is required");
        }

        [Test]
        public void Apply_UnlinkFlow_WithoutFrom_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "unlink_flow",
                ["assetPath"] = "Assets/Some.vfx",
                ["to"] = new JObject { ["index"] = 1 }
            }), "from is required");
        }

        [Test]
        public void Apply_RenameParameter_WithoutName_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "rename_parameter",
                ["assetPath"] = "Assets/Some.vfx",
                ["parameterIndex"] = 0
            }), "exposedName");
        }

        [Test]
        public void Apply_ReorderParameter_WithoutOrder_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "reorder_parameter",
                ["assetPath"] = "Assets/Some.vfx",
                ["parameterIndex"] = 0
            }), "order");
        }

        [Test]
        public void Apply_RenameCategory_WithoutNewCategory_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "rename_category",
                ["assetPath"] = "Assets/Some.vfx",
                ["category"] = "Tuning"
            }), "newCategory is required");
        }

        [Test]
        public void Apply_SetOperatorOperandType_WithoutType_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_operand_type",
                ["assetPath"] = "Assets/Some.vfx",
                ["operatorIndex"] = 0
            }), "operandType is required");
        }

        [Test]
        public void Apply_SetInitialEventName_WithoutEventName_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_initial_event_name",
                ["assetPath"] = "Assets/Some.vfx"
            }), "eventName is required");
        }

        [Test]
        public void Apply_AddCustomAttribute_WithoutName_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_custom_attribute",
                ["assetPath"] = "Assets/Some.vfx",
                ["attributeType"] = "Float"
            }), "attributeName is required");
        }

        [Test]
        public void Apply_AddCustomAttribute_WithoutType_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_custom_attribute",
                ["assetPath"] = "Assets/Some.vfx",
                ["attributeName"] = "Heat"
            }), "attributeType is required");
        }

        [Test]
        public void Apply_SetBlockEnabled_WithoutEnabled_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_enabled",
                ["assetPath"] = "Assets/Some.vfx",
                ["contextType"] = "Update",
                ["blockIndex"] = 0
            }), "enabled is required");
        }

        [Test]
        public void Apply_ReorderBlock_WithoutToIndex_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "reorder_block",
                ["assetPath"] = "Assets/Some.vfx",
                ["contextType"] = "Update",
                ["blockIndex"] = 0
            }), "toIndex is required");
        }

        [Test]
        public void Apply_MoveBlock_WithoutToContextType_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "move_block",
                ["assetPath"] = "Assets/Some.vfx",
                ["contextType"] = "Update",
                ["blockIndex"] = 0
            }), "toContextType is required");
        }

        [Test]
        public void Apply_UpdateStickyNote_WithoutIndex_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "update_sticky_note",
                ["assetPath"] = "Assets/Some.vfx",
                ["title"] = "x"
            }), "index is required");
        }

        [Test]
        public void Apply_RemoveStickyNote_WithoutIndex_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "remove_sticky_note",
                ["assetPath"] = "Assets/Some.vfx"
            }), "index is required");
        }

        [Test]
        public void Apply_SetContextSetting_WithoutContextTypeOrIndex_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_context_setting",
                ["assetPath"] = "Assets/Some.vfx",
                ["setting"] = "loopDuration",
                ["value"] = "Constant"
            }), "contextType (or index) is required");
        }

        [Test]
        public void Apply_SetContextSetting_WithoutSetting_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_context_setting",
                ["assetPath"] = "Assets/Some.vfx",
                ["contextType"] = "Update",
                ["value"] = true
            }), "setting is required");
        }

        [Test]
        public void Apply_SetOperatorSetting_WithoutSetting_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_setting",
                ["assetPath"] = "Assets/Some.vfx",
                ["operatorIndex"] = 0,
                ["value"] = "x"
            }), "setting is required");
        }

        [Test]
        public void Apply_SetOperatorSetting_WithoutValue_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_setting",
                ["assetPath"] = "Assets/Some.vfx",
                ["operatorIndex"] = 0,
                ["setting"] = "m_OperatorName"
            }), "value is required");
        }

        [Test]
        public void Apply_UnlinkSlots_WithoutTarget_ReturnsRequiredError()
        {
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "unlink_slots",
                ["assetPath"] = "Assets/Some.vfx"
            }), "target is required");
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

        [Test]
        public void Settings_PreferencesScope_Set_UnknownPref_ReturnsDescriptiveError()
        {
            AssertError(VfxGraphHandler.Settings(new JObject
            {
                ["op"] = "set",
                ["scope"] = "preferences",
                ["setting"] = "noSuchPref",
                ["value"] = true
            }), "Unknown VFX preference");
        }

#if UNITY_VFX_GRAPH
        // ---- Behavioral tests (require VFX Graph) --------------------------

        private const string Fixture = "Assets/VfxFixtures/Minimal.vfx";
        private const string ShaderIncludeFixture = "Assets/VfxFixtures/HLSLInclude.hlsl";
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
        public void ApplyLinkSlots_DescendsIntoDescriptorNamedSubSlot()
        {
            string copy = CopyFixture("subslotlink");
            // Volume (Sphere) has a compound `sphere` input slot (TSphere) whose children include the
            // descriptor-named scalar `radius`; an exposed Float parameter supplies the value.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator", ["assetPath"] = copy, ["operatorName"] = "Volume (Sphere)"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter", ["assetPath"] = copy, ["parameterName"] = "R", ["type"] = "Float"
            });

            // Link the parameter's float output into the sphere slot's `radius` CHILD sub-slot.
            JObject link = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = copy,
                ["from"] = new JObject { ["node"] = "parameter", ["parameterIndex"] = 0, ["slot"] = 0 },
                ["to"] = new JObject
                {
                    ["node"] = "operator",
                    ["operatorIndex"] = 0,
                    ["slot"] = 0,
                    ["subPath"] = new JArray("radius")
                }
            }));
            Assert.IsNull(link.Value<string>("error"), $"sub-slot link should not error; got: {link}");

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));

            // The link landed on the `radius` child, not the top-level `sphere` slot: the parameter's
            // output link resolves to a slot NAMED "radius", and the parent sphere slot stays unlinked.
            var paramOut = (JArray)((JArray)after["parameters"])[0]["outputSlots"];
            var links = (JArray)paramOut[0]["links"];
            Assert.AreEqual(1, links.Count, "the parameter output should have exactly one link");
            Assert.AreEqual("radius", links[0].Value<string>("name"),
                "the link should resolve to the descriptor-named 'radius' sub-slot");
            Assert.AreEqual("operator", links[0]["node"].Value<string>("kind"));

            var sphereSlot = (JArray)((JArray)after["operators"])
                .First(o => (string)o["type"] == "SphereVolume")["inputSlots"];
            Assert.IsFalse(sphereSlot[0].Value<bool>("hasLink"),
                "the top-level sphere slot itself should remain unlinked — the link is on its child");

            // Unlinking the same sub-slot clears it.
            JObject unlink = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "unlink_slots",
                ["assetPath"] = copy,
                ["target"] = new JObject
                {
                    ["node"] = "operator",
                    ["operatorIndex"] = 0,
                    ["slot"] = 0,
                    ["subPath"] = new JArray("radius")
                }
            }));
            Assert.IsNull(unlink.Value<string>("error"), $"sub-slot unlink should not error; got: {unlink}");

            JObject afterUnlink = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var paramOut2 = (JArray)((JArray)afterUnlink["parameters"])[0]["outputSlots"];
            Assert.AreEqual(0, ((JArray)paramOut2[0]["links"]).Count,
                "unlinking the sub-slot should clear the parameter output link");
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
        public void ApplySubgraphOperator_CreatesAssetAndReferencesItFromParent()
        {
            string copy = CopyFixture("subgraphop");
            string subPath = $"{TempFolder}/SubOp.vfxoperator";

            JObject created = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "create_subgraph_asset",
                ["subgraphPath"] = subPath,
                ["kind"] = "operator"
            }));
            Assert.AreEqual("VisualEffectSubgraphOperator", created.Value<string>("assetType"));
            Assert.IsTrue(System.IO.File.Exists(subPath),
                $"operator subgraph asset file should exist on disk: {subPath}");

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator",
                ["assetPath"] = copy,
                ["operatorName"] = "Empty Subgraph Operator"
            });

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_setting",
                ["assetPath"] = copy,
                ["operatorIndex"] = 0,
                ["setting"] = "m_Subgraph",
                ["value"] = subPath
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            JToken op = ((JArray)after["operators"])[0];
            Assert.AreEqual("VFXSubgraphOperator", op.Value<string>("type"));
            JToken subRef = op["settings"]?["m_Subgraph"];
            Assert.IsNotNull(subRef, "m_Subgraph should be reported in operator settings");
            Assert.AreEqual("VisualEffectSubgraphOperator", subRef.Value<string>("type"));
            Assert.AreEqual(subPath, subRef.Value<string>("assetPath"),
                "m_Subgraph.assetPath should resolve to the created operator subgraph asset");
            AssertNoErrorTier(after);
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
        public void ApplyDeleteSystem_RemovesAllContextsOfTheAddressedSystemOnly()
        {
            string copy = CopyFixture("delsystem");

            // Capture the original system's data id, then build a second, disjoint system.
            JObject baseline = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            int originalInitData = FindContext(baseline, "Init").Value<int>("dataInstanceId");

            VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_context", ["assetPath"] = copy, ["contextName"] = "Initialize Particle" });
            VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_context", ["assetPath"] = copy, ["contextName"] = "Update Particle" });
            VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_context", ["assetPath"] = copy, ["contextName"] = "Output Particle|Unlit|Quad" });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_flow", ["assetPath"] = copy,
                ["from"] = new JObject { ["index"] = 4 }, ["to"] = new JObject { ["index"] = 5 }
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_flow", ["assetPath"] = copy,
                ["from"] = new JObject { ["index"] = 5 }, ["to"] = new JObject { ["index"] = 6 }
            });

            JObject before = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual(7, before.Value<int>("contextCount"), "both systems should be present");

            // Delete the second system by addressing any one of its members (its Update at index 5).
            JObject del = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "delete_system", ["assetPath"] = copy, ["index"] = 5
            }));
            Assert.AreEqual(3, del.Value<int>("removedContexts"),
                "all three contexts of the addressed system should be removed in one op");
            Assert.AreEqual(4, del.Value<int>("remainingContexts"));

            // The original system (Spawner/Init/Update/Output) must survive intact and disjoint.
            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual(4, after.Value<int>("contextCount"));
            var survivingInit = FindContext(after, "Init");
            Assert.IsNotNull(survivingInit, "the original Init should remain");
            Assert.AreEqual(originalInitData, survivingInit.Value<int>("dataInstanceId"),
                "the surviving system must be the original one (same VFXData id)");
            Assert.IsNotNull(FindContext(after, "Spawner"), "the Spawner should remain");
            Assert.IsNotNull(FindContext(after, "Output"), "the original Output should remain");
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplySetContextSetting_WritesSimulationSpaceViaProperty()
        {
            string copy = CopyFixture("simspace");

            // space is a public property (not a [VFXSetting] field) on the particle data — the op's
            // property fallback should reach it and the describe oracle should surface it.
            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_context_setting", ["assetPath"] = copy,
                ["contextType"] = "Init", ["setting"] = "space", ["value"] = "World"
            }));
            StringAssert.Contains("property", result.Value<string>("via"),
                "simulation space resolves through the property fallback, not a [VFXSetting] field");
            Assert.AreEqual("World", result.Value<string>("value"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            // The whole particle system shares one space (it lives on the shared VFXData).
            Assert.AreEqual("World", FindContext(after, "Init").Value<string>("simulationSpace"));
            Assert.AreEqual("World", FindContext(after, "Update").Value<string>("simulationSpace"));
            Assert.AreEqual("World", FindContext(after, "Output").Value<string>("simulationSpace"));
            AssertNoErrorTier(after);

            // Round-trip back to Local to prove it's a real read/write, not a constant.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_context_setting", ["assetPath"] = copy,
                ["contextType"] = "Init", ["setting"] = "space", ["value"] = "Local"
            });
            JObject relocal = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual("Local", FindContext(relocal, "Init").Value<string>("simulationSpace"));
        }

        [Test]
        public void ApplyAddCustomAttribute_CreatesAndReferencesInSetBlock()
        {
            string copy = CopyFixture("customattr");

            // Two custom attributes of different signatures.
            JObject heat = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_custom_attribute", ["assetPath"] = copy,
                ["attributeName"] = "Heat", ["attributeType"] = "Float",
                ["description"] = "per-particle heat"
            }));
            Assert.AreEqual("Float", heat.Value<string>("attributeType"));
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_custom_attribute", ["assetPath"] = copy,
                ["attributeName"] = "Swirl", ["attributeType"] = "Vector3"
            });

            // Describe surfaces both on the new customAttributes oracle.
            JObject afterAdd = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var attrs = (JArray)afterAdd["customAttributes"];
            Assert.AreEqual(2, afterAdd.Value<int>("customAttributeCount"));
            var heatDesc = attrs.FirstOrDefault(a => (string)a["attributeName"] == "Heat");
            Assert.IsNotNull(heatDesc, "Heat should be listed");
            Assert.AreEqual("Float", (string)heatDesc["type"]);
            Assert.AreEqual("per-particle heat", (string)heatDesc["description"]);
            Assert.IsTrue(attrs.Any(a => (string)a["attributeName"] == "Swirl"),
                "Swirl should be listed");

            // Reference the custom attribute: a SetAttribute block repointed to "Heat".
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Init", ["blockName"] = "|Set|_Color"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting", ["assetPath"] = copy,
                ["contextType"] = "Init", ["blockIndex"] = 0,
                ["setting"] = "attribute", ["value"] = "Heat"
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var initBlock = ((JArray)FindContext(after, "Init")["blocks"])[0];
            Assert.AreEqual("Heat", (string)initBlock["settings"]["attribute"],
                "the SetAttribute block should now drive the custom Heat attribute");
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyAddCustomAttribute_RejectsBuiltInAndDuplicateAndBadType()
        {
            string copy = CopyFixture("customattrbad");

            // A built-in attribute name can't be re-declared as custom.
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_custom_attribute", ["assetPath"] = copy,
                ["attributeName"] = "position", ["attributeType"] = "Vector3"
            }), "Failed to add custom attribute");

            // An unknown type lists the valid signatures.
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_custom_attribute", ["assetPath"] = copy,
                ["attributeName"] = "Foo", ["attributeType"] = "Matrix"
            }), "Unknown attribute type");

            // Declaring the same custom name twice fails the second time.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_custom_attribute", ["assetPath"] = copy,
                ["attributeName"] = "Heat", ["attributeType"] = "Float"
            });
            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_custom_attribute", ["assetPath"] = copy,
                ["attributeName"] = "Heat", ["attributeType"] = "Float"
            }), "Failed to add custom attribute");
        }

        [Test]
        public void ApplyAttribute_VariadicChannelsAndSourceVsCurrent()
        {
            string copy = CopyFixture("attrchannels");

            // Set Position is a variadic attribute → exposes a `channels` flags setting and a
            // `Source` (Slot/Source) setting; both compose via the existing set_block_setting.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Init", ["blockName"] = "|Set|_Position"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting", ["assetPath"] = copy,
                ["contextType"] = "Init", ["blockIndex"] = 0,
                ["setting"] = "channels", ["value"] = "XY"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting", ["assetPath"] = copy,
                ["contextType"] = "Init", ["blockIndex"] = 0,
                ["setting"] = "Source", ["value"] = "Source"
            });

            // A Get Position operator exposes the Source-vs-Current `location` setting.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator", ["assetPath"] = copy,
                ["operatorName"] = "Get|_Position"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_setting", ["assetPath"] = copy,
                ["operatorIndex"] = 0, ["setting"] = "location", ["value"] = "Source"
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var block = ((JArray)FindContext(after, "Init")["blocks"])[0];
            Assert.AreEqual("XY", (string)block["settings"]["channels"],
                "the variadic channels mask should be narrowed to XY");
            Assert.AreEqual("Source", (string)block["settings"]["Source"],
                "the Set block should read from Source");
            var op = ((JArray)after["operators"])[0];
            Assert.AreEqual("Source", (string)op["settings"]["location"],
                "the Get operator should read the Source (initial) attribute value");
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyCustomHLSL_BlockFunctionSelectorReshapesSlots()
        {
            string copy = CopyFixture("hlslfunc");

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockName"] = "Custom HLSL"
            });
            // Two functions with distinct signatures; the block defaults to the first (FuncA → _k).
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockIndex"] = 0, ["setting"] = "m_HLSLCode",
                ["value"] =
                    "void FuncA(inout VFXAttributes a, in float k){a.velocity *= k;}\n" +
                    "void FuncB(inout VFXAttributes a, in float3 dir, in float s){a.position += dir*s;}"
            });

            JObject before = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var blockBefore = ((JArray)FindContext(before, "Update")["blocks"])[0];
            CollectionAssert.AreEqual(new[] { "_k" },
                ((JArray)blockBefore["inputSlots"]).Select(s => (string)s["name"]).ToList(),
                "default should expose FuncA's single float input");
            var availBefore = blockBefore["settings"]["m_AvailableFunction"];
            Assert.AreEqual("FuncA", (string)availBefore["selection"]);
            CollectionAssert.AreEquivalent(new[] { "FuncA", "FuncB" },
                ((JArray)availBefore["values"]).Select(v => (string)v).ToList());

            // Select FuncB — the block reshapes to FuncB's (_dir, _s) inputs.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockIndex"] = 0,
                ["setting"] = "m_AvailableFunction", ["value"] = "FuncB"
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var blockAfter = ((JArray)FindContext(after, "Update")["blocks"])[0];
            CollectionAssert.AreEqual(new[] { "_dir", "_s" },
                ((JArray)blockAfter["inputSlots"]).Select(s => (string)s["name"]).ToList(),
                "selecting FuncB should reshape the slots to its (float3, float) inputs");
            Assert.AreEqual("FuncB", (string)blockAfter["settings"]["m_AvailableFunction"]["selection"]);
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyCustomHLSL_OperatorInlineSourceAndFunctionSelector()
        {
            string copy = CopyFixture("hlslop");

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator", ["assetPath"] = copy, ["operatorName"] = "Custom HLSL"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_setting", ["assetPath"] = copy, ["operatorIndex"] = 0,
                ["setting"] = "m_HLSLCode",
                ["value"] =
                    "float OpA(in float k){return k*2.0f;}\n" +
                    "float3 OpB(in float3 v, in float s){return v*s;}"
            });

            JObject before = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var opBefore = ((JArray)before["operators"])[0];
            CollectionAssert.AreEqual(new[] { "k" },
                ((JArray)opBefore["inputSlots"]).Select(s => (string)s["name"]).ToList(),
                "the operator should resync its slots to OpA's single input");

            // The operator's selector setting is plural (m_AvailableFunctions).
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_setting", ["assetPath"] = copy, ["operatorIndex"] = 0,
                ["setting"] = "m_AvailableFunctions", ["value"] = "OpB"
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var opAfter = ((JArray)after["operators"])[0];
            CollectionAssert.AreEqual(new[] { "v", "s" },
                ((JArray)opAfter["inputSlots"]).Select(s => (string)s["name"]).ToList(),
                "selecting OpB should reshape the operator's inputs to (float3, float)");
            Assert.AreEqual("OpB", (string)opAfter["settings"]["m_AvailableFunctions"]["selection"]);
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyCustomHLSL_BlockExternalShaderFileDrivesSlots()
        {
            if (!System.IO.File.Exists(ShaderIncludeFixture))
            {
                Assert.Ignore($"ShaderInclude fixture not present: {ShaderIncludeFixture}");
            }
            string copy = CopyFixture("hlslfile");

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockName"] = "Custom HLSL"
            });
            // Point the block at an external .hlsl (imported as a ShaderInclude). Its single function
            // Squash(inout VFXAttributes, in float factor) drives the block's input slots.
            JObject set = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockIndex"] = 0,
                ["setting"] = "m_ShaderFile", ["value"] = ShaderIncludeFixture
            }));
            Assert.AreEqual("ShaderInclude", (string)set["value"]["type"],
                "m_ShaderFile should resolve the .hlsl as a ShaderInclude asset");

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var block = ((JArray)FindContext(after, "Update")["blocks"])[0];
            CollectionAssert.AreEqual(new[] { "_factor" },
                ((JArray)block["inputSlots"]).Select(s => (string)s["name"]).ToList(),
                "the block's slots should derive from the external file's function signature");
            Assert.AreEqual(ShaderIncludeFixture, (string)block["settings"]["m_ShaderFile"]["assetPath"]);
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyEvents_GpuEventChainTriggerToSecondSystem()
        {
            string copy = CopyFixture("gpuevent");

            // A Trigger Event block in Update emits a GPU event (output slot `evt`).
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockName"] = "Trigger Event|On Die"
            });
            // A GPU Event context (contextType "SpawnerGPU") receives it via its `evt` input slot.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_context", ["assetPath"] = copy, ["contextName"] = "GPU Event"
            });
            JObject linked = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_slots", ["assetPath"] = copy,
                ["from"] = new JObject
                {
                    ["node"] = "block", ["contextType"] = "Update", ["blockIndex"] = 0, ["slot"] = 0
                },
                ["to"] = new JObject { ["node"] = "context", ["contextType"] = "SpawnerGPU", ["slot"] = 0 }
            }));
            Assert.IsNull(linked["error"], "linking the Trigger evt output to the GPU Event input should succeed");

            // The GPU Event context flows into a second particle system.
            VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_context", ["assetPath"] = copy, ["contextName"] = "Initialize Particle" });
            VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_context", ["assetPath"] = copy, ["contextName"] = "Output Particle|Unlit|Quad" });
            // Contexts: 0 Spawner,1 Init,2 Update,3 Output,4 SpawnerGPU,5 Init2,6 Output2.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_flow", ["assetPath"] = copy,
                ["from"] = new JObject { ["index"] = 4 }, ["to"] = new JObject { ["index"] = 5 }
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var contexts = (JArray)after["contexts"];
            var gpu = contexts.First(c => (string)c["contextType"] == "SpawnerGPU");
            Assert.AreEqual(5, ((JArray)gpu["outputs"])[0].Value<int>("index"),
                "the GPU Event context should flow into the second system's Initialize");
            // The Trigger block's evt output should resolve a link to the GPU Event context.
            var triggerBlock = ((JArray)FindContext(after, "Update")["blocks"])
                .First(b => ((string)b["name"]).Contains("Trigger"));
            var evtLinks = (JArray)((JArray)triggerBlock["outputSlots"])[0]["links"];
            Assert.AreEqual(1, evtLinks.Count, "the Trigger evt output should be linked");
            Assert.AreEqual("context", (string)evtLinks[0]["node"]["kind"]);
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyEvents_SpawnPayloadAndOutputEventContext()
        {
            string copy = CopyFixture("eventpayload");

            // A Set SpawnEvent <Attribute> block on the Spawner carries an event payload.
            JObject payload = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Spawner", ["blockName"] = "Set SpawnEvent Color"
            }));
            Assert.IsNull(payload["error"], "Set SpawnEvent Color should be a valid Spawner block");

            // An Output Event context (CPU callback endpoint) authors headless.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_context", ["assetPath"] = copy, ["contextName"] = "Output Event"
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var spawnerBlocks = (JArray)FindContext(after, "Spawner")["blocks"];
            Assert.IsTrue(spawnerBlocks.Any(b => ((string)b["name"]).Contains("SpawnEvent")),
                "the Spawner should carry a Set SpawnEvent payload block");
            Assert.IsNotNull(((JArray)after["contexts"]).FirstOrDefault(
                    c => (string)c["contextType"] == "OutputEvent"),
                "an Output Event context should have been added");
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplySetInitialEventName_RoundTripsThroughDescribe()
        {
            string copy = CopyFixture("initevent");

            // The committed fixture defaults to OnPlay.
            JObject baseline = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual("OnPlay", baseline.Value<string>("initialEventName"));

            JObject set = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_initial_event_name", ["assetPath"] = copy, ["eventName"] = "Launch"
            }));
            Assert.AreEqual("Launch", set.Value<string>("initialEventName"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual("Launch", after.Value<string>("initialEventName"),
                "the asset's default Initial Event Name should round-trip through describe");
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyOperator_CascadedAddRemoveInputsAndOperandType()
        {
            string copy = CopyFixture("cascaded");

            // Add is a cascaded numeric operator — starts with 2 float operands (a, b).
            VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_operator", ["assetPath"] = copy, ["operatorName"] = "Add" });

            JObject baseline = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual(2, ((JArray)((JArray)baseline["operators"])[0]["inputSlots"]).Count,
                "Add should start with 2 operands");

            // Grow to 3 (default type), then to 4 with an explicit Vector3 operand.
            JObject add1 = ToJObject(VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_operator_input", ["assetPath"] = copy, ["operatorIndex"] = 0 }));
            Assert.AreEqual(3, add1.Value<int>("operandCount"));
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator_input", ["assetPath"] = copy,
                ["operatorIndex"] = 0, ["operandType"] = "Vector3"
            });

            JObject grown = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual(4, ((JArray)((JArray)grown["operators"])[0]["inputSlots"]).Count,
                "two add_operator_input calls should yield 4 operands");

            // Retype the whole operator to Vector2 — every operand slot re-types.
            JObject retyped = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_operand_type", ["assetPath"] = copy,
                ["operatorIndex"] = 0, ["operandType"] = "Vector2"
            }));
            Assert.AreEqual("all-operands", retyped.Value<string>("via"));

            JObject afterType = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var slots = (JArray)((JArray)afterType["operators"])[0]["inputSlots"];
            CollectionAssert.AreEqual(Enumerable.Repeat("Vector2", 4).ToList(),
                slots.Select(s => (string)s["valueType"]).ToList(),
                "every operand slot should now report the Vector2 value type");
            AssertNoErrorTier(afterType);

            // Shrink back to 3, then refuse to drop below the minimum of 2.
            JObject removed = ToJObject(VfxGraphHandler.Apply(new JObject
            { ["op"] = "remove_operator_input", ["assetPath"] = copy, ["operatorIndex"] = 0 }));
            Assert.AreEqual(3, removed.Value<int>("operandCount"));
            VfxGraphHandler.Apply(new JObject
            { ["op"] = "remove_operator_input", ["assetPath"] = copy, ["operatorIndex"] = 0 });
            AssertError(VfxGraphHandler.Apply(new JObject
            { ["op"] = "remove_operator_input", ["assetPath"] = copy, ["operatorIndex"] = 0 }),
                "minimum");
        }

        [Test]
        public void ApplyOperator_UniformOperandTypeAndCascadeRejected()
        {
            string copy = CopyFixture("uniform");

            // Sine is a uniform numeric operator: one shared operand type, no add/remove input.
            VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_operator", ["assetPath"] = copy, ["operatorName"] = "Sine" });

            JObject set = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_operand_type", ["assetPath"] = copy,
                ["operatorIndex"] = 0, ["operandType"] = "Vector3"
            }));
            Assert.AreEqual("uniform", set.Value<string>("via"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual("Vector3",
                (string)((JArray)((JArray)after["operators"])[0]["inputSlots"])[0]["valueType"],
                "the uniform operand type should re-type the input slot to Vector3");
            AssertNoErrorTier(after);

            // A uniform operator has no cascaded inputs — add/remove are rejected with a clear error.
            AssertError(VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_operator_input", ["assetPath"] = copy, ["operatorIndex"] = 0 }),
                "not a cascaded operator");
        }

        [Test]
        public void ApplyBlackboard_RenameCategoryReorderDuplicate()
        {
            string copy = CopyFixture("blackboard");

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter", ["assetPath"] = copy,
                ["parameterName"] = "Rate", ["type"] = "Float", ["value"] = 1, ["category"] = "Tuning"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter", ["assetPath"] = copy,
                ["parameterName"] = "Tint", ["type"] = "Color", ["value"] = new JArray { 1, 0, 0, 1 },
                ["category"] = "Tuning"
            });

            // Rename param 0's exposed name.
            JObject renamed = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "rename_parameter", ["assetPath"] = copy,
                ["parameterIndex"] = 0, ["exposedName"] = "SpawnRate"
            }));
            Assert.AreEqual("SpawnRate", renamed.Value<string>("exposedName"));

            // Move param 1 to a different category; reorder param 0.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_parameter_category", ["assetPath"] = copy,
                ["parameterIndex"] = 1, ["category"] = "Visuals"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "reorder_parameter", ["assetPath"] = copy,
                ["parameterIndex"] = 0, ["order"] = 5
            });

            // Rename the remaining "Tuning" category (only param 0 is still in it).
            JObject cat = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "rename_category", ["assetPath"] = copy,
                ["category"] = "Tuning", ["newCategory"] = "Spawning"
            }));
            Assert.AreEqual(1, cat.Value<int>("parametersMoved"));

            // Duplicate param 0 — the clone inherits type/category, order+1, name "(1)".
            JObject dup = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "duplicate_parameter", ["assetPath"] = copy, ["parameterIndex"] = 0
            }));
            Assert.AreEqual("SpawnRate (1)", dup.Value<string>("exposedName"));
            Assert.AreEqual(3, dup.Value<int>("parameterCount"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var ps = (JArray)after["parameters"];
            var p0 = ps.First(p => (string)p["exposedName"] == "SpawnRate");
            Assert.AreEqual("Spawning", (string)p0["category"]);
            Assert.AreEqual(5, p0.Value<int>("order"));
            Assert.AreEqual("Visuals", (string)ps.First(p => (string)p["exposedName"] == "Tint")["category"]);
            var clone = ps.First(p => (string)p["exposedName"] == "SpawnRate (1)");
            Assert.AreEqual("Spawning", (string)clone["category"]);
            Assert.AreEqual(6, clone.Value<int>("order"), "the clone's order should be source order + 1");
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyRenameParameter_PreservesSlotLink()
        {
            string copy = CopyFixture("renamelink");

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter", ["assetPath"] = copy,
                ["parameterName"] = "Scale", ["type"] = "Float", ["value"] = 2
            });
            VfxGraphHandler.Apply(new JObject
            { ["op"] = "add_operator", ["assetPath"] = copy, ["operatorName"] = "Sine" });
            // Link the parameter's output into the Sine operator's input slot.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_slots", ["assetPath"] = copy,
                ["from"] = new JObject { ["node"] = "parameter", ["parameterIndex"] = 0, ["slot"] = 0 },
                ["to"] = new JObject { ["node"] = "operator", ["operatorIndex"] = 0, ["slot"] = 0 }
            });

            JObject before = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.IsTrue(((JArray)((JArray)before["operators"])[0]["inputSlots"])[0].Value<bool>("hasLink"),
                "precondition: the operator input should be linked to the parameter");

            // Rename the parameter — the link must survive (same VFXParameter, new name).
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "rename_parameter", ["assetPath"] = copy,
                ["parameterIndex"] = 0, ["exposedName"] = "Magnitude"
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual("Magnitude",
                (string)((JArray)after["parameters"])[0]["exposedName"]);
            Assert.IsTrue(((JArray)((JArray)after["operators"])[0]["inputSlots"])[0].Value<bool>("hasLink"),
                "the slot link should survive the rename");
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyContext_UnlinkAndRelinkSingleFlowEdge()
        {
            string copy = CopyFixture("unlinkflow");

            // Fixture flow: Spawner(0) → Init(1) → Update(2) → Output(3). Drop only Update→Output.
            JObject unlinked = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "unlink_flow", ["assetPath"] = copy,
                ["from"] = new JObject { ["index"] = 2 }, ["to"] = new JObject { ["index"] = 3 }
            }));
            Assert.IsNull(unlinked["error"]);

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var ctx = (JArray)after["contexts"];
            Assert.AreEqual(0, ((JArray)ctx[2]["outputs"]).Count, "Update→Output edge should be gone");
            Assert.AreEqual(0, ((JArray)ctx[3]["inputs"]).Count, "Output should have no input edge");
            // Sibling edges intact: Spawner→Init→Update still chained.
            Assert.AreEqual(2, ((JArray)ctx[1]["outputs"])[0].Value<int>("index"),
                "Init→Update should be untouched");
            Assert.AreEqual(0, ((JArray)ctx[1]["inputs"])[0].Value<int>("index"),
                "Spawner→Init should be untouched");
            AssertNoErrorTier(after);

            // Relink Update→Output restores the chain.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "link_flow", ["assetPath"] = copy,
                ["from"] = new JObject { ["index"] = 2 }, ["to"] = new JObject { ["index"] = 3 }
            });
            JObject relinked = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual(3, ((JArray)((JArray)relinked["contexts"])[2]["outputs"])[0].Value<int>("index"),
                "relinking should restore Update→Output");
            AssertNoErrorTier(relinked);
        }

        [Test]
        public void ApplyContext_SpawnConstantDurationSlotWritable()
        {
            string copy = CopyFixture("spawnslot");

            // Switching loopDuration to Constant exposes a LoopDuration value slot on the Spawner.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_context_setting", ["assetPath"] = copy,
                ["contextType"] = "Spawner", ["setting"] = "loopDuration", ["value"] = "Constant"
            });

            JObject withSlot = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var spawner = FindContext(withSlot, "Spawner");
            Assert.AreEqual(1, ((JArray)spawner["inputSlots"]).Count,
                "Constant loop duration should expose one value slot");

            // The per-mode slot takes a set_slot_value write.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_slot_value", ["assetPath"] = copy,
                ["target"] = new JObject
                {
                    ["node"] = "context", ["contextType"] = "Spawner", ["slot"] = 0
                },
                ["value"] = 3.5
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual(3.5,
                ((JArray)FindContext(after, "Spawner")["inputSlots"])[0].Value<double>("value"), 1e-4,
                "the Constant duration slot should hold the written value");
            AssertNoErrorTier(after);
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

        [Test]
        public void ApplyAttributes_ComposesViaSetAttributeBlockAndGetAttributeOperator()
        {
            // Attributes (#7) Pass-1 compose-confirm: no new op needed — the library exposes
            // `|Set|_<Attribute>` (all map to a single SetAttribute block class parameterized by
            // an `attribute` [VFXSetting]) and `Get|_<Attribute>` operators (VFXAttributeParameter).
            // Composition modes (Overwrite/Add/Multiply/Blend) are a [VFXSetting] writable via the
            // existing set_block_setting.
            string copy = CopyFixture("attr");

            // Add Set Color to Init.
            JObject add = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = copy,
                ["contextType"] = "Init",
                ["blockName"] = "|Set|_Color"
            }));
            Assert.AreEqual("SetAttribute", add.Value<string>("addedBlock"),
                "all Set <Attribute> descriptors instantiate the single SetAttribute block class");

            // Flip the composition mode from default (Overwrite) → Add.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting",
                ["assetPath"] = copy,
                ["contextType"] = "Init",
                ["blockIndex"] = 0,
                ["setting"] = "Composition",
                ["value"] = "Add"
            });

            // Add Get|_Position operator.
            JObject addOp = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator",
                ["assetPath"] = copy,
                ["operatorName"] = "Get|_Position"
            }));
            Assert.AreEqual("VFXAttributeParameter", addOp.Value<string>("addedOperator"),
                "Get|_<Attribute> operators all instantiate VFXAttributeParameter");

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));

            // Block surfaces with the expected runtime type + driven settings.
            JToken init = FindContext(after, "Init");
            var blocks = (JArray)init["blocks"];
            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual("SetAttribute", blocks[0].Value<string>("type"));
            JToken settings = blocks[0]["settings"];
            Assert.AreEqual("color", settings.Value<string>("attribute"),
                "the Set Color descriptor pre-wires the `attribute` setting to 'color'");
            Assert.AreEqual("Add", settings.Value<string>("Composition"),
                "set_block_setting should have toggled Composition from Overwrite to Add");

            // Operator surfaces with the expected runtime type.
            var operators = (JArray)after["operators"];
            Assert.AreEqual(1, operators.Count);
            Assert.AreEqual("VFXAttributeParameter", operators[0].Value<string>("type"));

            // Tier-2 oracle: no validator errors after composing the attribute pieces.
            var errors = (JArray)after["errors"];
            var blocking = errors.Where(e => (string)e["type"] == "Error").ToList();
            Assert.AreEqual(0, blocking.Count,
                $"attribute composition should not register Error-tier issues; got: {string.Join(", ", blocking.Select(e => (string)e["description"]))}");
        }

        [Test]
        public void Settings_PreferencesScope_Get_ReportsInstancingAndExperimentalOperator()
        {
            JObject result = ToJObject(VfxGraphHandler.Settings(
                new JObject { ["op"] = "get", ["scope"] = "preferences" }));
            Assert.AreEqual("preferences", result.Value<string>("scope"));
            JToken props = result["properties"];
            Assert.IsNotNull(props, "preferences get should report a 'properties' block");
            Assert.IsNotNull(props["instancingEnabled"],
                "VFXViewPreference should expose instancingEnabled");
            Assert.IsNotNull(props["displayExperimentalOperator"],
                "VFXViewPreference should expose displayExperimentalOperator");
            Assert.IsNotNull(props["multithreadUpdateEnabled"],
                "VFXViewPreference should expose multithreadUpdateEnabled");
        }

        [Test]
        public void Settings_PreferencesScope_SetInstancingEnabled_RoundTripsViaReRead()
        {
            // EditorPrefs are per-machine; capture and restore the original.
            JObject before = ToJObject(VfxGraphHandler.Settings(
                new JObject { ["op"] = "get", ["scope"] = "preferences" }));
            bool original = before["properties"].Value<bool>("instancingEnabled");

            try
            {
                bool target = !original;
                JObject set = ToJObject(VfxGraphHandler.Settings(new JObject
                {
                    ["op"] = "set",
                    ["scope"] = "preferences",
                    ["setting"] = "instancingEnabled",
                    ["value"] = target
                }));
                Assert.AreEqual("preferences", set.Value<string>("scope"));
                Assert.AreEqual(target, set.Value<bool>("value"),
                    "set should echo the new value read back via the canonical property");
                Assert.AreEqual("VFX.InstancingEnabled", set.Value<string>("editorPrefsKey"),
                    "the resolved EditorPrefs key should match the package's constant");

                JObject after = ToJObject(VfxGraphHandler.Settings(
                    new JObject { ["op"] = "get", ["scope"] = "preferences" }));
                Assert.AreEqual(target, after["properties"].Value<bool>("instancingEnabled"),
                    "re-read should reflect the new instancingEnabled value");
            }
            finally
            {
                VfxGraphHandler.Settings(new JObject
                {
                    ["op"] = "set",
                    ["scope"] = "preferences",
                    ["setting"] = "instancingEnabled",
                    ["value"] = original
                });
            }
        }

        [Test]
        public void Settings_PreferencesScope_AllowShaderExternalization_RoundTripsViaEditorPrefs()
        {
            // allowShaderExternalization has no public getter property on VFXViewPreference (only a
            // key constant + private field), so it exercises the EditorPrefs-direct read path.
            JObject before = ToJObject(VfxGraphHandler.Settings(
                new JObject { ["op"] = "get", ["scope"] = "preferences" }));
            Assert.IsNotNull(before["properties"]["allowShaderExternalization"],
                "preferences get should now expose allowShaderExternalization");
            bool original = before["properties"].Value<bool>("allowShaderExternalization");

            try
            {
                bool target = !original;
                JObject set = ToJObject(VfxGraphHandler.Settings(new JObject
                {
                    ["op"] = "set",
                    ["scope"] = "preferences",
                    ["setting"] = "allowShaderExternalization",
                    ["value"] = target
                }));
                Assert.AreEqual(target, set.Value<bool>("value"),
                    "set should echo the new value read back via EditorPrefs");
                Assert.AreEqual("VFX.allowShaderExternalization", set.Value<string>("editorPrefsKey"),
                    "the resolved EditorPrefs key should match the package's constant");

                JObject after = ToJObject(VfxGraphHandler.Settings(
                    new JObject { ["op"] = "get", ["scope"] = "preferences" }));
                Assert.AreEqual(target, after["properties"].Value<bool>("allowShaderExternalization"),
                    "re-read should reflect the new allowShaderExternalization value");
            }
            finally
            {
                VfxGraphHandler.Settings(new JObject
                {
                    ["op"] = "set",
                    ["scope"] = "preferences",
                    ["setting"] = "allowShaderExternalization",
                    ["value"] = original
                });
            }
        }

        [Test]
        public void ApplySetSlotValue_SetsScalarRateOnSpawnerBlock()
        {
            string copy = CopyFixture("slotscalar");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = copy,
                ["contextType"] = "Spawner",
                ["blockName"] = "Constant Spawn Rate"
            });

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_slot_value",
                ["assetPath"] = copy,
                ["target"] = new JObject
                {
                    ["node"] = "block",
                    ["contextType"] = "Spawner",
                    ["blockIndex"] = 0,
                    ["slot"] = 0
                },
                ["value"] = 42.5
            }));
            Assert.AreEqual("Rate", result["target"].Value<string>("slotName"));
            Assert.AreEqual(42.5f, result.Value<float>("value"), 1e-4f);

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var slots = (JArray)((JArray)FindContext(after, "Spawner")["blocks"])[0]["inputSlots"];
            JToken rate = slots.First(s => (string)s["name"] == "Rate");
            Assert.AreEqual(42.5f, rate.Value<float>("value"), 1e-4f,
                "the Rate slot value should round-trip through describe");
        }

        [Test]
        public void ApplySetSlotValue_WalksCompoundSubPathIntoBoundsCenter()
        {
            string copy = CopyFixture("slotcompound");

            // Whole sub-vector: bounds.center = (1,2,3)
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_slot_value",
                ["assetPath"] = copy,
                ["target"] = new JObject
                {
                    ["node"] = "context",
                    ["contextType"] = "Init",
                    ["slot"] = 0
                },
                ["subPath"] = new JArray("center"),
                ["value"] = new JArray(1, 2, 3)
            });

            // Nested leaf: bounds.size.x = 9 — must leave center untouched.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_slot_value",
                ["assetPath"] = copy,
                ["target"] = new JObject
                {
                    ["node"] = "context",
                    ["contextType"] = "Init",
                    ["slot"] = 0
                },
                ["subPath"] = new JArray("size", "x"),
                ["value"] = 9
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var slots = (JArray)FindContext(after, "Init")["inputSlots"];
            JToken bounds = slots.First(s => (string)s["name"] == "bounds")["value"];
            Assert.AreEqual(1f, bounds["center"].Value<float>("x"), 1e-4f);
            Assert.AreEqual(2f, bounds["center"].Value<float>("y"), 1e-4f);
            Assert.AreEqual(3f, bounds["center"].Value<float>("z"), 1e-4f);
            Assert.AreEqual(9f, bounds["size"].Value<float>("x"), 1e-4f,
                "nested subPath write should update size.x");
            Assert.AreEqual(1f, bounds["size"].Value<float>("y"), 1e-4f,
                "nested subPath write should leave the other components untouched");
        }

        [Test]
        public void ApplySetSlotValue_SetsObjectTypedTextureSlotByAssetPath()
        {
            const string texPath = "Assets/Materials/Dice/DiceTexture.png";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(texPath) == null)
            {
                Assert.Ignore($"Texture fixture {texPath} not present in this project.");
            }
            string copy = CopyFixture("slotobject");

            // The Output context's mainTexture slot is Object-typed (Texture2D) and defaults to null,
            // so its CLR type can't be read from the (null) current value — this exercises the
            // declared-type fallback (SlotClrType) plus the asset-path load path in CoerceToType.
            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_slot_value",
                ["assetPath"] = copy,
                ["target"] = new JObject
                {
                    ["node"] = "context",
                    ["contextType"] = "Output",
                    ["slot"] = 0
                },
                ["value"] = texPath
            }));
            Assert.IsNull(result.Value<string>("error"), $"set_slot_value should not error; got: {result}");
            Assert.AreEqual("mainTexture", result["target"].Value<string>("slotName"));
            Assert.AreEqual("DiceTexture", result["value"].Value<string>("name"),
                "the op result should report the bound texture asset");

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var slots = (JArray)FindContext(after, "Output")["inputSlots"];
            JToken main = slots.First(s => (string)s["name"] == "mainTexture")["value"];
            Assert.AreEqual("DiceTexture", main.Value<string>("name"),
                "the mainTexture slot value should round-trip through describe as the bound asset");
            Assert.AreEqual("Texture2D", main.Value<string>("type"));
        }

        [Test]
        public void ApplySetSlotValue_RecompilesCleanWithNoErrors()
        {
            string copy = CopyFixture("slotclean");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = copy,
                ["contextType"] = "Spawner",
                ["blockName"] = "Constant Spawn Rate"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_slot_value",
                ["assetPath"] = copy,
                ["target"] = new JObject
                {
                    ["node"] = "block",
                    ["contextType"] = "Spawner",
                    ["blockIndex"] = 0,
                    ["slot"] = 0
                },
                ["value"] = 17.0
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var errors = ((JArray)after["errors"])
                .Where(e => (string)e["type"] == "Error").ToList();
            Assert.AreEqual(0, errors.Count,
                "graph should recompile with zero Error-tier entries after set_slot_value");
        }

        [Test]
        public void ApplyUnlinkSlots_RemovesOperatorLinkReportedByDescribe()
        {
            string copy = CopyFixture("unlinkslots");
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

            // Sanity: the link is present before we remove it.
            JObject linked = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.IsTrue(((JArray)linked["operators"])[1]["inputSlots"][0].Value<bool>("hasLink"),
                "precondition: operator 1 input slot should be linked");

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "unlink_slots",
                ["assetPath"] = copy,
                ["target"] = new JObject { ["node"] = "operator", ["operatorIndex"] = 1, ["slot"] = 0 }
            }));
            Assert.AreEqual(1, result.Value<int>("linksRemoved"));
            Assert.AreEqual(0, result.Value<int>("remainingLinks"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var operators = (JArray)after["operators"];
            Assert.IsFalse(operators[1]["inputSlots"][0].Value<bool>("hasLink"),
                "operator 1 input slot should no longer report a link");
            Assert.AreEqual(0, ((JArray)operators[0]["outputSlots"][0]["links"]).Count,
                "operator 0 output should have no links after unlink");
            var errors = ((JArray)after["errors"])
                .Where(e => (string)e["type"] == "Error").ToList();
            Assert.AreEqual(0, errors.Count,
                "graph should recompile with zero Error-tier entries after unlink_slots");
        }

        [Test]
        public void ApplySetOperatorSetting_ChangesCustomHlslOperatorAndReshapesSlots()
        {
            string copy = CopyFixture("opsetting");
            JObject added = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_operator",
                ["assetPath"] = copy,
                ["operatorName"] = "Custom HLSL"
            }));
            Assert.AreEqual("CustomHLSL", added.Value<string>("addedOperator"));

            // String setting round-trips through describe (operators[].settings is the new oracle).
            JObject named = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_setting",
                ["assetPath"] = copy,
                ["operatorIndex"] = 0,
                ["setting"] = "m_OperatorName",
                ["value"] = "MyScale"
            }));
            Assert.AreEqual("MyScale", named.Value<string>("value"));

            // Writing the HLSL source resyncs the operator's input ports to the function signature.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_operator_setting",
                ["assetPath"] = copy,
                ["operatorIndex"] = 0,
                ["setting"] = "m_HLSLCode",
                ["value"] = "float MyScale(in float k){return k*2.0f;}"
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            JToken op = ((JArray)after["operators"])[0];
            Assert.AreEqual("MyScale", op["settings"]?["m_OperatorName"]?.ToString(),
                "operator name setting should round-trip through describe");
            var inputNames = ((JArray)op["inputSlots"]).Select(s => (string)s["name"]).ToList();
            CollectionAssert.AreEqual(new[] { "k" }, inputNames,
                "the single-argument HLSL function should reshape the input slots to one 'k' port");
            var errors = ((JArray)after["errors"])
                .Where(e => (string)e["type"] == "Error").ToList();
            Assert.AreEqual(0, errors.Count,
                "graph should recompile with zero Error-tier entries after set_operator_setting");
        }

        [Test]
        public void ApplyRemoveBlock_RemovesBlockAndCleansLinks()
        {
            string copy = CopyFixture("removeblock");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockName"] = "Turbulence"
            });

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "remove_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockIndex"] = 0
            }));
            Assert.AreEqual("Turbulence", result.Value<string>("removedBlock"));
            Assert.AreEqual(0, result.Value<int>("remainingBlocks"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual(0, ((JArray)FindContext(after, "Update")["blocks"]).Count);
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyRemoveOperator_RemovesLinkedOperatorWithoutDanglingLinks()
        {
            string copy = CopyFixture("removeop");
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
                ["op"] = "link_slots", ["assetPath"] = copy,
                ["from"] = new JObject { ["node"] = "operator", ["operatorIndex"] = 0, ["slot"] = 0 },
                ["to"] = new JObject { ["node"] = "operator", ["operatorIndex"] = 1, ["slot"] = 0 }
            });

            // Remove operator 1 (the link target); operator 0's output link must be cleaned up.
            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "remove_operator", ["assetPath"] = copy, ["operatorIndex"] = 1
            }));
            Assert.AreEqual(1, result.Value<int>("remainingOperators"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual(1, after.Value<int>("operatorCount"));
            Assert.AreEqual(0, ((JArray)((JArray)after["operators"])[0]["outputSlots"][0]["links"]).Count,
                "the surviving operator's output should have no dangling link to the removed one");
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyRemoveParameter_RemovesParameterFromGraph()
        {
            string copy = CopyFixture("removeparam");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter", ["assetPath"] = copy,
                ["parameterName"] = "Rate", ["type"] = "Float"
            });

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "remove_parameter", ["assetPath"] = copy, ["parameterIndex"] = 0
            }));
            Assert.AreEqual(0, result.Value<int>("remainingParameters"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual(0, ((JArray)after["parameters"]).Count);
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyRemoveContext_RemovesOutputAndUnlinksFlow()
        {
            string copy = CopyFixture("removectx");

            JObject result = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "remove_context", ["assetPath"] = copy, ["contextType"] = "Output"
            }));
            Assert.AreEqual("Output", result.Value<string>("removedContextType"));
            Assert.AreEqual(3, result.Value<int>("remainingContexts"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual(3, after.Value<int>("contextCount"));
            Assert.IsNull(FindContext(after, "Output"), "Output context should be gone");
            // Update's downstream flow edge to the removed Output must be cleaned up.
            Assert.AreEqual(0, ((JArray)FindContext(after, "Update")["outputs"]).Count,
                "Update should have no dangling flow edge after its Output was removed");
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplySetContextSetting_WritesContextAndDataLevelSettings()
        {
            string copy = CopyFixture("ctxsetting");

            // Context-level enum (Spawn loop), context-level bool (Update toggle).
            JObject spawn = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_context_setting", ["assetPath"] = copy,
                ["contextType"] = "Spawner", ["setting"] = "loopDuration", ["value"] = "Constant"
            }));
            Assert.AreEqual("context", spawn.Value<string>("via"));

            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_context_setting", ["assetPath"] = copy,
                ["contextType"] = "Update", ["setting"] = "ageParticles", ["value"] = false
            });

            // Data-level int (Init Capacity lives on VFXDataParticle, reached via GetData()).
            JObject cap = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_context_setting", ["assetPath"] = copy,
                ["contextType"] = "Init", ["setting"] = "capacity", ["value"] = 256
            }));
            Assert.AreEqual("data", cap.Value<string>("via"),
                "Init capacity should resolve through the context's particle data");

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            Assert.AreEqual("Constant", FindContext(after, "Spawner")["settings"]?["loopDuration"]?.ToString());
            Assert.IsFalse(FindContext(after, "Update")["settings"].Value<bool>("ageParticles"));
            Assert.AreEqual(256, FindContext(after, "Init")["settings"].Value<int>("capacity"));
            AssertNoErrorTier(after);
        }

        [Test]
        public void ApplyAddParameter_CoversTypeMatrixConstantAndRange()
        {
            string copy = CopyFixture("paramtypes");

            // A spread of value kinds: bool, int (+range), Vector3 (array), Color (array),
            // an Object type (Texture2D, default null), and a non-exposed constant Float.
            VfxGraphHandler.Apply(NewParam(copy, "B", "Bool", new JValue(true)));
            VfxGraphHandler.Apply(WithRange(NewParam(copy, "I", "Int", new JValue(7)), 0, 10));
            VfxGraphHandler.Apply(NewParam(copy, "V3", "Vector3", new JArray(1, 2, 3)));
            VfxGraphHandler.Apply(NewParam(copy, "C", "Color", new JArray(1, 0, 0, 1)));
            VfxGraphHandler.Apply(NewParam(copy, "T2", "Texture2D", null));
            JObject constResult = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_parameter", ["assetPath"] = copy,
                ["parameterName"] = "K", ["type"] = "Float",
                ["value"] = 9.0, ["exposed"] = false
            }));
            Assert.IsFalse(constResult.Value<bool>("exposed"));

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var byName = ((JArray)after["parameters"])
                .ToDictionary(p => (string)p["exposedName"], p => p);

            Assert.AreEqual("Boolean", byName["B"]["parameterType"].ToString());
            Assert.AreEqual("Vector3", byName["V3"]["parameterType"].ToString());
            Assert.AreEqual(1f, byName["V3"]["value"].Value<float>("x"), 1e-4f);
            Assert.AreEqual("Color", byName["C"]["parameterType"].ToString());
            Assert.AreEqual("Texture2D", byName["T2"]["parameterType"].ToString());

            // Int range round-trips via valueFilter=Range + min/max.
            Assert.AreEqual("Range", byName["I"]["valueFilter"].ToString());
            Assert.AreEqual(0, byName["I"].Value<int>("min"));
            Assert.AreEqual(10, byName["I"].Value<int>("max"));

            // Constant (non-exposed) is still a real graph node.
            Assert.IsFalse(byName["K"].Value<bool>("exposed"));

            AssertNoErrorTier(after);
        }

        private static JObject NewParam(string asset, string name, string type, JToken value)
        {
            var o = new JObject
            {
                ["op"] = "add_parameter", ["assetPath"] = asset,
                ["parameterName"] = name, ["type"] = type
            };
            if (value != null) o["value"] = value;
            return o;
        }

        private static JObject WithRange(JObject param, double min, double max)
        {
            param["min"] = min;
            param["max"] = max;
            return param;
        }

        [Test]
        public void ApplyStickyNote_UpdateEditsFieldsAndRemoveShrinksArray()
        {
            string copy = CopyFixture("stickyedit");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_sticky_note", ["assetPath"] = copy,
                ["title"] = "A", ["contents"] = "first", ["colorTheme"] = 1
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_sticky_note", ["assetPath"] = copy,
                ["title"] = "B", ["contents"] = "second", ["colorTheme"] = 2
            });

            // Update note 0: change title + contents only; B must stay intact.
            JObject upd = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "update_sticky_note", ["assetPath"] = copy,
                ["index"] = 0, ["title"] = "A-edited", ["contents"] = "changed"
            }));
            CollectionAssert.AreEquivalent(new[] { "title", "contents" },
                ((JArray)upd["changed"]).Select(t => t.ToString()).ToArray());

            JObject afterUpdate = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var notes = (JArray)afterUpdate["stickyNotes"];
            Assert.AreEqual("A-edited", notes[0].Value<string>("title"));
            Assert.AreEqual("changed", notes[0].Value<string>("contents"));
            Assert.AreEqual("B", notes[1].Value<string>("title"), "sibling note should be untouched");

            // Remove note 0: B slides into index 0.
            JObject rem = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "remove_sticky_note", ["assetPath"] = copy, ["index"] = 0
            }));
            Assert.AreEqual(1, rem.Value<int>("remaining"));

            JObject afterRemove = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var remaining = (JArray)afterRemove["stickyNotes"];
            Assert.AreEqual(1, remaining.Count);
            Assert.AreEqual("B", remaining[0].Value<string>("title"));
            AssertNoErrorTier(afterRemove);
        }

        [Test]
        public void ApplyBlock_EnableReorderAndMoveAcrossContexts()
        {
            string copy = CopyFixture("blockmove");
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockName"] = "Turbulence"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockName"] = "Gravity"
            });

            // Disable block 0 (Turbulence).
            JObject dis = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_enabled", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockIndex"] = 0, ["enabled"] = false
            }));
            Assert.IsFalse(dis.Value<bool>("enabled"));

            JObject afterDisable = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var blocks = (JArray)FindContext(afterDisable, "Update")["blocks"];
            Assert.IsFalse(blocks[0].Value<bool>("enabled"), "Turbulence should be disabled");
            Assert.IsTrue(blocks[1].Value<bool>("enabled"), "Gravity should stay enabled");

            // Reorder Turbulence (0) to index 1; the disabled state travels with the block.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "reorder_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockIndex"] = 0, ["toIndex"] = 1
            });
            JObject afterReorder = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            var reordered = (JArray)FindContext(afterReorder, "Update")["blocks"];
            Assert.AreEqual("Gravity", reordered[0].Value<string>("name"));
            Assert.AreEqual("Turbulence", reordered[1].Value<string>("name"));
            Assert.IsFalse(reordered[1].Value<bool>("enabled"),
                "the disabled state should follow Turbulence to its new position");

            // Add a Set Velocity block (valid in Init+Update) and move it to Init.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockName"] = "|Set|_Velocity"
            });
            JObject moved = ToJObject(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "move_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockIndex"] = 2, ["toContextType"] = "Init"
            }));
            Assert.AreEqual("Init", moved.Value<string>("toContextType"));

            JObject afterMove = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var initBlocks = (JArray)FindContext(afterMove, "Init")["blocks"];
            Assert.AreEqual(1, initBlocks.Count, "Init should now own the moved block");
            Assert.AreEqual(2, ((JArray)FindContext(afterMove, "Update")["blocks"]).Count,
                "Update should be back down to its two original blocks");
            AssertNoErrorTier(afterMove);
        }

        [Test]
        public void ApplyMoveBlock_RejectsIncompatibleTargetContext()
        {
            string copy = CopyFixture("blockmovebad");
            // Gravity is an Update-only force block; moving it to Initialize must be rejected.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockName"] = "Gravity"
            });

            AssertError(VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "move_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockIndex"] = 0, ["toContextType"] = "Init"
            }), "not compatible");

            // The block must still be in Update (rejection left the graph intact).
            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy }));
            Assert.AreEqual(1, ((JArray)FindContext(after, "Update")["blocks"]).Count);
        }

        [Test]
        public void DescribeGraph_IncludeErrors_SurfacesRealErrorTierIssue()
        {
            // Negative control for the Tier-2 oracle: prove includeErrors does NOT always return 0 —
            // a deliberately-broken graph must report an Error-tier entry, so every other test's
            // AssertNoErrorTier (and the "recompiles clean" claims) actually mean something.
            string copy = CopyFixture("errorctl");

            // A Custom HLSL block whose source has no valid function is a hard Error
            // ("No valid HLSL function has been provided"), not a Warning.
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "add_block", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockName"] = "Custom HLSL"
            });
            VfxGraphHandler.Apply(new JObject
            {
                ["op"] = "set_block_setting", ["assetPath"] = copy,
                ["contextType"] = "Update", ["blockIndex"] = 0,
                ["setting"] = "m_HLSLCode", ["value"] = "this is not valid hlsl @@@ {"
            });

            JObject after = ToJObject(VfxGraphHandler.DescribeGraph(
                new JObject { ["assetPath"] = copy, ["includeErrors"] = true }));
            var errors = (JArray)after["errors"];
            var errorTier = errors.Where(e => (string)e["type"] == "Error").ToList();
            Assert.Greater(errorTier.Count, 0,
                "the Tier-2 oracle must surface at least one Error-tier entry for a broken HLSL graph " +
                "(if this fails, includeErrors is a no-op and every AssertNoErrorTier is meaningless)");

            // And the benign NeedsRecording warning must NOT be miscounted as an Error — proving the
            // type filter the other tests rely on is discriminating, not blanket.
            Assert.IsTrue(errors.Any(e => (string)e["type"] == "Warning"),
                "the benign NeedsRecording warning should still be reported as a Warning, not an Error");
        }

        private static void AssertNoErrorTier(JObject describeResult)
        {
            var errors = ((JArray)describeResult["errors"])
                .Where(e => (string)e["type"] == "Error").ToList();
            Assert.AreEqual(0, errors.Count,
                "graph should recompile with zero Error-tier entries");
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
