namespace ArchipelInstaller.Models;

public enum InstallerExitCode
{
    Success = 0,
    NetworkError = 1,
    PermissionOrLockedError = 2,
    PathDetectionError = 3,
    IntegrityError = 4,
}
