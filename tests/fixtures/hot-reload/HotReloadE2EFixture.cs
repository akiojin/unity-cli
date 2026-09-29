using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HotReloadE2EFixture
{
    const string ObservationPath = "Library/HotReloadE2E/partial-observation.json";
    static double observationDeadline, nextObservation;
    static HotReloadProbe observedProbe;
    static readonly string[] PreferenceNames = { "EnableAutoReloadForChangedFiles", "EnableOnDemandReload" };

    [MenuItem("Tools/Unity CLI/Hot Reload/Start E2E Status Observation")]
    public static void StartStatusObservation()
    {
        StopStatusObservation();
        Directory.CreateDirectory("Library/HotReloadE2E");
        if (File.Exists(ObservationPath)) File.Delete(ObservationPath);
        observedProbe = Object.FindFirstObjectByType<HotReloadProbe>();
        observationDeadline = EditorApplication.timeSinceStartup + 45;
        nextObservation = 0;
        EditorApplication.update += ObserveStatus;
    }

    [MenuItem("Tools/Unity CLI/Hot Reload/Stop E2E Status Observation")]
    public static void StopStatusObservation() => EditorApplication.update -= ObserveStatus;

    static void ObserveStatus()
    {
        if (!EditorApplication.isPlaying || observedProbe == null || EditorApplication.timeSinceStartup >= observationDeadline)
        {
            StopStatusObservation();
            return;
        }
        if (EditorApplication.timeSinceStartup < nextObservation) return;
        nextObservation = EditorApplication.timeSinceStartup + 0.1;
        // Read directly on the Editor thread: TCP requests serialize behind the pending apply.
        var status = JObject.FromObject(UnityCliBridge.Handlers.HotReloadHandler.Status());
        File.WriteAllText(ObservationPath, JsonConvert.SerializeObject(new { value = observedProbe.value, status }));
        if (observedProbe.value == 21 && status.Value<string>("state") == "applying") StopStatusObservation();
    }

    static object Preference(string name) => System.Type.GetType("FastScriptReload.Editor.FastScriptReloadPreference, FastScriptReload.Editor", true)
        .GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);

    static object PreferenceCall(object preference, string name, params object[] supplied)
    {
        var method = preference.GetType().GetMethods().Where(m => m.Name == name).OrderBy(m => m.GetParameters().Length).First();
        var parameters = method.GetParameters();
        return method.Invoke(preference, parameters.Select((p, i) => i < supplied.Length ? supplied[i] : System.Type.Missing).ToArray());
    }

    [MenuItem("Tools/Unity CLI/Hot Reload/Configure E2E Backend")]
    public static void Configure()
    {
        Directory.CreateDirectory("Library/HotReloadE2E");
        var path = "Library/HotReloadE2E/preferences.json";
        if (!File.Exists(path))
            File.WriteAllText(path, JsonConvert.SerializeObject(PreferenceNames.ToDictionary(n => n,
                n => (bool)PreferenceCall(Preference(n), "GetEditorPersistedValueOrDefault"))));
        foreach (var name in PreferenceNames) PreferenceCall(Preference(name), "SetEditorPersistedValue", false);
    }

    [MenuItem("Tools/Unity CLI/Hot Reload/Restore E2E Backend")]
    public static void Restore()
    {
        var path = "Library/HotReloadE2E/preferences.json";
        if (!File.Exists(path)) return;
        foreach (var value in JsonConvert.DeserializeObject<Dictionary<string, bool>>(File.ReadAllText(path)))
            PreferenceCall(Preference(value.Key), "SetEditorPersistedValue", value.Value);
        File.Delete(path);
    }

    // Compile only: this is safe on ARM and never invokes the native loader.
    [MenuItem("Tools/Unity CLI/Hot Reload/Compile E2E Candidate")]
    public static async void CompileCandidate()
    {
        Directory.CreateDirectory("Library/HotReloadE2E");
        var output = Path.GetFullPath("Library/HotReloadE2E/compile.json");
        var directory = Path.Combine(Path.GetTempPath(), "unity-cli-compile-probe-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = File.ReadAllText("Assets/HotReloadProbe.cs").Replace("return input * 2;",
                "global::UnityCliBridge.HotReload.FastScriptReloadAdapter.Observe(\"compile-only\", \"Calculate\"); return input * 5;");
            var path = Path.Combine(directory, "HotReloadProbe.cs");
            File.WriteAllText(path, source);
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            var dispatcherType = assemblies.Select(a => a.GetType("ImmersiveVRTools.Runtime.Common.UnityMainThreadDispatcher")).First(t => t != null);
            var dispatcher = dispatcherType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            dispatcher = dispatcherType.GetMethod("EnsureInitialized").Invoke(dispatcher, null);
            var compiler = assemblies.Select(a => a.GetType("FastScriptReload.Editor.Compilation.DynamicAssemblyCompiler")).First(t => t != null);
            var result = await Task.Run(() => compiler.GetMethod("Compile").Invoke(null, new object[] { new List<string> { path }, dispatcher }));
            var type = result.GetType();
            var assembly = (Assembly)type.GetProperty("CompiledAssembly").GetValue(result);
            File.WriteAllText(output, JsonConvert.SerializeObject(new
            {
                success = !(bool)type.GetProperty("IsError").GetValue(result),
                method = assembly?.GetType("HotReloadProbe__Patched_")?.GetMethod("Calculate")?.ToString(),
                messages = type.GetProperty("MessagesFromCompilerProcess").GetValue(result)
            }));
        }
        catch (System.Exception error)
        {
            File.WriteAllText(output, JsonConvert.SerializeObject(new { success = false, error = error.ToString() }));
        }
        finally { Directory.Delete(directory, true); }
    }

    [MenuItem("Tools/Unity CLI/Hot Reload/Create E2E Fixture")]
    public static void Create()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var actor = new GameObject("HotReloadProbe");
        actor.AddComponent<HotReloadProbe>();
        actor.transform.position = new Vector3(2, 3, 5);
        Directory.CreateDirectory("Assets/Scenes/Generated/E2E/HotReload");
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/Scenes/Generated/E2E/HotReload/Probe.unity");
    }

    [MenuItem("Tools/Unity CLI/Hot Reload/Snapshot E2E Fixture")]
    public static void Snapshot()
    {
        var probe = Object.FindFirstObjectByType<HotReloadProbe>();
        var scene = SceneManager.GetActiveScene();
#if UNITY_6000_5_OR_NEWER
        var objectId = unchecked((int)EntityId.ToULong(probe.GetEntityId()));
#else
        var objectId = probe.GetInstanceID();
#endif
        Directory.CreateDirectory("Library/HotReloadE2E");
        File.WriteAllText("Library/HotReloadE2E/snapshot.json", JsonConvert.SerializeObject(new
        {
            playing = EditorApplication.isPlaying, scene = scene.path, sceneHandle = scene.handle.GetHashCode(),
            objectId, hp = probe.hp, score = probe.score,
            position = new[] { probe.transform.position.x, probe.transform.position.y, probe.transform.position.z },
            transient = probe.transientState, staticState = HotReloadProbe.staticState, value = probe.value
        }));
    }
}
