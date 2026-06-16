using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    /// <summary>
    /// Contract tests for VfxGraphHandler. These cover argument validation and op
    /// routing, which run before any reflection into the (internal) VFX Graph API,
    /// so they do not require com.unity.visualeffectgraph to be installed in the
    /// test project. Full authoring behavior is validated against a live editor
    /// bridge with VFX Graph present.
    /// </summary>
    [TestFixture]
    public class VfxGraphHandlerTests
    {
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
    }
}
