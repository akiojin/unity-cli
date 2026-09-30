using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;
using UnityEngine;

namespace UnityCliBridge.Core
{
    /// <summary>
    /// Per-Editor discovery lockfile (<c>~/.unity-cli/editors/&lt;pid&gt;.json</c>) that lets
    /// unity-cli find this Editor by project path. A background timer refreshes the
    /// heartbeat so the file stays fresh while the main thread is busy.
    /// </summary>
    internal static class EditorLockfile
    {
        internal const int SchemaVersion = 1;
        internal const int HeartbeatIntervalMs = 5000;
        internal const string DirectoryEnvVar = "UNITY_CLI_EDITORS_DIR";

        private static readonly object gate = new object();
        private static Timer heartbeatTimer;
        private static JObject current;

        internal static string DirectoryPath => ResolveDirectory(
            Environment.GetEnvironmentVariable(DirectoryEnvVar),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        internal static string FilePath => Path.Combine(DirectoryPath, $"{CurrentPid}.json");

        private static int CurrentPid => Process.GetCurrentProcess().Id;

        internal static string ResolveDirectory(string overrideDir, string userProfile)
        {
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                return overrideDir.Trim();
            }
            return Path.Combine(userProfile ?? string.Empty, ".unity-cli", "editors");
        }

        /// <summary>
        /// The host a client should connect to for a listener bound on <paramref name="bindAddress"/>.
        /// </summary>
        internal static string ConnectHostFor(System.Net.IPAddress bindAddress)
        {
            if (bindAddress == null ||
                bindAddress.Equals(System.Net.IPAddress.Any) ||
                bindAddress.Equals(System.Net.IPAddress.Loopback))
            {
                return "127.0.0.1";
            }
            if (bindAddress.Equals(System.Net.IPAddress.IPv6Any) ||
                bindAddress.Equals(System.Net.IPAddress.IPv6Loopback))
            {
                return "::1";
            }
            return bindAddress.ToString();
        }

        internal static JObject BuildContent(
            int pid,
            string projectPath,
            string host,
            int port,
            int configuredPort,
            string unityVersion,
            string bridgeVersion,
            string state,
            double startedAt,
            double heartbeatAt)
        {
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["pid"] = pid,
                ["projectPath"] = projectPath,
                ["host"] = host,
                ["port"] = port,
                ["configuredPort"] = configuredPort,
                ["unityVersion"] = unityVersion,
                ["bridgeVersion"] = bridgeVersion,
                ["state"] = state,
                ["startedAt"] = startedAt,
                ["heartbeatAt"] = heartbeatAt,
            };
        }

        /// <summary>
        /// Publishes this Editor's listener. Must be called on the main thread.
        /// </summary>
        internal static void Publish(string host, int port, int configuredPort)
        {
            try
            {
                var now = UnixNow();
                var projectPath = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath) ?? string.Empty);
                var content = BuildContent(
                    CurrentPid,
                    projectPath,
                    host,
                    port,
                    configuredPort,
                    Application.unityVersion,
                    BridgeVersion(),
                    "ready",
                    now,
                    now);
                lock (gate)
                {
                    current = content;
                    WriteLocked();
                }
                RemoveStaleLockfiles(DirectoryPath, projectPath, CurrentPid);
                StartHeartbeat();
            }
            catch (Exception ex)
            {
                BridgeLogger.LogWarning($"Failed to write Editor lockfile: {ex.Message}");
            }
        }

        /// <summary>
        /// Keeps the lockfile across a domain reload so clients see a reloading Editor, not a missing one.
        /// </summary>
        internal static void MarkReloading()
        {
            StopHeartbeat();
            UpdateState("reloading");
        }

        internal static void Delete()
        {
            StopHeartbeat();
            lock (gate)
            {
                current = null;
                try
                {
                    File.Delete(FilePath);
                }
                catch (Exception ex)
                {
                    BridgeLogger.LogWarning($"Failed to delete Editor lockfile: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Deletes lockfiles left behind for <paramref name="projectPath"/> by Editor processes
        /// that no longer exist (crash / force quit). Lockfiles of other projects are left for
        /// clients to report as unreachable.
        /// </summary>
        internal static int RemoveStaleLockfiles(string directory, string projectPath, int currentPid)
        {
            var removed = 0;
            if (!Directory.Exists(directory))
            {
                return removed;
            }
            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    var lockfile = JObject.Parse(File.ReadAllText(file));
                    var pid = lockfile.Value<int?>("pid") ?? 0;
                    var path = lockfile.Value<string>("projectPath");
                    if (pid == currentPid || !string.Equals(path, projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!IsProcessAlive(pid))
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch (Exception)
                {
                    // A lockfile being rewritten by another Editor is not ours to judge.
                }
            }
            return removed;
        }

        internal static bool IsProcessAlive(int pid)
        {
            if (pid <= 0)
            {
                return false;
            }
            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static void UpdateState(string state)
        {
            lock (gate)
            {
                if (current == null)
                {
                    return;
                }
                current["state"] = state;
                current["heartbeatAt"] = UnixNow();
                WriteLocked();
            }
        }

        private static void StartHeartbeat()
        {
            lock (gate)
            {
                heartbeatTimer?.Dispose();
                heartbeatTimer = new Timer(_ => Heartbeat(), null, HeartbeatIntervalMs, HeartbeatIntervalMs);
            }
        }

        private static void StopHeartbeat()
        {
            lock (gate)
            {
                heartbeatTimer?.Dispose();
                heartbeatTimer = null;
            }
        }

        private static void Heartbeat()
        {
            lock (gate)
            {
                if (current == null)
                {
                    return;
                }
                current["heartbeatAt"] = UnixNow();
                try
                {
                    WriteLocked();
                }
                catch (Exception)
                {
                    // Retried on the next tick; a missed heartbeat only matters after minutes.
                }
            }
        }

        private static void WriteLocked()
        {
            var directory = DirectoryPath;
            Directory.CreateDirectory(directory);
            var target = FilePath;
            var temp = target + ".tmp";
            File.WriteAllText(temp, current.ToString(Formatting.Indented));
            // Rename over the old file so readers never observe a missing or partial lockfile.
            if (File.Exists(target))
            {
                File.Replace(temp, target, null);
            }
            else
            {
                File.Move(temp, target);
            }
        }

        private static string BridgeVersion()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(EditorLockfile).Assembly);
                return info?.version;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static double UnixNow()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
