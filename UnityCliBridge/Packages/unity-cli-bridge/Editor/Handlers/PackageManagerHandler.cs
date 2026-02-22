using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Package Manager operations including search, install, and management
    /// </summary>
    public static class PackageManagerHandler
    {
        private static SearchRequest searchRequest;
        private static ListRequest listRequest;
        private static AddRequest addRequest;
        private static RemoveRequest removeRequest;
        private static Dictionary<string, UnityEditor.PackageManager.PackageInfo> packageCache = new Dictionary<string, UnityEditor.PackageManager.PackageInfo>();
        
        /// <summary>
        /// Handle package manager operations
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                switch (action.ToLower())
                {
                    case "search":
                        return SearchPackages(parameters);
                    case "list":
                        return ListInstalledPackages(parameters);
                    case "add":
                    case "install":
                        return InstallPackage(parameters);
                    case "remove":
                    case "uninstall":
                        return RemovePackage(parameters);
                    case "info":
                        return GetPackageInfo(parameters);
                    case "recommend":
                        return GetRecommendedPackages(parameters);
                    default:
                        return new { error = $"Unknown action: {action}" };
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("PackageManagerHandler", $"Error handling {action}: {e.Message}");
                return new { error = e.Message };
            }
        }
        
        /// <summary>
        /// Search for packages in Unity Registry
        /// </summary>
        private static object SearchPackages(JObject parameters)
{
            try
            {
                var keyword = parameters["keyword"]?.ToString();
                var limit = parameters["limit"]?.ToObject<int>() ?? 20;

                if (string.IsNullOrEmpty(keyword))
                {
                    return new { error = "Search keyword is required" };
                }

                // Use SearchAll() to get all packages, then filter by keyword
                // Client.Search(keyword) expects exact package ID match, not keyword search
                searchRequest = Client.SearchAll();

                // Wait for search to complete (synchronously for MCP)
                while (!searchRequest.IsCompleted)
                {
                    System.Threading.Thread.Sleep(10);
                }

                if (searchRequest.Status == StatusCode.Success)
                {
                    var results = new List<object>();

                    // Filter packages by keyword matching displayName, name, description, or keywords
                    var filteredPackages = searchRequest.Result
                        .Where(package => MatchesKeyword(package, keyword))
                        .Take(limit);

                    foreach (var package in filteredPackages)
                    {
                        results.Add(new
                        {
                            packageId = package.packageId,
                            name = package.name,
                            displayName = package.displayName,
                            description = package.description,
                            version = package.versions.latestCompatible ?? package.versions.latest,
                            author = package.author?.name,
                            keywords = package.keywords,
                            category = DeterminePackageCategory(package)
                        });
                    }

                    return new
                    {
                        success = true,
                        action = "search",
                        keyword = keyword,
                        packages = results,
                        totalCount = results.Count,
                        message = $"Found {results.Count} packages matching '{keyword}'"
                    };
                }
                else if (searchRequest.Status == StatusCode.Failure)
                {
                    return new { error = $"Search failed: {searchRequest.Error?.message}" };
                }

                return new { error = "Search request did not complete successfully" };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("PackageManagerHandler", $"Error searching packages: {e.Message}");
                return new { error = $"Failed to search packages: {e.Message}" };
            }
        }        

        /// <summary>
        /// Check if a package matches the search keyword
        /// </summary>
        private static bool MatchesKeyword(UnityEditor.PackageManager.PackageInfo package, string keyword)
        {
            var lowerKeyword = keyword.ToLower();

            // Match against displayName
            if (package.displayName?.ToLower().Contains(lowerKeyword) == true)
                return true;

            // Match against name (package identifier)
            if (package.name?.ToLower().Contains(lowerKeyword) == true)
                return true;

            // Match against description
            if (package.description?.ToLower().Contains(lowerKeyword) == true)
                return true;

            // Match against keywords array
            if (package.keywords != null)
            {
                if (package.keywords.Any(k => k.ToLower().Contains(lowerKeyword)))
                    return true;
            }

            return false;
        }
        /// <summary>
        /// List installed packages
        /// </summary>
        private static object ListInstalledPackages(JObject parameters)
        {
            try
            {
                var includeBuiltIn = parameters["includeBuiltIn"]?.ToObject<bool>() ?? false;
                
                listRequest = Client.List(includeBuiltIn);
                
                // Wait for list to complete
                while (!listRequest.IsCompleted)
                {
                    System.Threading.Thread.Sleep(10);
                }
                
                if (listRequest.Status == StatusCode.Success)
                {
                    var packages = new List<object>();
                    
                    foreach (var package in listRequest.Result)
                    {
                        packages.Add(new
                        {
                            packageId = package.packageId,
                            name = package.name,
                            displayName = package.displayName,
                            version = package.version,
                            source = package.source.ToString(),
                            isDirectDependency = package.isDirectDependency,
                            category = DeterminePackageCategory(package.name)
                        });
                    }
                    
                    // Sort by name
                    packages = packages.OrderBy(p => (p as dynamic).name).ToList();
                    
                    return new
                    {
                        success = true,
                        action = "list",
                        packages = packages,
                        totalCount = packages.Count,
                        message = $"Found {packages.Count} installed packages"
                    };
                }
                else if (listRequest.Status == StatusCode.Failure)
                {
                    return new { error = $"List failed: {listRequest.Error?.message}" };
                }
                
                return new { error = "List request did not complete successfully" };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("PackageManagerHandler", $"Error listing packages: {e.Message}");
                return new { error = $"Failed to list packages: {e.Message}" };
            }
        }
        
        /// <summary>
        /// Install a package
        /// </summary>
        private static object InstallPackage(JObject parameters)
        {
            try
            {
                var packageId = parameters["packageId"]?.ToString();
                var version = parameters["version"]?.ToString();
                
                if (string.IsNullOrEmpty(packageId))
                {
                    return new { error = "Package ID is required" };
                }
                
                // Construct full package identifier
                var packageIdentifier = packageId;
                if (!string.IsNullOrEmpty(version))
                {
                    // If version is specified, append it
                    if (!packageId.Contains("@"))
                    {
                        packageIdentifier = $"{packageId}@{version}";
                    }
                }
                
                BridgeLogger.Log("PackageManagerHandler", $"Installing package: {packageIdentifier}");
                
                addRequest = Client.Add(packageIdentifier);
                
                // Wait for installation to complete
                while (!addRequest.IsCompleted)
                {
                    System.Threading.Thread.Sleep(10);
                }
                
                if (addRequest.Status == StatusCode.Success)
                {
                    var package = addRequest.Result;
                    
                    return new
                    {
                        success = true,
                        action = "install",
                        packageId = package.packageId,
                        name = package.name,
                        displayName = package.displayName,
                        version = package.version,
                        message = $"Successfully installed {package.displayName} version {package.version}"
                    };
                }
                else if (addRequest.Status == StatusCode.Failure)
                {
                    return new { error = $"Installation failed: {addRequest.Error?.message}" };
                }
                
                return new { error = "Installation request did not complete successfully" };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("PackageManagerHandler", $"Error installing package: {e.Message}");
                return new { error = $"Failed to install package: {e.Message}" };
            }
        }
        
        /// <summary>
        /// Remove a package
        /// </summary>
        private static object RemovePackage(JObject parameters)
        {
            try
            {
                var packageName = parameters["packageName"]?.ToString();
                
                if (string.IsNullOrEmpty(packageName))
                {
                    return new { error = "Package name is required" };
                }
                
                BridgeLogger.Log("PackageManagerHandler", $"Removing package: {packageName}");
                
                removeRequest = Client.Remove(packageName);
                
                // Wait for removal to complete
                while (!removeRequest.IsCompleted)
                {
                    System.Threading.Thread.Sleep(10);
                }
                
                if (removeRequest.Status == StatusCode.Success)
                {
                    return new
                    {
                        success = true,
                        action = "remove",
                        packageName = packageName,
                        message = $"Successfully removed {packageName}"
                    };
                }
                else if (removeRequest.Status == StatusCode.Failure)
                {
                    return new { error = $"Removal failed: {removeRequest.Error?.message}" };
                }
                
                return new { error = "Removal request did not complete successfully" };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("PackageManagerHandler", $"Error removing package: {e.Message}");
                return new { error = $"Failed to remove package: {e.Message}" };
            }
        }
        
        /// <summary>
        /// Get detailed package information
        /// </summary>
        private static object GetPackageInfo(JObject parameters)
        {
            try
            {
                var packageName = parameters["packageName"]?.ToString();
                
                if (string.IsNullOrEmpty(packageName))
                {
                    return new { error = "Package name is required" };
                }
                
                // Try to find the package in installed packages
                listRequest = Client.List(true);
                
                while (!listRequest.IsCompleted)
                {
                    System.Threading.Thread.Sleep(10);
                }
                
                if (listRequest.Status == StatusCode.Success)
                {
                    var package = listRequest.Result.FirstOrDefault(p => p.name == packageName);
                    
                    if (package != null)
                    {
                        return new
                        {
                            success = true,
                            action = "info",
                            package = new
                            {
                                packageId = package.packageId,
                                name = package.name,
                                displayName = package.displayName,
                                version = package.version,
                                description = package.description,
                                documentationUrl = package.documentationUrl,
                                changelogUrl = package.changelogUrl,
                                licensesUrl = package.licensesUrl,
                                source = package.source.ToString(),
                                author = package.author?.name,
                                dependencies = package.dependencies?.Select(d => new
                                {
                                    name = d.name,
                                    version = d.version
                                }).ToArray(),
                                keywords = package.keywords,
                                category = DeterminePackageCategory(package.name)
                            }
                        };
                    }
                    
                    return new { error = $"Package '{packageName}' not found in installed packages" };
                }
                
                return new { error = "Failed to retrieve package information" };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("PackageManagerHandler", $"Error getting package info: {e.Message}");
                return new { error = $"Failed to get package info: {e.Message}" };
            }
        }
        
        /// <summary>
        /// Get recommended packages
        /// </summary>
        private static object GetRecommendedPackages(JObject parameters)
        {
            var category = parameters["category"]?.ToString()?.ToLower();
            
            var recommendations = new Dictionary<string, List<object>>
            {
                ["essential"] = new List<object>
                {
                    new { packageId = "com.unity.textmeshpro", name = "TextMeshPro", description = "Advanced text rendering" },
                    new { packageId = "com.unity.inputsystem", name = "Input System", description = "New input system for Unity" },
                    new { packageId = "com.unity.addressables", name = "Addressables", description = "Asset management system" }
                },
                ["rendering"] = new List<object>
                {
                    new { packageId = "com.unity.render-pipelines.universal", name = "Universal RP", description = "Universal Render Pipeline" },
                    new { packageId = "com.unity.render-pipelines.high-definition", name = "HDRP", description = "High Definition Render Pipeline" },
                    new { packageId = "com.unity.postprocessing", name = "Post Processing", description = "Post-processing effects" }
                },
                ["tools"] = new List<object>
                {
                    new { packageId = "com.unity.probuilder", name = "ProBuilder", description = "3D modeling and level design" },
                    new { packageId = "com.unity.cinemachine", name = "Cinemachine", description = "Smart camera system" },
                    new { packageId = "com.unity.timeline", name = "Timeline", description = "Create cinematic content" }
                },
                ["networking"] = new List<object>
                {
                    new { packageId = "com.unity.netcode.gameobjects", name = "Netcode for GameObjects", description = "Network framework" },
                    new { packageId = "com.unity.transport", name = "Unity Transport", description = "Low-level networking" }
                },
                ["mobile"] = new List<object>
                {
                    new { packageId = "com.unity.mobile.notifications", name = "Mobile Notifications", description = "Push notifications" },
                    new { packageId = "com.unity.ads", name = "Unity Ads", description = "Monetization through ads" },
                    new { packageId = "com.unity.purchasing", name = "In-App Purchasing", description = "Monetization through IAP" }
                }
            };
            
            if (!string.IsNullOrEmpty(category) && recommendations.ContainsKey(category))
            {
                return new
                {
                    success = true,
                    action = "recommend",
                    category = category,
                    packages = recommendations[category],
                    message = $"Recommended packages for {category}"
                };
            }
            
            // Return all categories if no specific category requested
            return new
            {
                success = true,
                action = "recommend",
                categories = recommendations.Keys.ToArray(),
                allPackages = recommendations,
                message = "Available package categories and recommendations"
            };
        }
        
        /// <summary>
        /// Determine package category based on name
        /// </summary>
        private static string DeterminePackageCategory(string packageName)
        {
            if (packageName.Contains("render") || packageName.Contains("pipeline") || packageName.Contains("shader"))
                return "Rendering";
            if (packageName.Contains("ui") || packageName.Contains("text") || packageName.Contains("canvas"))
                return "UI";
            if (packageName.Contains("physics") || packageName.Contains("collider"))
                return "Physics";
            if (packageName.Contains("audio") || packageName.Contains("sound"))
                return "Audio";
            if (packageName.Contains("animation") || packageName.Contains("timeline"))
                return "Animation";
            if (packageName.Contains("network") || packageName.Contains("multiplayer"))
                return "Networking";
            if (packageName.Contains("mobile") || packageName.Contains("android") || packageName.Contains("ios"))
                return "Mobile";
            if (packageName.Contains("xr") || packageName.Contains("ar") || packageName.Contains("vr"))
                return "XR";
            if (packageName.Contains("test") || packageName.Contains("performance"))
                return "Testing";
            
            return "General";
        }
        
        /// <summary>
        /// Determine package category based on PackageInfo
        /// </summary>
        private static string DeterminePackageCategory(UnityEditor.PackageManager.PackageInfo package)
        {
            // Check keywords first
            if (package.keywords != null)
            {
                foreach (var keyword in package.keywords)
                {
                    var lowerKeyword = keyword.ToLower();
                    if (lowerKeyword.Contains("render")) return "Rendering";
                    if (lowerKeyword.Contains("ui")) return "UI";
                    if (lowerKeyword.Contains("physics")) return "Physics";
                    if (lowerKeyword.Contains("audio")) return "Audio";
                    if (lowerKeyword.Contains("network")) return "Networking";
                }
            }
            
            // Fall back to name-based detection
            return DeterminePackageCategory(package.name);
        }
    }
}