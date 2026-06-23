using System;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.PlayMode.Vfx
{
    /// <summary>
    /// Runtime (play-mode) verification of VfxGraphHandler's public-API path (vfx_runtime).
    /// These run in Play Mode, build a live VisualEffect GameObject from a committed fixture,
    /// advance frames, then drive + read the component through the handler exactly as the CLI
    /// does over the bridge. The oracle here is the LIVE component, not edit-mode describe —
    /// this is the project's runtime verification mode.
    ///
    /// This assembly (the shared all-platforms UnityCliBridge.PlayModeTests) cannot reference the
    /// Editor-only handler at compile time, so — like the UI PlayMode tests — it reaches the
    /// handler and the VisualEffect type via reflection and Assert.Ignore-s when the editor
    /// assembly or the VFX package is absent. Asset binding goes through the handler's set_asset
    /// op (which loads the asset by path internally), so no UnityEditor reference is needed.
    /// </summary>
    public class VfxRuntimePlayModeTests
    {
        private const string Fixture = "Assets/VfxFixtures/Minimal.vfx";
        private const string RigName = "VfxRuntimeRig";

        private GameObject _rig;

        [TearDown]
        public void TearDown()
        {
            if (_rig != null)
            {
                UnityEngine.Object.DestroyImmediate(_rig);
                _rig = null;
            }
            CleanupAuthored();
        }

        /// <summary>
        /// Harness smoke test: a live VisualEffect in play mode, bound + read back through the
        /// vfx_runtime handler. Proves the rig + play-mode loop + the handler's reflection path
        /// all work before any feature-specific runtime op is layered on top.
        /// </summary>
        [UnityTest]
        public IEnumerator Runtime_GetState_OnLiveVisualEffect_ReportsAssetAndPlayState()
        {
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            BuildRig();

            // Advance a handful of frames so the effect initializes and ticks.
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            JObject state = InvokeRuntime(new JObject
            {
                ["op"] = "get_state",
                ["gameObject"] = RigName
            });

            Assert.IsNull(state.Value<string>("error"), $"get_state should not error; got: {state}");
            Assert.IsTrue(state.Value<bool>("hasAsset"), "live VisualEffect should report its bound asset");
            Assert.AreEqual("Minimal", state.Value<string>("asset"), "asset name should round-trip");
            // The component is actually live: default play state, readable particle count.
            Assert.IsFalse(state.Value<bool>("pause"), "a freshly built effect should not be paused");
            Assert.AreEqual(1f, state.Value<float>("playRate"), 0.001f, "default play rate is 1");
            Assert.GreaterOrEqual(state.Value<int>("aliveParticleCount"), 0,
                "aliveParticleCount should be readable from the live component");
        }

        /// <summary>
        /// Per-instance Initial Event Name override (#6 runtime tail). The asset default is set
        /// authoring-side (vfx_apply set_initial_event_name); this is the live
        /// VisualEffect.initialEventName property that overrides it per component. Proves the
        /// runtime op writes the property and it round-trips through get_state.
        /// </summary>
        [UnityTest]
        public IEnumerator Runtime_SetInitialEventName_OverridesPerInstanceAndReadsBack()
        {
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            BuildRig();
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            // Override the per-instance initial event name to a sentinel value.
            JObject set = InvokeRuntime(new JObject
            {
                ["op"] = "set_initial_event_name",
                ["gameObject"] = RigName,
                ["name"] = "OnCustomStart"
            });
            Assert.IsNull(set.Value<string>("error"), $"set_initial_event_name should not error; got: {set}");
            Assert.AreEqual("OnCustomStart", set.Value<string>("initialEventName"),
                "the op result should echo the applied initial event name");

            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            // The override persists on the live component and is visible to a fresh get_state read.
            JObject state = InvokeRuntime(new JObject
            {
                ["op"] = "get_state",
                ["gameObject"] = RigName
            });
            Assert.AreEqual("OnCustomStart", state.Value<string>("initialEventName"),
                "the per-instance initialEventName override should round-trip via get_state");

            // Empty string is a distinct, valid value (suppresses auto-play) — proves it's a real
            // read/write of the property, not a constant echo.
            JObject cleared = InvokeRuntime(new JObject
            {
                ["op"] = "set_initial_event_name",
                ["gameObject"] = RigName,
                ["name"] = ""
            });
            Assert.IsNull(cleared.Value<string>("error"), $"clearing should not error; got: {cleared}");
            Assert.AreNotEqual("OnCustomStart", cleared.Value<string>("initialEventName"),
                "clearing the initial event name must change it away from the prior value");
        }

        /// <summary>
        /// Runtime SetTexture on an Object-typed exposed property (#9 runtime tail). Authors a copy
        /// of the fixture with an exposed Texture2D parameter wired into the Output's mainTexture slot
        /// (exposed params only survive into the runtime sheet when USED), binds it to a live effect,
        /// then sets a texture by path and confirms the round-trip via get_state (hasTexture +
        /// textureName). Edit-mode authoring goes through the handler; the bind/set/read is runtime.
        /// </summary>
        [UnityTest]
        public IEnumerator Runtime_SetTexture_BindsObjectPropertyAndReadsBack()
        {
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            const string texPath = "Assets/Materials/Dice/DiceTexture.png";
            string authored = AuthorTexturedFixture(texPath);
            if (authored == null)
            {
                yield break; // AuthorTexturedFixture already Assert.Ignore-d (missing inputs)
            }

            _rig = new GameObject(RigName);
            Type vfxType = FindType("UnityEngine.VFX.VisualEffect");
            if (vfxType == null)
            {
                Assert.Ignore("VisualEffect type not found (VFX package not installed).");
            }
            _rig.AddComponent(vfxType);

            JObject bound = InvokeRuntime(new JObject
            {
                ["op"] = "set_asset",
                ["gameObject"] = RigName,
                ["assetPath"] = authored
            });
            Assert.IsNull(bound.Value<string>("error"), $"set_asset should not error; got: {bound}");

            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            JObject set = InvokeRuntime(new JObject
            {
                ["op"] = "set_texture",
                ["gameObject"] = RigName,
                ["name"] = "Tex",
                ["assetPath"] = texPath
            });
            Assert.IsNull(set.Value<string>("error"), $"set_texture should not error; got: {set}");
            Assert.IsTrue(set.Value<bool>("hasTexture"),
                "the wired exposed Texture2D param should be present in the runtime property sheet");
            Assert.AreEqual("DiceTexture", set.Value<string>("textureName"),
                "GetTexture should report the bound texture asset name");

            CleanupAuthored();
        }

        // ---- Rig + handler plumbing (reflection — no compile-time Editor/VFX reference) -------

        private string _authoredFolder;

        /// <summary>
        /// Copy the fixture into a temp folder, add an exposed Texture2D parameter, and link it into
        /// the Output context's mainTexture slot so it is "used" (and therefore survives compilation
        /// into the runtime property sheet). Returns the authored asset path, or null after an Ignore.
        /// </summary>
        private string AuthorTexturedFixture(string texPath)
        {
            // Copy the fixture, then author against the copy via handler ops (which operate by path).
            _authoredFolder = "Assets/UnityCliBridgeTests/VfxRuntime";
            string dest = _authoredFolder + "/Textured.vfx";
            if (!CopyAsset(Fixture, dest))
            {
                Assert.Ignore($"Could not copy fixture {Fixture} (likely absent).");
            }

            // Exposed Texture2D parameter.
            JObject param = InvokeApply(new JObject
            {
                ["op"] = "add_parameter",
                ["assetPath"] = dest,
                ["parameterName"] = "Tex",
                ["type"] = "Texture2D"
            });
            Assert.IsNull(param.Value<string>("error"), $"add_parameter should not error; got: {param}");

            // Wire the parameter output into the Output context's mainTexture input (slot 0) so it is
            // used by the compiled graph.
            JObject link = InvokeApply(new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = dest,
                ["from"] = new JObject { ["node"] = "parameter", ["parameterIndex"] = 0, ["slot"] = 0 },
                ["to"] = new JObject
                {
                    ["node"] = "context",
                    ["contextType"] = "Output",
                    ["slot"] = 0
                }
            });
            Assert.IsNull(link.Value<string>("error"), $"link_slots should not error; got: {link}");
            return dest;
        }

        private void CleanupAuthored()
        {
            if (_authoredFolder != null)
            {
                DeleteAsset("Assets/UnityCliBridgeTests");
                _authoredFolder = null;
            }
        }

        // AssetDatabase is editor-only; reach it reflectively so this all-platforms assembly compiles.
        private static bool CopyAsset(string from, string to)
        {
            Type adb = FindType("UnityEditor.AssetDatabase");
            if (adb == null) return false;
            EnsureFolder("Assets/UnityCliBridgeTests");
            EnsureFolder("Assets/UnityCliBridgeTests/VfxRuntime");
            bool ok = (bool)adb.GetMethod("CopyAsset", new[] { typeof(string), typeof(string) })
                .Invoke(null, new object[] { from, to });
            if (ok)
            {
                ImportAsset(to);
            }
            return ok;
        }

        private static void EnsureFolder(string path)
        {
            Type adb = FindType("UnityEditor.AssetDatabase");
            if (adb == null) return;
            bool valid = (bool)adb.GetMethod("IsValidFolder").Invoke(null, new object[] { path });
            if (valid) return;
            int slash = path.LastIndexOf('/');
            adb.GetMethod("CreateFolder").Invoke(null,
                new object[] { path.Substring(0, slash), path.Substring(slash + 1) });
        }

        private static void ImportAsset(string path)
        {
            Type adb = FindType("UnityEditor.AssetDatabase");
            adb?.GetMethod("ImportAsset", new[] { typeof(string) })?.Invoke(null, new object[] { path });
        }

        private static void DeleteAsset(string path)
        {
            Type adb = FindType("UnityEditor.AssetDatabase");
            adb?.GetMethod("DeleteAsset", new[] { typeof(string) })?.Invoke(null, new object[] { path });
        }

        private static JObject InvokeApply(JObject parameters)
        {
            Type handlerType = FindType("UnityCliBridge.Handlers.VfxGraphHandler");
            if (handlerType == null)
            {
                Assert.Ignore("VfxGraphHandler not found (Editor assembly not loaded).");
            }
            MethodInfo method = handlerType.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "VfxGraphHandler.Apply not found");
            object result = method.Invoke(null, new object[] { parameters });
            return result as JObject ?? JObject.FromObject(result);
        }

        /// <summary>
        /// Create a named GameObject carrying a live VisualEffect bound to the fixture, binding the
        /// asset via the handler's set_asset op (which loads it through AssetDatabase internally).
        /// </summary>
        private void BuildRig()
        {
            Type vfxType = FindType("UnityEngine.VFX.VisualEffect");
            if (vfxType == null)
            {
                Assert.Ignore("VisualEffect type not found (VFX package not installed).");
            }

            _rig = new GameObject(RigName);
            _rig.AddComponent(vfxType);

            JObject bound = InvokeRuntime(new JObject
            {
                ["op"] = "set_asset",
                ["gameObject"] = RigName,
                ["assetPath"] = Fixture
            });
            if (bound.Value<string>("error") != null)
            {
                Assert.Ignore($"Could not bind fixture (likely not present): {bound.Value<string>("error")}");
            }
        }

        private static JObject InvokeRuntime(JObject parameters)
        {
            Type handlerType = FindType("UnityCliBridge.Handlers.VfxGraphHandler");
            if (handlerType == null)
            {
                Assert.Ignore("VfxGraphHandler not found (Editor assembly not loaded).");
            }

            MethodInfo method = handlerType.GetMethod("Runtime",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "VfxGraphHandler.Runtime not found");

            object result = method.Invoke(null, new object[] { parameters });
            return result as JObject ?? JObject.FromObject(result);
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
