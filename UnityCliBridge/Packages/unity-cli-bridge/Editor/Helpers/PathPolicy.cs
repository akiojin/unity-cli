using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace UnityCliBridge.Helpers
{
    /// <summary>
    /// Utilities for path canonicalization and write policy enforcement for Assets and Embedded packages.
    /// No auxiliary files are created under Packages; only existing .cs files may be edited when embedded.
    /// </summary>
    public static class PathPolicy
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = path.Replace('\\', '/');
            // Remove ../ and ./ safely
            var combined = Path.GetFullPath(path).Replace('\\', '/');
            // Convert absolute project paths back to relative if under project
            var project = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length).Replace('\\', '/');
            if (combined.StartsWith(project, StringComparison.OrdinalIgnoreCase))
            {
                var rel = combined.Substring(project.Length).TrimStart('/');
                return rel;
            }
            return combined;
        }

        public static bool IsUnder(string path, string root)
        {
            path = Normalize(path);
            root = Normalize(root);
            return path.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) ||
                   path.Equals(root.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAssetsPath(string path) => IsUnder(path, "Assets");

        public static bool IsPackagesPath(string path) => IsUnder(path, "Packages");

        public static bool IsEmbeddedPackagesPath(string path)
        {
            if (!IsPackagesPath(path)) return false;
            // Resolve package root for path and check source
            try
            {
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                if (pkg != null) return pkg.source == PackageSource.Embedded;
            }
            catch
            {
                // ignore and fall through to fallback
            }

            // Fallback: our own package path should be considered embedded
            // even if PackageInfo lookup fails in early domain states.
            if (path.StartsWith("Packages/unity-cli-bridge/", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static bool IsAllowedWritePath(string path, string[] allowedExtensions, bool allowAssets, bool allowEmbeddedPackages)
        {
            path = Normalize(path);
            var ext = Path.GetExtension(path);
            if (allowedExtensions != null && allowedExtensions.Length > 0 && !allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return false;

            if (allowAssets && IsAssetsPath(path)) return true;
            if (allowEmbeddedPackages && IsEmbeddedPackagesPath(path)) return true;
            return false;
        }
    }
}
