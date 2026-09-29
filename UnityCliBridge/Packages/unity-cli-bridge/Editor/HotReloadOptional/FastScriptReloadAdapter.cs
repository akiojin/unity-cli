using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FastScriptReload.Editor;
using FastScriptReload.Editor.Compilation;
using FastScriptReload.Runtime;
using ImmersiveVRTools.Runtime.Common;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityCliBridge.HotReload
{
    [InitializeOnLoad]
    public static class FastScriptReloadAdapter
    {
        const string DirtyKey = "UnityCliBridge.HotReload.Dirty";
        const string RecoverKey = "UnityCliBridge.HotReload.RecoverRequested";
        const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        static Session session;
        static bool busy;
        static int generation;
        static volatile ExecutionObservations observations;
        static bool recoveryRequired;
        static string requestedRevision, lastTransaction;
        static string[] lastApplied = new string[0], lastFailed = new string[0], lastUnverified = new string[0];
        static Task<CompileResult> activeCompilation;
        static bool compilationSinceDomainLoad, recoveryPending;
        static Dictionary<string, Dictionary<string, string>> compilingSources;
        static Dictionary<string, string> compilingOutputs;
        static bool compilationCompleted, compilationHadErrors;
        static bool cleanCompilationRequested, compilingExplicitCleanBuild;
        static readonly HashSet<string> successfullyCompiledAssemblies = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<string, Provenance> provenance = new Dictionary<string, Provenance>(StringComparer.Ordinal);
        static string LedgerPath => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Library/UnityCliHotReloadProvenance.json"));
        const string RecoveryGuidance = "Run action=recover, wait for Play Mode to stop and a clean compilation/domain reload, then begin a new session. Preview source is unsaved; Play Mode does not restart automatically.";

        sealed class Session
        {
            public string Path, Baseline, BaselineRevision, AppliedSource, AppliedRevision;
            public Type Type;
            public int Generation;
        }
        sealed class Provenance
        {
            public Dictionary<string, string> Sources;
            public string AssemblyHash;
        }

        static FastScriptReloadAdapter()
        {
            // A new domain after explicit recovery is the only point at which recovery completes.
            if (SessionState.GetBool(RecoverKey, false))
            {
                SessionState.SetBool(DirtyKey, false);
                SessionState.SetBool(RecoverKey, false);
            }
            recoveryRequired = SessionState.GetBool(DirtyKey, false);
            try
            {
                if (File.Exists(LedgerPath))
                    foreach (var entry in Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, Provenance>>(File.ReadAllText(LedgerPath)))
                        provenance[entry.Key] = entry.Value;
            }
            catch (Exception) { provenance.Clear(); }
            CompilationPipeline.compilationStarted += _ => CaptureCompilation();
            CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
            {
                var failed = messages.Any(message => message.type == CompilerMessageType.Error);
                compilationHadErrors |= failed;
                if (!failed) successfullyCompiledAssemblies.Add(System.IO.Path.GetFileNameWithoutExtension(path));
            };
            CompilationPipeline.compilationFinished += _ => compilationCompleted = true;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode)
                {
                    generation++;
                    if (session != null) { recoveryRequired = true; SessionState.SetBool(DirtyKey, true); }
                }
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => { generation++; FinishCompilation(); };
        }

        static bool PlatformSupported => IntPtr.Size == 8 &&
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64;

        public static object Status() => new
        {
            available = true,
            backend = "FastScriptReload",
            platformSupported = PlatformSupported,
            supported = PlatformSupported,
            code = PlatformSupported ? null : "HOT_RELOAD_PLATFORM_UNSUPPORTED",
            limitation = PlatformSupported ? null : "Only x64 Editor processes are supported; Apple Silicon ARM64 is unsupported.",
            state = recoveryPending ? "recovery_pending" : busy ? "applying" : recoveryRequired ? "recovery_required" : session == null ? "idle" : "ready",
            baselineRevision = session?.BaselineRevision,
            appliedRevision = session?.AppliedRevision,
            path = session?.Path,
            recoveryRequired,
            requestedRevision,
            transaction = lastTransaction,
            applied = lastApplied,
            failed = lastFailed,
            unverified = lastUnverified,
            recovery = recoveryRequired ? RecoveryGuidance : null
        };

        // Called by injected candidate code only when the game naturally invokes a changed method.
        public static void Observe(string transaction, string method) => observations?.Observe(transaction, method);

        public static async Task<object> Handle(JObject parameters)
        {
            try
            {
                string action = parameters.Value<string>("action") ?? "status";
                if (action == "status") return Status();
                if (action == "recover") return Recover();
                if (!PlatformSupported) return Error("platform_unsupported", "FSR preview currently supports only x64 Editor processes.");
                if (busy) return Error("busy", "A preview transaction is already running.");
                if (recoveryRequired) return Error("recovery_required", "Stop and fully recompile with action=recover before another preview.");
                EnsureExclusive();
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed)
                    return Error("invalid_editor_state", "Enter Play Mode after compilation/import completes.");
                if (action == "begin") return Begin(parameters.Value<string>("path"));
                if (action == "apply") return await Apply(parameters);
                return Error("invalid_action", "Expected begin, apply, status or recover.");
            }
            catch (Exception e)
            {
                var error = e.GetBaseException();
                return Error(error.Data["code"] as string ?? "preview_rejected", error.Message);
            }
        }

        static object Begin(string path)
        {
            if (session != null) return Error("session_exists", "Recover the existing preview session before beginning another.");
            var absolute = ResolvePath(path);
            var baseline = File.ReadAllText(absolute);
            var validation = PreviewValidation.Compare(baseline, baseline, "baseline");
            var assemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(path);
            if (string.IsNullOrEmpty(assemblyName)) throw new InvalidOperationException("The script has no compiled assembly mapping.");
            var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => !a.IsDynamic && a.GetName().Name == System.IO.Path.GetFileNameWithoutExtension(assemblyName));
            var type = assembly?.GetType(validation.TypeName, false);
            if (type == null || type.IsGenericType || type.Assembly.IsDynamic)
                throw new InvalidOperationException("The single script class must map to an existing compiled type.");
            if (type.BaseType != typeof(MonoBehaviour) && type.BaseType != typeof(object))
                throw new InvalidOperationException("Only direct MonoBehaviour subclasses or plain classes are supported.");
            var assemblyPath = assembly.Location;
            if (compilationSinceDomainLoad || !provenance.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(assemblyName), out var proof) ||
                !CompilationProof.Matches(proof.Sources, absolute, assemblyPath, proof.AssemblyHash))
                return Error("baseline_unproven", "No matching source/assembly compilation proof exists. Run action=recover for a clean compilation/domain reload, then enter Play Mode and begin again.");
            if (type.GetMethods(Methods).Any(m => HasDetour(m)))
                throw new InvalidOperationException("This type already has detours. Stop and fully recompile before beginning.");
            session = new Session { Path = path, Baseline = baseline, BaselineRevision = Revision(baseline), AppliedSource = baseline,
                AppliedRevision = Revision(baseline), Type = type, Generation = generation };
            return new { success = true, state = "ready", baselineRevision = session.BaselineRevision, appliedRevision = session.AppliedRevision, path };
        }

        static async Task<object> Apply(JObject parameters)
        {
            if (session == null) return Error("session_required", "Begin a preview session first.");
            var current = session;
            Guard(current);
            if (parameters.Value<string>("expectedRevision") != current.AppliedRevision)
                return Error("stale_revision", "expectedRevision must match the last verified appliedRevision.");
            var source = parameters.Value<string>("source");
            var transaction = Guid.NewGuid().ToString("N");
            var change = PreviewValidation.Compare(current.AppliedSource, source, transaction);
            if (change.Methods.Length == 0)
                return new { success = true, state = "unchanged", appliedRevision = current.AppliedRevision, applied = new string[0], failed = new string[0], unverified = new string[0] };
            var timeout = parameters.Value<double?>("timeoutSeconds") ?? 10;
            if (double.IsNaN(timeout) || double.IsInfinity(timeout) || timeout < 1 || timeout > 60)
                return Error("invalid_timeout", "timeoutSeconds must be between 1 and 60.");
            var originals = current.Type.GetMethods(Methods).ToDictionary(method => method.Name, method => method);
            if (change.Methods.Any(name => !originals.ContainsKey(name)) || originals.Values.Any(m => m.IsGenericMethod || m.IsAbstract || m.GetMethodBody() == null))
                throw new InvalidOperationException("Every changed method must map unambiguously to a compiled method body.");
            var tempDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unity-cli-hot-reload", transaction);
            Directory.CreateDirectory(tempDirectory);
            var tempPath = System.IO.Path.Combine(tempDirectory, System.IO.Path.GetFileName(current.Path));
            File.WriteAllText(tempPath, change.InstrumentedSource);
            busy = true;
            requestedRevision = Revision(source);
            lastTransaction = transaction;
            lastApplied = lastFailed = new string[0];
            lastUnverified = change.Methods;
            var applied = new List<string>();
            var failed = new List<string>();
            var patchAttempted = false;
            Dictionary<string, MethodInfo> targets = null;
            Task<CompileResult> compiling = null;
            observations = new ExecutionObservations(transaction, change.Methods);
            try
            {
                var dispatcher = UnityMainThreadDispatcher.Instance.EnsureInitialized();
                compiling = Task.Run(() => DynamicAssemblyCompiler.Compile(new List<string> { tempPath }, dispatcher));
                activeCompilation = compiling;
                var deadline = DateTime.UtcNow.AddSeconds(timeout);
                while (!compiling.IsCompleted)
                {
                    Guard(current);
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("Candidate compilation timed out; no late patch will be applied.");
                    await Task.Delay(25);
                }
                var result = await compiling;
                Guard(current);
                if (result.IsError || result.CompiledAssembly == null)
                    throw new InvalidOperationException("Candidate compilation failed: " + string.Join("\n", result.MessagesFromCompilerProcess));
                var patchedType = result.CompiledAssembly.GetType(current.Type.FullName + AssemblyChangesLoader.ClassnamePatchedPostfix, false);
                if (patchedType == null) throw new InvalidOperationException("FSR candidate type mapping was not found.");
                var candidateMethods = patchedType.GetMethods(Methods);
                targets = originals.Keys.ToDictionary(name => name, name => candidateMethods.SingleOrDefault(m => m.Name == name));
                if (targets.Values.Any(m => m == null) || candidateMethods.Length != originals.Count)
                    throw new InvalidOperationException("FSR candidate methods could not be mapped exactly to every original method.");
                patchAttempted = true;
                SessionState.SetBool(DirtyKey, true);
                current.AppliedRevision = null;
                AssemblyChangesLoader.Instance.DynamicallyUpdateMethodsForCreatedAssembly(result.CompiledAssembly, new AssemblyChangesLoaderEditorOptionsNeededInBuild(false, false));
                foreach (var name in originals.Keys)
                {
                    if (DetourMatches(originals[name], targets[name])) applied.Add(name); else failed.Add(name);
                }
                if (failed.Count != 0) throw new InvalidOperationException("FSR did not install every requested detour.");
                while (observations.Unverified().Length != 0)
                {
                    Guard(current);
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("Changed methods were not all naturally executed before the verification deadline.");
                    await Task.Delay(25);
                }
                Guard(current);
                // Recheck ownership after execution; another patcher may have displaced a target.
                if (originals.Keys.Any(name => !DetourMatches(originals[name], targets[name])))
                    throw new InvalidOperationException("A detour changed during execution verification.");
                current.AppliedSource = source;
                current.AppliedRevision = Revision(source);
                lastApplied = applied.ToArray();
                lastFailed = failed.ToArray();
                lastUnverified = observations.Unverified();
                return new { success = true, state = "applied", transaction, baselineRevision = current.BaselineRevision,
                    appliedRevision = current.AppliedRevision, applied = applied.ToArray(), failed = failed.ToArray(), unverified = observations.Unverified() };
            }
            catch (Exception e)
            {
                recoveryRequired = true;
                SessionState.SetBool(DirtyKey, true);
                if (patchAttempted) current.AppliedRevision = null;
                var inspectionFailures = new List<string>();
                applied.Clear();
                if (patchAttempted && targets != null)
                    foreach (var name in originals.Keys)
                    {
                        try { if (DetourMatches(originals[name], targets[name])) applied.Add(name); }
                        catch (Exception) { inspectionFailures.Add(name); }
                    }
                lastApplied = applied.ToArray();
                lastFailed = originals.Keys.Except(applied).ToArray();
                lastUnverified = observations.Unverified().Union(inspectionFailures).ToArray();
                return new { success = false, error = "verification_failed", code = "HOT_RELOAD_VERIFICATION_FAILED", message = e.GetBaseException().Message, state = "recovery_required", transaction,
                    requestedRevision, appliedRevision = current.AppliedRevision, applied = lastApplied, failed = lastFailed,
                    unverified = lastUnverified, recoveryRequired = true, recovery = RecoveryGuidance };
            }
            finally
            {
                observations = null;
                busy = false;
                // FSR has no cancellation API: ignore abandoned results and wait for its reader before deleting.
                if (compiling != null && !compiling.IsCompleted)
                    _ = compiling.ContinueWith(task => { var ignored = task.Exception; Cleanup(tempDirectory); }, TaskScheduler.Default);
                else Cleanup(tempDirectory);
            }
        }

        static void Guard(Session current)
        {
            if (current != session || current.Generation != generation || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("The Play Mode/compilation session changed; recover before continuing.");
            EnsureExclusive();
            if (Revision(File.ReadAllText(ResolvePath(current.Path))) != current.BaselineRevision)
                throw new InvalidOperationException("Disk source changed outside this preview. Recover and compile the disk source before continuing.");
        }

        static void EnsureExclusive()
        {
            if ((bool)FastScriptReloadPreference.EnableAutoReloadForChangedFiles.GetEditorPersistedValueOrDefault() ||
                (bool)FastScriptReloadPreference.EnableOnDemandReload.GetEditorPersistedValueOrDefault())
                throw new InvalidOperationException("Disable Fast Script Reload auto reload and on-demand reload in its settings before beginning. Existing preferences are never changed automatically.");
        }

        static object Recover()
        {
            if (recoveryPending) return new { success = true, state = "recovery_pending", recoveryRequired = true, recovery = RecoveryGuidance };
            generation++;
            recoveryRequired = true;
            recoveryPending = true;
            SessionState.SetBool(DirtyKey, true);
            if (busy || (activeCompilation != null && !activeCompilation.IsCompleted))
            {
                EditorApplication.update += DrainBeforeRecovery;
                return new { success = true, state = "recovery_pending", recoveryRequired = true,
                    message = "Waiting for the candidate compiler to drain before stopping Play Mode. Its result will not be applied.", recovery = RecoveryGuidance };
            }
            RequestRecovery();
            return new { success = true, state = "recovery_requested", recoveryRequired = true,
                message = "Stopping Play Mode and requesting a clean script compilation/domain reload. Unsaved preview source is discarded; Play Mode will not restart.", recovery = RecoveryGuidance };
        }

        static void DrainBeforeRecovery()
        {
            if (busy || (activeCompilation != null && !activeCompilation.IsCompleted)) return;
            EditorApplication.update -= DrainBeforeRecovery;
            RequestRecovery();
        }

        static void RequestRecovery()
        {
            Action request = () =>
            {
                SessionState.SetBool(RecoverKey, true);
                cleanCompilationRequested = true;
                CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
            };
            if (EditorApplication.isPlaying)
            {
                Action<PlayModeStateChange> stopped = null;
                stopped = state => { if (state != PlayModeStateChange.EnteredEditMode) return; EditorApplication.playModeStateChanged -= stopped; request(); };
                EditorApplication.playModeStateChanged += stopped;
                EditorApplication.isPlaying = false;
            }
            else EditorApplication.delayCall += () => request();
        }

        static void CaptureCompilation()
        {
            generation++;
            compilationSinceDomainLoad = true;
            compilationCompleted = compilationHadErrors = false;
            compilingExplicitCleanBuild = cleanCompilationRequested;
            cleanCompilationRequested = false;
            successfullyCompiledAssemblies.Clear();
            provenance.Clear();
            try { File.WriteAllText(LedgerPath, "{}"); }
            catch (IOException e) { Debug.LogWarning("Hot reload compilation proof could not be invalidated: " + e.Message); }
            catch (UnauthorizedAccessException e) { Debug.LogWarning("Hot reload compilation proof could not be invalidated: " + e.Message); }
            compilingSources = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            compilingOutputs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var assembly in CompilationPipeline.GetAssemblies())
            {
                try
                {
                    compilingSources[assembly.name] = CompilationProof.Capture(assembly.sourceFiles);
                    compilingOutputs[assembly.name] = assembly.outputPath;
                }
                catch (IOException) { provenance.Remove(assembly.name); }
                catch (UnauthorizedAccessException) { provenance.Remove(assembly.name); }
            }
        }

        static void FinishCompilation()
        {
            // Clean builds can omit per-assembly events, and scriptCompilationFailed is stale
            // inside compilationFinished. The subsequent assembly reload is the commit boundary.
            if (!compilationCompleted || compilingSources == null || compilingOutputs == null) return;
            foreach (var name in compilingSources.Keys)
            {
                provenance.Remove(name);
                try
                {
                    var captured = compilingSources[name];
                    if (!compilationHadErrors && !EditorUtility.scriptCompilationFailed &&
                        CompilationProof.CanCommit(compilingExplicitCleanBuild, successfullyCompiledAssemblies, name) &&
                        compilingOutputs.TryGetValue(name, out var output) &&
                        File.Exists(output) && captured.All(pair => File.Exists(pair.Key) && CompilationProof.HashFile(pair.Key) == pair.Value))
                        provenance[name] = new Provenance { Sources = captured, AssemblyHash = CompilationProof.HashFile(output) };
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try { File.WriteAllText(LedgerPath, Newtonsoft.Json.JsonConvert.SerializeObject(provenance)); }
            catch (IOException e) { Debug.LogWarning("Hot reload compilation proof could not be saved: " + e.Message); }
            catch (UnauthorizedAccessException e) { Debug.LogWarning("Hot reload compilation proof could not be saved: " + e.Message); }
            compilingSources = null;
            compilingOutputs = null;
        }

        static IDictionary Detours()
        {
            var patchTools = typeof(HarmonyLib.Harmony).Assembly.GetType("HarmonyLib.PatchTools", true);
            var field = patchTools.GetField("detours", BindingFlags.Static | BindingFlags.NonPublic);
            return field?.GetValue(null) as IDictionary ?? throw new InvalidOperationException("The installed Harmony detour verification API is incompatible.");
        }
        static bool HasDetour(MethodInfo original) { var table = Detours(); lock (table) return table.Contains(original); }
        static bool DetourMatches(MethodInfo original, MethodInfo target)
        {
            var table = Detours();
            lock (table)
            {
                var detour = table[original];
                if (detour == null) return false;
                var contracts = detour.GetType().GetInterfaces();
                object Read(string name) => contracts.Select(t => t.GetProperty(name)).FirstOrDefault(p => p != null)?.GetValue(detour);
                return Equals(Read("Source"), original) && Equals(Read("Target"), target) && Equals(Read("IsApplied"), true);
            }
        }
        static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("path must name a single Assets/*.cs source file.");
            var root = System.IO.Path.GetFullPath(Application.dataPath);
            var absolute = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path.Substring(7)));
            if (!absolute.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(absolute))
                throw new InvalidOperationException("Source path must remain inside Assets and exist.");
            for (var item = absolute; item != root; item = System.IO.Path.GetDirectoryName(item))
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Symbolic source paths are unsupported.");
            return absolute;
        }
        static string Revision(string source)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source))).Replace("-", "").ToLowerInvariant();
        }
        static object Error(string code, string message) => new { success = false, error = code,
            code = code.StartsWith("HOT_RELOAD_", StringComparison.Ordinal) ? code : "HOT_RELOAD_" + code.ToUpperInvariant(),
            message, recoveryRequired, recovery = recoveryRequired ? RecoveryGuidance : null };
        static void Cleanup(string directory) { try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
