using ArchipelInstaller.Models;

namespace ArchipelInstaller.Services;

public sealed class InstallerException : Exception
{
    public InstallerException(InstallerExitCode exitCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
    }

    public InstallerExitCode ExitCode { get; }
}
