using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UnityCliBridge.Editor.UpmSigning
{
    public static class UpmPackSigner
    {
        private const string DefaultPackageRelativePath = "Packages/unity-cli-bridge";
        private const string PackagePathArg = "-upmPackagePath";
        private const string OutputDirArg = "-upmOutputDir";

        private static PackRequest packRequest;
        private static string outputDirAbs;

        public static void PackSignedFromCommandLine()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();

                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var packagePath = GetArgValue(args, PackagePathArg);
                var packageDirAbs = Path.GetFullPath(Path.Combine(projectRoot, packagePath ?? DefaultPackageRelativePath));
                if (!File.Exists(Path.Combine(packageDirAbs, "package.json")))
                {
                    throw new ArgumentException($"package.json not found under: {packageDirAbs}");
                }

                outputDirAbs = GetArgValue(args, OutputDirArg);
                if (string.IsNullOrWhiteSpace(outputDirAbs))
                {
                    outputDirAbs = Path.GetFullPath(Path.Combine(projectRoot, "..", "dist", "upm"));
                }
                Directory.CreateDirectory(outputDirAbs);

                var orgId =
                    GetArgValue(args, "-cloudOrganization") ??
                    Environment.GetEnvironmentVariable("UNITY_CLOUD_ORG_ID") ??
                    Environment.GetEnvironmentVariable("UNITY_CLOUD_ORGANIZATION_ID");
                if (string.IsNullOrWhiteSpace(orgId))
                {
                    throw new ArgumentException(
                        "Missing org id. Provide -cloudOrganization or set UNITY_CLOUD_ORG_ID / UNITY_CLOUD_ORGANIZATION_ID."
                    );
                }

                Debug.Log($"[upm-pack] package: {packageDirAbs}");
                Debug.Log($"[upm-pack] output: {outputDirAbs}");
                Debug.Log($"[upm-pack] org: {orgId}");

                packRequest = Client.Pack(packageDirAbs, outputDirAbs, orgId);
                EditorApplication.update += PollPackRequest;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[upm-pack] Failed to start pack: {ex}");
                EditorApplication.Exit(1);
            }
        }

        private static void PollPackRequest()
        {
            if (packRequest == null || !packRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PollPackRequest;

            if (packRequest.Status != StatusCode.Success)
            {
                Debug.LogError($"[upm-pack] Pack failed: {packRequest.Error?.message ?? "Unknown error"}");
                EditorApplication.Exit(1);
                return;
            }

            var tgz = FindLatestTgz(outputDirAbs);
            if (string.IsNullOrWhiteSpace(tgz))
            {
                Debug.LogError($"[upm-pack] Pack succeeded but no .tgz found in: {outputDirAbs}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[upm-pack] Packed tgz: {tgz}");
            EditorApplication.Exit(0);
        }

        private static string FindLatestTgz(string directoryAbs)
        {
            try
            {
                return Directory
                    .GetFiles(directoryAbs, "*.tgz", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .Select(fi => fi.FullName)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
