using System;
using System.Collections.Generic;
using UnityCliBridge.Evaluation;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Handlers
{
    /// <summary>Trusted, synchronous Editor evaluation. This is not a security sandbox.</summary>
    public static class EvalHandler
    {
        private const int MaxAssemblies = 128;
        private const int MaxResults = 256;
        private const int MaxCodeLength = 65536;
        private static int emittedAssemblies;
        private static int evaluations;
        private static long? memoryAtFirstEvaluation;
        private static bool evaluating;
        private sealed class Entry
        {
            public string Code;
            public string Mode;
            public JObject Result;
        }
        private static readonly Dictionary<string, Entry> Results = new Dictionary<string, Entry>();
        private static readonly Queue<string> ResultOrder = new Queue<string>();

        /// <summary>Counters of this Editor domain; they reset on Domain Reload.</summary>
        public static JObject GetStats(JObject args)
        {
            if (args?["collect"]?.Type == JTokenType.Boolean && (bool)args["collect"]) GC.Collect();
            var memory = GC.GetTotalMemory(false);
            return new JObject
            {
                ["evaluations"] = evaluations,
                ["emittedAssemblies"] = emittedAssemblies,
                ["maxEmittedAssemblies"] = MaxAssemblies,
                ["cachedCompilations"] = EvalCompiler.CachedCompilations,
                ["compileCacheHits"] = EvalCompiler.CacheHits,
                ["referenceCount"] = EvalCompiler.ReferenceCount,
                ["referenceBuilds"] = EvalCompiler.ReferenceBuilds,
                ["warmedUp"] = EvalCompiler.WarmedUp,
                ["storedResults"] = Results.Count,
                ["maxStoredResults"] = MaxResults,
                ["loadedAssemblies"] = AppDomain.CurrentDomain.GetAssemblies().Length,
                ["managedMemoryBytes"] = memory,
                ["managedMemoryGrowthBytes"] = memoryAtFirstEvaluation.HasValue ? memory - memoryAtFirstEvaluation.Value : 0
            };
        }

        public static JObject GetStatus(JObject args)
        {
            var id = args?["requestId"]?.Type == JTokenType.String ? (string)args["requestId"] : null;
            if (string.IsNullOrWhiteSpace(id)) return EvalResult.Create(id, "invalid_request", "requestId is required.");
            return Results.TryGetValue(id, out var entry) ? (JObject)entry.Result.DeepClone() : EvalResult.Create(id, "unknown", "No result in this Editor domain; it may have expired or the domain reloaded.");
        }

        public static JObject Evaluate(JObject args)
        {
            var id = args?["requestId"] == null ? Guid.NewGuid().ToString("N") : args["requestId"].Type == JTokenType.String ? (string)args["requestId"] : null;
            var code = args?["code"]?.Type == JTokenType.String ? (string)args["code"] : null;
            var mode = args?["mode"] == null ? "expression" : args["mode"].Type == JTokenType.String ? (string)args["mode"] : null;
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || string.IsNullOrWhiteSpace(code) || code.Length > MaxCodeLength || (mode != "expression" && mode != "statements"))
                return EvalResult.Create(id, "invalid_request", "Provide code (1–65536 characters), expression/statements mode and an optional requestId (1–128 characters).");
            if (Results.TryGetValue(id, out var previous))
                return previous.Code == code && previous.Mode == mode ? (JObject)previous.Result.DeepClone() : EvalResult.Create(id, "request_conflict", "requestId was already used with different input.");
            if (evaluating || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode)
                return EvalResult.Create(id, "busy", "Editor is evaluating, compiling, refreshing or changing Play Mode.");
            // Identical source reuses its assembly, so only source not compiled in this domain counts against the limit.
            if (emittedAssemblies >= MaxAssemblies && !EvalCompiler.IsCached(code, mode))
                return EvalResult.Create(id, "reload_required", "128 evaluation assemblies have been emitted. Reload the Editor domain before evaluating new source.");

            if (!memoryAtFirstEvaluation.HasValue) memoryAtFirstEvaluation = GC.GetTotalMemory(false);
            evaluations++;
            // Keep the newest results; the oldest request becomes unknown.
            while (Results.Count >= MaxResults) Results.Remove(ResultOrder.Dequeue());
            var result = EvalResult.Create(id, "running");
            Results.Add(id, new Entry { Code = code, Mode = mode, Result = result });
            ResultOrder.Enqueue(id);
            evaluating = true;
            var logs = (JArray)result["logs"];
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (logs.Count < 128) logs.Add(new JObject { ["type"] = type.ToString(), ["message"] = EvalResult.Limit(message), ["stackTrace"] = EvalResult.Limit(stack) });
            };
            Application.logMessageReceived += capture;
            try
            {
                EvalCompiler.Run(code, mode, result, () => emittedAssemblies++);
            }
            catch (Exception ex)
            {
                EvalResult.Fail(result, "runtime_error", ex);
            }
            finally
            {
                Application.logMessageReceived -= capture;
                evaluating = false;
            }
            return (JObject)result.DeepClone();
        }

    }
}
