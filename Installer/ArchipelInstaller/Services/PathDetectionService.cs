using System.Text.Json;
using Microsoft.Win32;
using ArchipelInstaller.Models;
using ArchipelInstaller.Utils;

namespace ArchipelInstaller.Services;

public sealed class PathDetectionService
{
    private readonly Logger logger;
    private readonly string configPath;

    public PathDetectionService(Logger logger)
    {
        this.logger = logger;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appData, "ArchipelInstaller");
        FileUtils.EnsureDirectory(configDir);
        configPath = Path.Combine(configDir, "config.json");
    }

    public async Task<string> DetectEpsilonRootAsync(
        InstallerOptions options,
        bool allowManualSelection,
        Func<Task<string?>>? manualPicker,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(options.ForcedPath))
        {
            await logger.InfoAsync($"Chemin forcé via --path: {options.ForcedPath}");
            var forced = ValidateRootOrThrow(options.ForcedPath);
            await SaveConfigAsync(forced, cancellationToken);
            return forced;
        }

        var config = await LoadConfigAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(config?.LastEpsilonRoot))
        {
            var fromConfig = TryValidateRoot(config.LastEpsilonRoot);
            if (fromConfig is not null)
            {
                await logger.InfoAsync($"Chemin trouvé via config: {fromConfig}");
                return fromConfig;
            }
        }

        foreach (var candidate in GetKnownCandidates())
        {
            var validated = TryValidateRoot(candidate);
            if (validated is not null)
            {
                await logger.InfoAsync($"Chemin détecté automatiquement: {validated}");
                await SaveConfigAsync(validated, cancellationToken);
                return validated;
            }
        }

        if (allowManualSelection && manualPicker is not null)
        {
            await logger.WarnAsync("Auto-détection échouée, ouverture du sélecteur manuel.");
            var chosen = await manualPicker();
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                var validated = ValidateRootOrThrow(chosen);
                await SaveConfigAsync(validated, cancellationToken);
                return validated;
            }
        }

        throw new InstallerException(
            InstallerExitCode.PathDetectionError,
            "Impossible de détecter le dossier Epsilon_retail_. Utilisez --path ou sélectionnez le dossier manuellement.");
    }

    private string ValidateRootOrThrow(string rootPath)
    {
        var validated = TryValidateRoot(rootPath);
        if (validated is null)
        {
            throw new InstallerException(
                InstallerExitCode.PathDetectionError,
                $"Chemin Epsilon_retail_ invalide: {rootPath}");
        }

        return validated;
    }

    private string? TryValidateRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(rootPath.Trim());
        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        var inferredRoot = InferEpsilonRoot(fullPath);
        if (inferredRoot is null)
        {
            return null;
        }

        var folderName = Path.GetFileName(inferredRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(folderName, "Epsilon_retail_", StringComparison.OrdinalIgnoreCase))
        {
            var hasPatchStructure = Directory.Exists(Path.Combine(inferredRoot, "Patches", "Archipel"));
            if (!hasPatchStructure)
            {
                return null;
            }
        }

        try
        {
            var patchesDir = Path.Combine(inferredRoot, "Patches");
            FileUtils.EnsureDirectory(patchesDir);
            return inferredRoot;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InstallerException(
                InstallerExitCode.PermissionOrLockedError,
                $"Impossible d'accéder/créer le dossier Patches dans {inferredRoot}",
                exception);
        }
    }

    private static string? InferEpsilonRoot(string selectedPath)
    {
        var full = Path.GetFullPath(selectedPath.Trim());
        if (!Directory.Exists(full))
        {
            return null;
        }

        if (IsEpsilonRootName(full))
        {
            return full;
        }

        var folderName = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(folderName, "Archipel", StringComparison.OrdinalIgnoreCase))
        {
            var patches = Directory.GetParent(full)?.FullName;
            var maybeRoot = patches is null ? null : Directory.GetParent(patches)?.FullName;
            if (!string.IsNullOrWhiteSpace(maybeRoot) && Directory.Exists(maybeRoot))
            {
                return maybeRoot;
            }
        }

        if (string.Equals(folderName, "Patches", StringComparison.OrdinalIgnoreCase))
        {
            var maybeRoot = Directory.GetParent(full)?.FullName;
            if (!string.IsNullOrWhiteSpace(maybeRoot) && Directory.Exists(maybeRoot))
            {
                return maybeRoot;
            }
        }

        if (Directory.Exists(Path.Combine(full, "Patches", "Archipel")))
        {
            return full;
        }

        var current = new DirectoryInfo(full);
        for (var i = 0; i < 4 && current.Parent is not null; i++)
        {
            current = current.Parent;
            if (IsEpsilonRootName(current.FullName))
            {
                return current.FullName;
            }

            if (Directory.Exists(Path.Combine(current.FullName, "Patches", "Archipel")))
            {
                return current.FullName;
            }
        }

        try
        {
            foreach (var candidate in Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(full, candidate);
                var depth = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
                if (depth > 3)
                {
                    continue;
                }

                if (IsEpsilonRootName(candidate) || Directory.Exists(Path.Combine(candidate, "Patches", "Archipel")))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool IsEpsilonRootName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(name, "Epsilon_retail_", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetKnownCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var cwd = Environment.CurrentDirectory;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var directCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(baseDirectory, "Epsilon_retail_"),
            Path.Combine(cwd, "Epsilon_retail_"),
            Path.Combine(documents, "Epsilon_retail_"),
            Path.Combine(programFiles, "Epsilon_retail_"),
            Path.Combine(localAppData, "Epsilon_retail_"),
        };

        foreach (var candidate in directCandidates)
        {
            yield return candidate;
        }

        foreach (var registryCandidate in GetRegistryCandidates())
        {
            yield return registryCandidate;
        }

        foreach (var parent in new[] { baseDirectory, cwd, documents, programFiles, localAppData })
        {
            if (!Directory.Exists(parent))
            {
                continue;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(parent, "Epsilon_retail_", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                yield return child;
            }
        }
    }

    private IEnumerable<string> GetRegistryCandidates()
    {
        var keyPaths = new[]
        {
            @"SOFTWARE\Epsilon",
            @"SOFTWARE\WOW6432Node\Epsilon",
            @"SOFTWARE\Archipel",
            @"SOFTWARE\WOW6432Node\Archipel",
        };

        var valueNames = new[] { "InstallPath", "Path", "GamePath" };

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var keyPath in keyPaths)
            {
                using var key = root.OpenSubKey(keyPath);
                if (key is null)
                {
                    continue;
                }

                foreach (var valueName in valueNames)
                {
                    var value = key.GetValue(valueName) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }
        }
    }

    private async Task<InstallerConfig?> LoadConfigAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<InstallerConfig>(stream, cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveConfigAsync(string epsilonRoot, CancellationToken cancellationToken)
    {
        var config = new InstallerConfig { LastEpsilonRoot = epsilonRoot };
        await using var stream = new FileStream(configPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, config, cancellationToken: cancellationToken);
    }
}
