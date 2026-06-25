using System;
using System.IO;

namespace UnityCliBridge.Helpers
{
    public static class CapturePathResolver
    {
        public const string CaptureDirectoryName = "capture";

        public static string GetProjectRootFromAssetsPath(string dataPath)
        {
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                throw new ArgumentException("dataPath must not be empty.", nameof(dataPath));
            }

            return Path.GetFullPath(Path.Combine(dataPath, "..")).Replace('\\', '/');
        }

        public static string GetCaptureDirectory(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("projectRoot must not be empty.", nameof(projectRoot));
            }

            return Path.Combine(projectRoot, ".unity", CaptureDirectoryName).Replace('\\', '/');
        }

        public static string BuildCaptureFilePath(
            string projectRoot,
            string prefix,
            string mode,
            string timestamp,
            string extension)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("prefix must not be empty.", nameof(prefix));
            }

            if (string.IsNullOrWhiteSpace(timestamp))
            {
                throw new ArgumentException("timestamp must not be empty.", nameof(timestamp));
            }

            var normalizedExtension = string.IsNullOrEmpty(extension) || extension.StartsWith(".")
                ? extension
                : "." + extension;
            var fileName = string.IsNullOrWhiteSpace(mode)
                ? $"{prefix}_{timestamp}{normalizedExtension}"
                : $"{prefix}_{mode}_{timestamp}{normalizedExtension}";

            return Path.Combine(GetCaptureDirectory(projectRoot), fileName).Replace('\\', '/');
        }
    }
}
