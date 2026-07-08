namespace PdfUnlocker;

/// <summary>
/// A handled error that maps directly to a process exit code (spec §4.6/§5).
/// Anything that is <em>not</em> one of these is treated as an unexpected error (code 10).
/// </summary>
internal sealed class UnlockException : Exception
{
    public ExitCode Code { get; }

    public UnlockException(ExitCode code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }
}
