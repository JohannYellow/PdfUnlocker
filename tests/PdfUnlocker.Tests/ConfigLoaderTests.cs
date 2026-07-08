using System.Text;

namespace PdfUnlocker.Tests;

public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _dir;

    public ConfigLoaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pdfu-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteConfig(string content)
    {
        string path = Path.Combine(_dir, Constants.ConfigFileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void MissingFile_ReturnsEmpty()
    {
        var result = ConfigLoader.Load(Path.Combine(_dir, "nope.config"));
        Assert.Empty(result);
    }

    [Fact]
    public void SkipsBlankLinesAndComments_KeepsOrder()
    {
        string path = WriteConfig("# comment\n\nfirst\n   \n  # indented comment\nsecond\n");
        var result = ConfigLoader.Load(path);
        Assert.Equal(new[] { "first", "second" }, result);
    }

    [Fact]
    public void DoesNotTrimPasswords()
    {
        // Leading and trailing spaces must be preserved verbatim.
        string path = WriteConfig("  leading\ntrailing  \n a b \n");
        var result = ConfigLoader.Load(path);
        Assert.Equal(new[] { "  leading", "trailing  ", " a b " }, result);
    }

    [Fact]
    public void AllWhitespaceLine_IsSkipped()
    {
        string path = WriteConfig("real\n     \n");
        var result = ConfigLoader.Load(path);
        Assert.Equal(new[] { "real" }, result);
    }
}
