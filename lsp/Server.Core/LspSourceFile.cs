using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UnityCli.Lsp.Core;

/// <summary>
/// On-disk shape of a C# source file that LSP writes must keep: the encoding (and whether it
/// carries a BOM) and the newline convention. New files are UTF-8 without a BOM.
/// </summary>
public readonly record struct LspSourceFileFormat(Encoding Encoding, string? NewLine)
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static LspSourceFileFormat NewFile { get; } = new(Utf8NoBom, null);

    public static LspSourceFileFormat Detect(byte[] bytes) => Decode(bytes).Format;

    public static (string Text, LspSourceFileFormat Format) Decode(byte[] bytes)
    {
        var encoding = DetectEncoding(bytes);
        var preamble = encoding.GetPreamble().Length;
        var text = encoding.GetString(bytes, preamble, bytes.Length - preamble);
        return (text, new LspSourceFileFormat(encoding, DetectNewLine(text)));
    }

    /// <summary>
    /// Rewrites line breaks (CRLF, LF, and a lone CR, which C# also treats as a line terminator) to the
    /// file's convention; mixed or unknown conventions are left as given. A lone CR inside a string
    /// literal is part of the string's value and is kept.
    /// </summary>
    public string ApplyNewlines(string text) => ApplyNewlines(text, MaxScanStates);

    internal string ApplyNewlines(string text, int maxScanStates)
    {
        if (NewLine is null) return text;
        var hasLoneCr = HasLoneCr(text);
        var literalCrs = hasLoneCr ? FindLoneCrsInStringLiterals(text, maxScanStates) : null;
        // A scan over its state budget keeps every lone CR: rewriting one inside a string changes its value.
        var keepAll = hasLoneCr && literalCrs is null;
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n') result.Append(NewLine);
            else if (c != '\r') result.Append(c);
            else if (i + 1 < text.Length && text[i + 1] == '\n') { result.Append(NewLine); i++; }
            else result.Append(keepAll || literalCrs?.Contains(i) == true ? "\r" : NewLine);
        }
        return result.ToString();
    }

    // Plain CRLF input has no lone CR to classify, so it skips parsing.
    private static bool HasLoneCr(string text)
    {
        for (var i = text.IndexOf('\r'); i >= 0; i = text.IndexOf('\r', i + 1))
        {
            if (i + 1 >= text.Length || text[i + 1] != '\n') return true;
        }
        return false;
    }

    private const int MaxScanStates = 200_000;

    // Returns null when the scan exceeds maxStates.
    private static HashSet<int>? FindLoneCrsInStringLiterals(string text, int maxStates) => new LiteralCrScan(text, maxStates).Run();

    private enum DirectiveKind { Other, If, Elif, Else, EndIf }

    // Walks every lexer state that some preprocessor configuration reaches, independent of the current
    // project's symbols: each #if/#elif condition may be true or false. Code is lexed only from the line
    // after a directive, where the lexer state is fresh, so an unterminated comment or string in one
    // branch never decides how another branch is lexed. A skipped branch is scanned line by line as
    // Roslyn does, so a directive-looking line inside its strings ends the branch, while the same line
    // lexed as active code is string content. A lone CR is kept when any reached state reads it inside
    // a well-formed string; an unterminated string token is not valid C# in that configuration and keeps
    // nothing.
    //
    // A state is the innermost open #if group, a position at its level, and whether the group has passed
    // its #else; nothing below the innermost group decides what happens at that level. Each group is
    // solved once: the positions after its #endif are its exits, and every caller resumes at each exit
    // with its own state. The cost therefore follows the directive count rather than the number of
    // configurations or the nesting depth.
    private sealed class LiteralCrScan
    {
        private const int Root = -1;
        private static readonly CSharpParseOptions Options = CSharpParseOptions.Default.WithPreprocessorSymbols(Lines.TakenSymbol);
        private readonly string _text;
        private readonly string _lexText;
        private readonly int _lastCommentEnd;
        private readonly int _maxStates;
        private readonly HashSet<int> _positions = new();
        private readonly Dictionary<int, (DirectiveKind Kind, int End)?> _nextDirective = new();
        private readonly Lines _lines;
        private readonly HashSet<(int Group, int Position, bool AfterElse)> _active = new();
        private readonly Stack<(int Group, int Position, bool AfterElse)> _pending = new();
        private readonly Dictionary<int, List<int>> _exits = new();
        private readonly Dictionary<int, List<(int Group, bool AfterElse)>> _callers = new();

        public LiteralCrScan(string text, int maxStates)
        {
            _text = text;
            _maxStates = maxStates;
            _lines = new Lines(text);
            _lexText = _lines.WithTakenConditions();
            _lastCommentEnd = text.LastIndexOf("*/", StringComparison.Ordinal);
        }

        public HashSet<int>? Run()
        {
            Active(Root, 0, afterElse: false);
            while (_pending.TryPop(out var state))
            {
                if (_active.Count + _exits.Count > _maxStates) return null;
                ScanActive(state.Group, state.Position, state.AfterElse);
            }
            return _positions;
        }

        // Group is the position after the innermost open #if, or Root at the file level.
        private void Active(int group, int position, bool afterElse)
        {
            if (_active.Add((group, position, afterElse))) _pending.Push((group, position, afterElse));
        }

        // Roslyn reports a stray #elif/#else/#endif, or an #elif/#else after the group's #else, as a bad
        // directive and keeps lexing in the same state.
        private void ScanActive(int group, int position, bool afterElse)
        {
            if (NextDirective(position) is not var (kind, end)) return;
            var inGroup = group != Root;
            switch (kind)
            {
                case DirectiveKind.If:
                    Enter(end, group, afterElse);
                    break;
                case DirectiveKind.Elif or DirectiveKind.Else when inGroup && !afterElse:
                    // The rest of the group is skipped; only its #endif matters.
                    if (_lines.MatchingEndIf(end) is { } endIf) Exit(group, _lines.LineEnd(endIf));
                    break;
                case DirectiveKind.EndIf when inGroup:
                    Exit(group, end);
                    break;
                default:
                    Active(group, end, afterElse);
                    break;
            }
        }

        // Opens the group whose #if ends at start; the caller resumes after each of its exits.
        private void Enter(int start, int caller, bool callerAfterElse)
        {
            var isNew = !_exits.TryGetValue(start, out var exits);
            if (isNew) _exits[start] = exits = new List<int>();
            if (!_callers.TryGetValue(start, out var callers)) _callers[start] = callers = new List<(int, bool)>();
            if (callers.Contains((caller, callerAfterElse))) return;
            callers.Add((caller, callerAfterElse));
            foreach (var exit in exits!) Active(caller, exit, callerAfterElse);
            if (!isNew) return;

            // Taken: the #if branch. Skipped: each later #elif may be the first branch taken, and an #else is
            // taken once every earlier condition is false.
            Active(start, start, afterElse: false);
            foreach (var (kind, line) in _lines.Branches(start))
            {
                var next = _lines.LineEnd(line);
                if (kind == DirectiveKind.Elif) Active(start, next, afterElse: false);
                else if (kind == DirectiveKind.Else)
                {
                    Active(start, next, afterElse: true);
                    return;
                }
                else Exit(start, next);
            }
        }

        private void Exit(int group, int position)
        {
            var exits = _exits[group];
            if (exits.Contains(position)) return;
            exits.Add(position);
            foreach (var (caller, afterElse) in _callers[group]) Active(caller, position, afterElse);
        }

        // Lexes active code from a line start up to the next directive and records the CRs of the
        // well-formed strings on the way. Lexing is independent of the group, so it runs once per start.
        // The lexer reads a token's whole leading trivia before returning it, so it lexes a window that
        // doubles until the next directive's line is inside it; tokens before that line end before it,
        // so the window does not change them.
        private (DirectiveKind Kind, int End)? NextDirective(int position)
        {
            if (_nextDirective.TryGetValue(position, out var cached)) return cached;
            for (var length = 512; ; length *= 2)
            {
                var end = (int)Math.Min(_text.Length, (long)position + length);
                var complete = end == _text.Length;
                var strings = new List<SyntaxToken>();
                (DirectiveKind, int)? next = null;
                var found = false;
                foreach (var token in SyntaxFactory.ParseTokens(_lexText.Substring(position, end - position), 0, position, Options))
                {
                    var directive = token.LeadingTrivia.FirstOrDefault(trivia => trivia.IsDirective);
                    if (directive.IsDirective)
                    {
                        var start = directive.SpanStart;
                        while (start > 0 && !SyntaxFacts.IsNewLine(_text[start - 1])) start--;
                        var lineEnd = _lines.LineEnd(start);
                        found = complete || lineEnd < end;
                        next = (_lines.Classify(start), lineEnd);
                        break;
                    }
                    if (token.IsKind(SyntaxKind.EndOfFileToken))
                    {
                        found = complete || IsUnterminatedComment(token.LeadingTrivia.LastOrDefault());
                        break;
                    }
                    strings.Add(token);
                }
                if (!found) continue;
                foreach (var token in strings) CollectString(token);
                _nextDirective[position] = next;
                return next;
            }
        }

        // A block comment with no "*/" after its opening runs to the end of the file, hiding everything after it.
        private bool IsUnterminatedComment(SyntaxTrivia trivia) =>
            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) && _lastCommentEnd < trivia.SpanStart + 2;

        private void CollectString(SyntaxToken token, int offset = 0)
        {
            if (token.IsKind(SyntaxKind.InterpolatedStringToken))
            {
                // The lexer reads an interpolated string as one token; its holes are code, not content.
                // Judge each parsed literal separately: a closed literal in a hole still counts when
                // the outer interpolation is unterminated. HasError rejects only the outer text.
                // Error recovery may put a closed inner literal in skipped-token trivia.
                foreach (var part in SyntaxFactory.ParseExpression(token.Text, options: Options).DescendantTokens(descendIntoTrivia: true))
                {
                    CollectString(part, offset + token.SpanStart);
                }
            }
            else if (IsMultiLineStringContent(token.Kind()) && !HasError(token)) Add(offset + token.SpanStart, offset + token.Span.End);
        }

        private void Add(int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                if (_text[i] == '\r' && (i + 1 >= _text.Length || _text[i + 1] != '\n')) _positions.Add(i);
            }
        }

        // An unterminated literal (CS1039, CS8997) runs past where the string really ends, so its extent is
        // wrong; other errors, such as raw-string indentation, leave the extent intact. An interpolated
        // string's text token is well-formed only when the enclosing literal is closed. Only the token's
        // own diagnostics count: trailing trivia can hold a later, unrelated literal's.
        private static bool IsUnterminated(SyntaxToken token) => token.WithoutTrivia().GetDiagnostics().Any(d => d.Id is "CS1039" or "CS8997");

        private static bool HasError(SyntaxToken token)
        {
            var literal = token.IsKind(SyntaxKind.InterpolatedStringTextToken) ? token.Parent?.Parent : null;
            return IsUnterminated(token) || literal is InterpolatedStringExpressionSyntax interpolated && interpolated.StringEndToken.IsMissing;
        }

    }

    // The lines as Roslyn's scan of skipped text sees them, indexed so that a skipped group's #elif, #else,
    // and #endif lines are found without rescanning the group. A skipped group ends at the first #endif
    // that brings the #if/#endif count back below its level.
    private sealed class Lines
    {
        private readonly string _text;
        private readonly List<int> _starts = new();
        private readonly Dictionary<int, int> _index = new();
        private readonly List<DirectiveKind> _kinds = new();
        // The #if minus #endif count before each line.
        private readonly List<int> _levels = new();
        private readonly Dictionary<int, List<int>> _endIfsByLevel = new();
        private readonly Dictionary<int, List<int>> _branchesByLevel = new();

        public Lines(string text)
        {
            _text = text;
            var level = 0;
            for (var position = 0; position < text.Length; position = LineEnd(position))
            {
                var line = _starts.Count;
                var kind = Classify(position);
                _index[position] = line;
                _starts.Add(position);
                _kinds.Add(kind);
                _levels.Add(level);
                if (kind == DirectiveKind.EndIf) Lookup(_endIfsByLevel, level).Add(line);
                else if (kind is DirectiveKind.Elif or DirectiveKind.Else) Lookup(_branchesByLevel, level).Add(line);
                if (kind == DirectiveKind.If) level++;
                else if (kind == DirectiveKind.EndIf) level--;
            }
        }

        public const string TakenSymbol = "_";

        // The lexer reads a whole skipped group before the token after a false #if, which would make
        // lexing up to the next directive cost the rest of a deeply nested file. The copy it lexes has each
        // #if condition made only of plain characters replaced, in place, by a defined symbol. Such
        // characters lex the same as the symbol and spaces inside a string, comment, or skipped text,
        // so token boundaries and offsets are unchanged.
        public string WithTakenConditions()
        {
            var copy = _text.ToCharArray();
            for (var line = 0; line < _starts.Count; line++)
            {
                if (_kinds[line] != DirectiveKind.If) continue;
                var start = SkipWhitespace(_text.IndexOf('#', _starts[line]) + 1) + "if".Length;
                var end = start;
                while (end < _text.Length && !SyntaxFacts.IsNewLine(_text[end])) end++;
                // At least two characters: a space keeps the symbol apart from "if".
                if (end - start < 2 || _text[start..end].Trim().Length == 0 || !_text[start..end].All(IsPlainConditionCharacter)) continue;
                for (var i = start; i < end; i++) copy[i] = ' ';
                copy[end - 1] = TakenSymbol[0];
            }
            return new string(copy);
        }

        private static bool IsPlainConditionCharacter(char c) =>
            char.IsAsciiLetterOrDigit(c) || c is '_' or '!' or '&' or '|' or '(' or ')' or '=' or ' ' or '\t';

        // The start of the #endif line that closes the skipped group starting at position, if any.
        public int? MatchingEndIf(int position) =>
            _index.TryGetValue(position, out var line) && EndIfLine(line) is { } endIf ? _starts[endIf] : null;

        // The #elif and #else lines at the level of the skipped group starting at position, then its #endif.
        public IEnumerable<(DirectiveKind Kind, int Start)> Branches(int position)
        {
            if (!_index.TryGetValue(position, out var line)) yield break;
            var endIf = EndIfLine(line);
            if (_branchesByLevel.TryGetValue(_levels[line], out var branches))
            {
                for (var i = FirstAtOrAfter(branches, line); i < branches.Count && (endIf is null || branches[i] < endIf); i++)
                    yield return (_kinds[branches[i]], _starts[branches[i]]);
            }
            if (endIf is { } end) yield return (DirectiveKind.EndIf, _starts[end]);
        }

        private int? EndIfLine(int line)
        {
            if (!_endIfsByLevel.TryGetValue(_levels[line], out var endIfs)) return null;
            var i = FirstAtOrAfter(endIfs, line);
            return i < endIfs.Count ? endIfs[i] : null;
        }

        private static int FirstAtOrAfter(List<int> sorted, int value)
        {
            var i = sorted.BinarySearch(value);
            return i >= 0 ? i : ~i;
        }

        private static List<int> Lookup(Dictionary<int, List<int>> map, int key)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>();
            return list;
        }

        // The conditional directive starting a line: '#' and its name after optional whitespace.
        public DirectiveKind Classify(int line)
        {
            var i = SkipWhitespace(line);
            if (i >= _text.Length || _text[i] != '#') return DirectiveKind.Other;
            var nameStart = SkipWhitespace(i + 1);
            var nameEnd = nameStart;
            while (nameEnd < _text.Length && SyntaxFacts.IsIdentifierPartCharacter(_text[nameEnd])) nameEnd++;
            return _text[nameStart..nameEnd] switch
            {
                "if" => DirectiveKind.If,
                "elif" => DirectiveKind.Elif,
                "else" => DirectiveKind.Else,
                "endif" => DirectiveKind.EndIf,
                _ => DirectiveKind.Other,
            };
        }

        private int SkipWhitespace(int i)
        {
            while (i < _text.Length && SyntaxFacts.IsWhitespace(_text[i])) i++;
            return i;
        }

        // The start of the next line; CRLF is one line break.
        public int LineEnd(int i)
        {
            while (i < _text.Length && !SyntaxFacts.IsNewLine(_text[i])) i++;
            if (i < _text.Length && _text[i] == '\r' && i + 1 < _text.Length && _text[i + 1] == '\n') i++;
            return Math.Min(i + 1, _text.Length);
        }
    }

    // Only verbatim, raw, and interpolated text can span lines; a CR ends any other literal.
    private static bool IsMultiLineStringContent(SyntaxKind kind) => kind is
        SyntaxKind.StringLiteralToken or SyntaxKind.Utf8StringLiteralToken or
        SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken or
        SyntaxKind.InterpolatedStringTextToken;

    private static Encoding DetectEncoding(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0) return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF) return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        return Utf8NoBom;
    }

    private static string? DetectNewLine(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lf++;
            else if (text[i] != '\r') continue;
            else if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
            else cr++;
        }
        if (crlf > 0 && lf == 0 && cr == 0) return "\r\n";
        if (lf > 0 && crlf == 0 && cr == 0) return "\n";
        if (cr > 0 && crlf == 0 && lf == 0) return "\r";
        return null;
    }
}

public static class LspSourceFile
{
    public static async Task<(string Text, LspSourceFileFormat Format)> ReadAsync(string path)
        => LspSourceFileFormat.Decode(await File.ReadAllBytesAsync(path));

    public static Task WriteAsync(string path, string text, LspSourceFileFormat format)
        => File.WriteAllTextAsync(path, format.ApplyNewlines(text), format.Encoding);
}
