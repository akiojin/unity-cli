using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
#if UNITY_INCLUDE_TESTS
using UnityEditor.TestTools.TestRunner.Api;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Test Runner execution for automated testing via the Unity CLI Bridge
    /// Implements SPEC-e7c9b50c: Unity Test Execution Feature
    /// </summary>
    public static class TestExecutionHandler
    {
#if UNITY_INCLUDE_TESTS
        private static TestRunnerApi testRunnerApi;
        private static TestResultCollector currentCollector;
        private static bool isTestRunning;
        private static string currentTestMode;
        private static string currentRunId;
        private static DateTime? runStartedAtUtc;
        private static DateTime? runLastUpdateUtc;
        private static string RunStatePath => Path.GetFullPath(Path.Combine(GetWorkspaceRoot(), ".unity/tests/test-run-state.json"));
        private static bool playModeOptionsPatched;
        private static bool prevEnterPlayModeOptionsEnabled;
        private static EnterPlayModeOptions prevEnterPlayModeOptions;
        internal static Func<bool> DirtySceneDetector = DetectDirtyScenes;
        internal static Func<bool> PlayModeDetector = () => Application.isPlaying;
        private static readonly string DefaultResultsFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.unity/test-results"));
        private static string lastResultPath;
        private static JObject lastResultSummary;
        private static DateTime? lastResultTimestampUtc;

        /// <summary>
        /// Test result structure
        /// </summary>
        public class TestResultData
        {
            public string name;
            public string fullName;
            public string status;
            public double duration;
            public string message;
            public string stackTrace;
            public string output;
        }

        /// <summary>
        /// Execute Unity tests based on specified filters and modes.
        /// </summary>
        public static object RunTests(JObject parameters)
        {
            try
            {
                string testMode = parameters["testMode"]?.ToString() ?? "EditMode";
                string filter = parameters["filter"]?.ToString();
                string category = parameters["category"]?.ToString();
                string namespaceFilter = parameters["namespace"]?.ToString();
                bool includeDetails = parameters["includeDetails"]?.ToObject<bool>() ?? false;
                string exportPath = parameters["exportPath"]?.ToString();
                string resolvedExportPath = ResolveExportPath(exportPath, testMode);

                if (testMode != "EditMode" && testMode != "PlayMode" && testMode != "All")
                {
                    return new { error = "Invalid testMode. Must be EditMode, PlayMode, or All" };
                }

                // Block test run while compiling or when compile errors exist
                var compResultObj = CompilationHandler.GetCompilationState(new JObject { ["includeMessages"] = false });
                var compResult = compResultObj != null ? JObject.FromObject(compResultObj) : null;
                bool isCompiling = compResult?["isCompiling"]?.ToObject<bool>() ?? false;
                int errorCount = compResult?["errorCount"]?.ToObject<int>() ?? 0;
                string lastCompilationTime = compResult?["lastCompilationTime"]?.ToString();

                if (isCompiling)
                {
                    return new
                    {
                        error = "Cannot run tests while compilation is in progress.",
                        code = "COMPILING",
                        lastCompilationTime
                    };
                }

                if (errorCount > 0)
                {
                    return new
                    {
                        error = $"Cannot run tests because the last compilation has {errorCount} error(s).",
                        code = "COMPILATION_ERRORS",
                        errorCount,
                        lastCompilationTime
                    };
                }

                if (PlayModeDetector?.Invoke() ?? Application.isPlaying)
                {
                    return new { error = "Cannot run tests while Play Mode is active. Exit Play Mode before running tests." };
                }

                if (DirtySceneDetector())
                {
                    return new { error = "There are unsaved scene changes. Please save or discard your changes before running tests." };
                }

                // Save current scene to avoid "Save Scene" dialog after tests
                var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                if (activeScene.isDirty)
                {
                    if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(activeScene))
                    {
                        BridgeLogger.LogWarning("TestExecutionHandler", "Failed to save scene before test execution. Scene save dialog may appear.");
                    }
                }

                // Cancel previous execution if running (no manual execution expected)
                if (isTestRunning && currentCollector != null && testRunnerApi != null)
                {
                    testRunnerApi.UnregisterCallbacks(currentCollector);
                    isTestRunning = false;
                }

                if (testRunnerApi == null)
                {
                    testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
                }

                var filterSettings = new Filter
                {
                    testMode = ParseTestMode(testMode)
                };

                if (!string.IsNullOrEmpty(filter))
                {
                    filterSettings.testNames = new[] { filter };
                }

                if (!string.IsNullOrEmpty(category))
                {
                    filterSettings.categoryNames = new[] { category };
                }

                if (!string.IsNullOrEmpty(namespaceFilter))
                {
                    filterSettings.assemblyNames = new[] { namespaceFilter };
                }

                // Reduce domain reload impact during tests that may enter Play Mode.
                // Even EditMode tests can enter Play Mode via EnterPlayMode/ExitPlayMode (UnityTest).
                ApplyEnterPlayModeOptionsPatch();

                currentCollector = new TestResultCollector(resolvedExportPath, includeDetails, testMode);
                var collector = currentCollector;
                currentTestMode = testMode;
                currentRunId = Guid.NewGuid().ToString();
                runStartedAtUtc = DateTime.UtcNow;
                runLastUpdateUtc = runStartedAtUtc;
                SaveRunState("running");
                testRunnerApi.RegisterCallbacks(collector);

                isTestRunning = true;

                testRunnerApi.Execute(new ExecutionSettings(filterSettings));

                // Return immediately - test runs asynchronously
                return new
                {
                    status = "running",
                    runId = currentRunId,
                    message = "Test execution started. Use get_test_status to check progress."
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("TestExecutionHandler", $"Error running tests: {e.Message}\\n{e.StackTrace}");
                isTestRunning = false;
                return new { error = $"Failed to run tests: {e.Message}" };
            }
        }

        /// <summary>
        /// Get current test execution status and results
        /// </summary>
        public static object GetTestStatus(JObject parameters)
        {
            try
            {
                bool includeExportedResults = parameters?["includeTestResults"]?.ToObject<bool>() ?? false;
                bool includeFileContent = parameters?["includeFileContent"]?.ToObject<bool>() ?? false;

                if (isTestRunning)
                {
                    // Watchdog: if Play Mode already exited and no updates for >10s, abort
                    var elapsedSinceLast = runLastUpdateUtc.HasValue
                        ? (DateTime.UtcNow - runLastUpdateUtc.Value).TotalSeconds
                        : 0;
                    if (!(Application.isPlaying) && elapsedSinceLast > 10)
                    {
                        isTestRunning = false;
                        currentCollector = null;
                        SaveRunState("error", "RUNNER_TIMEOUT");
                        return new
                        {
                            status = "error",
                            code = "RUNNER_TIMEOUT",
                            message = "PlayMode ended but test runner did not finish within watchdog window.",
                            testMode = currentTestMode,
                            runId = currentRunId,
                            elapsedSeconds = (runStartedAtUtc.HasValue ? (DateTime.UtcNow - runStartedAtUtc.Value).TotalSeconds : (double?)null),
                            watchdogSeconds = elapsedSinceLast
                        };
                    }

                    SaveRunState("running");

	                    return new
	                    {
	                        status = "running",
	                        message = "Test execution in progress",
	                        testMode = currentTestMode,
	                        runId = currentRunId,
	                        source = "run_tests",
	                        elapsedSeconds = runStartedAtUtc.HasValue ? (DateTime.UtcNow - runStartedAtUtc.Value).TotalSeconds : (double?)null
	                    };
	                }

                if (currentCollector == null)
                {
                    var persisted = LoadRunState();
                    if (persisted != null && persisted.status == "running")
                    {
	                        return new
	                        {
	                            status = "running",
	                            message = "Test execution in progress (persisted state)",
	                            testMode = persisted.testMode,
	                            runId = persisted.runId,
	                            source = "run_tests",
	                            persisted = true,
	                            elapsedSeconds = persisted.runStartedAt.HasValue ? (DateTime.UtcNow - persisted.runStartedAt.Value).TotalSeconds : (double?)null
	                        };
	                    }

                    return new
                    {
                        status = "idle",
                        message = "No test execution in progress or completed",
                        runId = currentRunId,
                        testMode = currentTestMode
                    };
                }

                // Test execution completed - return results
                var collector = currentCollector;

                var completed = new Dictionary<string, object>
                {
                    ["status"] = "completed",
                    ["success"] = collector.FailedTests.Count == 0,
                    ["totalTests"] = collector.TotalTests,
                    ["passedTests"] = collector.PassedTests.Count,
                    ["failedTests"] = collector.FailedTests.Count,
                    ["skippedTests"] = collector.SkippedTests.Count,
                    ["inconclusiveTests"] = collector.InconclusiveTests.Count,
                    ["runId"] = currentRunId,
                    ["testMode"] = currentTestMode,
                    ["failures"] = collector.FailedTests.Select(t => new
                    {
                        testName = t.fullName,
                        message = t.message,
                        stackTrace = t.stackTrace
                    }).ToList(),
                    ["tests"] = collector.AllResults.Select(t => new
                    {
                        name = t.name,
                        fullName = t.fullName,
                        status = t.status,
                        duration = t.duration,
                        message = t.message,
                        output = t.output
                    }).ToList()
                };

                if (includeExportedResults)
                {
                    completed["latestResult"] = BuildLatestResult(includeFileContent);
                }

                ClearRunState();
                return completed;
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("TestExecutionHandler", $"Error getting test status: {e.Message}");
                return new { status = "error", error = $"Failed to get test status: {e.Message}" };
            }
        }

        /// <summary>
        /// Returns last exported test results (summary + optional file content)
        /// </summary>
        public static object GetLastTestResults(JObject parameters)
        {
            try
            {
                if (string.IsNullOrEmpty(lastResultPath) || !File.Exists(lastResultPath))
                {
                    return new
                    {
                        status = "missing",
                        message = "No exported test results are available yet. Run tests first."
                    };
                }

                bool includeFileContent = parameters?["includeFileContent"]?.ToObject<bool>() ?? true;
                string fileContent = includeFileContent ? File.ReadAllText(lastResultPath) : null;

                return new
                {
                    status = "available",
                    path = lastResultPath,
                    generatedAt = lastResultTimestampUtc?.ToString("o"),
                    summary = lastResultSummary ?? JObject.Parse(File.ReadAllText(lastResultPath)),
                    fileContent
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("TestExecutionHandler", $"Error reading exported test results: {e.Message}");
                return new { status = "error", error = $"Failed to read test results: {e.Message}" };
            }
        }

        private static TestMode ParseTestMode(string testMode)
        {
            switch (testMode)
            {
                case "EditMode":
                    return TestMode.EditMode;
                case "PlayMode":
                    return TestMode.PlayMode;
                case "All":
                    return TestMode.EditMode | TestMode.PlayMode;
                default:
                    return TestMode.EditMode;
            }
        }

        private static string ResolveExportPath(string exportPath, string testMode)
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string targetPath = exportPath;

                if (string.IsNullOrEmpty(targetPath))
                {
                    Directory.CreateDirectory(DefaultResultsFolder);
                    var fileName = $"TestResults_{testMode}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                    targetPath = Path.Combine(DefaultResultsFolder, fileName);
                }
                else
                {
                    if (!Path.IsPathRooted(targetPath))
                    {
                        targetPath = Path.Combine(projectRoot, targetPath);
                    }

                    var directory = Path.GetDirectoryName(targetPath);
                    if (string.IsNullOrEmpty(directory))
                    {
                        directory = DefaultResultsFolder;
                        targetPath = Path.Combine(directory, targetPath);
                    }

                    Directory.CreateDirectory(directory);

                    if (string.IsNullOrEmpty(Path.GetExtension(targetPath)))
                    {
                        targetPath = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".json";
                    }
                }

                return Path.GetFullPath(targetPath);
            }
            catch (Exception ex)
            {
                BridgeLogger.LogWarning("TestExecutionHandler", $"Failed to resolve export path '{exportPath}': {ex.Message}. Using default folder.");
                Directory.CreateDirectory(DefaultResultsFolder);
                var fallback = Path.Combine(DefaultResultsFolder, $"TestResults_{testMode}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                return Path.GetFullPath(fallback);
            }
        }

        private static bool DetectDirtyScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void ResetForTesting()
        {
            DirtySceneDetector = DetectDirtyScenes;
            PlayModeDetector = () => Application.isPlaying;
            lastResultPath = null;
            lastResultSummary = null;
            lastResultTimestampUtc = null;
        }

        internal static void OnResultsExported(string exportPath, JObject summary)
        {
            lastResultPath = exportPath;
            lastResultSummary = summary == null ? null : (JObject)summary.DeepClone();
            lastResultTimestampUtc = DateTime.UtcNow;
        }

        private static void ApplyEnterPlayModeOptionsPatch()
        {
            if (playModeOptionsPatched) return;
            prevEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            prevEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;

            // Enable Enter Play Mode Options and disable domain reload to keep callbacks alive during tests
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            playModeOptionsPatched = true;
        }

        private static void RestoreEnterPlayModeOptions()
        {
            if (!playModeOptionsPatched) return;
            EditorSettings.enterPlayModeOptionsEnabled = prevEnterPlayModeOptionsEnabled;
            EditorSettings.enterPlayModeOptions = prevEnterPlayModeOptions;
            playModeOptionsPatched = false;
        }

        private static object BuildLatestResult(bool includeFileContent)
        {
            if (string.IsNullOrEmpty(lastResultPath) || !File.Exists(lastResultPath) || lastResultSummary == null)
            {
                return new
                {
                    status = "missing",
                    message = "No exported test results are available yet. Run tests first."
                };
            }

            string content = null;
            if (includeFileContent)
            {
                try
                {
                    content = File.ReadAllText(lastResultPath);
                }
                catch (Exception ex)
                {
                    BridgeLogger.LogWarning("TestExecutionHandler", $"Failed to read test results file '{lastResultPath}': {ex.Message}");
                }
            }

            return new
            {
                status = "available",
                path = lastResultPath,
                generatedAt = lastResultTimestampUtc?.ToString("o"),
                summary = lastResultSummary,
                fileContent = content
            };
        }

        private class PersistedRunState
        {
            public string runId;
            public string testMode;
            public string status;
            public DateTime? runStartedAt;
            public DateTime? lastUpdate;
            public string code;
        }

        private static void SaveRunState(string status, string code = null)
        {
            try
            {
                var path = RunStatePath;
                var state = new PersistedRunState
                {
                    runId = currentRunId,
                    testMode = currentTestMode,
                    status = status,
                    runStartedAt = runStartedAtUtc,
                    lastUpdate = runLastUpdateUtc,
                    code = code
                };
                var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, json);
                BridgeLogger.Log("TestExecutionHandler", $"Persisted run state to {path} (status={status})");
            }
            catch (Exception ex)
            {
                BridgeLogger.LogWarning("TestExecutionHandler", $"Failed to persist run state: {ex.Message}");
            }
        }

        private static PersistedRunState LoadRunState()
        {
            try
            {
                var path = RunStatePath;
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                var state = JsonConvert.DeserializeObject<PersistedRunState>(json);
                BridgeLogger.Log("TestExecutionHandler", $"Loaded persisted run state from {path}: {state?.status} runId={state?.runId}");
                return state;
            }
            catch (Exception ex)
            {
                BridgeLogger.LogWarning("TestExecutionHandler", $"Failed to load run state: {ex.Message}");
                return null;
            }
        }

        private static void ClearRunState()
        {
            try
            {
                var path = RunStatePath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                    BridgeLogger.Log("TestExecutionHandler", $"Cleared persisted run state at {path}");
                }
            }
            catch { }
        }

        private static string ResolveRunStatePath()
        {
            return RunStatePath;
        }

        private static string GetWorkspaceRoot()
        {
            try
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
            catch
            {
                return Environment.CurrentDirectory;
            }
        }

        private class TestResultCollector : ICallbacks
        {
            private readonly string exportPath;
            private readonly bool includeDetailsInFile;
            private readonly string testMode;
            private DateTime runStartedAtUtc;

            public TestResultCollector(string exportPath, bool includeDetailsInFile, string testMode)
            {
                this.exportPath = exportPath;
                this.includeDetailsInFile = includeDetailsInFile;
                this.testMode = testMode;
            }

            public int TotalTests { get; private set; }
            public List<TestResultData> PassedTests { get; } = new List<TestResultData>();
            public List<TestResultData> FailedTests { get; } = new List<TestResultData>();
            public List<TestResultData> SkippedTests { get; } = new List<TestResultData>();
            public List<TestResultData> InconclusiveTests { get; } = new List<TestResultData>();
            public List<TestResultData> AllResults { get; } = new List<TestResultData>();

            public void RunStarted(ITestAdaptor testsToRun)
            {
                runStartedAtUtc = DateTime.UtcNow;
                runLastUpdateUtc = runStartedAtUtc;
                TotalTests = CountTests(testsToRun);
                BridgeLogger.Log("TestExecutionHandler", $"Starting test run with {TotalTests} tests");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                isTestRunning = false;
                runLastUpdateUtc = DateTime.UtcNow;
                BridgeLogger.Log("TestExecutionHandler", $"Test run finished. Passed: {PassedTests.Count}, Failed: {FailedTests.Count}");
                ExportResults(result);
                RestoreEnterPlayModeOptions();
            }

            public void TestStarted(ITestAdaptor test)
            {
                BridgeLogger.Log("TestExecutionHandler", $"Test started: {test.FullName}");
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                BridgeLogger.Log("TestExecutionHandler", $"Test finished: {result.Test.FullName} [{result.TestStatus}]");
                runLastUpdateUtc = DateTime.UtcNow;

                var testResult = new TestResultData
                {
                    name = result.Test.Name,
                    fullName = result.Test.FullName,
                    status = result.TestStatus.ToString(),
                    duration = result.Duration,
                    message = result.Message,
                    stackTrace = result.StackTrace,
                    output = result.Output
                };

                AllResults.Add(testResult);

                switch (result.TestStatus)
                {
                    case TestStatus.Passed:
                        PassedTests.Add(testResult);
                        break;
                    case TestStatus.Failed:
                        FailedTests.Add(testResult);
                        break;
                    case TestStatus.Skipped:
                        SkippedTests.Add(testResult);
                        break;
                    case TestStatus.Inconclusive:
                        InconclusiveTests.Add(testResult);
                        break;
                }
            }

            private int CountTests(ITestAdaptor test)
            {
                if (!test.HasChildren)
                {
                    return test.IsSuite ? 0 : 1;
                }

                int count = 0;
                foreach (var child in test.Children)
                {
                    count += CountTests(child);
                }
                return count;
            }

            private void ExportResults(ITestResultAdaptor result)
            {
                if (string.IsNullOrEmpty(exportPath)) return;

                try
                {
                    var generatedAt = DateTime.UtcNow;
                    var summary = new JObject
                    {
                        ["generatedAt"] = generatedAt.ToString("o"),
                        ["runStartedAt"] = runStartedAtUtc.ToString("o"),
                        ["durationSeconds"] = result?.Duration ?? (generatedAt - runStartedAtUtc).TotalSeconds,
                        ["testMode"] = testMode,
                        ["totalTests"] = TotalTests,
                        ["passed"] = PassedTests.Count,
                        ["failed"] = FailedTests.Count,
                        ["skipped"] = SkippedTests.Count,
                        ["inconclusive"] = InconclusiveTests.Count,
                        ["status"] = FailedTests.Count == 0 ? "passed" : "failed"
                    };

                    var failures = FailedTests.Select(t => new JObject
                    {
                        ["name"] = t.name,
                        ["fullName"] = t.fullName,
                        ["message"] = t.message,
                        ["stackTrace"] = t.stackTrace
                    }).ToList();

                    summary["failures"] = new JArray(failures);

                    if (includeDetailsInFile)
                    {
                        summary["tests"] = JArray.FromObject(AllResults);
                    }

                    var directory = Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(exportPath, summary.ToString(Formatting.Indented));
                    TestExecutionHandler.OnResultsExported(exportPath, summary);
                }
                catch (Exception ex)
                {
                    BridgeLogger.LogError("TestExecutionHandler", $"Failed to export test results to '{exportPath}': {ex.Message}");
                }
            }

        }
#else
        /// <summary>
        /// Fallback when Unity Test Framework is unavailable.
        /// </summary>
        public static object RunTests(JObject parameters)
        {
            _ = parameters;
            return new
            {
                error = "Unity Test Framework (com.unity.test-framework) が有効ではありません。テストを実行するにはパッケージを導入し UNITY_INCLUDE_TESTS を定義してください。"
            };
        }

        public static object GetTestStatus(JObject parameters)
        {
            _ = parameters;
            return new
            {
                status = "error",
                error = "Unity Test Framework is not available in this project."
            };
        }

        public static object GetLastTestResults(JObject parameters)
        {
            _ = parameters;
            return new
            {
                status = "error",
                error = "Unity Test Framework is not available in this project."
            };
        }
#endif
    }
}
