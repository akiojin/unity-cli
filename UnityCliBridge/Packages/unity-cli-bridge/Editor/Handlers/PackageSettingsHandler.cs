using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;
using UnityEngine;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Accesses package/user settings through Unity Settings Manager when available.
    /// Reflection is used so the bridge does not need a hard compile-time dependency.
    /// </summary>
    public static class PackageSettingsHandler
    {
        public static object GetPackageSetting(JObject parameters)
        {
            try
            {
                var package = parameters["package"]?.ToString();
                var key = parameters["key"]?.ToString();
                var scopeName = parameters["scope"]?.ToString() ?? "project";
                if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(key))
                {
                    return new { error = "package and key are required", code = "INVALID_ARGUMENT" };
                }

                if (TryResolveContext(package, scopeName, out var context))
                {
                    if (!TryGetValue(context.SettingsInstance, key, context.ScopeValue, out var value))
                    {
                        return new
                        {
                            success = false,
                            package = package,
                            key = key,
                            scope = context.ScopeName,
                            error = "setting_not_found"
                        };
                    }

                    return new
                    {
                        success = true,
                        package = package,
                        key = key,
                        scope = context.ScopeName,
                        value = ConvertToToken(value)
                    };
                }

                var store = LoadStore(GetStorePath(package, scopeName));
                var token = SelectPathToken(store, key);
                if (token == null)
                {
                    return new
                    {
                        success = false,
                        package = package,
                        key = NormalizePath(key),
                        scope = NormalizeScopeName(scopeName),
                        error = "setting_not_found"
                    };
                }

                return new
                {
                    success = true,
                    package = package,
                    key = NormalizePath(key),
                    scope = NormalizeScopeName(scopeName),
                    value = token.DeepClone()
                };
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("PackageSettingsHandler", $"Error getting package setting: {ex.Message}");
                return new { error = $"Failed to get package setting: {ex.Message}" };
            }
        }

        public static object SetPackageSetting(JObject parameters)
        {
            try
            {
                var package = parameters["package"]?.ToString();
                var key = parameters["key"]?.ToString();
                var scopeName = parameters["scope"]?.ToString() ?? "project";
                if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(key) || parameters["value"] == null)
                {
                    return new { error = "package, key, and value are required", code = "INVALID_ARGUMENT" };
                }
                if (!(parameters["confirmChanges"]?.ToObject<bool>() ?? false))
                {
                    return new
                    {
                        error = "confirmChanges must be true to update settings",
                        code = "CONFIRMATION_REQUIRED"
                    };
                }

                if (TryResolveContext(package, scopeName, out var context))
                {
                    TryGetValue(context.SettingsInstance, key, context.ScopeValue, out var previousValue);

                    var clrValue = ConvertToClr(parameters["value"]);
                    InvokeSet(context.SettingsInstance, key, clrValue, context.ScopeValue);
                    InvokeSave(context.SettingsInstance);

                    return new
                    {
                        success = true,
                        package = package,
                        key = key,
                        scope = context.ScopeName,
                        previousValue = previousValue != null ? ConvertToToken(previousValue) : null,
                        value = parameters["value"].DeepClone()
                    };
                }

                var storePath = GetStorePath(package, scopeName);
                var store = LoadStore(storePath);
                var previousToken = SelectPathToken(store, key)?.DeepClone();
                SetPathToken(store, key, parameters["value"]?.DeepClone() ?? JValue.CreateNull());
                SaveStore(storePath, store);

                return new
                {
                    success = true,
                    package = package,
                    key = NormalizePath(key),
                    scope = NormalizeScopeName(scopeName),
                    previousValue = previousToken,
                    value = parameters["value"].DeepClone()
                };
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("PackageSettingsHandler", $"Error setting package setting: {ex.Message}");
                return new { error = $"Failed to set package setting: {ex.Message}" };
            }
        }

        private sealed class SettingsContext
        {
            public object SettingsInstance { get; set; }
            public object ScopeValue { get; set; }
            public string ScopeName { get; set; }
        }

        private static bool TryResolveContext(string package, string scopeName, out SettingsContext context)
        {
            context = null;
            var settingsType = FindType(
                "UnityEditor.SettingsManagement.Settings, Unity.SettingsManager.Editor",
                "UnityEditor.SettingsManagement.Settings, Unity.SettingsManager");
            var scopeType = FindType(
                "UnityEditor.SettingsManagement.SettingsScope, Unity.SettingsManager.Editor",
                "UnityEditor.SettingsManagement.SettingsScope, Unity.SettingsManager");
            if (settingsType == null || scopeType == null)
            {
                return false;
            }

            var normalizedScope = NormalizeScopeName(scopeName);
            var scopeValue = Enum.Parse(
                scopeType,
                normalizedScope == "user" ? "User" : "Project",
                true);
            var settingsInstance = CreateSettingsInstance(settingsType, package);

            context = new SettingsContext
            {
                SettingsInstance = settingsInstance,
                ScopeValue = scopeValue,
                ScopeName = normalizedScope
            };
            return true;
        }

        private static Type FindType(params string[] candidates)
        {
            return candidates
                .Select(Type.GetType)
                .FirstOrDefault(t => t != null);
        }

        private static object CreateSettingsInstance(Type settingsType, string package)
        {
            foreach (var ctor in settingsType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                {
                    return ctor.Invoke(new object[] { package });
                }
                if (parameters.Length == 0)
                {
                    return ctor.Invoke(Array.Empty<object>());
                }
            }

            throw new MissingMethodException("Could not create SettingsManagement.Settings instance");
        }

        private static bool TryGetValue(object settingsInstance, string key, object scopeValue, out object value)
        {
            value = null;
            foreach (var candidate in new[]
            {
                (typeof(string), (object)"__unity_cli_missing__"),
                (typeof(bool), (object)false),
                (typeof(int), (object)int.MinValue),
                (typeof(double), (object)double.MinValue)
            })
            {
                var result = InvokeGet(settingsInstance, key, scopeValue, candidate.Item1, candidate.Item2);
                if (!Equals(result, candidate.Item2))
                {
                    value = result;
                    return true;
                }
            }
            return false;
        }

        private static object InvokeGet(object settingsInstance, string key, object scopeValue, Type valueType, object fallback)
        {
            var method = settingsInstance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethodDefinition);
            if (method == null)
            {
                throw new MissingMethodException("Settings.Get<T> was not found");
            }

            var generic = method.MakeGenericMethod(valueType);
            var arguments = BuildMethodArguments(generic.GetParameters(), key, fallback, scopeValue);
            return generic.Invoke(settingsInstance, arguments);
        }

        private static void InvokeSet(object settingsInstance, string key, object value, object scopeValue)
        {
            var valueType = value?.GetType() ?? typeof(string);
            var method = settingsInstance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Set" && m.IsGenericMethodDefinition);
            if (method == null)
            {
                throw new MissingMethodException("Settings.Set<T> was not found");
            }

            var generic = method.MakeGenericMethod(valueType);
            var arguments = BuildMethodArguments(generic.GetParameters(), key, value, scopeValue);
            generic.Invoke(settingsInstance, arguments);
        }

        private static object[] BuildMethodArguments(ParameterInfo[] parameters, string key, object value, object scopeValue)
        {
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (i == 0 && parameter.ParameterType == typeof(string))
                {
                    args[i] = key;
                }
                else if (parameter.ParameterType.IsInstanceOfType(value))
                {
                    args[i] = value;
                }
                else if (parameter.ParameterType.IsEnum)
                {
                    args[i] = scopeValue;
                }
                else if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue;
                }
                else
                {
                    args[i] = value;
                }
            }
            return args;
        }

        private static void InvokeSave(object settingsInstance)
        {
            var save = settingsInstance.GetType().GetMethod("Save", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            save?.Invoke(settingsInstance, Array.Empty<object>());
        }

        private static object ConvertToClr(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Boolean:
                    return token.ToObject<bool>();
                case JTokenType.Integer:
                    return token.ToObject<int>();
                case JTokenType.Float:
                    return token.ToObject<double>();
                case JTokenType.String:
                    return token.ToObject<string>();
                default:
                    return token.ToString(Newtonsoft.Json.Formatting.None);
            }
        }

        private static JToken ConvertToToken(object value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            if (value is string text)
            {
                var trimmed = text.Trim();
                if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) || (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
                {
                    try
                    {
                        return JToken.Parse(trimmed);
                    }
                    catch
                    {
                        return new JValue(text);
                    }
                }
                return new JValue(text);
            }

            return JToken.FromObject(value);
        }

        private static string NormalizeScopeName(string scopeName)
        {
            return string.Equals(scopeName, "user", StringComparison.OrdinalIgnoreCase)
                ? "user"
                : "project";
        }

        private static string NormalizePath(string raw)
        {
            return string.Join(
                "/",
                (raw ?? string.Empty)
                    .Replace('.', '/')
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            );
        }

        private static string GetStorePath(string package, string scopeName)
        {
            var projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            var rootFolder = NormalizeScopeName(scopeName) == "user" ? "UserSettings" : "ProjectSettings";
            var dir = Path.Combine(projectRoot, rootFolder, "UnityCliPackageSettings");
            var fileName = SanitizeFileName(package) + ".json";
            return Path.Combine(dir, fileName);
        }

        private static JObject LoadStore(string path)
        {
            if (!File.Exists(path))
            {
                return new JObject();
            }

            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
        }

        private static void SaveStore(string path, JObject store)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, store.ToString());
        }

        private static JToken SelectPathToken(JToken root, string key)
        {
            var current = root;
            foreach (var segment in NormalizePath(key).Split('/'))
            {
                current = current?[segment];
                if (current == null)
                {
                    return null;
                }
            }
            return current;
        }

        private static void SetPathToken(JObject root, string key, JToken value)
        {
            var segments = NormalizePath(key).Split('/');
            JObject current = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (current[segments[i]] is not JObject next)
                {
                    next = new JObject();
                    current[segments[i]] = next;
                }
                current = next;
            }
            current[segments[^1]] = value;
        }

        private static string SanitizeFileName(string raw)
        {
            var fileName = raw ?? "package";
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }
            return fileName.Replace('/', '_').Replace('\\', '_');
        }
    }
}
