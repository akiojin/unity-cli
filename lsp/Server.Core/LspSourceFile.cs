using System;
using System.Collections.Generic;
using System.IO;
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

    private static bool HasLoneCr(string text)
    {
        for (var i = text.IndexOf('\r'); i >= 0; i = text.IndexOf('\r', i + 1))
            if (i + 1 >= text.Length || text[i + 1] != '\n') return true;
        return false;
    }

    private static HashSet<int> FindLoneCrsInStringLiterals(string text)
    {
        var positions = new HashSet<int>();
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        var branches = ConditionalBranches(root).ToArray();
        var scan = new LiteralCrScan(text, positions, new(), new(), new(), branches);
        scan.Collect(0, root, new TextSpan(0, text.Length));
        foreach (var run in scan.Runs)
            if (!run.Dropped) positions.UnionWith(run.Positions);
        foreach (var branch in branches)
        {
            if (branch.AlreadyActive || scan.CleanActivations.Contains(branch.DirectiveStart)) continue;
            // Select one branch and its ancestors, leaving sibling branches disabled. Their
            // arbitrary text (including unfinished comments) must not affect this branch's lexer.
            var (view, offsets) = SelectBranch(text, branch.Choices);
            var viewRoot = CSharpSyntaxTree.ParseText(view).GetRoot();
            // Disabled text can contain directive-looking lines inside a string. Only accept
            // a selection when its entire path is still real directives in the selected view.
            var scope = SelectedScope(viewRoot, offsets, branch);
            if (scope is not null)
                CollectStringCrs(viewRoot, view, offsets, positions, scope, branches, HasSyntaxErrors(viewRoot, scope.Value, offsets));
        }
        return positions;
    }

    private static void CollectStringCrs(SyntaxNode root, string text, int[]? offsets, HashSet<int> positions,
        TextSpan? bounds = null, IReadOnlyList<ConditionalBranch>? branches = null, bool conservative = false, int sourceOffset = 0,
        List<StringRun>? runs = null)
    {
        foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
        {
            if (token.Span.IsEmpty || !IsMultiLineStringContent(token.Kind())) continue;
            var tokenPositions = runs is null ? null : new List<int>();
            int? limit = null;
            if (conservative && branches is not null)
            {
                var anchor = token.Parent?.AncestorsAndSelf().OfType<InterpolatedStringExpressionSyntax>().FirstOrDefault()?.SpanStart ?? token.SpanStart;
                var originalStart = offsets?[anchor] ?? sourceOffset + anchor;
                limit = branches.Where(b => b.BodyStart <= originalStart && originalStart < b.BodyEnd)
                    .OrderBy(b => b.BodyEnd - b.BodyStart).FirstOrDefault()?.BodyEnd;
            }
            for (var i = token.SpanStart; i < token.Span.End; i++)
            {
                if (text[i] != '\r' || (i + 1 < text.Length && text[i + 1] == '\n')) continue;
                var original = offsets?[i] ?? sourceOffset + i;
                if (limit is not null && original >= limit.Value) continue;
                if (bounds is not null && !bounds.Value.Contains(original)) continue;
                if (tokenPositions is not null) tokenPositions.Add(original);
                else positions.Add(original);
            }
            if (tokenPositions is { Count: > 0 }) runs!.Add(new(sourceOffset + token.SpanStart, tokenPositions));
        }
    }

    private readonly record struct ConditionalChoice(int DirectiveStart, TextSpan Condition, bool Enabled);
    private sealed record ConditionalBranch(int DirectiveStart, List<ConditionalChoice> Choices, int BodyStart)
    {
        public int BodyEnd { get; set; }
        public bool AlreadyActive { get; init; }
    }

    private sealed class ConditionalGroup(List<ConditionalChoice> parents)
    {
        public List<ConditionalChoice> Parents { get; } = parents;
        public List<ConditionalChoice> Conditions { get; } = new();
        public List<ConditionalChoice> Current { get; set; } = parents;
        public ConditionalBranch? Branch { get; private set; }

        public List<ConditionalChoice> Select(bool lastCondition)
        {
            var choices = new List<ConditionalChoice>(Parents);
            for (var i = 0; i < Conditions.Count; i++)
            {
                choices.Add(Conditions[i] with { Enabled = lastCondition && i == Conditions.Count - 1 });
            }
            return Current = choices;
        }

        public ConditionalBranch StartBranch(DirectiveTriviaSyntax directive, bool lastCondition, int fileEnd)
        {
            if (Branch is not null) Branch.BodyEnd = directive.FullSpan.Start;
            return Branch = new(directive.SpanStart, Select(lastCondition), directive.FullSpan.End)
            {
                BodyEnd = fileEnd,
                AlreadyActive = directive.IsActive && directive is BranchingDirectiveTriviaSyntax { BranchTaken: true },
            };
        }
    }

    private static IEnumerable<ConditionalBranch> ConditionalBranches(SyntaxNode root)
    {
        var groups = new Stack<ConditionalGroup>();
        var branches = new List<ConditionalBranch>();
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            switch (trivia.GetStructure())
            {
                case IfDirectiveTriviaSyntax directive:
                    var group = new ConditionalGroup(groups.TryPeek(out var parent) ? parent.Current : new());
                    groups.Push(group);
                    group.Conditions.Add(new(directive.SpanStart, directive.Condition.Span, true));
                    branches.Add(group.StartBranch(directive, true, root.FullSpan.End));
                    break;
                case ElifDirectiveTriviaSyntax directive when groups.TryPeek(out var elifGroup):
                    elifGroup.Conditions.Add(new(directive.SpanStart, directive.Condition.Span, true));
                    branches.Add(elifGroup.StartBranch(directive, true, root.FullSpan.End));
                    break;
                case ElseDirectiveTriviaSyntax directive when groups.TryPeek(out var elseGroup):
                    branches.Add(elseGroup.StartBranch(directive, false, root.FullSpan.End));
                    break;
                case EndIfDirectiveTriviaSyntax directive when groups.Count > 0:
                    var ended = groups.Pop();
                    if (ended.Branch is not null) ended.Branch.BodyEnd = directive.FullSpan.Start;
                    break;
            }
        }
        return branches;
    }

    private static (string Text, int[] Offsets) SelectBranch(string text, List<ConditionalChoice> choices)
    {
        var view = new StringBuilder(text.Length);
        var offsets = new List<int>(text.Length);
        var original = 0;
        foreach (var choice in choices.OrderBy(choice => choice.Condition.Start))
        {
            while (original < choice.Condition.Start) { offsets.Add(original); view.Append(text[original++]); }
            var replacement = choice.Enabled ? "true" : "false";
            for (var i = 0; i < replacement.Length; i++)
            {
                offsets.Add(i == replacement.Length - 1 ? Math.Max(choice.Condition.Start, choice.Condition.End - 1) : choice.Condition.Start);
                view.Append(replacement[i]);
            }
            original = choice.Condition.End;
        }
        while (original < text.Length) { offsets.Add(original); view.Append(text[original++]); }
        return (view.ToString(), offsets.ToArray());
    }

    private static TextSpan? SelectedScope(SyntaxNode root, int[] offsets, ConditionalBranch branch)
    {
        var choices = branch.Choices.ToDictionary(choice => choice.DirectiveStart);
        var found = new HashSet<int>();
        DirectiveTriviaSyntax? target = null;
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.GetStructure() is not DirectiveTriviaSyntax directive) continue;
            var original = offsets[directive.SpanStart];
            var taken = directive switch
            {
                IfDirectiveTriviaSyntax condition => condition.BranchTaken,
                ElifDirectiveTriviaSyntax condition => condition.BranchTaken,
                ElseDirectiveTriviaSyntax condition => condition.BranchTaken,
                _ => false,
            };
            if (original == branch.DirectiveStart && directive.IsActive && taken) target = directive;
            if (!choices.TryGetValue(original, out var choice)) continue;
            if (!directive.IsActive || taken != choice.Enabled) return null;
            found.Add(original);
        }
        if (target is null || found.Count != choices.Count) return null;
        var end = branch.BodyEnd;
        var related = target.GetRelatedDirectives();
        var index = 0;
        while (index < related.Count && related[index].SpanStart != target.SpanStart) index++;
        if (index + 1 < related.Count)
        {
            var actualEnd = offsets[related[index + 1].FullSpan.Start];
            // A valid selected view knows which directive-looking lines were really literal text.
            // An invalid view may have swallowed a sibling with an unfinished string/comment;
            // retain the original extent so that branch cannot protect unrelated code newlines.
            if (actualEnd <= end || !HasSyntaxErrors(root, TextSpan.FromBounds(branch.BodyStart, actualEnd), offsets))
                end = actualEnd;
        }
        return TextSpan.FromBounds(branch.BodyStart, Math.Max(branch.BodyStart, end));
    }

    private static bool HasSyntaxErrors(SyntaxNode root, TextSpan scope, int[]? offsets = null, int sourceOffset = 0)
    {
        foreach (var diagnostic in root.GetDiagnostics())
        {
            // #error does not alter lexing; CS0116 comes from parsing a class-field fragment at file scope.
            if (diagnostic.Severity != DiagnosticSeverity.Error || diagnostic.Id is "CS1029" or "CS0116") continue;
            var span = diagnostic.Location.SourceSpan;
            var start = offsets is null ? sourceOffset + span.Start : span.Start < offsets.Length ? offsets[span.Start] : offsets[^1] + 1;
            var end = offsets is null ? sourceOffset + span.End : span.Length > 0 ? offsets[span.End - 1] + 1 : start;
            if (start < scope.End && end >= scope.Start) return true;
        }
        return false;
    }

    private const string ActiveDirective = "#if _";
    private static readonly CSharpParseOptions ActiveBranchOptions = CSharpParseOptions.Default.WithPreprocessorSymbols("_");

    private sealed class StringRun(int tokenStart, List<int> positions)
    {
        public int TokenStart { get; } = tokenStart;
        public List<int> Positions { get; } = positions;
        public bool Dropped { get; set; }
    }

    // Reparse skipped branches in isolation to discover real extents through directive-looking literal
    // lines. The context-preserving selections above recover descendants hidden by a malformed sibling.
    private readonly record struct LiteralCrScan(string Text, HashSet<int> Positions, List<StringRun> Runs, HashSet<int> Activated,
        HashSet<int> CleanActivations, IReadOnlyList<ConditionalBranch> Branches)
    {
        public void Collect(int offset, SyntaxNode root, TextSpan scope)
        {
            var originalScope = new TextSpan(offset + scope.Start, scope.Length);
            var runs = new List<StringRun>();
            CollectStringCrs(root, root.ToFullString(), null, Positions, originalScope, Branches,
                HasSyntaxErrors(root, originalScope, sourceOffset: offset), offset, runs);
            Runs.AddRange(runs);
            foreach (var directive in root.DescendantNodes(descendIntoTrivia: true).OfType<IfDirectiveTriviaSyntax>())
            {
                if (!directive.IsActive || !scope.Contains(directive.Span)) continue;
                var group = directive.GetRelatedDirectives();
                for (var b = 0; b < group.Count; b++)
                {
                    var end = b + 1 < group.Count ? group[b + 1].FullSpan.Start : scope.End;
                    if (group[b] is BranchingDirectiveTriviaSyntax { BranchTaken: false }) Activate(offset, group[b], offset + end, runs);
                }
            }
        }

        private void Activate(int offset, DirectiveTriviaSyntax directive, int outerEnd, List<StringRun> outerRuns)
        {
            var start = offset + directive.Span.Start;
            if (directive.Span.Length < ActiveDirective.Length || !Activated.Add(start)) return;
            for (var length = directive.Span.Length + 256L; ; length *= 2)
            {
                var end = (int)Math.Min(Text.Length, start + length);
                var window = ActiveDirective + new string(' ', directive.Span.Length - ActiveDirective.Length) + Text[(start + directive.Span.Length)..end];
                var root = CSharpSyntaxTree.ParseText(window, ActiveBranchOptions).GetRoot();
                var opening = root.DescendantNodes(descendIntoTrivia: true).OfType<IfDirectiveTriviaSyntax>().First();
                var group = opening.GetRelatedDirectives();
                var complete = end == Text.Length;
                if (group.Count > 1 && (complete || group[1].FullSpan.End < window.Length))
                {
                    var branchEnd = start + group[1].FullSpan.Start;
                    foreach (var run in outerRuns)
                        if (run.TokenStart >= outerEnd && run.TokenStart < branchEnd) run.Dropped = true;
                    var scope = TextSpan.FromBounds(opening.FullSpan.End, group[1].FullSpan.Start);
                    if (!HasSyntaxErrors(root, new TextSpan(start + scope.Start, scope.Length), sourceOffset: start)) CleanActivations.Add(start);
                    Collect(start, root, scope);
                    var next = group.Count > 2 ? group[2].FullSpan.Start : window.Length;
                    if (group[1] is BranchingDirectiveTriviaSyntax) Activate(start, group[1], start + next, new());
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
