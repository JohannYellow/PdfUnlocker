using System.Security;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfUnlocker;

/// <summary>Outcome of a successful <see cref="Unlocker.Run"/> call.</summary>
internal enum UnlockStatus
{
    /// <summary>The input was not encrypted; a copy was written.</summary>
    WasUnencrypted,

    /// <summary>A configured password opened the input; an unencrypted copy was written.</summary>
    Unlocked,
}

/// <summary>
/// Opens a PDF, tries the configured passwords, and writes an unencrypted copy (spec §4.4/§6).
/// </summary>
internal static class Unlocker
{
    /// <summary>
    /// Builds the output path by appending <c>_unlock</c> to the base name while keeping the
    /// original directory and extension: <c>C:\docs\Report.pdf → C:\docs\Report_unlock.pdf</c>.
    /// </summary>
    public static string BuildOutputPath(string inputPath)
    {
        string dir = Path.GetDirectoryName(inputPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(inputPath);
        string ext = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{stem}{Constants.UnlockSuffix}{ext}");
    }

    /// <summary>
    /// Runs the unlock algorithm and writes the output file.
    /// </summary>
    /// <param name="inputPath">Path to the source PDF (already verified to exist).</param>
    /// <param name="configPath">Path to the password config; only read if the input is encrypted.</param>
    /// <returns>How the file was handled (see <see cref="UnlockStatus"/>).</returns>
    /// <exception cref="UnlockException">A handled error mapping to an exit code (spec §5).</exception>
    public static UnlockStatus Run(string inputPath, string configPath)
    {
        string outputPath = BuildOutputPath(inputPath);

        // Step 1: unencrypted check. Opening with no password succeeds for a file that is not
        // encrypted (and one encrypted with an empty user password); an encrypted file throws
        // PdfReaderException here. A genuinely corrupt/non-PDF file throws a different type
        // (e.g. InvalidOperationException), which propagates as an unexpected error.
        if (TryOpen(inputPath, password: null, out PdfDocument? plain))
        {
            using (plain)
                SaveUnlocked(plain!, outputPath);
            return UnlockStatus.WasUnencrypted;
        }

        // Step 2: the input is encrypted, so we now need the password list.
        IReadOnlyList<string> passwords = ConfigLoader.Load(configPath);
        if (passwords.Count == 0)
            throw new UnlockException(ExitCode.ConfigMissing,
                $"Input is encrypted but no usable passwords were found in '{configPath}'.");

        foreach (string password in passwords)
        {
            if (TryOpen(inputPath, password, out PdfDocument? source))
            {
                using (source)
                    SaveUnlocked(source!, outputPath);
                return UnlockStatus.Unlocked;
            }
        }

        // Step 3: nothing matched.
        throw new UnlockException(ExitCode.NoPasswordMatched,
            "No configured password unlocked the file.");
    }

    /// <summary>
    /// Attempts to open <paramref name="inputPath"/> in Import mode. Returns true and sets
    /// <paramref name="document"/> on success. A wrong/absent password (PdfReaderException) returns
    /// false so the caller tries the next candidate; every other exception propagates so a
    /// corrupt/non-PDF file or I/O failure is not mistaken for a bad password (spec §5/§6.1).
    /// </summary>
    private static bool TryOpen(string inputPath, string? password, out PdfDocument? document)
    {
        document = null;
        try
        {
            document = password is null
                ? PdfReader.Open(inputPath, PdfDocumentOpenMode.Import)
                : PdfReader.Open(inputPath, password, PdfDocumentOpenMode.Import);
            return true;
        }
        catch (PdfReaderException)
        {
            // Wrong or missing password — expected; caller continues.
            return false;
        }
    }

    /// <summary>
    /// Copies the source pages into a brand-new document and saves it. The new document has no
    /// security settings, so the saved file is unencrypted. Overwrites the output if it exists.
    /// </summary>
    private static void SaveUnlocked(PdfDocument source, string outputPath)
    {
        using var output = new PdfDocument();
        foreach (PdfPage page in source.Pages)
            output.AddPage(page);

        // Intentionally leave SecuritySettings untouched: no security => unencrypted output.
        try
        {
            output.Save(outputPath);
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or DirectoryNotFoundException
                                      or PathTooLongException
                                      or SecurityException)
        {
            throw new UnlockException(ExitCode.OutputWriteFailed,
                $"Failed to write the output file '{outputPath}': {ex.Message}", ex);
        }
    }
}
