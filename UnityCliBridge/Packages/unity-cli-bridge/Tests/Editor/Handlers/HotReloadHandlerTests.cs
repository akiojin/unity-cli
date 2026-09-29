using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Core;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests.Editor.Handlers
{
    public class HotReloadHandlerTests
    {
        [Test]
        public void HotReloadRoutesAreRegisteredWithoutMandatoryBackendDependency()
        {
            CollectionAssert.Contains(BridgeCommandRouter.RegisteredCommandTypes, "hot_reload");
            CollectionAssert.Contains(BridgeCommandRouter.RegisteredCommandTypes, "hot_reload_status");
            Assert.IsFalse(typeof(HotReloadHandler).Assembly.GetReferencedAssemblies()
                .Any(a => a.Name.StartsWith("FastScriptReload", StringComparison.Ordinal)));
        }

        [Test]
        public async Task MissingBackendNeverReportsAppliedOrChangesPlayState()
        {
            if (Type.GetType("UnityCliBridge.HotReload.FastScriptReloadAdapter, UnityCliBridge.HotReload.Editor") != null)
                Assert.Ignore("This case requires the optional package to be absent.");
            var wasPlaying = UnityEditor.EditorApplication.isPlaying;
            var status = JObject.FromObject(HotReloadHandler.Status());
            Assert.AreEqual(false, status["supported"].Value<bool>());
            Assert.AreEqual("HOT_RELOAD_PACKAGE_MISSING", status["code"].Value<string>());
            foreach (var action in new[] { "begin", "apply", "recover" })
            {
                var result = JObject.FromObject(await HotReloadHandler.Handle(new JObject { ["action"] = action }));
                Assert.AreEqual(false, result["success"].Value<bool>());
                Assert.AreEqual("HOT_RELOAD_PACKAGE_MISSING", result["code"].Value<string>());
                Assert.AreEqual(JTokenType.Null, result["appliedRevision"].Type);
                Assert.AreEqual(wasPlaying, UnityEditor.EditorApplication.isPlaying);
            }
            var routed = JObject.Parse(await BridgeCommandRouter.Handle(new UnityCliBridge.Models.Command
            {
                Id = "missing-backend", Type = "hot_reload", Parameters = new JObject { ["action"] = "begin" }
            }));
            Assert.AreEqual("error", routed["status"].Value<string>(), "CLI must receive a transport error, not success with nested error data.");
            Assert.AreEqual("HOT_RELOAD_PACKAGE_MISSING", routed["code"].Value<string>());
        }
    }
}
