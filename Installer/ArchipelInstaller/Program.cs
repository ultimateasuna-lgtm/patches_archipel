using System.Windows;
using ArchipelInstaller.Models;
using ArchipelInstaller.Services;

namespace ArchipelInstaller;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = InstallerOptions.Parse(args);
        var logger = new Logger();
        var installerService = BuildInstallerService(logger);

        if (options.Silent)
        {
            return RunSilentAsync(installerService, logger, options).GetAwaiter().GetResult();
        }

        var app = new App();
        var window = new MainWindow(installerService, logger, options);
        app.Run(window);
        return (int)window.FinalExitCode;
    }

    private static async Task<int> RunSilentAsync(InstallerService installerService, Logger logger, InstallerOptions options)
    {
        await logger.InfoAsync("Mode silent activé.");
        var result = await installerService.RunAsync(options, allowManualSelection: false, manualPicker: null, CancellationToken.None);
        await logger.InfoAsync(result.Message);
        return (int)result.ExitCode;
    }

    private static InstallerService BuildInstallerService(Logger logger)
    {
        var pathDetectionService = new PathDetectionService(logger);
        var gitHubDownloadService = new GitHubDownloadService(logger);
        var manifestService = new ManifestService();
        var installTransactionService = new InstallTransactionService(logger);

        return new InstallerService(
            logger,
            pathDetectionService,
            gitHubDownloadService,
            manifestService,
            installTransactionService);
    }
}
