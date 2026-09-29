using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace UnityCliBridge.TestScenes
{
    // Fixtures and independent acceptance assertions; production bake APIs are called by the CLI.
    public static class UnityCliBakeE2EFixture
    {
        public const string Root = "Assets/Scenes/Generated/E2E/Baking";

        [MenuItem("Tools/Unity CLI/Baking/Prepare Lighting")]
        public static void Lighting() { Prepare("Lighting", true, false); }
        [MenuItem("Tools/Unity CLI/Baking/Prepare Legacy")]
        public static void Legacy() { Prepare("Legacy", true, false); }
        [MenuItem("Tools/Unity CLI/Baking/Prepare Surface")]
        public static void Surface() { Prepare("Surface", true, true); }
        [MenuItem("Tools/Unity CLI/Baking/Prepare Occlusion")]
        public static void Occlusion() { Prepare("Occlusion", true, false); }
        [MenuItem("Tools/Unity CLI/Baking/Prepare Empty")]
        public static void Empty() { Prepare("Empty", false, false); }
        [MenuItem("Tools/Unity CLI/Baking/Prepare NoOutput")]
        public static void NoOutput()
        {
            Prepare("NoOutput", true, true);
            ExcludeSurfaceGeometry();
        }

        [MenuItem("Tools/Unity CLI/Baking/Exclude Surface Geometry")]
        public static void ExcludeSurfaceGeometry()
        {
            var type = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation", true);
            var surface = GameObject.Find("BakeSurface").GetComponent(type);
            var collect = type.GetProperty("collectObjects");
            collect.SetValue(surface, Enum.Parse(collect.PropertyType, "Volume"));
            type.GetProperty("center").SetValue(surface, new Vector3(1000, 1000, 1000));
            type.GetProperty("size").SetValue(surface, Vector3.one);
            EditorUtility.SetDirty(surface);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }

        private static void Prepare(string name, bool geometry, bool surface)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var previous = SceneManager.GetSceneAt(i);
                if (previous.isDirty && !previous.path.StartsWith(Root + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Save unrelated dirty scenes before Baking E2E.");
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var directory = Root + "/" + name;
            AssetDatabase.DeleteAsset(directory);
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            if (geometry)
            {
                Cube("Floor", new Vector3(0, -0.25f, 0), new Vector3(12, 0.5f, 12));
                Cube("Wall", new Vector3(0, 2, 3), new Vector3(10, 4, 0.5f));
                Cube("RearWall", new Vector3(-5, 2, 0), new Vector3(0.5f, 4, 6));
                var light = new GameObject("BakedLight").AddComponent<Light>();
                light.type = LightType.Directional;
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
                var camera = new GameObject("Camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 2, -5);
                var area = new GameObject("OcclusionArea").AddComponent<OcclusionArea>();
                area.center = new Vector3(0, 2, 0);
                area.size = new Vector3(12, 6, 12);
            }
            if (surface)
            {
                var holder = new GameObject("BakeSurface");
                holder.transform.position = new Vector3(30, 0, 20);
                holder.transform.rotation = Quaternion.Euler(0, 37, 0);
                foreach (var childName in new[] { "Floor", "Wall", "RearWall" })
                    GameObject.Find(childName).transform.SetParent(holder.transform, false);
                var type = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation", true);
                var component = holder.AddComponent(type);
                var collect = type.GetProperty("collectObjects");
                collect.SetValue(component, Enum.Parse(collect.PropertyType, "Children"));
            }
            var settings = new LightingSettings
            {
                bakedGI = true,
                realtimeGI = false,
                lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                lightmapResolution = 2,
                lightmapMaxSize = 128,
                directSampleCount = 8,
                indirectSampleCount = 8,
                environmentSampleCount = 8,
                maxBounces = 1
            };
            AssetDatabase.CreateAsset(settings, directory + "/Settings.lighting");
            Lightmapping.lightingSettings = settings;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), directory + "/" + name + ".unity");
            AssetDatabase.SaveAssets();
            File.Delete(Root + "/validation.json");
        }

        private static void Cube(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
#pragma warning disable 618
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI |
                StaticEditorFlags.NavigationStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
#pragma warning restore 618
        }

        [MenuItem("Tools/Unity CLI/Baking/Validate Lighting")]
        public static void ValidateLighting() { Validate("Lighting"); }
        [MenuItem("Tools/Unity CLI/Baking/Validate Legacy")]
        public static void ValidateLegacy() { Validate("Legacy"); }
        [MenuItem("Tools/Unity CLI/Baking/Validate Surface")]
        public static void ValidateSurface() { Validate("Surface"); }
        [MenuItem("Tools/Unity CLI/Baking/Validate Occlusion")]
        public static void ValidateOcclusion() { Validate("Occlusion"); }

        [Serializable]
        private class Evidence
        {
            public string target;
            public string unityVersion;
            public bool passed;
            public string error;
            public int lightmapCount;
            public int pathCorners;
            public string persistedAsset;
        }

        private static void Require(bool value, string description)
        {
            if (!value) throw new InvalidOperationException(description);
        }

        private static void Validate(string name)
        {
            var result = new Evidence { target = name, unityVersion = Application.unityVersion };
            try
            {
                // Do not save here: the tool must have persisted the scene itself.
                var scenePath = Root + "/" + name + "/" + name + ".unity";
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorUtility.UnloadUnusedAssetsImmediate();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                if (name == "Lighting")
                {
                    result.persistedAsset = AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset);
                    Require(File.Exists(result.persistedAsset), "LightingDataAsset must survive scene reload");
                    result.lightmapCount = LightmapSettings.lightmaps.Length;
                    Require(result.lightmapCount > 0, "At least one baked lightmap must be assigned after reload");
                    Require(LightmapSettings.lightmaps.All(m => m.lightmapColor != null &&
                        File.Exists(AssetDatabase.GetAssetPath(m.lightmapColor))), "Lightmaps must exist on disk");
                    Require(GameObject.Find("Floor").GetComponent<Renderer>().lightmapIndex < result.lightmapCount,
                        "Floor must reference the persisted lightmap");
                }
                else if (name == "Legacy" || name == "Surface")
                {
                    if (name == "Surface")
                    {
                        var type = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation", true);
                        var component = GameObject.Find("BakeSurface").GetComponent(type);
                        var data = (UnityEngine.Object)type.GetProperty("navMeshData").GetValue(component);
                        result.persistedAsset = AssetDatabase.GetAssetPath(data);
                    }
                    else result.persistedAsset = ReferencedAsset(scenePath, "m_NavMeshData");
                    Require(File.Exists(result.persistedAsset), "NavMeshData asset must remain assigned after reload");
                    var startPoint = new Vector3(-2, 0, -2);
                    var endPoint = new Vector3(2, 0, -2);
                    if (name == "Surface")
                    {
                        var transform = GameObject.Find("BakeSurface").transform;
                        startPoint = transform.TransformPoint(startPoint);
                        endPoint = transform.TransformPoint(endPoint);
                    }
                    Require(NavMesh.SamplePosition(startPoint, out var start, 1, NavMesh.AllAreas), "NavMesh start sample at fixture world position");
                    Require(NavMesh.SamplePosition(endPoint, out var end, 1, NavMesh.AllAreas), "NavMesh end sample at fixture world position");
                    var path = new NavMeshPath();
                    Require(NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) &&
                        path.status == NavMeshPathStatus.PathComplete, "Reloaded NavMesh must provide a complete path");
                    result.pathCorners = path.corners.Length;
                    Require(result.pathCorners >= 2, "NavMesh path must have endpoints");
                }
                else
                {
                    result.persistedAsset = ReferencedAsset(scenePath, "m_OcclusionCullingData");
                    Require(File.Exists(result.persistedAsset), "Occlusion data must remain assigned in saved scene");
                    Require(new FileInfo(result.persistedAsset).Length > 0, "Occlusion data must not be empty");
                    // Unity 6 exposes native class 363 as UnityEngine.Object and reports
                    // umbraDataSize=0 even for a populated, assigned asset in batchmode.
                    // Inspect the persisted PVS payload instead of trusting that counter.
                    var data = File.ReadAllText(result.persistedAsset);
                    Require(data.Contains("OcclusionCullingData:"), "Persisted asset is native occlusion data");
                    Require(Regex.IsMatch(data, @"m_PVSData:\s*[a-fA-F0-9]{32,}"), "Persisted occlusion PVS payload must be nonempty");
                    Require(Regex.IsMatch(data, @"sizeRenderers:\s*[1-9][0-9]*"), "Occlusion data must include fixture renderers");
                }
                result.passed = true;
            }
            catch (Exception ex) { result.error = ex.ToString(); }
            File.WriteAllText(Root + "/validation.json", JsonUtility.ToJson(result, true));
            if (!result.passed) Debug.LogError(result.error);
        }

        private static string ReferencedAsset(string scenePath, string field)
        {
            var match = Regex.Match(File.ReadAllText(scenePath), field + @":\s*\{[^}]*guid:\s*([a-fA-F0-9]{32})");
            Require(match.Success, field + " must contain a saved asset GUID");
            return AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
        }
    }
}
