using System;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Evaluation
{
    internal static class EvalResult
    {
        internal static string Limit(string value) => value == null || value.Length <= 4096 ? value : value.Substring(0, 4096);
        internal static JObject Create(string id, string state, string message = null) => new JObject
        {
            ["requestId"] = id, ["state"] = state, ["value"] = null, ["logs"] = new JArray(), ["diagnostics"] = new JArray(),
            ["exception"] = message == null ? null : new JObject { ["message"] = message }
        };
        internal static void Fail(JObject result, string state, Exception ex)
        {
            result["state"] = state;
            result["exception"] = new JObject { ["type"] = ex.GetType().FullName, ["message"] = Limit(ex.Message), ["stackTrace"] = Limit(ex.StackTrace) };
        }
    }
}
