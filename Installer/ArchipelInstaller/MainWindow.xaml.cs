using System.Windows;
using System.Windows.Forms;
using ArchipelInstaller.Models;
using ArchipelInstaller.Services;

namespace ArchipelInstaller
{
    public partial class MainWindow : Window
    {
        private readonly InstallerService installerService;
        private readonly Logger logger;
        private readonly InstallerOptions startupOptions;
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private bool isInstalling;

        public MainWindow(InstallerService installerService, Logger logger, InstallerOptions startupOptions)
        {
            InitializeComponent();

            this.installerService = installerService;
            this.logger = logger;
            this.startupOptions = startupOptions;

            FinalExitCode = InstallerExitCode.Success;
            SilentModeCheckBox.IsChecked = startupOptions.Silent;

            this.logger.LogReceived += OnLogReceived;
            this.installerService.ProgressChanged += OnProgressChanged;
            Closed += OnClosed;
        }

        public InstallerExitCode FinalExitCode { get; private set; }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (isInstalling)
            {
                return;
            }

            isInstalling = true;
            InstallButton.IsEnabled = false;

            try
            {
                AppendLog("Démarrage de l'installation...");

                var runOptions = new InstallerOptions
                {
                    DryRun = startupOptions.DryRun,
                    NoClose = startupOptions.NoClose,
                    ForcedPath = startupOptions.ForcedPath,
                    Silent = SilentModeCheckBox.IsChecked == true,
                };

                var result = await installerService.RunAsync(
                    runOptions,
                    allowManualSelection: true,
                    manualPicker: ManualPickEpsilonFolderAsync,
                    cancellationTokenSource.Token);

                FinalExitCode = result.ExitCode;
                AppendLog($"Résultat: {result.Message}");

                if (result.ExitCode != InstallerExitCode.Success && runOptions.Silent == false)
                {
                    System.Windows.MessageBox.Show(result.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                if (result.ExitCode == InstallerExitCode.Success && !runOptions.NoClose)
                {
                    await Task.Delay(800);
                    Close();
                }
            }
            finally
            {
                isInstalling = false;
                InstallButton.IsEnabled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (isInstalling)
            {
                cancellationTokenSource.Cancel();
                AppendLog("Annulation demandée...");
            }

            Close();
        }

        private Task<string?> ManualPickEpsilonFolderAsync()
        {
            return Dispatcher.InvokeAsync(() =>
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Sélectionnez le dossier Epsilon_retail_",
                    ShowNewFolderButton = false,
                    UseDescriptionForTitle = true,
                };

                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    return dialog.SelectedPath;
                }

                return null;
            }).Task;
        }

        private void OnLogReceived(string logLine)
        {
            Dispatcher.Invoke(() => AppendLog(logLine));
        }

        private void OnProgressChanged(int percent, string message)
        {
            Dispatcher.Invoke(() =>
            {
                InstallProgressBar.Value = percent;
                ProgressTextBlock.Text = $"{percent}% - {message}";
            });
        }

        private void AppendLog(string line)
        {
            LogsTextBox.AppendText(line + Environment.NewLine);
            LogsTextBox.ScrollToEnd();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            logger.LogReceived -= OnLogReceived;
            installerService.ProgressChanged -= OnProgressChanged;
            cancellationTokenSource.Dispose();
        }
    }
}
