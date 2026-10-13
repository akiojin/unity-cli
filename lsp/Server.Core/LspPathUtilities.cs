using System;

namespace UnityCli.Lsp.Core;

public static class LspPathUtilities
{
    public static string NormalizeRelative(string relative) =>
        (relative ?? string.Empty).Replace('\\', '/');

    public static string Path2Uri(string path)
    {
        var normalized = path.Replace('\\', '/');
        // Escape raw path segments before parsing so literal % sequences survive.
        if (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0])
            && normalized[1] == ':' && normalized[2] == '/')
        {
            return FileUri("file:///" + normalized[..2] + EscapePath(normalized[2..])).AbsoluteUri;
        }

        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Expected an absolute file path.", nameof(path));
        }

        var prefix = normalized.StartsWith("//", StringComparison.Ordinal) ? "file:" : "file://";
        return FileUri(prefix + EscapePath(normalized)).AbsoluteUri;
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    public static string Uri2Path(string uri) =>
        FileUri(uri).LocalPath;

    private static Uri FileUri(string value)
    {
        var uri = new Uri(value, UriKind.Absolute);
        if (!uri.IsFile)
        {
            throw new ArgumentException("Expected an absolute file path or file URI.", nameof(value));
        }

        return uri;
    }

    public static string ToRel(string fullPath, string root)
    {
        var normFull = NormalizeRelative(fullPath);
        var normRoot = NormalizeRelative(root).TrimEnd('/');
        if (normFull.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
        {
            return normFull.Substring(normRoot.Length + 1);
        }

        return normFull;
    }

    public static string NormalizeNamePath(string raw) =>
        string.Join(
            '/',
            (raw ?? string.Empty)
                .Replace('.', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        );

    public static string? ContainerPath(string namePath)
    {
        var segments = NormalizeNamePath(namePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length <= 1)
        {
            return null;
        }

        return string.Join('/', segments[..^1]);
    }

    public static string? ContainerName(string namePath)
    {
        var containerPath = ContainerPath(namePath);
        if (string.IsNullOrEmpty(containerPath))
        {
            return null;
        }

        var segments = containerPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? null : segments[^1];
    }
}
