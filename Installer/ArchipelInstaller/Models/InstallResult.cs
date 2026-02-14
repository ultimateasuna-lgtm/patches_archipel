namespace ArchipelInstaller.Models;

public sealed class InstallResult
{
    public required InstallerExitCode ExitCode { get; init; }
    public required string Message { get; init; }
    public bool AlreadyUpToDate { get; init; }
}
