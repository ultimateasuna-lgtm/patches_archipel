using ArchipelInstaller.Models;
using ArchipelInstaller.Utils;

namespace ArchipelInstaller.Services;

public sealed class InstallTransactionService
{
    private readonly Logger logger;

    public InstallTransactionService(Logger logger)
    {
        this.logger = logger;
    }

    public async Task ApplyAtomicInstallAsync(
        string stagedArchipelDirectory,
        string targetArchipelDirectory,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var targetParent = Path.GetDirectoryName(targetArchipelDirectory);
        if (string.IsNullOrWhiteSpace(targetParent))
        {
            throw new InstallerException(InstallerExitCode.PathDetectionError, "Chemin de destination invalide.");
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var newCandidate = Path.Combine(targetParent, $"Archipel.new.{stamp}");
        var backupDirectory = Path.Combine(targetParent, $"Archipel.backup.{stamp}");

        if (dryRun)
        {
            await logger.InfoAsync($"[DRY-RUN] Préparation staging: {newCandidate}");
            await logger.InfoAsync($"[DRY-RUN] Swap atomique cible: {targetArchipelDirectory}");
            return;
        }

        try
        {
            FileUtils.EnsureDirectory(targetParent);

            if (Directory.Exists(newCandidate))
            {
                Directory.Delete(newCandidate, recursive: true);
            }

            await logger.InfoAsync("Copie du staging vers le volume de destination.");
            await Task.Run(() => FileUtils.CopyDirectory(stagedArchipelDirectory, newCandidate), cancellationToken);

            var hadExistingTarget = Directory.Exists(targetArchipelDirectory);
            if (hadExistingTarget)
            {
                await logger.InfoAsync("Renommage de l'installation existante en backup.");
                Directory.Move(targetArchipelDirectory, backupDirectory);
            }

            try
            {
                await logger.InfoAsync("Activation atomique de la nouvelle version Archipel.");
                Directory.Move(newCandidate, targetArchipelDirectory);
            }
            catch
            {
                if (Directory.Exists(backupDirectory) && !Directory.Exists(targetArchipelDirectory))
                {
                    await logger.WarnAsync("Rollback vers la version backup.");
                    Directory.Move(backupDirectory, targetArchipelDirectory);
                }

                throw;
            }

            if (Directory.Exists(backupDirectory))
            {
                await logger.InfoAsync("Suppression du backup après succès.");
                Directory.Delete(backupDirectory, recursive: true);
            }

            PurgeOldBackups(targetParent, TimeSpan.FromDays(7));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InstallerException(
                InstallerExitCode.PermissionOrLockedError,
                "Échec de la transaction atomique. Vérifiez que le jeu/launcher est fermé (fichiers possiblement verrouillés).",
                exception);
        }
        finally
        {
            if (Directory.Exists(newCandidate))
            {
                try
                {
                    Directory.Delete(newCandidate, recursive: true);
                }
                catch
                {
                    // Ignoré volontairement.
                }
            }
        }
    }

    private void PurgeOldBackups(string targetParent, TimeSpan maxAge)
    {
        var now = DateTime.Now;
        foreach (var backup in Directory.EnumerateDirectories(targetParent, "Archipel.backup.*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var info = new DirectoryInfo(backup);
                if (now - info.CreationTime > maxAge)
                {
                    Directory.Delete(backup, recursive: true);
                }
            }
            catch
            {
                // Ignoré volontairement.
            }
        }
    }
}
