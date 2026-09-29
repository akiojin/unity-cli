using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityCliBridge.Tests
{
    public class BakeHandlerTests
    {
        const string Folder = "Assets/Scenes/Generated/E2E/BakeHandlerTests";
        const string ScenePath = Folder + "/Bake.unity";

        static JObject Call(string name, JObject args)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityCliBridge.Handlers.BakeHandler")).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, "BakeHandler must implement scene bake jobs");
            var value = type.GetMethod(name).Invoke(null, new object[] { args });
            return value is string json ? JObject.Parse(json) : JObject.FromObject(value);
        }

        [SetUp] public void Setup()
        {
            System.IO.Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        }

        [TearDown] public void Cleanup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Tick();
            AssetDatabase.DeleteAsset(Folder);
        }

        static void Tick()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityCliBridge.Handlers.BakeHandler")).FirstOrDefault(t => t != null);
            type?.GetMethod("Update", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.Invoke(null, null);
        }

        static JObject QueueBake()
        {
            GameObject.CreatePrimitive(PrimitiveType.Plane);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return Call("StartBake", new JObject { ["target"] = "navmesh-legacy", ["scenePath"] = ScenePath });
        }

        [Test] public void QueuedJobIsRunningAndHasNoArtifacts()
        {
            var accepted = QueueBake();
            Assert.That((string)accepted["status"], Is.EqualTo("running"), accepted.ToString());
            var status = Call("GetStatus", new JObject { ["jobId"] = accepted["jobId"] });
            Assert.That((string)status["phase"], Is.EqualTo("queued"));
            Assert.That(status["artifacts"].Count(), Is.Zero);
            Assert.That((bool)status["verification"]["passed"], Is.False);
            status["status"] = "succeeded";
            Assert.That((string)Call("GetStatus", new JObject { ["jobId"] = accepted["jobId"] })["status"], Is.EqualTo("running"));
        }

        [Test] public void ConcurrentBakeIsRejectedBeforeEditorTick()
        {
            var accepted = QueueBake();
            Assert.That((string)accepted["status"], Is.EqualTo("running"), accepted.ToString());
            var second = Call("StartBake", new JObject { ["target"] = "navmesh-legacy", ["scenePath"] = ScenePath });
            Assert.That((string)second["code"], Is.EqualTo("BAKE_BUSY"));
        }

        [Test] public void SceneChangeFailsAcceptedJobWithoutSuccessArtifacts()
        {
            var accepted = QueueBake();
            Assert.That((string)accepted["status"], Is.EqualTo("running"), accepted.ToString());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Tick();
            var status = Call("GetStatus", new JObject { ["jobId"] = accepted["jobId"] });
            Assert.That((string)status["status"], Is.EqualTo("failed"));
            Assert.That((string)status["code"], Is.EqualTo("SCENE_CHANGED"));
            Assert.That(status["artifacts"].Count(), Is.Zero);
        }

        [Test] public void SceneEditedAfterAcceptanceFailsBeforeBaking()
        {
            var accepted = QueueBake();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Tick();
            var status = Call("GetStatus", new JObject { ["jobId"] = accepted["jobId"] });
            Assert.That((string)status["status"], Is.EqualTo("failed"));
            Assert.That((string)status["code"], Is.EqualTo("SCENE_CHANGED"));
        }

        [Test] public void LegacyRejectsSurfaceOwnedNavigationData()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.AI.Navigation.NavMeshSurface")).FirstOrDefault(t => t != null);
            if (type == null) Assert.Ignore("AI Navigation optional package is absent.");
            var root = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var surface = root.AddComponent(type);
            var data = new UnityEngine.AI.NavMeshData();
            AssetDatabase.CreateAsset(data, Folder + "/ExistingNavMesh.asset");
            var serialized = new SerializedObject(surface);
            serialized.FindProperty("m_NavMeshData").objectReferenceValue = data;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            var result = Call("StartBake", new JObject { ["target"] = "navmesh-legacy", ["scenePath"] = ScenePath });
            Assert.That((string)result["code"], Is.EqualTo("MIXED_NAVMESH_BACKENDS"), result.ToString());
        }

        [Test] public void MissingTargetIsRejected()
        {
            Assert.That(Call("StartBake", new JObject { ["scenePath"] = ScenePath })["code"]?.ToString(), Is.EqualTo("INVALID_TARGET"));
        }

        [Test] public void UnknownJobDoesNotReportSuccess()
        {
            Assert.That(Call("GetStatus", new JObject { ["jobId"] = "missing" })["code"]?.ToString(), Is.EqualTo("JOB_NOT_FOUND"));
        }

        [Test] public void DifferentSceneIsRejected()
        {
            Assert.That(Call("StartBake", new JObject { ["target"] = "lighting", ["scenePath"] = "Assets/Other.unity" })["code"]?.ToString(), Is.EqualTo("SCENE_MISMATCH"));
        }

        [Test] public void AdditiveSceneIsRejectedWithoutSwitchingActiveScene()
        {
            var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
            var result = Call("StartBake", new JObject { ["target"] = "occlusion", ["scenePath"] = ScenePath });
            Assert.That((string)result["code"], Is.EqualTo("MULTIPLE_SCENES"));
            Assert.That(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), Is.EqualTo(original));
        }

        [Test] public void DirtySceneIsRejected()
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Assert.That(Call("StartBake", new JObject { ["target"] = "lighting", ["scenePath"] = ScenePath })["code"]?.ToString(), Is.EqualTo("SCENE_DIRTY"));
        }

        [TestCase("lighting")]
        [TestCase("navmesh-legacy")]
        [TestCase("occlusion")]
        public void EmptySceneIsRejected(string target)
        {
            Assert.That(Call("StartBake", new JObject { ["target"] = target, ["scenePath"] = ScenePath })["code"]?.ToString(), Is.EqualTo("NO_GEOMETRY"));
        }

        [Test] public void SurfaceRequiresExplicitPath()
        {
            Assert.That(Call("StartBake", new JObject { ["target"] = "navmesh-surface", ["scenePath"] = ScenePath })["code"]?.ToString(), Is.EqualTo("SURFACE_PATH_REQUIRED"));
        }
    }
}
