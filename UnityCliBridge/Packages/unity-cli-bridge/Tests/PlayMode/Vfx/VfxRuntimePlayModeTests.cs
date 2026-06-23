using System;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.VFX;

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
        private GameObject _camera;

        [TearDown]
        public void TearDown()
        {
            if (_rig != null)
            {
                UnityEngine.Object.DestroyImmediate(_rig);
                _rig = null;
            }
            if (_camera != null)
            {
                UnityEngine.Object.DestroyImmediate(_camera);
                _camera = null;
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

        /// <summary>
        /// Runtime SetMesh on an Object-typed exposed property (#9 runtime tail). Authors a copy of the
        /// fixture whose output is swapped to an Unlit Mesh output, with an exposed Mesh parameter wired
        /// into that output's slot-0 mesh (so it survives compilation), binds it, then sets a Mesh asset
        /// and confirms the round-trip via get_state (hasMesh + meshName).
        /// </summary>
        [UnityTest]
        public IEnumerator Runtime_SetMesh_BindsObjectPropertyAndReadsBack()
        {
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            string meshPath;
            string authored = AuthorMeshFixture(out meshPath);
            if (authored == null)
            {
                yield break; // AuthorMeshFixture already Assert.Ignore-d
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
                ["op"] = "set_mesh",
                ["gameObject"] = RigName,
                ["name"] = "Msh",
                ["assetPath"] = meshPath
            });
            Assert.IsNull(set.Value<string>("error"), $"set_mesh should not error; got: {set}");
            Assert.IsTrue(set.Value<bool>("hasMesh"),
                "the wired exposed Mesh param should be present in the runtime property sheet");
            Assert.AreEqual("RuntimeTestMesh", set.Value<string>("meshName"),
                "GetMesh should report the bound mesh asset name");

            CleanupAuthored();
        }

        /// <summary>
        /// Output Event CPU callback (#6 runtime tail). Authors a graph with an Output Event context
        /// (named "OnTest") flow-linked from the Spawner, subscribes a C# handler to the live
        /// VisualEffect.outputEventReceived, plays, and asserts the callback fires with the matching
        /// event nameId — the CPU round-trip from the GPU/spawn machinery back into managed code.
        /// This is why the VFX PlayMode tests have their own asmdef that references the VFX runtime:
        /// the event delegate (Action&lt;VFXOutputEventArgs&gt;) can't be built by pure reflection.
        /// </summary>
        [UnityTest]
        public IEnumerator Runtime_OutputEvent_FiresCpuCallbackForNamedEvent()
        {
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            string authored = AuthorOutputEventFixture();
            if (authored == null)
            {
                yield break; // already Assert.Ignore-d
            }

            // A camera that frames the rig — output events are dispatched during the VFX render/update
            // tick, which a culled (unrendered) effect skips.
            _camera = new GameObject("VfxRuntimeCam");
            var cam = _camera.AddComponent<Camera>();
            _camera.transform.position = new Vector3(0f, 0f, -5f);
            _camera.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor;

            _rig = new GameObject(RigName);
            _rig.transform.position = Vector3.zero;
            var vfx = _rig.AddComponent<VisualEffect>();

            int total = 0;
            int wantId = Shader.PropertyToID("OnTest");
            bool sawWanted = false;
            void OnOutputEvent(VFXOutputEventArgs args)
            {
                total++;
                if (args.nameId == wantId)
                {
                    sawWanted = true;
                }
            }
            vfx.outputEventReceived += OnOutputEvent;

            try
            {
                // Bind via the handler (loads the asset by path) and play long enough for the spawn
                // machinery to emit the output event back to the CPU.
                JObject bound = InvokeRuntime(new JObject
                {
                    ["op"] = "set_asset",
                    ["gameObject"] = RigName,
                    ["assetPath"] = authored
                });
                Assert.IsNull(bound.Value<string>("error"), $"set_asset should not error; got: {bound}");

                // Let Reinit settle, then advance frames; the camera renders the effect so the VFX
                // manager processes it and dispatches the Output Event back to the CPU. Simulate()
                // additionally guarantees the spawn machinery ticks even if rendering is throttled.
                yield return null;
                for (int i = 0; i < 240 && !sawWanted; i++)
                {
                    vfx.Simulate(0.05f, 1);
                    yield return null;
                }
            }
            finally
            {
                vfx.outputEventReceived -= OnOutputEvent;
            }

            Assert.Greater(total, 0, "the Output Event context should have fired at least one CPU callback");
            Assert.IsTrue(sawWanted,
                "the callback should report the 'OnTest' event nameId from the Output Event context");

            CleanupAuthored();
        }

        // ---- Rig + handler plumbing (reflection — no compile-time Editor reference) -----------

        private string _authoredFolder;

        /// <summary>
        /// Copy the fixture, give the Spawner a Constant Spawn Rate so the system runs, and add an
        /// Output Event context ("OnTest") flow-linked from the Spawner. Returns the asset path or null.
        /// </summary>
        private string AuthorOutputEventFixture()
        {
            if (FindType("UnityCliBridge.Handlers.VfxGraphHandler") == null)
            {
                Assert.Ignore("VfxGraphHandler not found (Editor assembly not loaded).");
            }

            _authoredFolder = "Assets/UnityCliBridgeTests/VfxRuntime";
            string dest = _authoredFolder + "/OutEvent.vfx";
            if (!CopyAsset(Fixture, dest))
            {
                Assert.Ignore($"Could not copy fixture {Fixture} (likely absent).");
            }

            JObject rate = InvokeApply(new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = dest,
                ["contextType"] = "Spawner",
                ["blockName"] = "Constant Spawn Rate"
            });
            Assert.IsNull(rate.Value<string>("error"), $"add_block (spawn rate) should not error; got: {rate}");

            JObject add = InvokeApply(new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = dest,
                ["contextName"] = "Output Event",
                ["settings"] = new JObject { ["eventName"] = "OnTest" }
            });
            Assert.IsNull(add.Value<string>("error"), $"add_context (output event) should not error; got: {add}");

            JObject link = InvokeApply(new JObject
            {
                ["op"] = "link_flow",
                ["assetPath"] = dest,
                ["from"] = new JObject { ["contextType"] = "Spawner" },
                ["to"] = new JObject { ["contextType"] = "OutputEvent" }
            });
            Assert.IsNull(link.Value<string>("error"), $"link_flow should not error; got: {link}");
            return dest;
        }

        /// <summary>
        /// Copy the fixture, swap its Quad output for an Unlit Mesh output (so contextType "Output"
        /// unambiguously resolves to it), add an exposed Mesh parameter wired into the mesh output's
        /// slot-0 mesh, and create a small Mesh asset to bind. Returns the authored asset path (and the
        /// mesh asset path via out), or null after an Ignore.
        /// </summary>
        private string AuthorMeshFixture(out string meshPath)
        {
            meshPath = null;
            Type handlerType = FindType("UnityCliBridge.Handlers.VfxGraphHandler");
            if (handlerType == null)
            {
                Assert.Ignore("VfxGraphHandler not found (Editor assembly not loaded).");
            }

            _authoredFolder = "Assets/UnityCliBridgeTests/VfxRuntime";
            string dest = _authoredFolder + "/MeshOut.vfx";
            if (!CopyAsset(Fixture, dest))
            {
                Assert.Ignore($"Could not copy fixture {Fixture} (likely absent).");
            }

            // Swap the Quad output for a Mesh output so a single "Output" context remains.
            JObject rm = InvokeApply(new JObject
            {
                ["op"] = "remove_context",
                ["assetPath"] = dest,
                ["contextType"] = "Output"
            });
            Assert.IsNull(rm.Value<string>("error"), $"remove_context should not error; got: {rm}");

            JObject addOut = InvokeApply(new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = dest,
                ["contextName"] = "Output Particle|Unlit|Mesh",
                ["linkFrom"] = "Update"
            });
            Assert.IsNull(addOut.Value<string>("error"), $"add_context (mesh output) should not error; got: {addOut}");

            // Exposed Mesh parameter, wired into the mesh output's slot-0 mesh (makes it used).
            JObject param = InvokeApply(new JObject
            {
                ["op"] = "add_parameter",
                ["assetPath"] = dest,
                ["parameterName"] = "Msh",
                ["type"] = "Mesh"
            });
            Assert.IsNull(param.Value<string>("error"), $"add_parameter should not error; got: {param}");

            JObject link = InvokeApply(new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = dest,
                ["from"] = new JObject { ["node"] = "parameter", ["parameterIndex"] = 0, ["slot"] = 0 },
                ["to"] = new JObject { ["node"] = "context", ["contextType"] = "Output", ["slot"] = 0 }
            });
            Assert.IsNull(link.Value<string>("error"), $"link_slots should not error; got: {link}");

            meshPath = _authoredFolder + "/RuntimeTestMesh.asset";
            if (!CreateMeshAsset(meshPath, "RuntimeTestMesh"))
            {
                Assert.Ignore("Could not create a Mesh asset (AssetDatabase unavailable).");
            }
            return dest;
        }

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

        // Create a tiny named Mesh asset on disk (content irrelevant — only identity is asserted).
        private static bool CreateMeshAsset(string path, string name)
        {
            Type adb = FindType("UnityEditor.AssetDatabase");
            if (adb == null) return false;
            EnsureFolder("Assets/UnityCliBridgeTests");
            EnsureFolder("Assets/UnityCliBridgeTests/VfxRuntime");
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            adb.GetMethod("CreateAsset", new[] { typeof(UnityEngine.Object), typeof(string) })
                .Invoke(null, new object[] { mesh, path });
            ImportAsset(path);
            return true;
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
