using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityCliBridge.Models;

namespace UnityCliBridge.Handlers
{
    // Unity API access is confined to Initialize, Start and Run (Editor main thread).
    // The TCP reader only sees cloned JSON under Gate and never calls Response helpers.
    [InitializeOnLoad]
    internal static class PlayerBuildHandler
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, JObject> Jobs = new Dictionary<string, JObject>();
        private static string activeId;
        private static readonly string Journal;

        static PlayerBuildHandler()
        {
            Journal = Path.GetFullPath(Path.Combine(Application.dataPath, "../.unity/player-build-last.json"));
            try
            {
                if (File.Exists(Journal))
                {
                    var job = JObject.Parse(File.ReadAllText(Journal));
                    if ((string)job["state"] == "queued" || (string)job["state"] == "running")
                    {
                        job["state"] = "interrupted";
                        job["error"] = "Editor restarted or assemblies reloaded before build completion.";
                        job["code"] = "BUILD_INTERRUPTED";
                        job["errors"] = new JArray((string)job["error"]);
                    }
                    Jobs[(string)job["buildId"]] = job;
                }
            }
            catch (Exception ex) { Debug.LogWarning("Cannot restore Player build status: " + ex.Message); }
        }

        internal static void Initialize() { } // Prime on the main thread before accepting TCP commands.

        internal static bool TryHandleBackground(Command command, out string response)
        {
            if (string.Equals(command.Type, "get_build_status", StringComparison.OrdinalIgnoreCase))
            {
                response = Status(command);
                return true;
            }
            lock (Gate)
            {
                if (activeId != null)
                {
                    response = Error(command.Id, "BUILD_BUSY", "A Player build is queued or running.");
                    return true;
                }
            }
            response = null;
            return false;
        }

        internal static string Status(Command command)
        {
            var id = command.Parameters?["buildId"];
            if (id?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)id))
                return Error(command.Id, "INVALID_BUILD_PARAMETERS", "buildId is required.");
            JObject snapshot;
            lock (Gate)
            {
                if (!Jobs.TryGetValue((string)id, out var job))
                    return Error(command.Id, "BUILD_NOT_FOUND", "Unknown buildId (only recent builds are retained).");
                snapshot = (JObject)job.DeepClone();
            }
            if ((string)snapshot["state"] == "running" || (string)snapshot["state"] == "queued")
                snapshot["durationSeconds"] = (DateTime.UtcNow - snapshot["startedAt"].Value<DateTime>()).TotalSeconds;
            if ((string)snapshot["state"] == "failed" || (string)snapshot["state"] == "interrupted")
                return Error(command.Id, (string)snapshot["code"] ?? "BUILD_FAILED", (string)snapshot["error"] ?? "Player build failed.", snapshot);
            return Success(command.Id, snapshot);
        }

        internal static string Start(Command command)
        {
            var p = command.Parameters;
            if (p?["target"]?.Type != JTokenType.String || p["outputPath"]?.Type != JTokenType.String ||
                !(p["scenes"] is JArray scenes) || scenes.Count == 0 || scenes.Any(s => s.Type != JTokenType.String) ||
                (p["development"] != null && p["development"].Type != JTokenType.Boolean))
                return Error(command.Id, "INVALID_BUILD_PARAMETERS", "Explicit target, nonempty scenes, outputPath and optional boolean development are required.");
            if (!Enum.TryParse((string)p["target"], out BuildTarget target) ||
                (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneOSX))
                return Error(command.Id, "INVALID_BUILD_PARAMETERS", "Supported targets: StandaloneWindows64, StandaloneOSX.");
            if (TryHandleBackground(command, out var busy)) return busy;
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                return Error(command.Id, "BUILD_COMPILATION_ERROR", "Scripts are compiling or have compilation errors.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
                return Error(command.Id, "BUILD_EDITOR_BUSY", "Exit Play Mode and wait for imports/builds before building.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                return Error(command.Id, "BUILD_MODULE_MISSING", "Install the selected platform build module for this Editor.");
            if (EditorUserBuildSettings.activeBuildTarget != target)
                return Error(command.Id, "BUILD_TARGET_MISMATCH", "Select the requested active build target explicitly before building; this tool does not switch targets.");
            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    return Error(command.Id, "BUILD_UNSAVED_SCENES", "Save dirty scenes explicitly before building.");
            var paths = scenes.Values<string>().ToArray();
            if (paths.Any(s => string.IsNullOrWhiteSpace(s) || !s.StartsWith("Assets/", StringComparison.Ordinal) ||
                s.Contains("..") || !s.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(s) == null))
                return Error(command.Id, "INVALID_BUILD_SCENES", "Every scene must be an existing saved Assets/*.unity scene.");
            string output;
            try { output = ValidateOutput((string)p["outputPath"], target); }
            catch (Exception ex) { return Error(command.Id, "INVALID_OUTPUT_PATH", ex.Message); }
            var id = Guid.NewGuid().ToString("N");
            var snapshot = new JObject
            {
                ["buildId"] = id, ["state"] = "queued", ["target"] = target.ToString(),
                ["scenes"] = new JArray(paths), ["outputPath"] = output,
                ["startedAt"] = DateTime.UtcNow, ["durationSeconds"] = 0,
                ["reportResult"] = null, ["totalErrors"] = null, ["totalWarnings"] = null,
                ["changedProjectSettings"] = new JArray(),
                ["artifacts"] = new JArray(), ["errors"] = new JArray(), ["warnings"] = new JArray()
            };
            lock (Gate)
            {
                // Keep a bounded session history; only the latest build survives restart.
                if (Jobs.Count >= 16) Jobs.Remove(Jobs.Keys.First());
                Jobs[id] = snapshot;
                activeId = id;
            }
            try { Persist(snapshot); }
            catch (Exception ex)
            {
                lock (Gate) { Jobs.Remove(id); activeId = null; }
                return Error(command.Id, "BUILD_STATUS_IO_ERROR", ex.Message);
            }
            var options = new BuildPlayerOptions
            {
                scenes = paths, target = target, locationPathName = output,
                options = BuildOptions.DetailedBuildReport | ((bool?)p["development"] == true ? BuildOptions.Development : BuildOptions.None)
            };
            EditorApplication.delayCall += () => Run(id, options);
            return Success(command.Id, (JObject)snapshot.DeepClone());
        }

        internal static string ValidateOutput(string path, BuildTarget target)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new ArgumentException("outputPath must be an absolute executable/app path.");
            var output = Path.GetFullPath(path);
            var extension = target == BuildTarget.StandaloneWindows64 ? ".exe" : ".app";
            if (!output.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("outputPath must end in " + extension);
            var parent = Path.GetDirectoryName(output);
            if (File.Exists(output) || Directory.Exists(output) || File.Exists(parent) ||
                (Directory.Exists(parent) && Directory.EnumerateFileSystemEntries(parent).Any()))
                throw new ArgumentException("Use a new or empty dedicated output directory; existing output is never overwritten.");
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (var protectedFolder in new[] { "Assets", "Packages", "ProjectSettings", "Library" })
            {
                var root = Path.Combine(project, protectedFolder) + Path.DirectorySeparatorChar;
                if (output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Build output cannot be inside " + protectedFolder);
            }
            for (var cursor = parent; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
                if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Output directory ancestors must not be symbolic links.");
            return output;
        }

        private static void Run(string id, BuildPlayerOptions options)
        {
            JObject job;
            lock (Gate) { job = (JObject)Jobs[id].DeepClone(); job["state"] = "running"; Jobs[id] = job; }
            var start = DateTime.UtcNow;
            var settingsBefore = new Dictionary<string, string>();
            try
            {
                settingsBefore = CaptureProjectSettings();
                Persist(job);
                if (EditorUserBuildSettings.activeBuildTarget != options.target || EditorApplication.isCompiling ||
                    EditorUtility.scriptCompilationFailed || EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Editor build target or compilation/play state changed after accepting the build.");
                ValidateOutput(options.locationPathName, options.target);
                Directory.CreateDirectory(Path.GetDirectoryName(options.locationPathName));
                // A write probe fails before entering Unity's build pipeline on unwritable paths.
                var probe = Path.Combine(Path.GetDirectoryName(options.locationPathName), ".unity-cli-write-" + id);
                using (File.Create(probe)) { }
                File.Delete(probe);
                var report = BuildPipeline.BuildPlayer(options);
                if (report == null) throw new InvalidOperationException("Unity returned no BuildReport.");
                job = (JObject)job.DeepClone();
                job["reportResult"] = report.summary.result.ToString();
                job["totalErrors"] = report.summary.totalErrors;
                job["totalWarnings"] = report.summary.totalWarnings;
                job["durationSeconds"] = report.summary.totalTime.TotalSeconds;
                job["outputPath"] = report.summary.outputPath;
                job["errors"] = new JArray(report.steps.SelectMany(s => s.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception || m.type == LogType.Assert).Select(m => m.content));
                job["warnings"] = new JArray(report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Warning).Select(m => m.content));
                var artifacts = report.GetFiles().Select(f => Path.GetFullPath(f.path)).Distinct().ToArray();
                job["artifacts"] = new JArray(artifacts);
                var exists = options.target == BuildTarget.StandaloneWindows64
                    ? File.Exists(options.locationPathName) && Directory.Exists(Path.Combine(Path.GetDirectoryName(options.locationPathName), Path.GetFileNameWithoutExtension(options.locationPathName) + "_Data"))
                    : Directory.Exists(Path.Combine(options.locationPathName, "Contents/Resources/Data"));
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                    throw new InvalidOperationException("BuildReport result: " + report.summary.result);
                if (!exists || artifacts.Length == 0 || artifacts.Any(f => !File.Exists(f)))
                    throw new InvalidOperationException("BuildReport succeeded but required artifacts are missing.");
                job["state"] = "succeeded";
            }
            catch (Exception ex)
            {
                job = (JObject)job.DeepClone();
                job["state"] = "failed"; job["code"] = "BUILD_FAILED"; job["error"] = ex.Message;
                if (job["errors"] is JArray errors && errors.Count == 0) errors.Add(ex.Message);
                if ((string)job["reportResult"] == null) job["durationSeconds"] = (DateTime.UtcNow - start).TotalSeconds;
            }
            try
            {
                var settingsAfter = CaptureProjectSettings();
                job["changedProjectSettings"] = new JArray(settingsBefore.Keys.Union(settingsAfter.Keys)
                    .Where(path => !settingsBefore.TryGetValue(path, out var before) ||
                        !settingsAfter.TryGetValue(path, out var after) || before != after).OrderBy(path => path));
                Persist(job);
            }
            catch (Exception ex)
            {
                job["state"] = "failed"; job["code"] = "BUILD_STATUS_IO_ERROR"; job["error"] = ex.Message;
            }
            lock (Gate) { Jobs[id] = job; activeId = null; }
        }

        private static void Persist(JObject job)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Journal));
            var temp = Journal + ".tmp";
            File.WriteAllText(temp, job.ToString(Formatting.None));
            if (File.Exists(Journal)) File.Replace(temp, Journal, null);
            else File.Move(temp, Journal);
        }

        private static Dictionary<string, string> CaptureProjectSettings()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProjectSettings"));
            using (var sha = SHA256.Create())
                return Directory.GetFiles(root).ToDictionary(path => "ProjectSettings/" + Path.GetFileName(path),
                    path => Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))));
        }

        private static string Success(string id, JObject result) =>
            new JObject { ["id"] = id, ["status"] = "success", ["result"] = result }.ToString(Formatting.None);

        private static string Error(string id, string code, string message, JObject details = null) =>
            new JObject { ["id"] = id, ["status"] = "error", ["code"] = code, ["error"] = message, ["details"] = details }.ToString(Formatting.None);
    }
}
