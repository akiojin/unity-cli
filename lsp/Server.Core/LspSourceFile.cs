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
    public string ApplyNewlines(string text)
    {
        if (NewLine is null) return text;
        var literalCrs = HasLoneCr(text) ? FindLoneCrsInStringLiterals(text) : null;
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n') result.Append(NewLine);
            else if (c != '\r') result.Append(c);
            else if (i + 1 < text.Length && text[i + 1] == '\n') { result.Append(NewLine); i++; }
            else result.Append(literalCrs?.Contains(i) == true ? "\r" : NewLine);
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

    private static HashSet<int> FindLoneCrsInStringLiterals(string text) => new LiteralCrScan(text).Run();

    private enum DirectiveKind { Other, If, Elif, Else, EndIf }

    // Walks every lexer state that some preprocessor configuration reaches, independent of the current
    // project's symbols: each #if/#elif condition may be true or false. Code is lexed only from the line
    // after a directive, where the lexer state is fresh, so an unterminated comment or string in one
    // branch never decides how another branch is lexed. A skipped branch is scanned line by line as
    // Roslyn does, so a directive-looking line inside its strings ends the branch, while the same line
    // lexed as active code is string content. A lone CR is kept when any reached state reads it inside
    // a well-formed string; an unterminated string token is not valid C# in that configuration and keeps
    // nothing. States are a position plus the open #if groups, so the cost follows the directive count
    // rather than the number of configurations.
    private sealed class LiteralCrScan
    {
        private static readonly CSharpParseOptions Options = CSharpParseOptions.Default;
        private readonly string _text;
        private readonly HashSet<int> _positions = new();
        private readonly Dictionary<int, (DirectiveKind Kind, int End)?> _nextDirective = new();
        private readonly HashSet<(int Position, string Groups)> _active = new();
        private readonly HashSet<(int Position, string Groups, bool Taken)> _skipped = new();
        private readonly Stack<(int Position, string Groups, bool? Taken)> _pending = new();

        public LiteralCrScan(string text) => _text = text;

        public HashSet<int> Run()
        {
            Active(0, "");
            while (_pending.TryPop(out var state))
            {
                if (state.Taken is { } taken) ScanSkipped(state.Position, state.Groups, taken);
                else ScanActive(state.Position, state.Groups);
            }
            return _positions;
        }

        // Groups holds one char per open #if, outermost first: Else once the group has passed its #else.
        private const char Open = 'i';
        private const char Else = 'e';

        private void Active(int position, string groups)
        {
            if (_active.Add((position, groups))) _pending.Push((position, groups, null));
        }

        // A skipped branch of the innermost open #if; Taken records whether an earlier branch was taken.
        private void Skipped(int position, string groups, bool taken)
        {
            if (_skipped.Add((position, groups, taken))) _pending.Push((position, groups, taken));
        }

        // Roslyn reports a stray #elif/#else/#endif, or an #elif/#else after the group's #else, as a bad
        // directive and keeps lexing in the same state.
        private void ScanActive(int position, string groups)
        {
            if (NextDirective(position) is not var (kind, end)) return;
            var inGroup = groups.Length > 0;
            var afterElse = inGroup && groups[^1] == Else;
            switch (kind)
            {
                case DirectiveKind.If:
                    Active(end, groups + Open);
                    Skipped(end, groups + Open, taken: false);
                    break;
                case DirectiveKind.Elif when inGroup && !afterElse:
                    Skipped(end, groups, taken: true);
                    break;
                case DirectiveKind.Else when inGroup && !afterElse:
                    Skipped(end, groups[..^1] + Else, taken: true);
                    break;
                case DirectiveKind.EndIf when inGroup:
                    Active(end, groups[..^1]);
                    break;
                default:
                    Active(end, groups);
                    break;
            }
        }

        private void ScanSkipped(int position, string groups, bool taken)
        {
            var nested = 0;
            var afterElse = groups[^1] == Else;
            for (var line = position; line < _text.Length; line = LineEnd(line))
            {
                var kind = Classify(line);
                if (kind == DirectiveKind.If) nested++;
                else if (kind == DirectiveKind.EndIf && nested > 0) nested--;
                else if (nested > 0 || afterElse && kind is DirectiveKind.Elif or DirectiveKind.Else) continue;
                else if (kind == DirectiveKind.EndIf)
                {
                    Active(LineEnd(line), groups[..^1]);
                    return;
                }
                else if (kind == DirectiveKind.Elif && !taken) Active(LineEnd(line), groups);
                else if (kind == DirectiveKind.Else && taken) afterElse = true;
                else if (kind == DirectiveKind.Else)
                {
                    Active(LineEnd(line), groups[..^1] + Else);
                    return;
                }
            }
        }

        // Lexes active code from a line start up to the next directive and records the CRs of the
        // well-formed strings on the way. Lexing is independent of the depth, so it runs once per start.
        private (DirectiveKind Kind, int End)? NextDirective(int position)
        {
            if (_nextDirective.TryGetValue(position, out var cached)) return cached;
            (DirectiveKind, int)? next = null;
            foreach (var token in SyntaxFactory.ParseTokens(_text, position, position, Options))
            {
                var directive = token.LeadingTrivia.FirstOrDefault(trivia => trivia.IsDirective);
                if (directive.IsDirective)
                {
                    var start = directive.SpanStart;
                    while (start > 0 && !SyntaxFacts.IsNewLine(_text[start - 1])) start--;
                    next = (Classify(start), LineEnd(start));
                    break;
                }
                if (token.IsKind(SyntaxKind.EndOfFileToken)) break;
                CollectString(token);
            }
            _nextDirective[position] = next;
            return next;
        }

        private void CollectString(SyntaxToken token)
        {
            if (token.IsKind(SyntaxKind.InterpolatedStringToken))
            {
                if (IsUnterminated(token)) return;
                // The lexer reads an interpolated string as one token; its holes are code, not content.
                foreach (var part in SyntaxFactory.ParseExpression(token.Text, options: Options).DescendantTokens())
                {
                    if (IsMultiLineStringContent(part.Kind()) && !HasError(part)) Add(token.SpanStart + part.SpanStart, token.SpanStart + part.Span.End);
                }
            }
            else if (IsMultiLineStringContent(token.Kind()) && !HasError(token)) Add(token.SpanStart, token.Span.End);
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

        // The conditional directive starting a line: '#' and its name after optional whitespace.
        private DirectiveKind Classify(int line)
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
        private int LineEnd(int i)
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
