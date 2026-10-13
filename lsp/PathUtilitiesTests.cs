using UnityCli.Lsp.Core;
using Xunit;

public sealed class PathUtilitiesTests
{
    [Fact]
    public void NormalizeRelative_ConvertsBackslashes()
    {
        Assert.Equal("Assets/Scripts/Player.cs", LspPathUtilities.NormalizeRelative(@"Assets\Scripts\Player.cs"));
    }

    [Fact]
    public void NormalizeNamePath_NormalizesDotsAndSlashes()
    {
        Assert.Equal("Player/Config/Value", LspPathUtilities.NormalizeNamePath("Player.Config/Value"));
    }

    [Fact]
    public void ContainerHelpers_ReturnExpectedValues()
    {
        Assert.Equal("Player/Config", LspPathUtilities.ContainerPath("Player/Config/Value"));
        Assert.Equal("Config", LspPathUtilities.ContainerName("Player/Config/Value"));
        Assert.Null(LspPathUtilities.ContainerPath("Player"));
    }

    [Fact]
    public void ContainerName_ReturnsNullForEmptyContainer()
    {
        Assert.Null(LspPathUtilities.ContainerName(""));
    }

    [Fact]
    public void ToRel_ReturnsPathRelativeToRoot()
    {
        Assert.Equal(
            "Assets/Scripts/Player.cs",
            LspPathUtilities.ToRel("/repo/Assets/Scripts/Player.cs", "/repo"));
    }

    [Fact]
    public void ToRel_ReturnsOriginalWhenOutsideRoot()
    {
        Assert.Equal(
            "/other/Assets/Scripts/Player.cs",
            LspPathUtilities.ToRel("/other/Assets/Scripts/Player.cs", "/repo"));
    }

    [Fact]
    public void Path2Uri_UsesFilePrefix()
    {
        Assert.Equal("file:///repo/Assets/Scripts/Player.cs", LspPathUtilities.Path2Uri("/repo/Assets/Scripts/Player.cs"));
    }

    [Theory]
    [InlineData(@"C:\repo\Assets\Player.cs", "file:///C:/repo/Assets/Player.cs")]
    [InlineData("C:/repo/Assets/Player.cs", "file:///C:/repo/Assets/Player.cs")]
    [InlineData(@"\\server\share\Assets\Player.cs", "file://server/share/Assets/Player.cs")]
    [InlineData(@"C:\my project\Assets\日本語 #%.cs", "file:///C:/my%20project/Assets/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23%25.cs")]
    [InlineData("/my project/Assets/日本語 #%.cs", "file:///my%20project/Assets/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23%25.cs")]
    [InlineData(@"\\server\my share\Assets\A #%.cs", "file://server/my%20share/Assets/A%20%23%25.cs")]
    public void Path2Uri_ProducesCanonicalEscapedFileUris(string path, string expected)
    {
        Assert.Equal(expected, LspPathUtilities.Path2Uri(path));
    }

    [Theory]
    [InlineData(@"C:\repo\Assets\Player.cs", "file:///C:/repo/Assets/Player.cs")]
    [InlineData(@"d:\my project\Assets\日本語 #%.cs", "file:///d:/my%20project/Assets/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23%25.cs")]
    [InlineData("/repo/Assets/Player.cs", "file:///repo/Assets/Player.cs")]
    [InlineData("/my project/Assets/日本語 #%.cs", "file:///my%20project/Assets/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23%25.cs")]
    [InlineData(@"\\server\share\Assets\Player.cs", "file://server/share/Assets/Player.cs")]
    [InlineData(@"\\server\my share\Assets\A #%.cs", "file://server/my%20share/Assets/A%20%23%25.cs")]
    [InlineData("/repo/Assets/A %20.cs", "file:///repo/Assets/A%20%2520.cs")]
    [InlineData(@"C:\repo\Assets\A %20.cs", "file:///C:/repo/Assets/A%20%2520.cs")]
    [InlineData(@"C:\repo\Assets\日本語 # %20.cs", "file:///C:/repo/Assets/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23%20%2520.cs")]
    [InlineData("/repo/Assets/日本語 # %20.cs", "file:///repo/Assets/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23%20%2520.cs")]
    [InlineData(@"\\server\share\Assets\日本語 # %20.cs", "file://server/share/Assets/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23%20%2520.cs")]
    [InlineData("/repo/Assets/A?.cs", "file:///repo/Assets/A%3F.cs")]
    public void FileUris_RoundTripPathsAndUris(string path, string uri)
    {
        Assert.Equal(path, LspPathUtilities.Uri2Path(uri));
        Assert.Equal(path, LspPathUtilities.Uri2Path(LspPathUtilities.Path2Uri(path)));
        Assert.Equal(uri, LspPathUtilities.Path2Uri(LspPathUtilities.Uri2Path(uri)));
    }

    [Fact]
    public void FileUriConversions_RejectNonFileUris()
    {
        Assert.Throws<System.ArgumentException>(() => LspPathUtilities.Uri2Path("https://example.com/Asset.cs"));
        Assert.Throws<System.ArgumentException>(() => LspPathUtilities.Path2Uri("https://example.com/Asset.cs"));
    }
}
