using System.Text;

namespace PdfUnlocker;

/// <summary>
/// Loads the candidate-password list from <c>PdfUnlocker.config</c> (spec §4.2/§4.3).
/// </summary>
internal static class ConfigLoader
{
    /// <summary>
    /// Full path to the config file. It lives beside the executable, NOT in the current
    /// working directory: when launched via Explorer's "Open With" / "Send To", the working
    /// directory is the PDF's folder or a system folder. <see cref="AppContext.BaseDirectory"/>
    /// resolves the exe folder correctly even for single-file publishes, whereas
    /// <c>Assembly.Location</c> is empty in that case.
    /// </summary>
    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, Constants.ConfigFileName);

    /// <summary>
    /// Reads candidate passwords in file order. Blank/whitespace-only lines and comment
    /// lines (first non-whitespace char is <c>#</c>) are skipped. Only the line terminator is
    /// removed — passwords are deliberately NOT trimmed, because leading/trailing spaces can be
    /// a significant part of a real password.
    /// </summary>
    /// <returns>The usable passwords, in order. Empty if the file is missing or has none.</returns>
    public static IReadOnlyList<string> Load(string configPath)
    {
        if (!File.Exists(configPath))
            return Array.Empty<string>();

        // ReadAllLines strips the trailing \r/\n but preserves any leading/trailing spaces.
        string[] lines = File.ReadAllLines(configPath, Encoding.UTF8);

        var passwords = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.TrimStart().StartsWith('#'))
                continue;
            passwords.Add(line);
        }

        return passwords;
    }
}
