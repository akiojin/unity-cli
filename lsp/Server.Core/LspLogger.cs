namespace UnityCli.Lsp.Core;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static class LspLogger
{
    private const string Prefix = "[unity-cli:lsp]";

    public static void Info(string message) =>
        Console.Error.WriteLine($"{Prefix} {message}");

    public static void Error(string message) =>
        Console.Error.WriteLine($"{Prefix} ERROR: {message}");

    public static void Debug(string method) =>
        Console.Error.WriteLine($"{Prefix} {method}");
}
