using System;
using System.Text.RegularExpressions;

namespace UnityCliBridge.Helpers
{
    public static class GlobMatcher
    {
        public static bool IsMatch(string path, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            var rx = GlobToRegex(pattern);
            return Regex.IsMatch(path, rx, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        // Supports **, *, ?, and simple path globs. Converts to regex string.
        public static string GlobToRegex(string pattern)
        {
            // Escape regex special, then replace globs
            string rx = Regex.Escape(pattern)
                .Replace(@"\*\*", "__GLOBSTAR__") // temp token
                .Replace(@"\*", "[^/]*")
                .Replace(@"\?", ".")
                .Replace("__GLOBSTAR__", ".*");
            return "^" + rx + "$";
        }
    }
}

