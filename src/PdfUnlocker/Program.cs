using PdfUnlocker;

// Entry point + orchestration only (spec §3): parse the argument, set up the log next to the
// input (falling back to the exe folder), run the unlock, and map the result to an exit code.

// No arguments means the exe was double-clicked (e.g. PdfUnlockerSetup.exe): run setup.
// Send To always passes the PDF path, so the installed copy never lands here by accident.
if (args.Length == 0)
    return (int)Setup.RunInteractive();

string exeDir = AppContext.BaseDirectory;

string? inputPath = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : null;

// The log goes next to the input PDF that "caused" the run; if there is no usable/existing
// input path, it falls back to the executable's folder (spec §4.5).
string logFolder = ResolveLogFolder(inputPath, exeDir);

using var logger = new RunLogger(logFolder);

ExitCode exit;
try
{
    if (inputPath is null)
        throw new UnlockException(ExitCode.UsageError,
            "Usage: PdfUnlocker <path-to-pdf>. Exactly one PDF path argument is required.");

    if (!File.Exists(inputPath))
        throw new UnlockException(ExitCode.InputNotFound,
            $"Input file not found or not a file: '{inputPath}'.");

    UnlockStatus status = Unlocker.Run(inputPath, ConfigLoader.ConfigPath);
    _ = status; // success either way; the distinction is informational only
    exit = ExitCode.Success;
}
catch (UnlockException ex)
{
    logger.LogError(ex.Message, ex.InnerException, inputPath);
    exit = ex.Code;
}
catch (Exception ex)
{
    logger.LogError("Unexpected error.", ex, inputPath);
    exit = ExitCode.Unexpected;
}

return (int)exit;

// Uses the input's directory only when the input file actually exists; otherwise the exe folder
// (a missing/blank argument or a nonexistent input has no reliable folder to write into).
static string ResolveLogFolder(string? inputPath, string exeDir)
{
    if (inputPath is null)
        return exeDir;

    try
    {
        string full = Path.GetFullPath(inputPath);
        string? dir = Path.GetDirectoryName(full);
        if (File.Exists(full) && dir is not null && Directory.Exists(dir))
            return dir;
    }
    catch
    {
        // Fall through to the exe folder on any path-resolution failure.
    }

    return exeDir;
}
