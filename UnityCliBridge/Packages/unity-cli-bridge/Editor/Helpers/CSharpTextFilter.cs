using System;
using System.Collections.Generic;

namespace UnityCliBridge.Helpers
{
    public static class CSharpTextFilter
    {
        // Removes comments (// and /* */) and string literals from lines to reduce false positives in text-based matching.
        public static string[] FilterLines(string[] lines)
        {
            var result = new string[lines.Length];
            bool inBlockComment = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var s = lines[i];
                result[i] = FilterLine(s, ref inBlockComment);
            }
            return result;
        }

        private static string FilterLine(string s, ref bool inBlockComment)
        {
            var chars = s.ToCharArray();
            bool inString = false;
            bool inChar = false;
            for (int i = 0; i < chars.Length; i++)
            {
                if (inBlockComment)
                {
                    if (i + 1 < chars.Length && chars[i] == '*' && chars[i + 1] == '/')
                    {
                        inBlockComment = false;
                        chars[i] = ' '; chars[i + 1] = ' ';
                        i++; continue;
                    }
                    chars[i] = ' ';
                    continue;
                }

                if (inString)
                {
                    if (chars[i] == '\\') { i++; if (i < chars.Length) chars[i] = ' '; continue; }
                    if (chars[i] == '"') { inString = false; chars[i] = ' '; continue; }
                    chars[i] = ' ';
                    continue;
                }
                if (inChar)
                {
                    if (chars[i] == '\\') { i++; if (i < chars.Length) chars[i] = ' '; continue; }
                    if (chars[i] == '\'') { inChar = false; chars[i] = ' '; continue; }
                    chars[i] = ' ';
                    continue;
                }

                if (i + 1 < chars.Length && chars[i] == '/' && chars[i + 1] == '*')
                {
                    inBlockComment = true;
                    chars[i] = ' '; chars[i + 1] = ' ';
                    i++; continue;
                }
                if (i + 1 < chars.Length && chars[i] == '/' && chars[i + 1] == '/')
                {
                    // line comment: null out rest of line
                    for (int j = i; j < chars.Length; j++) chars[j] = ' ';
                    break;
                }
                if (chars[i] == '"') { inString = true; chars[i] = ' '; continue; }
                if (chars[i] == '\'') { inChar = true; chars[i] = ' '; continue; }
            }
            return new string(chars);
        }
    }
}

