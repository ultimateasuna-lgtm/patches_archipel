namespace ArchipelInstaller.Models;

public sealed class InstallResult
{
    public InstallerExitCode ExitCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool AlreadyUpToDate { get; init; }
}
