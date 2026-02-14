using ArchipelInstaller.Models;
using ArchipelInstaller.Utils;

namespace ArchipelInstaller.Services;

public sealed class InstallerService
{
    private readonly Logger logger;
    private readonly PathDetectionService pathDetectionService;
    private readonly GitHubDownloadService gitHubDownloadService;
    private readonly ManifestService manifestService;
    private readonly InstallTransactionService installTransactionService;

    public InstallerService(
        Logger logger,
        PathDetectionService pathDetectionService,
        GitHubDownloadService gitHubDownloadService,
        ManifestService manifestService,
        InstallTransactionService installTransactionService)
    {
        this.logger = logger;
        this.pathDetectionService = pathDetectionService;
        this.gitHubDownloadService = gitHubDownloadService;
        this.manifestService = manifestService;
        this.installTransactionService = installTransactionService;
    }

    public event Action<int, string>? ProgressChanged;

    public async Task<InstallResult> RunAsync(
        InstallerOptions options,
        bool allowManualSelection,
        Func<Task<string?>>? manualPicker,
        CancellationToken cancellationToken)
    {
        string? tempRoot = null;
        try
        {
            ProgressChanged?.Invoke(0, "Détection du chemin cible...");
            var epsilonRoot = await pathDetectionService.DetectEpsilonRootAsync(options, allowManualSelection, manualPicker, cancellationToken);
            var targetArchipel = Path.Combine(epsilonRoot, "Patches", "Archipel");
            await logger.InfoAsync($"Dossier cible: {targetArchipel}");

            ProgressChanged?.Invoke(8, "Scan GitHub avant téléchargement...");
            var remoteManifest = await gitHubDownloadService.FetchRemoteManifestAsync(cancellationToken, OnProgress);

            ProgressChanged?.Invoke(35, "Calcul du manifest local...");
            await logger.InfoAsync("Calcul du manifest local (Git blob SHA-1). ");
            var localManifest = await manifestService.BuildGitBlobManifestAsync(targetArchipel, cancellationToken);

            if (localManifest.Count > 0 && manifestService.AreEquivalent(localManifest, remoteManifest))
            {
                ProgressChanged?.Invoke(100, "Déjà à jour.");
                await logger.InfoAsync("Aucune mise à jour nécessaire: déjà à jour (scan GitHub). ");
                return new InstallResult
                {
                    ExitCode = InstallerExitCode.Success,
                    Message = "Déjà à jour",
                    AlreadyUpToDate = true,
                };
            }

            if (options.DryRun)
            {
                ProgressChanged?.Invoke(100, "Dry-run terminé: mise à jour requise.");
                await logger.InfoAsync("Dry-run: des différences ont été détectées, aucun téléchargement/applique effectué.");
                return new InstallResult
                {
                    ExitCode = InstallerExitCode.Success,
                    Message = "Dry-run: des changements seraient appliqués.",
                    AlreadyUpToDate = false,
                };
            }

            tempRoot = Path.Combine(Path.GetTempPath(), "ArchipelInstaller", Guid.NewGuid().ToString("N"));
            FileUtils.EnsureDirectory(tempRoot);

            ProgressChanged?.Invoke(45, "Mise à jour requise: téléchargement du ZIP...");
            var stagedArchipel = await gitHubDownloadService.DownloadAndExtractArchipelAsync(tempRoot, cancellationToken, OnProgress);
            manifestService.ValidateExtractedIntegrity(stagedArchipel);

            ProgressChanged?.Invoke(80, "Application de la transaction atomique...");
            await installTransactionService.ApplyAtomicInstallAsync(stagedArchipel, targetArchipel, options.DryRun, cancellationToken);
            ProgressChanged?.Invoke(100, "Installation terminée avec succès.");
            await logger.InfoAsync("Installation/Mise à jour terminée avec succès.");

            return new InstallResult
            {
                ExitCode = InstallerExitCode.Success,
                Message = "Installation / mise à jour terminée.",
                AlreadyUpToDate = false,
            };
        }
        catch (InstallerException exception)
        {
            await logger.ErrorAsync(exception.Message, exception);
            return new InstallResult
            {
                ExitCode = exception.ExitCode,
                Message = exception.Message,
                AlreadyUpToDate = false,
            };
        }
        catch (Exception exception)
        {
            await logger.ErrorAsync("Erreur inattendue", exception);
            return new InstallResult
            {
                ExitCode = InstallerExitCode.PermissionOrLockedError,
                Message = "Erreur inattendue: " + exception.Message,
                AlreadyUpToDate = false,
            };
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempRoot))
            {
                try
                {
                    FileUtils.DeleteDirectoryIfExists(tempRoot);
                }
                catch (Exception cleanupException)
                {
                    await logger.WarnAsync("Nettoyage temp incomplet: " + cleanupException.Message);
                }
            }
        }
    }

    private void OnProgress(int percent, string message)
    {
        ProgressChanged?.Invoke(percent, message);
    }
}
