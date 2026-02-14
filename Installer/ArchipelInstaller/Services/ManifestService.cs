using ArchipelInstaller.Models;
using ArchipelInstaller.Utils;

namespace ArchipelInstaller.Services;

public sealed class ManifestService
{
    public async Task<Dictionary<string, ManifestEntry>> BuildManifestAsync(string rootDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }

        var files = Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var manifest = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new FileInfo(file);
            var relative = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            var hash = await HashUtils.ComputeSha256Async(file, cancellationToken);

            manifest[relative] = new ManifestEntry
            {
                RelativePath = relative,
                Sha256 = hash,
                Size = info.Length,
            };
        }

        return manifest;
    }

    public async Task<Dictionary<string, ManifestEntry>> BuildGitBlobManifestAsync(string rootDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }

        var files = Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var manifest = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new FileInfo(file);
            var relative = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            var gitBlobSha1 = await HashUtils.ComputeGitBlobSha1Async(file, cancellationToken);

            manifest[relative] = new ManifestEntry
            {
                RelativePath = relative,
                GitBlobSha1 = gitBlobSha1,
                Size = info.Length,
            };
        }

        return manifest;
    }

    public void ValidateExtractedIntegrity(string extractedArchipelDirectory)
    {
        if (!Directory.Exists(extractedArchipelDirectory))
        {
            throw new InstallerException(InstallerExitCode.IntegrityError, "Le dossier Archipel extrait est introuvable.");
        }

        var files = Directory.EnumerateFiles(extractedArchipelDirectory, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length == 0)
        {
            throw new InstallerException(InstallerExitCode.IntegrityError, "Le dossier Archipel extrait est vide.");
        }

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            if (info.Length == 0)
            {
                throw new InstallerException(InstallerExitCode.IntegrityError, $"Fichier vide détecté: {file}");
            }
        }
    }

    public bool AreEquivalent(
        IReadOnlyDictionary<string, ManifestEntry> localManifest,
        IReadOnlyDictionary<string, ManifestEntry> remoteManifest)
    {
        if (localManifest.Count != remoteManifest.Count)
        {
            return false;
        }

        foreach (var (relativePath, remoteEntry) in remoteManifest)
        {
            if (!localManifest.TryGetValue(relativePath, out var localEntry))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(remoteEntry.GitBlobSha1))
            {
                if (!string.Equals(localEntry.GitBlobSha1, remoteEntry.GitBlobSha1, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (!string.Equals(localEntry.Sha256, remoteEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (remoteEntry.Size >= 0 && localEntry.Size != remoteEntry.Size)
            {
                return false;
            }
        }

        return true;
    }
}
