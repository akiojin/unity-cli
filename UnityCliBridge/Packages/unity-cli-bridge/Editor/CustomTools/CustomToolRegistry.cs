using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge
{
    public static class CustomToolRegistry
    {
        private static readonly JsonSerializer JsonSerializer = JsonSerializer.CreateDefault();
        private static Dictionary<string, ToolEntry> _Tools;

        public static bool TryExecute(string commandName, JObject parameters, out object result, out string error)
        {
            EnsureScanned();
            result = null;
            error = null;

            if (string.IsNullOrEmpty(commandName) || !_Tools.TryGetValue(commandName, out var entry))
            {
                return false;
            }

            try
            {
                var args = BuildArguments(entry, parameters ?? new JObject());
                result = NormalizeResult(entry.Method.Invoke(null, args));
                return true;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                BridgeLogger.LogError("CustomToolRegistry", inner.ToString());
                error = inner.Message;
                return true;
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("CustomToolRegistry", ex.ToString());
                error = ex.Message;
                return true;
            }
        }

        public static object ListTools()
        {
            EnsureScanned();
            return new
            {
                success = true,
                tools = _Tools.Values
                    .OrderBy(tool => tool.Attribute.Name, StringComparer.Ordinal)
                    .Select(ToToolSummary)
                    .ToArray(),
                count = _Tools.Count,
            };
        }

        public static object GetToolSchema(JObject parameters)
        {
            EnsureScanned();
            var name = parameters?["name"]?.ToString() ?? parameters?["toolName"]?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                return new
                {
                    success = false,
                    message = "name or toolName is required.",
                };
            }

            if (!_Tools.TryGetValue(name, out var entry))
            {
                return new
                {
                    success = false,
                    name,
                    message = $"Custom tool not found: {name}",
                };
            }

            return new
            {
                success = true,
                tool = ToToolSummary(entry),
                params_schema = BuildParamsSchema(entry),
                response_schema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = true,
                },
            };
        }

        public static object Refresh()
        {
            _Tools = null;
            EnsureScanned();
            return new
            {
                success = true,
                count = _Tools.Count,
            };
        }

        public static bool IsAllowedInPlayMode(string commandName)
        {
            EnsureScanned();
            return !string.IsNullOrEmpty(commandName)
                && _Tools.TryGetValue(commandName, out var entry)
                && entry.Attribute.AllowInPlayMode;
        }

        public static bool HasTool(string commandName)
        {
            EnsureScanned();
            return !string.IsNullOrEmpty(commandName) && _Tools.ContainsKey(commandName);
        }

        private static void EnsureScanned()
        {
            if (_Tools != null)
            {
                return;
            }

            var tools = new Dictionary<string, ToolEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(type => type != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                    foreach (var method in type.GetMethods(flags))
                    {
                        var attribute = method.GetCustomAttribute<UnityCliToolAttribute>();
                        if (attribute == null)
                        {
                            continue;
                        }
                        if (string.IsNullOrEmpty(attribute.Name))
                        {
                            BridgeLogger.LogWarning("CustomToolRegistry", $"Skipping custom CLI tool with empty name: {type.FullName}.{method.Name}");
                            continue;
                        }
                        if (!IsSupportedSignature(method))
                        {
                            BridgeLogger.LogWarning("CustomToolRegistry", $"Skipping custom CLI tool with unsupported signature: {type.FullName}.{method.Name}");
                            continue;
                        }
                        if (tools.ContainsKey(attribute.Name))
                        {
                            BridgeLogger.LogWarning("CustomToolRegistry", $"Duplicate custom CLI tool ignored: {attribute.Name}");
                            continue;
                        }

                        tools.Add(attribute.Name, new ToolEntry(attribute, method));
                    }
                }
            }

            _Tools = tools;
        }

        private static bool IsSupportedSignature(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return true;
            }
            if (parameters.Length != 1)
            {
                return false;
            }
            return true;
        }

        private static object[] BuildArguments(ToolEntry entry, JObject parameters)
        {
            var methodParameters = entry.Method.GetParameters();
            if (methodParameters.Length == 0)
            {
                return Array.Empty<object>();
            }

            var parameterType = methodParameters[0].ParameterType;
            if (parameterType == typeof(JObject))
            {
                return new object[] { parameters };
            }
            if (typeof(JToken).IsAssignableFrom(parameterType))
            {
                return new object[] { parameters };
            }

            var normalized = ApplyAliases(parameters, parameterType);
            return new object[] { normalized.ToObject(parameterType, JsonSerializer) };
        }

        private static JObject ApplyAliases(JObject parameters, Type requestType)
        {
            var normalized = parameters == null ? new JObject() : new JObject(parameters);
            foreach (var member in GetRequestMembers(requestType))
            {
                var attribute = member.GetCustomAttribute<UnityCliParamAttribute>();
                var aliases = attribute?.Aliases;
                if (aliases == null || aliases.Length == 0)
                {
                    continue;
                }

                var primaryName = member.Name;
                if (normalized.Property(primaryName, StringComparison.OrdinalIgnoreCase) != null)
                {
                    continue;
                }

                foreach (var alias in aliases)
                {
                    var aliasProperty = normalized.Property(alias, StringComparison.OrdinalIgnoreCase);
                    if (aliasProperty == null)
                    {
                        continue;
                    }
                    normalized[primaryName] = aliasProperty.Value.DeepClone();
                    break;
                }
            }
            return normalized;
        }

        private static object NormalizeResult(object result)
        {
            if (result == null || result is string || result.GetType().IsPrimitive)
            {
                return result;
            }
            if (result is JToken)
            {
                return result;
            }
            if (result is System.Collections.IDictionary)
            {
                return result;
            }

            var type = result.GetType();
            if (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal))
            {
                return result;
            }

            var normalized = new Dictionary<string, object>();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                normalized[ToCamelCase(field.Name)] = field.GetValue(result);
            }
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                normalized[ToCamelCase(property.Name)] = property.GetValue(result, null);
            }
            return normalized.Count == 0 ? result : normalized;
        }

        private static object ToToolSummary(ToolEntry entry)
        {
            return new
            {
                name = entry.Attribute.Name,
                description = entry.Attribute.Description,
                mutating = entry.Attribute.Mutating,
                allowInPlayMode = entry.Attribute.AllowInPlayMode,
                method = $"{entry.Method.DeclaringType.FullName}.{entry.Method.Name}",
                assembly = entry.Method.DeclaringType.Assembly.GetName().Name,
            };
        }

        private static Dictionary<string, object> BuildParamsSchema(ToolEntry entry)
        {
            var methodParameters = entry.Method.GetParameters();
            if (methodParameters.Length == 0)
            {
                return new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["additionalProperties"] = false,
                };
            }

            var parameterType = methodParameters[0].ParameterType;
            if (parameterType == typeof(JObject) || typeof(JToken).IsAssignableFrom(parameterType))
            {
                return new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = true,
                };
            }

            var properties = new Dictionary<string, object>();
            var required = new List<string>();
            foreach (var member in GetRequestMembers(parameterType))
            {
                var attribute = member.GetCustomAttribute<UnityCliParamAttribute>();
                var memberType = GetMemberType(member);
                var schema = new Dictionary<string, object>
                {
                    ["type"] = GetJsonSchemaType(memberType),
                };
                if (!string.IsNullOrEmpty(attribute?.Description))
                {
                    schema["description"] = attribute.Description;
                }
                if (attribute?.Aliases != null && attribute.Aliases.Length > 0)
                {
                    schema["aliases"] = attribute.Aliases;
                }
                properties[member.Name] = schema;

                if (attribute != null && attribute.Required)
                {
                    required.Add(member.Name);
                }
            }

            var result = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false,
            };
            if (required.Count > 0)
            {
                result["required"] = required.ToArray();
            }
            return result;
        }

        private static IEnumerable<MemberInfo> GetRequestMembers(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            foreach (var field in type.GetFields(flags))
            {
                yield return field;
            }
            foreach (var property in type.GetProperties(flags))
            {
                if (property.CanWrite && property.GetIndexParameters().Length == 0)
                {
                    yield return property;
                }
            }
        }

        private static Type GetMemberType(MemberInfo member)
        {
            var field = member as FieldInfo;
            if (field != null)
            {
                return field.FieldType;
            }
            return ((PropertyInfo)member).PropertyType;
        }

        private static string GetJsonSchemaType(Type type)
        {
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
            {
                type = nullable;
            }
            if (type == typeof(string) || type.IsEnum)
            {
                return "string";
            }
            if (type == typeof(bool))
            {
                return "boolean";
            }
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                return "number";
            }
            if (type.IsPrimitive)
            {
                return "integer";
            }
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
            {
                return "array";
            }
            return "object";
        }

        private static string ToCamelCase(string text)
        {
            if (string.IsNullOrEmpty(text) || char.IsLower(text[0]))
            {
                return text;
            }
            return char.ToLowerInvariant(text[0]) + text.Substring(1);
        }

        private sealed class ToolEntry
        {
            public ToolEntry(UnityCliToolAttribute attribute, MethodInfo method)
            {
                Attribute = attribute;
                Method = method;
            }

            public UnityCliToolAttribute Attribute { get; }
            public MethodInfo Method { get; }
        }
    }
}
