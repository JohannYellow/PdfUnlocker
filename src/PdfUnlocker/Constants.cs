namespace PdfUnlocker;

/// <summary>Shared magic strings, kept in one place per the build spec.</summary>
internal static class Constants
{
    public const string ConfigFileName = "PdfUnlocker.config";
    public const string LogFileName = "PdfUnlocker.log";
    public const string UnlockSuffix = "_unlock";
}

/// <summary>Process exit codes (see spec §4.6). Values are meaningful when scripted.</summary>
internal enum ExitCode
{
    Success = 0,
    UsageError = 1,
    InputNotFound = 2,
    ConfigMissing = 3,
    NoPasswordMatched = 4,
    OutputWriteFailed = 5,
    Unexpected = 10,
}
