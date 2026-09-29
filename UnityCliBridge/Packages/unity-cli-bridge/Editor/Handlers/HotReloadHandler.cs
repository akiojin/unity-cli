using System;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Handlers
{
    // Do not reference FSR or its Roslyn assemblies from the always-loaded Bridge.
    public static class HotReloadHandler
    {
        private const string AdapterName =
            "UnityCliBridge.HotReload.FastScriptReloadAdapter, UnityCliBridge.HotReload.Editor";

        public static object Status()
        {
            try
            {
                var adapter = Type.GetType(AdapterName);
                return adapter == null ? Unavailable(false) :
                    adapter.GetMethod("Status", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            }
            catch (Exception exception) { return Failure(exception); }
        }

        public static async Task<object> Handle(JObject parameters)
        {
            try
            {
                var adapter = Type.GetType(AdapterName);
                if (adapter == null) return Unavailable(true);
                return await (Task<object>)adapter.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { parameters ?? new JObject() });
            }
            catch (Exception exception) { return Failure(exception); }
        }

        private static object Unavailable(bool operation)
        {
            var installed = Type.GetType("FastScriptReload.Runtime.AssemblyChangesLoader, FastScriptReload.Runtime") != null;
            var reason = installed
                ? "The optional adapter requires FastScriptReload 1.8.0. Check package version and Editor compilation diagnostics."
                : "Install optional MIT package com.handzlikchris.fastscriptreload 1.8.0 to preview C# method changes.";
            var result = new JObject
            {
                ["success"] = !operation,
                ["supported"] = false,
                ["state"] = "unavailable",
                ["code"] = installed ? "HOT_RELOAD_ADAPTER_UNAVAILABLE" : "HOT_RELOAD_PACKAGE_MISSING",
                ["reason"] = reason,
                ["backend"] = "FastScriptReload",
                ["requiredVersion"] = "1.8.0",
                ["requiredRuntime"] = "Unity Editor 2022.3+ / Mono / x64; Apple Silicon Editor is unsupported",
                ["appliedRevision"] = null,
                ["recoveryRequired"] = false
            };
            if (operation) result["error"] = reason;
            return result;
        }

        private static object Failure(Exception exception)
        {
            var cause = (exception as TargetInvocationException)?.InnerException ?? exception;
            return new
            {
                success = false, error = cause.Message, code = "HOT_RELOAD_ADAPTER_ERROR",
                appliedRevision = (string)null, recoveryRequired = true,
                recovery = "Stop Play Mode and run normal script compilation with Domain Reload before retrying."
            };
        }
    }
}
