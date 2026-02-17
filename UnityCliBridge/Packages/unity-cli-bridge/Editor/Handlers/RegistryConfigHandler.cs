using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Package Registry configuration (OpenUPM, Unity NuGet, etc.)
    /// </summary>
    public static class RegistryConfigHandler
    {
        private const string ManifestPath = "Packages/manifest.json";
        
        // OpenUPM registry configuration
        private const string OpenUPMUrl = "https://package.openupm.com";
        private const string OpenUPMName = "OpenUPM";
        
        // Unity NuGet registry configuration
        private const string UnityNuGetUrl = "https://unitynuget-registry.azurewebsites.net";
        private const string UnityNuGetName = "Unity NuGet";
        
        /// <summary>
        /// Handle registry configuration operations
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                switch (action.ToLower())
                {
                    case "list":
                        return ListRegistries();
                    case "add_openupm":
                        return AddOpenUPMRegistry(parameters);
                    case "add_nuget":
                        return AddUnityNuGetRegistry(parameters);
                    case "remove":
                        return RemoveRegistry(parameters);
                    case "add_scope":
                        return AddScopeToRegistry(parameters);
                    case "recommend":
                        return GetRecommendedPackages(parameters);
                    default:
                        return new { error = $"Unknown action: {action}" };
                }
            }
            catch (Exception e)
            {
                McpLogger.LogError("RegistryConfigHandler", $" Error handling {action}: {e.Message}");
                return new { error = e.Message };
            }
        }
        
        /// <summary>
        /// List configured registries
        /// </summary>
        private static object ListRegistries()
        {
            try
            {
                if (!File.Exists(ManifestPath))
                {
                    return new { error = "manifest.json not found" };
                }
                
                var manifestContent = File.ReadAllText(ManifestPath);
                var manifest = JObject.Parse(manifestContent);
                
                var scopedRegistries = manifest["scopedRegistries"] as JArray;
                
                if (scopedRegistries == null || scopedRegistries.Count == 0)
                {
                    return new
                    {
                        success = true,
                        registries = new object[0],
                        message = "No scoped registries configured"
                    };
                }
                
                var registries = new List<object>();
                foreach (var registry in scopedRegistries)
                {
                    var scopes = registry["scopes"] as JArray;
                    registries.Add(new
                    {
                        name = registry["name"]?.ToString(),
                        url = registry["url"]?.ToString(),
                        scopes = scopes?.Select(s => s.ToString()).ToArray() ?? new string[0]
                    });
                }
                
                return new
                {
                    success = true,
                    registries = registries,
                    totalCount = registries.Count,
                    message = $"Found {registries.Count} configured registries"
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("RegistryConfigHandler", $" Error listing registries: {e.Message}");
                return new { error = $"Failed to list registries: {e.Message}" };
            }
        }
        
        /// <summary>
        /// Add OpenUPM registry
        /// </summary>
        private static object AddOpenUPMRegistry(JObject parameters)
        {
            try
            {
                var scopes = parameters["scopes"]?.ToObject<string[]>();
                var autoAddPopular = parameters["autoAddPopular"]?.ToObject<bool>() ?? true;
                
                if (!File.Exists(ManifestPath))
                {
                    return new { error = "manifest.json not found" };
                }
                
                var manifestContent = File.ReadAllText(ManifestPath);
                var manifest = JObject.Parse(manifestContent);
                
                var scopedRegistries = manifest["scopedRegistries"] as JArray ?? new JArray();
                
                // Check if OpenUPM already exists
                var existingOpenUPM = scopedRegistries.FirstOrDefault(r => 
                    r["url"]?.ToString() == OpenUPMUrl || 
                    r["name"]?.ToString() == OpenUPMName);
                
                if (existingOpenUPM != null)
                {
                    // Update existing OpenUPM registry
                    var existingScopes = existingOpenUPM["scopes"] as JArray ?? new JArray();
                    
                    if (scopes != null)
                    {
                        foreach (var scope in scopes)
                        {
                            if (!existingScopes.Any(s => s.ToString() == scope))
                            {
                                existingScopes.Add(scope);
                            }
                        }
                    }
                    
                    existingOpenUPM["scopes"] = existingScopes;
                }
                else
                {
                    // Add new OpenUPM registry
                    var openUPMScopes = new JArray();
                    
                    // Default popular scopes
                    if (autoAddPopular)
                    {
                        var popularScopes = new[]
                        {
                            "com.cysharp",
                            "com.neuecc",
                            "com.demigiant",
                            "com.yasirkula",
                            "com.coffee",
                            "com.littlebigfun",
                            "jp.keijiro",
                            "com.svermeulen",
                            "com.dbrizov"
                        };
                        
                        foreach (var scope in popularScopes)
                        {
                            openUPMScopes.Add(scope);
                        }
                    }
                    
                    // Add custom scopes
                    if (scopes != null)
                    {
                        foreach (var scope in scopes)
                        {
                            if (!openUPMScopes.Any(s => s.ToString() == scope))
                            {
                                openUPMScopes.Add(scope);
                            }
                        }
                    }
                    
                    var openUPMRegistry = new JObject
                    {
                        ["name"] = OpenUPMName,
                        ["url"] = OpenUPMUrl,
                        ["scopes"] = openUPMScopes
                    };
                    
                    scopedRegistries.Add(openUPMRegistry);
                }
                
                manifest["scopedRegistries"] = scopedRegistries;
                
                // Write back to manifest.json
                File.WriteAllText(ManifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));
                
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                
                return new
                {
                    success = true,
                    action = "add_openupm",
                    message = existingOpenUPM != null 
                        ? "Updated OpenUPM registry configuration" 
                        : "Successfully added OpenUPM registry",
                    registryUrl = OpenUPMUrl,
                    scopes = (existingOpenUPM?["scopes"] ?? scopedRegistries.Last["scopes"])?.ToObject<string[]>()
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("RegistryConfigHandler", $" Error adding OpenUPM: {e.Message}");
                return new { error = $"Failed to add OpenUPM registry: {e.Message}" };
            }
        }
        
        /// <summary>
        /// Add Unity NuGet registry
        /// </summary>
        private static object AddUnityNuGetRegistry(JObject parameters)
        {
            try
            {
                var scopes = parameters["scopes"]?.ToObject<string[]>();
                var autoAddPopular = parameters["autoAddPopular"]?.ToObject<bool>() ?? true;
                
                if (!File.Exists(ManifestPath))
                {
                    return new { error = "manifest.json not found" };
                }
                
                var manifestContent = File.ReadAllText(ManifestPath);
                var manifest = JObject.Parse(manifestContent);
                
                var scopedRegistries = manifest["scopedRegistries"] as JArray ?? new JArray();
                
                // Check if Unity NuGet already exists
                var existingNuGet = scopedRegistries.FirstOrDefault(r => 
                    r["url"]?.ToString() == UnityNuGetUrl || 
                    r["name"]?.ToString() == UnityNuGetName);
                
                if (existingNuGet != null)
                {
                    // Update existing Unity NuGet registry
                    var existingScopes = existingNuGet["scopes"] as JArray ?? new JArray();
                    
                    if (scopes != null)
                    {
                        foreach (var scope in scopes)
                        {
                            if (!existingScopes.Any(s => s.ToString() == scope))
                            {
                                existingScopes.Add(scope);
                            }
                        }
                    }
                    
                    existingNuGet["scopes"] = existingScopes;
                }
                else
                {
                    // Add new Unity NuGet registry
                    var nugetScopes = new JArray();
                    
                    // Default scopes for NuGet packages
                    if (autoAddPopular)
                    {
                        var defaultScopes = new[]
                        {
                            "org.nuget",
                            "system",
                            "newtonsoft",
                            "microsoft"
                        };
                        
                        foreach (var scope in defaultScopes)
                        {
                            nugetScopes.Add(scope);
                        }
                    }
                    
                    // Add custom scopes
                    if (scopes != null)
                    {
                        foreach (var scope in scopes)
                        {
                            if (!nugetScopes.Any(s => s.ToString() == scope))
                            {
                                nugetScopes.Add(scope);
                            }
                        }
                    }
                    
                    var nugetRegistry = new JObject
                    {
                        ["name"] = UnityNuGetName,
                        ["url"] = UnityNuGetUrl,
                        ["scopes"] = nugetScopes
                    };
                    
                    scopedRegistries.Add(nugetRegistry);
                }
                
                manifest["scopedRegistries"] = scopedRegistries;
                
                // Write back to manifest.json
                File.WriteAllText(ManifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));
                
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                
                return new
                {
                    success = true,
                    action = "add_nuget",
                    message = existingNuGet != null 
                        ? "Updated Unity NuGet registry configuration" 
                        : "Successfully added Unity NuGet registry",
                    registryUrl = UnityNuGetUrl,
                    scopes = (existingNuGet?["scopes"] ?? scopedRegistries.Last["scopes"])?.ToObject<string[]>()
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("RegistryConfigHandler", $" Error adding Unity NuGet: {e.Message}");
                return new { error = $"Failed to add Unity NuGet registry: {e.Message}" };
            }
        }
        
        /// <summary>
        /// Remove a registry
        /// </summary>
        private static object RemoveRegistry(JObject parameters)
        {
            try
            {
                var registryName = parameters["registryName"]?.ToString();
                
                if (string.IsNullOrEmpty(registryName))
                {
                    return new { error = "Registry name is required" };
                }
                
                if (!File.Exists(ManifestPath))
                {
                    return new { error = "manifest.json not found" };
                }
                
                var manifestContent = File.ReadAllText(ManifestPath);
                var manifest = JObject.Parse(manifestContent);
                
                var scopedRegistries = manifest["scopedRegistries"] as JArray;
                
                if (scopedRegistries == null || scopedRegistries.Count == 0)
                {
                    return new { error = "No scoped registries configured" };
                }
                
                var registryToRemove = scopedRegistries.FirstOrDefault(r => 
                    r["name"]?.ToString() == registryName);
                
                if (registryToRemove == null)
                {
                    return new { error = $"Registry '{registryName}' not found" };
                }
                
                scopedRegistries.Remove(registryToRemove);
                manifest["scopedRegistries"] = scopedRegistries;
                
                // Write back to manifest.json
                File.WriteAllText(ManifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));
                
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                
                return new
                {
                    success = true,
                    action = "remove",
                    message = $"Successfully removed registry '{registryName}'"
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("RegistryConfigHandler", $" Error removing registry: {e.Message}");
                return new { error = $"Failed to remove registry: {e.Message}" };
            }
        }
        
        /// <summary>
        /// Add scope to existing registry
        /// </summary>
        private static object AddScopeToRegistry(JObject parameters)
        {
            try
            {
                var registryName = parameters["registryName"]?.ToString();
                var scope = parameters["scope"]?.ToString();
                
                if (string.IsNullOrEmpty(registryName) || string.IsNullOrEmpty(scope))
                {
                    return new { error = "Registry name and scope are required" };
                }
                
                if (!File.Exists(ManifestPath))
                {
                    return new { error = "manifest.json not found" };
                }
                
                var manifestContent = File.ReadAllText(ManifestPath);
                var manifest = JObject.Parse(manifestContent);
                
                var scopedRegistries = manifest["scopedRegistries"] as JArray;
                
                if (scopedRegistries == null || scopedRegistries.Count == 0)
                {
                    return new { error = "No scoped registries configured" };
                }
                
                var registry = scopedRegistries.FirstOrDefault(r => 
                    r["name"]?.ToString() == registryName);
                
                if (registry == null)
                {
                    return new { error = $"Registry '{registryName}' not found" };
                }
                
                var scopes = registry["scopes"] as JArray ?? new JArray();
                
                if (!scopes.Any(s => s.ToString() == scope))
                {
                    scopes.Add(scope);
                    registry["scopes"] = scopes;
                    
                    // Write back to manifest.json
                    File.WriteAllText(ManifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));
                    
                    UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                    
                    return new
                    {
                        success = true,
                        action = "add_scope",
                        message = $"Successfully added scope '{scope}' to registry '{registryName}'",
                        scopes = scopes.Select(s => s.ToString()).ToArray()
                    };
                }
                else
                {
                    return new
                    {
                        success = true,
                        action = "add_scope",
                        message = $"Scope '{scope}' already exists in registry '{registryName}'",
                        scopes = scopes.Select(s => s.ToString()).ToArray()
                    };
                }
            }
            catch (Exception e)
            {
                McpLogger.LogError("RegistryConfigHandler", $" Error adding scope: {e.Message}");
                return new { error = $"Failed to add scope: {e.Message}" };
            }
        }
        
        /// <summary>
        /// Get recommended packages from registries
        /// </summary>
        private static object GetRecommendedPackages(JObject parameters)
        {
            var registry = parameters["registry"]?.ToString()?.ToLower();
            
            var recommendations = new Dictionary<string, List<object>>();
            
            // OpenUPM recommended packages
            recommendations["openupm"] = new List<object>
            {
                new { 
                    packageId = "com.cysharp.unitask", 
                    name = "UniTask", 
                    description = "Efficient async/await for Unity",
                    scope = "com.cysharp"
                },
                new { 
                    packageId = "com.neuecc.unirx", 
                    name = "UniRx", 
                    description = "Reactive Extensions for Unity",
                    scope = "com.neuecc"
                },
                new { 
                    packageId = "com.demigiant.dotween", 
                    name = "DOTween", 
                    description = "Animation engine for Unity",
                    scope = "com.demigiant"
                },
                new { 
                    packageId = "com.yasirkula.ingamedebugconsole", 
                    name = "In-game Debug Console", 
                    description = "Runtime debug console",
                    scope = "com.yasirkula"
                },
                new { 
                    packageId = "com.coffee.ui-particle", 
                    name = "UI Particle", 
                    description = "Particle system for UI",
                    scope = "com.coffee"
                },
                new { 
                    packageId = "jp.keijiro.klak.motion", 
                    name = "Klak Motion", 
                    description = "Motion toolkit for Unity",
                    scope = "jp.keijiro"
                }
            };
            
            // Unity NuGet recommended packages
            recommendations["nuget"] = new List<object>
            {
                new { 
                    packageId = "org.nuget.newtonsoft.json", 
                    name = "Newtonsoft.Json", 
                    description = "Popular JSON framework for .NET",
                    scope = "org.nuget"
                },
                new { 
                    packageId = "org.nuget.system.threading.tasks.extensions", 
                    name = "System.Threading.Tasks.Extensions", 
                    description = "Additional types for Tasks",
                    scope = "org.nuget"
                },
                new { 
                    packageId = "org.nuget.sqlite-net-pcl", 
                    name = "SQLite-net", 
                    description = "SQLite database for Unity",
                    scope = "org.nuget"
                },
                new { 
                    packageId = "org.nuget.csvhelper", 
                    name = "CsvHelper", 
                    description = "CSV file reading and writing",
                    scope = "org.nuget"
                }
            };
            
            if (!string.IsNullOrEmpty(registry) && recommendations.ContainsKey(registry))
            {
                return new
                {
                    success = true,
                    action = "recommend",
                    registry = registry,
                    packages = recommendations[registry],
                    message = $"Recommended packages for {registry}"
                };
            }
            
            // Return all recommendations if no specific registry requested
            return new
            {
                success = true,
                action = "recommend",
                allRecommendations = recommendations,
                message = "Recommended packages for available registries"
            };
        }
    }
}
