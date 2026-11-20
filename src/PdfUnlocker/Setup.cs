using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace PdfUnlocker;

/// <summary>What <see cref="Setup.Install"/> did.</summary>
internal sealed record SetupResult(string InstalledExePath, string ConfigPath, bool ConfigCreated);

/// <summary>
/// Setup mode, used when the exe is started with no arguments (double-clicking
/// PdfUnlockerSetup.exe). Installs into a fixed per-user folder that is always writable and
/// needs no admin rights: %APPDATA%\PdfUnlocker.
/// </summary>
internal static class Setup
{
    public const string AppDisplayName = "PDF Unlocker";
    public const string InstalledExeName = "PdfUnlocker.exe";
    public const string ShortcutFileName = AppDisplayName + ".lnk";

    public static string DefaultInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfUnlocker");

    public static string DefaultShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.SendTo), ShortcutFileName);

    /// <summary>
    /// Interactive setup: confirm, install, then open the config in Notepad. Errors are shown
    /// in a message box because a WinExe has no console.
    /// </summary>
    public static ExitCode RunInteractive()
    {
        string installDir = DefaultInstallDir;

        string prompt =
            $"This will set up {AppDisplayName} in:\n{installDir}\n\n" +
            "After setup, a config file will open in Notepad where you enter the passwords " +
            "to try when unlocking your PDFs.\n\n" +
            $"To unlock a PDF, right-click it and select Send to → {AppDisplayName}.\n" +
            "(On Windows 11, choose \"Show more options\" first.)";

        if (MessageBox(prompt, MB_OKCANCEL | MB_ICONINFORMATION) != IDOK)
            return ExitCode.Success;

        SetupResult result;
        try
        {
            string sourceExe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine the path of this program.");
            result = Install(sourceExe, installDir, DefaultShortcutPath);
        }
        catch (IOException ex)
        {
            MessageBox("Setup failed. If PDF Unlocker is currently running, wait for it to finish " +
                       $"and run setup again.\n\n{ex.Message}", MB_OK | MB_ICONERROR);
            return ExitCode.Unexpected;
        }
        catch (Exception ex)
        {
            MessageBox($"Setup failed.\n\n{ex.Message}", MB_OK | MB_ICONERROR);
            return ExitCode.Unexpected;
        }

        try
        {
            var notepad = new ProcessStartInfo("notepad.exe");
            notepad.ArgumentList.Add(result.ConfigPath);
            Process.Start(notepad);
        }
        catch (Exception ex)
        {
            MessageBox($"Setup completed, but Notepad could not be opened.\n" +
                       $"Edit this file to add your passwords:\n{result.ConfigPath}\n\n{ex.Message}",
                       MB_OK | MB_ICONWARNING);
        }

        return ExitCode.Success;
    }

    /// <summary>
    /// Copies the exe into <paramref name="installDir"/>, creates the config from the template
    /// if it does not exist yet (an existing config is never overwritten — it holds the user's
    /// passwords), and creates/refreshes the Send To shortcut. Safe to run repeatedly.
    /// </summary>
    public static SetupResult Install(string sourceExePath, string installDir, string shortcutPath)
    {
        Directory.CreateDirectory(installDir);

        string installedExe = Path.Combine(installDir, InstalledExeName);
        if (!string.Equals(Path.GetFullPath(sourceExePath), Path.GetFullPath(installedExe),
                           StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceExePath, installedExe, overwrite: true);
        }

        string configPath = Path.Combine(installDir, Constants.ConfigFileName);
        bool configCreated = false;
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, ConfigTemplate(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            configCreated = true;
        }

        string? shortcutDir = Path.GetDirectoryName(shortcutPath);
        if (shortcutDir is not null)
            Directory.CreateDirectory(shortcutDir);
        ShellLink.Create(shortcutPath, installedExe, installDir, "Remove the password from a PDF");

        return new SetupResult(installedExe, configPath, configCreated);
    }

    /// <summary>The starting config: PdfUnlocker.config.sample, embedded at build time.</summary>
    public static string ConfigTemplate()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ConfigTemplateResource)
            ?? throw new InvalidOperationException($"Embedded resource '{ConfigTemplateResource}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private const string ConfigTemplateResource = "PdfUnlocker.config.sample";

    // Minimal Win32 message box — avoids pulling in WinForms/WPF for three dialogs.
    private const uint MB_OK = 0x0, MB_OKCANCEL = 0x1;
    private const uint MB_ICONERROR = 0x10, MB_ICONWARNING = 0x30, MB_ICONINFORMATION = 0x40;
    private const int IDOK = 1;

    private static int MessageBox(string text, uint type) =>
        MessageBoxW(IntPtr.Zero, text, AppDisplayName + " Setup", type);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
