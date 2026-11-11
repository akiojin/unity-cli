using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Handlers
{
    // The Editor update loop must remain free for import, compilation and reload.
    // SessionState survives a domain reload; only status reads are retried by the CLI.
    [InitializeOnLoad]
    internal static class UnityPackageHandler
    {
        private const string ActiveKey = "UnityCliBridge.PackageImport.Active";
        private const string StateKey = "UnityCliBridge.PackageImport.Request.";
        private static double idleSince;
        private static double nextUpdate;

        static UnityPackageHandler()
        {
            AssetDatabase.importPackageCompleted += OnCompleted;
            AssetDatabase.importPackageFailed += OnFailed;
            AssetDatabase.importPackageCancelled += name => OnFailed(name, "Package import cancelled");
            EditorApplication.update += Update;
            idleSince = EditorApplication.timeSinceStartup;
            var active = Active();
            if (active != null)
            {
                active["domainReloads"] = (active.Value<int?>("domainReloads") ?? 0) + 1;
                Save(active);
            }
        }

        internal static object HandleCommand(string action, JObject parameters)
        {
            parameters = parameters ?? new JObject();
            try
            {
                switch (action)
                {
                    case "list_cached": return ListCached(parameters);
                    case "import_cached":
                        var listing = ListCached(parameters);
                        if (listing["error"] != null) return listing;
                        var selector = parameters.Value<string>("nameOrId");
                        if (string.IsNullOrWhiteSpace(selector)) return Error("INVALID_ARGUMENT", "nameOrId is required");
                        var matches = listing["packages"].OfType<JObject>().Where(item =>
                            string.Equals(item.Value<string>("id"), selector, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.Value<string>("name"), selector, StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (matches.Length == 0) return Error("PACKAGE_NOT_FOUND", "No cached package matches nameOrId");
                        if (matches.Length != 1) return Error("PACKAGE_AMBIGUOUS", "Multiple packages match; select a listed ID", new JArray(matches));
                        var import = (JObject)parameters.DeepClone();
                        import["path"] = matches[0]["path"];
                        return Start(import);
                    case "import": return Start(parameters);
                    case "import_status":
                        var id = parameters.Value<string>("requestId");
                        if (!ValidId(id)) return Error("INVALID_ARGUMENT", "requestId must contain 1-128 letters, digits, underscores or hyphens");
                        return Read(id) ?? Error("PACKAGE_IMPORT_NOT_FOUND", "No import with this requestId in this Editor session");
                    default: return Error("INVALID_ARGUMENT", "Unknown package import action");
                }
            }
            catch (Exception exception)
            {
                return Error("PACKAGE_IO_ERROR", exception.Message);
            }
        }

        private static JObject Error(string code, string message, JToken matches = null)
        {
            var result = new JObject { ["success"] = false, ["code"] = code, ["error"] = message };
            if (matches != null) result["matches"] = matches;
            return result;
        }

        private static bool ValidId(string id) => id != null && Regex.IsMatch(id, "^[a-zA-Z0-9_-]{1,128}$");
        private static JObject Read(string id)
        {
            var json = SessionState.GetString(StateKey + id, "");
            return string.IsNullOrEmpty(json) ? null : JObject.Parse(json);
        }
        private static void Save(JObject state) => SessionState.SetString(StateKey + state.Value<string>("requestId"), state.ToString());
        private static JObject Active() => Read(SessionState.GetString(ActiveKey, ""));

        private static JObject Start(JObject parameters)
        {
            if (parameters.Value<bool?>("interactive") == true) return Error("INVALID_ARGUMENT", "Only interactive:false is supported");
            var path = parameters.Value<string>("path");
            if (string.IsNullOrWhiteSpace(path)) return Error("INVALID_ARGUMENT", "path is required");
            path = Path.GetFullPath(path);
            var id = parameters.Value<string>("requestId") ?? Guid.NewGuid().ToString("N");
            if (!ValidId(id)) return Error("INVALID_ARGUMENT", "Invalid requestId");
            var existing = Read(id);
            if (existing != null)
            {
                if (!string.Equals(existing.Value<string>("path"), path.Replace('\\', '/'), StringComparison.Ordinal))
                    return Error("PACKAGE_REQUEST_CONFLICT", "requestId already refers to another path");
                return existing;
            }
            if (!File.Exists(path)) return Error("PACKAGE_NOT_FOUND", "Package file does not exist: " + path);
            if (!string.Equals(Path.GetExtension(path), ".unitypackage", StringComparison.OrdinalIgnoreCase))
                return Error("INVALID_UNITYPACKAGE", "Expected a .unitypackage file");
            if (Active() != null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return Error("PACKAGE_IMPORT_BUSY", "Wait for Edit Mode and the current import/compilation to finish");
            string[] paths;
            try { paths = ReadAssetPaths(path); }
            catch (Exception exception) { return Error("INVALID_UNITYPACKAGE", exception.Message); }
            var state = new JObject
            {
                ["success"] = true, ["requestId"] = id, ["path"] = path.Replace('\\', '/'),
                ["interactive"] = false, ["status"] = "queued", ["expectedAssets"] = new JArray(paths),
                ["compileState"] = JObject.FromObject(CompilationHandler.GetCompilationState(new JObject())),
                ["importedAssets"] = new JArray(), ["domainReloads"] = 0, ["startedAt"] = DateTime.UtcNow.ToString("o")
            };
            Save(state);
            SessionState.SetString(ActiveKey, id);
            EditorApplication.delayCall += () =>
            {
                state["status"] = "importing";
                Save(state);
                try { AssetDatabase.ImportPackage(path, false); }
                catch (Exception exception) { OnFailed(Path.GetFileNameWithoutExtension(path), exception.Message); }
            };
            return state;
        }

        private static bool Matches(JObject state, string name) => state != null &&
            (string.Equals(Path.GetFileNameWithoutExtension(state.Value<string>("path")), Path.GetFileName(name), StringComparison.Ordinal) ||
             string.Equals(Path.GetFileName(state.Value<string>("path")), Path.GetFileName(name), StringComparison.Ordinal));

        private static void OnCompleted(string name)
        {
            var state = Active();
            if (!Matches(state, name)) return;
            state["status"] = "imported";
            Save(state); // Save before Refresh can initiate script compilation/reload.
            idleSince = EditorApplication.timeSinceStartup;
            AssetDatabase.Refresh();
        }

        private static void OnFailed(string name, string message)
        {
            var state = Active();
            if (!Matches(state, name)) return;
            state["success"] = false;
            state["status"] = "failed";
            state["code"] = "PACKAGE_IMPORT_FAILED";
            state["error"] = message;
            Save(state);
            SessionState.EraseString(ActiveKey);
        }

        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextUpdate) return;
            nextUpdate = EditorApplication.timeSinceStartup + 0.25;
            var state = Active();
            if (state == null) return;
            var compilation = JObject.FromObject(CompilationHandler.GetCompilationState(new JObject()));
            state["compileState"] = compilation;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || state.Value<string>("status") != "imported")
            {
                idleSince = EditorApplication.timeSinceStartup;
                Save(state);
                return;
            }
            // Require stable idle after import and after static initialization on reload.
            if (EditorApplication.timeSinceStartup - idleSince < 1) { Save(state); return; }
            var paths = state["expectedAssets"].Values<string>().ToArray();
            var imported = paths.Where(path => (File.Exists(path) || Directory.Exists(path)) &&
                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))).ToArray();
            state["importedAssets"] = new JArray(imported);
            state["completedAt"] = DateTime.UtcNow.ToString("o");
            if (imported.Length != paths.Length || compilation["error"] != null || compilation.Value<int>("errorCount") > 0)
            {
                state["success"] = false;
                state["status"] = "failed";
                state["code"] = "PACKAGE_IMPORT_FAILED";
                state["error"] = "Import finished with missing assets or compilation errors; inspect compileState";
            }
            else state["status"] = "completed";
            Save(state);
            SessionState.EraseString(ActiveKey);
        }

        private static string CachePath(JObject parameters)
        {
            var root = parameters.Value<string>("cachePath");
            if (string.IsNullOrWhiteSpace(root)) root = Environment.GetEnvironmentVariable("UNITY_CLI_ASSET_STORE_CACHE_PATH");
            if (string.IsNullOrWhiteSpace(root))
            {
                var unityRoot = Environment.GetEnvironmentVariable("ASSETSTORE_CACHE_PATH");
                if (!string.IsNullOrWhiteSpace(unityRoot)) root = Path.Combine(unityRoot, "Asset Store-5.x");
            }
            if (string.IsNullOrWhiteSpace(root))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                if (Application.platform == RuntimePlatform.WindowsEditor)
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x");
                else if (Application.platform == RuntimePlatform.OSXEditor)
                    root = Path.Combine(home, "Library", "Unity", "Asset Store-5.x");
                else root = Path.Combine(home, ".local", "share", "unity3d", "Asset Store-5.x");
            }
            return Path.GetFullPath(root);
        }

        private static JObject ListCached(JObject parameters)
        {
            var root = CachePath(parameters);
            var packages = new JArray();
            if (Directory.Exists(root))
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => string.Equals(Path.GetExtension(path), ".unitypackage", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.Ordinal))
                {
                    var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var relative = path.Substring(prefix.Length).Replace('\\', '/');
                    var metadata = ReadMetadata(path);
                    string id;
                    using (var sha = SHA256.Create()) id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(relative))).Replace("-", "").ToLowerInvariant();
                    packages.Add(new JObject
                    {
                        ["id"] = metadata.Value<string>("id") ?? id,
                        ["publisher"] = metadata["publisher"]?["label"]?.Value<string>() ?? (relative.Contains("/") ? relative.Split('/')[0] : null),
                        ["name"] = metadata.Value<string>("title") ?? Path.GetFileNameWithoutExtension(path),
                        ["version"] = metadata.Value<string>("version"),
                        ["path"] = Path.GetFullPath(path).Replace('\\', '/'), ["size"] = new FileInfo(path).Length
                    });
                }
            }
            return new JObject { ["success"] = true, ["cachePath"] = root.Replace('\\', '/'), ["packages"] = packages, ["totalCount"] = packages.Count };
        }

        // Asset Store downloads embed JSON in gzip's A$ extra subfield. Read only
        // the bounded header, never decompress gigabytes merely to list a cache.
        private static JObject ReadMetadata(string path)
        {
            try
            {
                using (var file = File.OpenRead(path))
                using (var reader = new BinaryReader(file))
                {
                    var header = reader.ReadBytes(10);
                    if (header.Length != 10 || header[0] != 0x1f || header[1] != 0x8b || (header[3] & 4) == 0) return new JObject();
                    var extra = reader.ReadBytes(reader.ReadUInt16());
                    for (var offset = 0; offset + 4 <= extra.Length;)
                    {
                        var length = extra[offset + 2] | (extra[offset + 3] << 8);
                        if (offset + 4 + length > extra.Length) break;
                        if (extra[offset] == (byte)'A' && extra[offset + 1] == (byte)'$')
                            return JObject.Parse(Encoding.UTF8.GetString(extra, offset + 4, length));
                        offset += 4 + length;
                    }
                }
            }
            catch (Exception) { /* Missing/malformed optional metadata uses path-derived fields. */ }
            return new JObject();
        }

        // Validate the gzip/tar before calling Unity, which can otherwise only log
        // malformed archives. Never extract it ourselves or trust archive paths.
        private static string[] ReadAssetPaths(string path)
        {
            var paths = new Dictionary<string, string>();
            var contents = new HashSet<string>();
            var header = new byte[512];
            var buffer = new byte[81920];
            long total = 0;
            using (var file = File.OpenRead(path))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                while (true)
                {
                    ReadExactly(gzip, header, header.Length);
                    total += header.Length;
                    if (total > 16L * 1024 * 1024 * 1024) throw new InvalidDataException("Package exceeds 16 GiB expanded limit");
                    if (header.All(value => value == 0))
                    {
                        ReadExactly(gzip, header, header.Length);
                        if (header.Any(value => value != 0)) throw new InvalidDataException("Invalid tar terminator");
                        int read;
                        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (buffer.Take(read).Any(value => value != 0) || (total += read) > 16L * 1024 * 1024 * 1024)
                                throw new InvalidDataException("Invalid trailing tar data");
                        }
                        break;
                    }
                    var checksum = Octal(header, 148, 8);
                    var actual = header.Select((value, index) => index >= 148 && index < 156 ? 32 : (int)value).Sum();
                    if (checksum != actual) throw new InvalidDataException("Invalid tar checksum");
                    var name = Encoding.ASCII.GetString(header, 0, 100).TrimEnd('\0');
                    var size = Octal(header, 124, 12);
                    if (size < 0 || size > 16L * 1024 * 1024 * 1024 || (total += size) > 16L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("Package exceeds 16 GiB expanded limit");
                    if (header[156] == (byte)'5' && size == 0 && Regex.IsMatch(name, "^[a-fA-F0-9]{32}/$")) continue;
                    if ((header[156] != 0 && header[156] != (byte)'0') || !Regex.IsMatch(name, "^[a-fA-F0-9]{32}/(pathname|asset|asset.meta|preview.png)$"))
                        throw new InvalidDataException("Unsupported unitypackage entry: " + name);
                    if (name.EndsWith("/pathname", StringComparison.Ordinal))
                    {
                        if (size == 0 || size > 4096) throw new InvalidDataException("Invalid asset pathname length");
                        ReadExactly(gzip, buffer, (int)size);
                        var asset = new UTF8Encoding(false, true).GetString(buffer, 0, (int)size).TrimEnd('\0', '\n', '\r');
                        if (!(asset == "Assets" || asset.StartsWith("Assets/", StringComparison.Ordinal)) ||
                            asset.Contains('\\') || asset.Contains(':') || asset.Contains('\0') || asset.Split('/').Any(part => part == ".." || part == "." || part == ""))
                            throw new InvalidDataException("Unsafe asset pathname: " + asset);
                        paths.Add(name.Substring(0, 32), asset);
                    }
                    else
                    {
                        if (name.EndsWith("/asset", StringComparison.Ordinal) || name.EndsWith("/asset.meta", StringComparison.Ordinal)) contents.Add(name.Substring(0, 32));
                        var remaining = size;
                        while (remaining > 0)
                        {
                            var count = (int)Math.Min(remaining, buffer.Length);
                            ReadExactly(gzip, buffer, count);
                            remaining -= count;
                        }
                    }
                    var padding = (int)((512 - size % 512) % 512);
                    ReadExactly(gzip, buffer, padding);
                }
            }
            if (paths.Count == 0 || paths.Keys.Any(key => !contents.Contains(key))) throw new InvalidDataException("Package has no complete asset entries");
            return paths.Values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private static long Octal(byte[] data, int offset, int length) =>
            Convert.ToInt64(Encoding.ASCII.GetString(data, offset, length).Trim('\0', ' '), 8);

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read == 0) throw new InvalidDataException("Truncated unitypackage archive");
                offset += read;
            }
        }
    }
}
