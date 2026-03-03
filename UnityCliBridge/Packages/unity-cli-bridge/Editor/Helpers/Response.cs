using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Helpers
{
    /// <summary>
    /// Helper class for creating standardized response messages
    /// </summary>
    public static class Response
    {
        private static string _cachedPackageVersion;
        private static bool _packageVersionResolved;

        /// <summary>
        /// Gets the package version from package.json
        /// </summary>
        /// <returns>Package version string</returns>
        private static string GetPackageVersion()
        {
            if (_packageVersionResolved)
            {
                return _cachedPackageVersion ?? "unknown";
            }

            try
            {
                // First try PackageManager metadata (works for registry/cached packages too)
                try
                {
                    var assetPathCandidates = new string[]
                    {
                        "Packages/com.akiojin.unity-cli-bridge/package.json",
                        "Packages/unity-cli-bridge/package.json" // embedded in this repo
                    };

                    foreach (var assetPath in assetPathCandidates)
                    {
                        var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                        if (pkg != null && !string.IsNullOrEmpty(pkg.version))
                        {
                            _cachedPackageVersion = pkg.version;
                            return _cachedPackageVersion;
                        }
                    }
                }
                catch
                {
                    // fall through to filesystem heuristics
                }

                // Try multiple potential filesystem paths for package.json (embedded packages)
                string[] possiblePaths = new string[]
                {
                    "Packages/com.akiojin.unity-cli-bridge/package.json",
                    "Packages/unity-cli-bridge/package.json",
                    Path.Combine(Application.dataPath, "../Packages/com.akiojin.unity-cli-bridge/package.json"),
                    Path.Combine(Application.dataPath, "../Packages/unity-cli-bridge/package.json")
                };

                foreach (var path in possiblePaths)
                {
                    string fullPath = path;
                    if (!Path.IsPathRooted(path))
                    {
                        fullPath = Path.GetFullPath(path);
                    }

                    if (File.Exists(fullPath))
                    {
                        string jsonContent = File.ReadAllText(fullPath);
                        var packageInfo = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                        if (packageInfo != null && packageInfo.ContainsKey("version"))
                        {
                            _cachedPackageVersion = packageInfo["version"].ToString();
                            return _cachedPackageVersion;
                        }
                    }
                }

                // Fallback: scan PackageCache directory
                try
                {
                    var projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
                    var packageCacheRoot = Path.GetFullPath(Path.Combine(projectRoot, "Library/PackageCache"));
                    var ids = new string[] { "com.akiojin.unity-cli-bridge" };

                    foreach (var id in ids)
                    {
                        var dirs = Directory.Exists(packageCacheRoot)
                            ? Directory.GetDirectories(packageCacheRoot, $"{id}@*")
                            : Array.Empty<string>();

                        foreach (var dir in dirs)
                        {
                            var pkgJson = Path.Combine(dir, "package.json");
                            if (!File.Exists(pkgJson)) continue;

                            string jsonContent = File.ReadAllText(pkgJson);
                            var packageInfo = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                            if (packageInfo != null && packageInfo.ContainsKey("version"))
                            {
                                _cachedPackageVersion = packageInfo["version"].ToString();
                                return _cachedPackageVersion;
                            }
                        }
                    }
                }
                catch
                {
                    // ignore and fall through to assembly heuristic
                }

                // Fallback: try to get from assembly
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                if (assembly != null)
                {
                    var location = assembly.Location;
                    if (!string.IsNullOrEmpty(location) && location.Contains("com.akiojin.unity-cli-bridge"))
                    {
                        // Try to extract version from path if it contains version info
                        var match = System.Text.RegularExpressions.Regex.Match(
                            location,
                            @"com\.(akiojin\.unity-cli-bridge)@([0-9]+\.[0-9]+\.[0-9]+)");
                        if (match.Success)
                        {
                            _cachedPackageVersion = match.Groups[2].Value;
                            return _cachedPackageVersion;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                BridgeLogger.LogWarning("Response", $"Failed to get package version: {e.Message}");
            }
            finally
            {
                _packageVersionResolved = true;
            }

            _cachedPackageVersion = "unknown";
            return "unknown";
        }

        /// <summary>
        /// Creates a success response with optional data
        /// </summary>
        /// <param name="data">Optional data to include in the response</param>
        /// <returns>JSON string of the response</returns>
        public static string Success(object data = null)
        {
            var response = new JObject
            {
                ["status"] = "success",
                ["version"] = GetPackageVersion()
            };

            if (data != null)
            {
                response["data"] = JToken.FromObject(data);
            }

            return response.ToString(Formatting.None);
        }
        
        /// <summary>
        /// Creates a success response with command ID and optional data
        /// </summary>
        /// <param name="id">Command ID</param>
        /// <param name="data">Optional data to include in the response</param>
        /// <returns>JSON string of the response</returns>
        public static string Success(string id, object data = null)
        {
            var response = new JObject
            {
                ["id"] = id,
                ["status"] = "success",
                ["success"] = true,
                ["version"] = GetPackageVersion()
            };

            if (data != null)
            {
                response["data"] = JToken.FromObject(data);
            }

            return response.ToString(Formatting.None);
        }
        
        /// <summary>
        /// Creates an error response with message and optional error code
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="code">Optional error code</param>
        /// <param name="details">Optional additional error details</param>
        /// <returns>JSON string of the response</returns>
        public static string Error(string message, string code = null, object details = null)
        {
            var response = new JObject
            {
                ["status"] = "error",
                ["error"] = message
            };

            if (!string.IsNullOrEmpty(code))
            {
                response["code"] = code;
            }

            if (details != null)
            {
                response["details"] = JToken.FromObject(details);
            }

            return response.ToString(Formatting.None);
        }
        
        /// <summary>
        /// Creates an error response with command ID
        /// </summary>
        /// <param name="id">Command ID</param>
        /// <param name="message">Error message</param>
        /// <param name="code">Optional error code</param>
        /// <param name="details">Optional additional error details</param>
        /// <returns>JSON string of the response</returns>
        public static string ErrorWithId(string id, string message, string code = null, object details = null)
        {
            var response = new JObject
            {
                ["id"] = id,
                ["status"] = "error",
                ["success"] = false,
                ["error"] = message
            };

            if (!string.IsNullOrEmpty(code))
            {
                response["code"] = code;
            }

            if (details != null)
            {
                response["details"] = JToken.FromObject(details);
            }

            return response.ToString(Formatting.None);
        }
        
        /// <summary>
        /// Creates a response for the ping command
        /// </summary>
        /// <returns>JSON string of the pong response</returns>
        public static string Pong()
        {
            return Success(new { 
                message = "pong", 
                timestamp = System.DateTime.UtcNow.ToString("o"),
                version = GetPackageVersion()
            });
        }
        
        // ===== New Format Methods (Phase 1.1) =====
        
        /// <summary>
        /// Gets the current editor state
        /// </summary>
        /// <returns>Dictionary containing editor state information</returns>
        private static Dictionary<string, object> GetCurrentEditorState()
        {
            return new Dictionary<string, object>
            {
                ["isPlaying"] = EditorApplication.isPlaying,
                ["isPaused"] = EditorApplication.isPaused,
                ["version"] = GetPackageVersion(),
                ["timestamp"] = System.DateTime.UtcNow.ToString("o")
            };
        }
        
        /// <summary>
        /// Creates a standardized success response (new format)
        /// </summary>
        /// <param name="result">The result data</param>
        /// <returns>JSON string of the response</returns>
        public static string SuccessResult(object result, IEnumerable<Dictionary<string, object>> warnings = null)
        {
            var response = new Dictionary<string, object>
            {
                ["status"] = "success",
                ["result"] = result,
                ["editorState"] = GetCurrentEditorState()
            };

            AttachWarnings(response, warnings);
            
            return JsonConvert.SerializeObject(response);
        }
        
        /// <summary>
        /// Creates a standardized success response with command ID (new format)
        /// </summary>
        /// <param name="id">Command ID</param>
        /// <param name="result">The result data</param>
        /// <returns>JSON string of the response</returns>
        public static string SuccessResult(string id, object result, IEnumerable<Dictionary<string, object>> warnings = null)
        {
            var response = new Dictionary<string, object>
            {
                ["id"] = id,
                ["status"] = "success",
                ["result"] = result,
                ["editorState"] = GetCurrentEditorState()
            };

            AttachWarnings(response, warnings);
            
            return JsonConvert.SerializeObject(response);
        }

        private static void AttachWarnings(Dictionary<string, object> response, IEnumerable<Dictionary<string, object>> warnings)
        {
            if (warnings == null)
            {
                return;
            }

            var collected = new List<Dictionary<string, object>>();
            foreach (var warning in warnings)
            {
                if (warning != null)
                {
                    collected.Add(warning);
                }
            }

            if (collected.Count > 0)
            {
                response["warnings"] = collected;
            }
        }

        /// <summary>
        /// Appends warnings to an existing success response JSON payload.
        /// </summary>
        public static string AppendWarnings(string responseJson, IEnumerable<Dictionary<string, object>> warnings)
        {
            if (string.IsNullOrEmpty(responseJson) || warnings == null)
            {
                return responseJson;
            }

            var collected = new List<Dictionary<string, object>>();
            foreach (var warning in warnings)
            {
                if (warning != null)
                {
                    collected.Add(warning);
                }
            }

            if (collected.Count == 0)
            {
                return responseJson;
            }

            var token = JObject.Parse(responseJson);
            var status = token["status"]?.Value<string>();
            if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return responseJson;
            }

            token["warnings"] = JToken.FromObject(collected);
            return token.ToString(Formatting.None);
        }
        
        /// <summary>
        /// Creates a standardized error response (new format)
        /// </summary>
        /// <param name="errorMessage">Error message</param>
        /// <param name="code">Error code</param>
        /// <param name="details">Optional error details</param>
        /// <returns>JSON string of the response</returns>
        public static string ErrorResult(string errorMessage, string code = "UNKNOWN_ERROR", object details = null)
        {
            var response = new Dictionary<string, object>
            {
                ["status"] = "error",
                ["error"] = errorMessage,
                ["code"] = code
            };
            
            if (details != null)
            {
                response["details"] = details;
            }
            
            return JsonConvert.SerializeObject(response);
        }
        
        /// <summary>
        /// Creates a standardized error response with command ID (new format)
        /// </summary>
        /// <param name="id">Command ID</param>
        /// <param name="errorMessage">Error message</param>
        /// <param name="code">Error code</param>
        /// <param name="details">Optional error details</param>
        /// <returns>JSON string of the response</returns>
        public static string ErrorResult(string id, string errorMessage, string code = "UNKNOWN_ERROR", object details = null)
        {
            var response = new Dictionary<string, object>
            {
                ["id"] = id,
                ["status"] = "error",
                ["error"] = errorMessage,
                ["code"] = code
            };
            
            if (details != null)
            {
                response["details"] = details;
            }
            
            return JsonConvert.SerializeObject(response);
        }
    }
}
