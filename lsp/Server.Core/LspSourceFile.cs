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
        var literalCrs = text.Contains('\r') ? FindLoneCrsInStringLiterals(text) : null;
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

    private static HashSet<int> FindLoneCrsInStringLiterals(string text)
    {
        var positions = new HashSet<int>();
        CollectLoneCrsInStringLiterals(text, text, new TextSpan(0, text.Length), positions);
        return positions;
    }

    // Inspect every preprocessor branch, independent of the current project's symbols. Each inactive
    // branch is re-parsed alone in a same-length copy, so another branch's lexical state (an
    // unterminated comment or string) cannot change how it is read, and nested directives are
    // resolved as they would be with that branch active.
    private static void CollectLoneCrsInStringLiterals(string text, string syntaxText, TextSpan scope, HashSet<int> positions)
    {
        var root = CSharpSyntaxTree.ParseText(syntaxText).GetRoot();
        foreach (var token in root.DescendantTokens())
        {
            if (!IsMultiLineStringContent(token.Kind())) continue;
            var span = token.Span.Intersection(scope);
            if (span is null) continue;
            for (var i = span.Value.Start; i < span.Value.End; i++)
            {
                if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) positions.Add(i);
            }
        }

        foreach (var directive in root.DescendantNodes(descendIntoTrivia: true).OfType<IfDirectiveTriviaSyntax>())
        {
            if (!directive.IsActive || !scope.Contains(directive.Span)) continue;
            var group = directive.GetRelatedDirectives();
            for (var b = 0; b + 1 < group.Count; b++)
            {
                if (group[b] is BranchingDirectiveTriviaSyntax { BranchTaken: true }) continue;
                var branch = TextSpan.FromBounds(group[b].FullSpan.End, group[b + 1].FullSpan.Start);
                var chars = syntaxText.ToCharArray();
                for (var g = 0; g + 1 < group.Count; g++)
                {
                    Blank(chars, group[g].Span);
                    if (g != b) Blank(chars, TextSpan.FromBounds(group[g].FullSpan.End, group[g + 1].FullSpan.Start));
                }
                Blank(chars, group[^1].Span);
                CollectLoneCrsInStringLiterals(text, new string(chars), branch, positions);
            }
        }
    }

    // Replace with spaces but keep line breaks, so every position still maps to the original text.
    private static void Blank(char[] chars, TextSpan span)
    {
        for (var i = span.Start; i < span.End; i++)
        {
            if (!IsLineBreak(chars[i])) chars[i] = ' ';
        }
    }

    private static bool IsLineBreak(char c) => c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';

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
