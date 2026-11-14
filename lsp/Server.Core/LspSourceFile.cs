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

    // Rewrites a branch directive in place to one of these; every #if/#elif/#else directive is at least as long.
    private const string ActiveDirective = "#if _";
    private const string InactiveDirective = "#if F";
    private static readonly CSharpParseOptions BranchOptions = CSharpParseOptions.Default.WithPreprocessorSymbols("_");

    private static HashSet<int> FindLoneCrsInStringLiterals(string text)
    {
        var scan = new LiteralCrScan(text, new HashSet<int>(), new HashSet<int>(), new HashSet<int>());
        scan.Collect(0, CSharpSyntaxTree.ParseText(text).GetRoot(), new TextSpan(0, text.Length));
        return scan.Positions;
    }

    // Inspects every preprocessor configuration a branch directive can select, independent of the current
    // project's symbols. Whether a line is a directive at all depends on the lexical state the branches
    // before it leave (an unterminated comment or string hides later directives), so each branch directive
    // that a parse sees is flipped in turn: the text from that directive onward is parsed again with it
    // rewritten in place to an always-true or always-false "#if", the directives it reveals are flipped the
    // same way, and nothing before it can reach it. A lone CR is kept when any of these parses reads it
    // inside a well-formed string; an unterminated string token is not valid C# in that configuration and
    // keeps nothing. Positions are offsets into Text; Activated and Deactivated hold the directives already
    // parsed each way, so each directive is parsed at most twice.
    private readonly record struct LiteralCrScan(string Text, HashSet<int> Positions, HashSet<int> Activated, HashSet<int> Deactivated)
    {
        public void Collect(int offset, SyntaxNode root, TextSpan scope)
        {
            foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
            {
                if (!IsMultiLineStringContent(token.Kind()) || HasError(token)) continue;
                var span = token.Span.Intersection(scope);
                if (span is null) continue;
                for (var i = offset + span.Value.Start; i < offset + span.Value.End; i++)
                {
                    if (Text[i] == '\r' && (i + 1 >= Text.Length || Text[i + 1] != '\n')) Positions.Add(i);
                }
            }

            foreach (var directive in root.DescendantNodes(descendIntoTrivia: true).OfType<BranchingDirectiveTriviaSyntax>())
            {
                if (directive.IsActive && scope.Contains(directive.Span)) Flip(offset + directive.Span.Start, directive.Span.Length, !directive.BranchTaken);
            }
        }

        // An unterminated literal (CS1039, CS8997) runs past where the string really ends, so its extent is
        // wrong; other errors, such as raw-string indentation, leave the extent intact. An interpolated
        // string's text token is well-formed even when the enclosing literal is not closed.
        private static bool HasError(SyntaxToken token)
        {
            var literal = token.IsKind(SyntaxKind.InterpolatedStringTextToken) ? token.Parent?.Parent : null;
            return token.GetDiagnostics().Any(d => d.Id is "CS1039" or "CS8997") ||
                literal is InterpolatedStringExpressionSyntax interpolated && interpolated.StringEndToken.IsMissing;
        }

        // Parses the text from a branch directive to the end with that branch taken or skipped. Its sibling
        // #elif/#else and #endif directives keep their meaning, so the rest of the group, and the code after
        // it, is read the way that configuration reads it.
        private void Flip(int start, int length, bool taken)
        {
            // A malformed directive (a bare "#if") is too short to rewrite; it is not valid C# anyway.
            if (length < ActiveDirective.Length || !(taken ? Activated : Deactivated).Add(start)) return;
            var directive = taken ? ActiveDirective : InactiveDirective;
            var root = CSharpSyntaxTree.ParseText(directive + new string(' ', length - directive.Length) + Text[(start + length)..], BranchOptions).GetRoot();
            Collect(start, root, TextSpan.FromBounds(length, Text.Length - start));
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
