using System.Text;

namespace PdfUnlocker.Tests;

public sealed class UnlockerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _configPath;

    public UnlockerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pdfu-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, Constants.ConfigFileName);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string P(string name) => Path.Combine(_dir, name);

    private void WriteConfig(params string[] passwords) =>
        File.WriteAllText(_configPath, string.Join('\n', passwords) + "\n", new UTF8Encoding(false));

    [Theory]
    [InlineData(@"C:\docs\Report.pdf", @"C:\docs\Report_unlock.pdf")]
    [InlineData(@"C:\docs\a.b.pdf", @"C:\docs\a.b_unlock.pdf")]
    public void BuildOutputPath_AppendsSuffix(string input, string expected)
    {
        Assert.Equal(expected, Unlocker.BuildOutputPath(input));
    }

    [Fact]
    public void MatchingPassword_WritesUnencryptedCopy()
    {
        string input = P("enc.pdf");
        PdfTestFactory.CreateEncryptedV5(input, "secret");
        WriteConfig("nope", "secret");

        var status = Unlocker.Run(input, _configPath);

        Assert.Equal(UnlockStatus.Unlocked, status);
        string output = Unlocker.BuildOutputPath(input);
        Assert.True(File.Exists(output));
        Assert.True(PdfTestFactory.OpensWithoutPassword(output), "output should be unencrypted");
    }

    [Fact]
    public void PasswordAsLastEntry_StillSucceeds()
    {
        string input = P("enc.pdf");
        PdfTestFactory.CreateEncryptedV5(input, "last");
        WriteConfig("a", "b", "c", "last");

        var status = Unlocker.Run(input, _configPath);

        Assert.Equal(UnlockStatus.Unlocked, status);
        Assert.True(File.Exists(Unlocker.BuildOutputPath(input)));
    }

    [Fact]
    public void NoMatchingPassword_ThrowsNoPasswordMatched_AndWritesNoOutput()
    {
        string input = P("enc.pdf");
        PdfTestFactory.CreateEncryptedV5(input, "secret");
        WriteConfig("wrong1", "wrong2");

        var ex = Assert.Throws<UnlockException>(() => Unlocker.Run(input, _configPath));
        Assert.Equal(ExitCode.NoPasswordMatched, ex.Code);
        Assert.False(File.Exists(Unlocker.BuildOutputPath(input)));
    }

    [Fact]
    public void UnencryptedInput_CopiedWithoutConfig()
    {
        string input = P("plain.pdf");
        PdfTestFactory.CreatePlain(input);
        // No config file exists at all.

        var status = Unlocker.Run(input, _configPath);

        Assert.Equal(UnlockStatus.WasUnencrypted, status);
        Assert.True(File.Exists(Unlocker.BuildOutputPath(input)));
    }

    [Fact]
    public void ExistingOutput_IsOverwritten()
    {
        string input = P("enc.pdf");
        PdfTestFactory.CreateEncryptedV5(input, "secret");
        WriteConfig("secret");
        string output = Unlocker.BuildOutputPath(input);
        File.WriteAllText(output, "stale");

        Unlocker.Run(input, _configPath);

        Assert.True(PdfTestFactory.OpensWithoutPassword(output));
    }

    [Fact]
    public void EncryptedInput_WithMissingConfig_ThrowsConfigMissing()
    {
        string input = P("enc.pdf");
        PdfTestFactory.CreateEncryptedV5(input, "secret");
        // _configPath intentionally not written.

        var ex = Assert.Throws<UnlockException>(() => Unlocker.Run(input, _configPath));
        Assert.Equal(ExitCode.ConfigMissing, ex.Code);
    }

    [Fact]
    public void EncryptedInput_WithEmptyConfig_ThrowsConfigMissing()
    {
        string input = P("enc.pdf");
        PdfTestFactory.CreateEncryptedV5(input, "secret");
        File.WriteAllText(_configPath, "# only a comment\n\n");

        var ex = Assert.Throws<UnlockException>(() => Unlocker.Run(input, _configPath));
        Assert.Equal(ExitCode.ConfigMissing, ex.Code);
    }

    [Fact]
    public void CorruptFile_IsNotTreatedAsWrongPassword()
    {
        string input = P("junk.pdf");
        File.WriteAllText(input, "this is not a pdf");
        WriteConfig("secret");

        // Should NOT be an UnlockException(NoPasswordMatched); a corrupt file is a real error
        // that Program maps to the "unexpected" exit code (10).
        var ex = Record.Exception(() => Unlocker.Run(input, _configPath));
        Assert.NotNull(ex);
        Assert.False(ex is UnlockException, "corrupt file must not surface as a handled unlock error");
    }

    [Fact]
    public void Aes256_RevisionFive_OpensCorrectly()
    {
        string input = P("aes256.pdf");
        PdfTestFactory.CreateEncryptedV5(input, "pw256");
        WriteConfig("pw256");

        var status = Unlocker.Run(input, _configPath);
        Assert.Equal(UnlockStatus.Unlocked, status);
        Assert.True(PdfTestFactory.OpensWithoutPassword(Unlocker.BuildOutputPath(input)));
    }

    [Fact]
    public void Aes128_RevisionFour_OpensCorrectly()
    {
        string input = P("aes128.pdf");
        PdfTestFactory.CreateEncryptedV4Aes(input, "pw128");
        WriteConfig("pw128");

        var status = Unlocker.Run(input, _configPath);
        Assert.Equal(UnlockStatus.Unlocked, status);
        Assert.True(PdfTestFactory.OpensWithoutPassword(Unlocker.BuildOutputPath(input)));
    }

    [Fact]
    public void PasswordWithSignificantTrailingSpace_Matches()
    {
        string input = P("enc.pdf");
        PdfTestFactory.CreateEncryptedV5(input, "space ");   // trailing space is part of the password
        WriteConfig("space ");                                // must be preserved by the loader

        var status = Unlocker.Run(input, _configPath);
        Assert.Equal(UnlockStatus.Unlocked, status);
    }
}
