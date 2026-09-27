using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityCliBridge.Handlers
{
    /// <summary>Scene-scoped native bake jobs. Acceptance is distinct from verified completion.</summary>
    [InitializeOnLoad]
    public static class BakeHandler
    {
        const string SessionKey = "UnityCliBridge.BakeJobs";
        static readonly Dictionary<string, JObject> Jobs = new Dictionary<string, JObject>();
        static Job active;
        static readonly Type Legacy = typeof(Editor).Assembly.GetType("UnityEditor.AI.NavMeshBuilder");

        sealed class Job
        {
            public JObject Result;
            public Scene Scene;
            public string Target;
            public Component Surface;
            public NavMeshData Data;
            public AsyncOperation Operation;
            public bool Started;
            public int AgentType;
            public bool LightingCompleted;
            public double StartedAt;
            public double CompletedAt;
            public Dictionary<string, long> Before;
        }

        static BakeHandler()
        {
            var saved = SessionState.GetString(SessionKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                try
                {
                    foreach (JObject result in JArray.Parse(saved))
                    {
                        if ((string)result["status"] == "running")
                        {
                            result["status"] = "failed";
                            result["phase"] = "interrupted";
                            result["code"] = "DOMAIN_RELOAD";
                            result["reason"] = "Domain reload interrupted verification; start a new bake.";
                        }
                        Jobs[(string)result["jobId"]] = result;
                    }
                }
                catch (Exception) { SessionState.EraseString(SessionKey); }
            }
            EditorApplication.update += Update;
            Lightmapping.bakeCompleted += () => { if (active != null && active.Target == "lighting") active.LightingCompleted = true; };
            AssemblyReloadEvents.beforeAssemblyReload += () => { if (active != null) Fail("DOMAIN_RELOAD", "Domain reload interrupted the bake."); };
        }

        public static object StartBake(JObject args)
        {
            string target = args?["target"]?.Value<string>();
            if (!new[] { "lighting", "navmesh-legacy", "navmesh-surface", "occlusion" }.Contains(target))
                return Error("INVALID_TARGET", "Choose lighting, navmesh-legacy, navmesh-surface, or occlusion.");
            if (active != null || Lightmapping.isRunning || StaticOcclusionCulling.isRunning || LegacyRunning())
                return Error("BAKE_BUSY", "Another bake is running.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return Error("EDITOR_BUSY", "Wait for edit mode with compilation and asset imports complete.");
            var scene = SceneManager.GetActiveScene();
            string path = args?["scenePath"]?.Value<string>();
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".unity", StringComparison.Ordinal) || path != scene.path || !File.Exists(path))
                return Error("SCENE_MISMATCH", "scenePath must identify the saved active scene under Assets/.");
            if (SceneManager.sceneCount != 1 || EditorSceneManager.previewSceneCount != 0)
                return Error("MULTIPLE_SCENES", "Load only the target scene before baking.");
            if (scene.isDirty) return Error("SCENE_DIRTY", "Save the scene before baking.");
            Component surface = null;
            if (target == "navmesh-surface")
            {
                var surfacePath = args?["surfacePath"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(surfacePath)) return Error("SURFACE_PATH_REQUIRED", "Provide the target NavMeshSurface GameObject hierarchy path.");
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.AI.Navigation.NavMeshSurface")).FirstOrDefault(t => t != null);
                if (type == null) return Error("PACKAGE_MISSING", "Install com.unity.ai.navigation to bake NavMeshSurface.");
                var matches = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren(type, true)).Where(c => Hierarchy(c.transform) == surfacePath.Trim('/')).ToArray();
                if (matches.Length != 1) return Error("SURFACE_NOT_FOUND", "surfacePath must identify exactly one NavMeshSurface in the target scene.");
                surface = matches[0];
                if (!surface.gameObject.activeInHierarchy || (surface is Behaviour b && !b.enabled)) return Error("SURFACE_DISABLED", "Enable the target NavMeshSurface before baking.");
                if (type.GetMethod("UpdateNavMesh", new[] { typeof(NavMeshData) }) == null || type.GetMethod("AddData") == null || type.GetMethod("RemoveData") == null)
                    return Error("UNSUPPORTED_BACKEND", "This AI Navigation version does not expose the required bake API.");
            }
            var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>()).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var terrains = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>()).Where(t => t.enabled && t.terrainData != null).ToArray();
            bool colliders = target == "navmesh-surface" && scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>()).Any(c => c.enabled && !c.isTrigger);
            if (renderers.Length == 0 && terrains.Length == 0 && !colliders) return Error("NO_GEOMETRY", "The target scene contains no active geometry to bake.");
            if (target.StartsWith("navmesh", StringComparison.Ordinal))
            {
                var surfaceType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.AI.Navigation.NavMeshSurface")).FirstOrDefault(t => t != null);
                var surfaceData = surfaceType == null ? Array.Empty<Object>() : scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren(surfaceType, true)).Select(s => surfaceType.GetProperty("navMeshData")?.GetValue(s) as Object).Where(d => d != null).ToArray();
                var savedData = AssetDatabase.GetDependencies(path, true).Where(p => AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(NavMeshData));
                if ((target == "navmesh-legacy" && surfaceData.Length > 0) || (target == "navmesh-surface" && savedData.Except(surfaceData.Select(AssetDatabase.GetAssetPath)).Any()))
                    return Error("MIXED_NAVMESH_BACKENDS", "Remove navigation data owned by the other backend before baking; verification requires isolated navigation data.");
            }
            if (target == "navmesh-legacy" && (Legacy == null || Legacy.GetMethod("BuildNavMeshAsync") == null || Legacy.GetProperty("isRunning") == null))
                return Error("UNSUPPORTED_BACKEND", "Legacy NavMesh baking is unavailable in this Unity version; use navmesh-surface.");
            if (target == "lighting" && !Lightmapping.bakedGI) return Error("BAKED_GI_DISABLED", "Enable baked global illumination in Lighting Settings.");
            var result = new JObject
            {
                ["jobId"] = Guid.NewGuid().ToString("N"), ["status"] = "running", ["phase"] = "queued",
                ["code"] = "BAKE_STARTED", ["reason"] = "Bake accepted; poll get_scene_bake_status for verified completion.",
                ["unityVersion"] = Application.unityVersion, ["backend"] = target, ["scenePath"] = path,
                ["artifacts"] = new JArray(), ["verification"] = new JObject { ["passed"] = false }
            };
            active = new Job { Result = result, Scene = scene, Target = target, Surface = surface, StartedAt = EditorApplication.timeSinceStartup,
                Before = Snapshot(path) };
            Jobs[(string)result["jobId"]] = result;
            Persist();
            return result.DeepClone();
        }

        public static object GetStatus(JObject args)
        {
            string id = args?["jobId"]?.Value<string>();
            return id != null && Jobs.TryGetValue(id, out var result) ? result.DeepClone() : Error("JOB_NOT_FOUND", "No bake job exists for this jobId in this Editor session.");
        }

        static JObject Error(string code, string reason) => new JObject { ["status"] = "error", ["code"] = code, ["reason"] = reason, ["error"] = reason, ["unityVersion"] = Application.unityVersion };
        static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
        static bool LegacyRunning() => Legacy?.GetProperty("isRunning")?.GetValue(null) is bool running && running;
        static void LegacyCall(string name) => Legacy.GetMethod(name, Type.EmptyTypes).Invoke(null, null);
        static void Persist() => SessionState.SetString(SessionKey, new JArray(Jobs.Values.Select(v => v.DeepClone())).ToString(Newtonsoft.Json.Formatting.None));
        static Dictionary<string, long> Snapshot(string scenePath)
        {
            string outputFolder = Path.Combine(Path.GetDirectoryName(scenePath), Path.GetFileNameWithoutExtension(scenePath));
            var existing = Directory.Exists(outputFolder) ? Directory.GetFiles(outputFolder, "*", SearchOption.AllDirectories) : Array.Empty<string>();
            return AssetDatabase.GetDependencies(scenePath, true).Concat(existing).Select(p => p.Replace('\\', '/')).Distinct().Where(File.Exists).ToDictionary(p => p, p => File.GetLastWriteTimeUtc(p).Ticks);
        }

        static void Update()
        {
            if (active == null) return;
            try
            {
                if (!active.Scene.IsValid() || SceneManager.sceneCount != 1 || SceneManager.GetActiveScene() != active.Scene || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                { Fail("SCENE_CHANGED", "The scene or Editor state changed during baking."); return; }
                if (EditorApplication.timeSinceStartup - active.StartedAt > 1800)
                { Fail("BAKE_TIMEOUT", "Bake exceeded the 30 minute timeout."); return; }
                if (!active.Started)
                {
                    if (active.Scene.isDirty) { Fail("SCENE_CHANGED", "The scene was modified before the bake started."); return; }
                    active.Started = true;
                    active.Result["phase"] = "baking";
                    switch (active.Target)
                    {
                        case "lighting":
                            Lightmapping.Clear();
                            if (!Lightmapping.BakeAsync()) { Fail("BAKE_FAILED", "Unity rejected the lighting bake."); return; }
                            break;
                        case "occlusion":
                            StaticOcclusionCulling.Clear();
                            if (!StaticOcclusionCulling.GenerateInBackground()) { Fail("BAKE_FAILED", "Unity rejected the occlusion bake."); return; }
                            break;
                        case "navmesh-legacy":
                            LegacyCall("ClearAllNavMeshes");
                            LegacyCall("BuildNavMeshAsync");
                            break;
                        case "navmesh-surface":
                            int agent = (int)active.Surface.GetType().GetProperty("agentTypeID").GetValue(active.Surface);
                            active.AgentType = agent;
                            active.Data = new NavMeshData(agent)
                            {
                                name = "NavMesh-" + active.Result["jobId"],
                                position = active.Surface.transform.position,
                                rotation = active.Surface.transform.rotation
                            };
                            active.Operation = (AsyncOperation)active.Surface.GetType().GetMethod("UpdateNavMesh").Invoke(active.Surface, new object[] { active.Data });
                            if (active.Operation == null) { Fail("BAKE_FAILED", "NavMeshSurface returned no build operation."); return; }
                            break;
                    }
                    Persist();
                    return;
                }
                bool running = active.Target == "lighting" ? Lightmapping.isRunning : active.Target == "occlusion" ? StaticOcclusionCulling.isRunning : active.Target == "navmesh-legacy" ? LegacyRunning() : !active.Operation.isDone;
                if (running) return;
                // Native workers can stop before the resulting assets and scene references are imported.
                if (active.CompletedAt == 0)
                {
                    active.CompletedAt = EditorApplication.timeSinceStartup;
                    active.Result["phase"] = "finalizing";
                    return;
                }
                if (EditorApplication.isUpdating || EditorApplication.timeSinceStartup - active.CompletedAt < 1) return;
                if (active.Target == "lighting" && !active.LightingCompleted) { Fail("BAKE_FAILED", "Lighting stopped without a completion event."); return; }
                VerifyAndSave();
            }
            catch (Exception ex) { Fail("BAKE_FAILED", ex.GetBaseException().Message); }
        }

        static void VerifyAndSave()
        {
            var job = active;
            job.Result["phase"] = "verifying";
            if (job.Target.StartsWith("navmesh", StringComparison.Ordinal) && !VerifyNavigation(job))
            { Fail("ARTIFACT_VERIFICATION_FAILED", "Generated NavMesh cannot answer a complete path query."); return; }
            if (job.Target == "navmesh-surface")
            {
                if (job.Data == null || job.Data.sourceBounds.size.sqrMagnitude == 0) throw new InvalidOperationException("The surface build produced no navigation geometry.");
                string folder = Path.GetDirectoryName(job.Scene.path).Replace('\\', '/');
                AssetDatabase.CreateAsset(job.Data, AssetDatabase.GenerateUniqueAssetPath(folder + "/NavMesh-" + job.Result["jobId"] + ".asset"));
                job.Surface.GetType().GetMethod("RemoveData").Invoke(job.Surface, null);
                var serialized = new SerializedObject(job.Surface);
                var property = serialized.FindProperty("m_NavMeshData");
                if (property == null) throw new InvalidOperationException("The NavMeshSurface data reference cannot be persisted by this package version.");
                property.objectReferenceValue = job.Data;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                job.Surface.GetType().GetMethod("AddData").Invoke(job.Surface, null);
                EditorUtility.SetDirty(job.Surface);
            }
            if (job.Target == "lighting" && (Lightmapping.lightingDataAsset == null || LightmapSettings.lightmaps.Length == 0 || LightmapSettings.lightmaps.Any(m => m.lightmapColor == null)))
            { Fail("ARTIFACT_VERIFICATION_FAILED", "Lighting requires valid LightingDataAsset and non-empty lightmaps."); return; }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(job.Scene);
            if (!EditorSceneManager.SaveScene(job.Scene)) { Fail("SAVE_FAILED", "Unity could not save the baked scene."); return; }
            AssetDatabase.ImportAsset(job.Scene.path, ImportAssetOptions.ForceUpdate);
            var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(job.Scene.path, true));
            var artifacts = new List<string>();
            if (job.Target == "lighting")
            {
                artifacts.Add(AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset));
                foreach (var map in LightmapSettings.lightmaps)
                {
                    artifacts.Add(AssetDatabase.GetAssetPath(map.lightmapColor));
                    if (map.lightmapDir != null) artifacts.Add(AssetDatabase.GetAssetPath(map.lightmapDir));
                    if (map.shadowMask != null) artifacts.Add(AssetDatabase.GetAssetPath(map.shadowMask));
                }
            }
            else
            {
                artifacts.AddRange(dependencies.Where(p => job.Target == "occlusion" ? HasOcclusionData(p) : AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(NavMeshData)));
                if (job.Target == "navmesh-surface") artifacts = new List<string> { AssetDatabase.GetAssetPath(job.Data) };
            }
            artifacts = artifacts.Distinct().ToList();
            job.Result["verification"] = new JObject
            {
                ["passed"] = false,
                ["candidates"] = new JArray(artifacts.Select(p => new JObject
                {
                    ["path"] = p, ["sceneReferenced"] = dependencies.Contains(p), ["exists"] = File.Exists(p),
                    ["updated"] = File.Exists(p) && (!job.Before.TryGetValue(p, out long stamp) || File.GetLastWriteTimeUtc(p).Ticks != stamp)
                })),
                ["assetDependencies"] = new JArray(dependencies.Where(p => p.EndsWith(".asset", StringComparison.Ordinal)).Select(p => new JObject { ["path"] = p, ["type"] = AssetDatabase.GetMainAssetTypeAtPath(p)?.FullName })),
                ["occlusionBytes"] = job.Target == "occlusion" ? (JToken)StaticOcclusionCulling.umbraDataSize : JValue.CreateNull()
            };
            bool valid = artifacts.Count > 0 && artifacts.All(p => !string.IsNullOrEmpty(p) && p.StartsWith("Assets/", StringComparison.Ordinal) && dependencies.Contains(p) && File.Exists(p) && new FileInfo(p).Length > 0 && (!job.Before.TryGetValue(p, out long stamp) || File.GetLastWriteTimeUtc(p).Ticks != stamp));
            if (!valid) { Fail("ARTIFACT_VERIFICATION_FAILED", "Artifacts must be non-empty, updated by this bake, persisted under Assets, and referenced by the saved target scene."); return; }
            job.Result["artifacts"] = new JArray(artifacts);
            job.Result["verification"] = new JObject { ["passed"] = true, ["persisted"] = true, ["sceneReferenced"] = true, ["generatedThisRun"] = true, ["navigationQuery"] = job.Target.StartsWith("navmesh", StringComparison.Ordinal) ? (JToken)true : JValue.CreateNull() };
            job.Result["status"] = "succeeded";
            job.Result["phase"] = "complete";
            job.Result["code"] = "BAKE_SUCCEEDED";
            job.Result["reason"] = "Bake artifacts and saved scene references verified.";
            active = null;
            Persist();
        }

        static bool VerifyNavigation(Job job)
        {
            // A fresh surface is queried in isolation so old loaded surfaces cannot satisfy the check.
            NavMeshDataInstance isolated = default;
            Component[] surfaces = Array.Empty<Component>();
            try
            {
                if (job.Target == "navmesh-surface")
                {
                    surfaces = job.Scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren(job.Surface.GetType())).Where(s => s is Behaviour b && b.isActiveAndEnabled).ToArray();
                    foreach (var s in surfaces) s.GetType().GetMethod("RemoveData").Invoke(s, null);
                    if (NavMesh.CalculateTriangulation().indices.Length != 0) return false;
                    isolated = NavMesh.AddNavMeshData(job.Data, job.Surface.transform.position, job.Surface.transform.rotation);
                }
                var mesh = NavMesh.CalculateTriangulation();
                if (mesh.indices.Length < 3) return false;
                var a = mesh.vertices[mesh.indices[0]];
                var b = mesh.vertices[mesh.indices[1]];
                var c = mesh.vertices[mesh.indices[2]];
                var start = (a + b + c) / 3;
                var end = (start + b) / 2;
                var filter = new NavMeshQueryFilter { areaMask = NavMesh.AllAreas, agentTypeID = job.AgentType };
                if (!NavMesh.SamplePosition(start, out var hitA, 0.25f, filter) || !NavMesh.SamplePosition(end, out var hitB, 0.25f, filter)) return false;
                var path = new NavMeshPath();
                return NavMesh.CalculatePath(hitA.position, hitB.position, filter, path) && path.status == NavMeshPathStatus.PathComplete && path.corners.Length >= 2;
            }
            finally
            {
                if (isolated.valid) isolated.Remove();
                foreach (var s in surfaces) if (s != null) s.GetType().GetMethod("AddData").Invoke(s, null);
            }
        }

        static bool HasOcclusionData(string path)
        {
            // Unity exposes native OcclusionCullingData as UnityEngine.Object, not a managed asset type.
            // Inspect the serialized PVS payload; umbraDataSize was zero in the tested batch environment.
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) return false;
            using (var serialized = new SerializedObject(asset))
            {
                var payload = serialized.FindProperty("m_PVSData");
                return payload != null && payload.isArray && payload.arraySize > 0;
            }
        }

        static void Fail(string code, string reason)
        {
            if (active == null) return;
            var job = active;
            active = null;
            try
            {
                if (job.Started)
                {
                    if (job.Target == "lighting" && Lightmapping.isRunning) Lightmapping.Cancel();
                    if (job.Target == "occlusion" && StaticOcclusionCulling.isRunning) StaticOcclusionCulling.Cancel();
                    if (job.Target == "navmesh-legacy" && LegacyRunning()) LegacyCall("Cancel");
                    if (job.Target == "navmesh-surface" && job.Data != null) UnityEngine.AI.NavMeshBuilder.Cancel(job.Data);
                }
            }
            catch (Exception) { /* Preserve the original failure even when cancellation is unavailable. */ }
            if (job.Data != null && !AssetDatabase.Contains(job.Data)) Object.DestroyImmediate(job.Data);
            job.Result["status"] = "failed";
            job.Result["phase"] = "failed";
            job.Result["code"] = code;
            job.Result["reason"] = reason;
            Persist();
        }
    }
}
