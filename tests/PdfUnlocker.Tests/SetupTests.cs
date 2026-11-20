namespace PdfUnlocker.Tests;

/// <summary>
/// Exercises <see cref="Setup.Install"/> against a temp folder only — never the real
/// %APPDATA% or Send To folder.
/// </summary>
public sealed class SetupTests : IDisposable
{
    private readonly string _dir;
    private readonly string _sourceExe;
    private readonly string _installDir;
    private readonly string _shortcutPath;

    public SetupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pdfu-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _sourceExe = Path.Combine(_dir, "download", "PdfUnlockerSetup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_sourceExe)!);
        File.WriteAllText(_sourceExe, "fake exe v1");
        _installDir = Path.Combine(_dir, "Roaming", "PdfUnlocker");
        _shortcutPath = Path.Combine(_dir, "SendTo", Setup.ShortcutFileName);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Install_CopiesExeAsPdfUnlocker_CreatesConfigAndShortcut()
    {
        SetupResult result = Setup.Install(_sourceExe, _installDir, _shortcutPath);

        Assert.Equal(Path.Combine(_installDir, "PdfUnlocker.exe"), result.InstalledExePath);
        Assert.Equal("fake exe v1", File.ReadAllText(result.InstalledExePath));

        Assert.True(result.ConfigCreated);
        Assert.Equal(Path.Combine(_installDir, Constants.ConfigFileName), result.ConfigPath);
        Assert.Equal(Setup.ConfigTemplate(), File.ReadAllText(result.ConfigPath));

        Assert.True(File.Exists(_shortcutPath));
        Assert.Equal(result.InstalledExePath, ShellLink.GetTarget(_shortcutPath), ignoreCase: true);
    }

    [Fact]
    public void ConfigTemplate_ContainsNoActivePasswords()
    {
        // A fresh config must not make the tool try made-up example passwords.
        Setup.Install(_sourceExe, _installDir, _shortcutPath);
        Assert.Empty(ConfigLoader.Load(Path.Combine(_installDir, Constants.ConfigFileName)));
    }

    [Fact]
    public void Reinstall_KeepsExistingConfig_AndUpdatesExe()
    {
        Setup.Install(_sourceExe, _installDir, _shortcutPath);
        string configPath = Path.Combine(_installDir, Constants.ConfigFileName);
        File.WriteAllText(configPath, "my real password\n");
        File.WriteAllText(_sourceExe, "fake exe v2");

        SetupResult result = Setup.Install(_sourceExe, _installDir, _shortcutPath);

        Assert.False(result.ConfigCreated);
        Assert.Equal("my real password\n", File.ReadAllText(configPath));
        Assert.Equal("fake exe v2", File.ReadAllText(result.InstalledExePath));
    }

    [Fact]
    public void Install_FromInstalledLocation_DoesNotCopyOntoItself()
    {
        SetupResult first = Setup.Install(_sourceExe, _installDir, _shortcutPath);

        // Double-clicking the installed PdfUnlocker.exe re-runs setup from the install folder.
        SetupResult again = Setup.Install(first.InstalledExePath, _installDir, _shortcutPath);

        Assert.Equal("fake exe v1", File.ReadAllText(again.InstalledExePath));
        Assert.True(File.Exists(_shortcutPath));
    }
}
