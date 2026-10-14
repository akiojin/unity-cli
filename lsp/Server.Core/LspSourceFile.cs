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

    private const string ActiveSymbol = "_";
    private const string ActiveDirective = "#if " + ActiveSymbol;
    private static readonly CSharpParseOptions ActiveBranchOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(ActiveSymbol);

    private static HashSet<int> FindLoneCrsInStringLiterals(string text)
    {
        var scan = new LiteralCrScan(text, new HashSet<int>(), new HashSet<int>());
        scan.Collect(0, CSharpSyntaxTree.ParseText(text).GetRoot(), new TextSpan(0, text.Length));
        return scan.Positions;
    }

    // Inspects every preprocessor branch, independent of the current project's symbols. Each branch a
    // parse skipped is parsed again on its own with that branch active, so the parser decides where the
    // branch really ends (a directive-looking line inside its strings is string content) and no other
    // branch's lexical state (an unterminated comment or string) reaches it. Positions are offsets into
    // Text; Activated holds the branch directives already parsed that way, so each is parsed once.
    private readonly record struct LiteralCrScan(string Text, HashSet<int> Positions, HashSet<int> Activated)
    {
        public void Collect(int offset, SyntaxNode root, TextSpan scope)
        {
            foreach (var token in root.DescendantTokens())
            {
                if (!IsMultiLineStringContent(token.Kind())) continue;
                var span = token.Span.Intersection(scope);
                if (span is null) continue;
                for (var i = offset + span.Value.Start; i < offset + span.Value.End; i++)
                {
                    if (Text[i] == '\r' && (i + 1 >= Text.Length || Text[i + 1] != '\n')) Positions.Add(i);
                }
            }

            foreach (var directive in root.DescendantNodes(descendIntoTrivia: true).OfType<IfDirectiveTriviaSyntax>())
            {
                if (!directive.IsActive || !scope.Contains(directive.Span)) continue;
                var group = directive.GetRelatedDirectives();
                for (var b = 0; b < group.Count; b++)
                {
                    var end = b + 1 < group.Count ? group[b + 1].FullSpan.Start : scope.End;
                    if (group[b] is BranchingDirectiveTriviaSyntax { BranchTaken: false }) Activate(offset, group[b], offset + end);
                }
            }
        }

        // Parses the text from a skipped branch's directive onward, with that directive rewritten in place
        // to an always-true "#if" (every #if/#elif/#else directive is at least as long). The window doubles
        // until the branch's next directive is fully inside it, so the cost follows the branch size. That
        // next #elif/#else is followed too: a directive inside a string can hide it from the outer parse.
        // A branch that never closes once active is not valid C# that way; it keeps the outer extent.
        private void Activate(int offset, DirectiveTriviaSyntax directive, int outerEnd)
        {
            // A malformed directive (a bare "#if") is too short to rewrite; it is not valid C# anyway.
            var start = offset + directive.Span.Start;
            if (directive.Span.Length < ActiveDirective.Length || !Activated.Add(start)) return;
            for (var length = 256; ; length *= 2)
            {
                var end = (int)Math.Min(Text.Length, (long)start + length);
                var window = ActiveDirective + new string(' ', directive.Span.Length - ActiveDirective.Length) + Text[(start + directive.Span.Length)..end];
                var root = CSharpSyntaxTree.ParseText(window, ActiveBranchOptions).GetRoot();
                var opening = root.DescendantNodes(descendIntoTrivia: true).OfType<IfDirectiveTriviaSyntax>().First();
                var group = opening.GetRelatedDirectives();
                var complete = end == Text.Length;
                if (group.Count > 1 && (complete || group[1].FullSpan.End < window.Length))
                {
                    Collect(start, root, TextSpan.FromBounds(opening.FullSpan.End, group[1].FullSpan.Start));
                    var next = group.Count > 2 ? group[2].FullSpan.Start : window.Length;
                    if (group[1] is BranchingDirectiveTriviaSyntax) Activate(start, group[1], start + next);
                    return;
                }
                if (complete)
                {
                    Collect(start, root, TextSpan.FromBounds(opening.FullSpan.End, Math.Max(opening.FullSpan.End, outerEnd - start)));
                    return;
                }
            }
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
