using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityCliBridge.Core;
using UnityCliBridge.Helpers;
using UnityCliBridge.Logging;
using UnityCliBridge.Models;

namespace UnityCliBridge.Tools
{
    /// <summary>Immutable for one Editor domain; rebuilt automatically after recompilation.</summary>
    [InitializeOnLoad]
    internal sealed class CustomToolRegistry
    {
        private sealed class Entry
        {
            internal MethodInfo Method;
            internal CustomToolParameter[] Parameters;
            internal JObject Descriptor;
        }

        private static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;
        internal static readonly CustomToolRegistry Current;
        private readonly Dictionary<string, Entry> tools = new Dictionary<string, Entry>(StringComparer.Ordinal);

        static CustomToolRegistry()
        {
            Current = new CustomToolRegistry(TypeCache.GetMethodsWithAttribute<UnityCliToolAttribute>(),
                BuiltinToolNames.Names.Concat(BridgeCommandRouter.RegisteredCommandTypes));
        }

        internal CustomToolRegistry(IEnumerable<MethodInfo> methods, IEnumerable<string> reservedNames)
        {
            var reserved = new HashSet<string>(reservedNames, StringComparer.OrdinalIgnoreCase);
            var candidates = methods.Select(method => new { Method = method, Attribute = method.GetCustomAttribute<UnityCliToolAttribute>() })
                .Where(candidate => candidate.Attribute != null)
                .GroupBy(candidate => candidate.Attribute.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.Ordinal);
            foreach (var group in candidates)
            {
                var name = group.Key;
                try
                {
                    if (reserved.Contains(name)) throw new ArgumentException("name is reserved by a builtin tool");
                    if (group.Count() != 1) throw new ArgumentException("multiple methods declare the same name");
                    if (name.Length == 0 || name[0] < 'a' || name[0] > 'z'
                        || name.Any(c => !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_'))
                        throw new ArgumentException("name must match [a-z][a-z0-9_]*");
                    var candidate = group.Single();
                    var method = candidate.Method;
                    if (!method.IsPublic || !method.IsStatic || method.ContainsGenericParameters
                        || method.DeclaringType.ContainsGenericParameters
                        || typeof(Task).IsAssignableFrom(method.ReturnType)
                        || typeof(System.Collections.IEnumerator).IsAssignableFrom(method.ReturnType)
                        || method.IsDefined(typeof(AsyncStateMachineAttribute), false))
                        throw new ArgumentException("method must be public static, synchronous and non-generic");
                    var parameters = method.GetParameters().Select(parameter => new CustomToolParameter(parameter)).ToArray();
                    var properties = new JObject();
                    foreach (var parameter in parameters) properties[parameter.Name] = parameter.Schema;
                    tools.Add(name, new Entry
                    {
                        Method = method,
                        Parameters = parameters,
                        Descriptor = new JObject
                        {
                            ["name"] = name,
                            ["description"] = candidate.Attribute.Description ?? method.Name,
                            ["source"] = "custom", ["executor"] = "remote",
                            ["mutating"] = candidate.Attribute.Mutating,
                            ["params_schema"] = new JObject
                            {
                                ["type"] = "object", ["properties"] = properties,
                                ["required"] = new JArray(parameters.Where(parameter => parameter.Required).Select(parameter => parameter.Name)),
                                ["additionalProperties"] = false
                            },
                            ["response_schema"] = new JObject()
                        }
                    });
                }
                catch (ArgumentException error)
                {
                    BridgeLogger.LogWarning("CustomTools", $"Cannot register '{name}': {error.Message}");
                }
            }
        }

        internal JArray Describe() => new JArray(tools.Values.Select(entry => entry.Descriptor.DeepClone()));

        internal bool TryHandle(Command command, out string response)
        {
            response = null;
            if (command?.Type == null || !tools.TryGetValue(command.Type, out var entry)) return false;
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId)
            {
                response = Response.ErrorResult(command.Id, "Custom tools require the Unity main thread", "PRECONDITION_FAILED");
                return true;
            }
            object[] arguments;
            try
            {
                var parameters = command.Parameters ?? new JObject();
                var unknown = parameters.Properties().FirstOrDefault(property => !entry.Parameters.Any(parameter => parameter.Name == property.Name));
                if (unknown != null) throw new ArgumentException($"Unknown argument '{unknown.Name}'");
                arguments = entry.Parameters.Select(parameter => parameter.Bind(parameters)).ToArray();
            }
            catch (ArgumentException error)
            {
                response = Response.ErrorResult(command.Id, error.Message, "INVALID_ARGUMENT", new { tool = command.Type });
                return true;
            }
            try
            {
                response = Response.SuccessResult(command.Id, entry.Method.Invoke(null, arguments));
            }
            catch (Exception error)
            {
                var cause = error is TargetInvocationException invocation ? invocation.InnerException ?? error : error;
                response = Response.ErrorResult(command.Id, cause.Message, "CUSTOM_TOOL_FAILED",
                    new { tool = command.Type, exceptionType = cause.GetType().FullName });
            }
            return true;
        }
    }
}
