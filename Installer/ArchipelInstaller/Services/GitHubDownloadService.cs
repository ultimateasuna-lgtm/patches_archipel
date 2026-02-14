using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using ArchipelInstaller.Models;
using ArchipelInstaller.Utils;

namespace ArchipelInstaller.Services;

public sealed class GitHubDownloadService
{
    private const string RepoZipUrl = "https://github.com/ultimateasuna-lgtm/patches_archipel/archive/refs/heads/main.zip";
    private const string RepoTreeApiUrl = "https://api.github.com/repos/ultimateasuna-lgtm/patches_archipel/git/trees/main?recursive=1";
    private const string RemotePrefix = "Patches/Archipel/";
    private readonly Logger logger;
    private readonly HttpClient httpClient;

    public GitHubDownloadService(Logger logger)
    {
        this.logger = logger;

        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ArchipelInstaller/1.0");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<Dictionary<string, ManifestEntry>> FetchRemoteManifestAsync(
        CancellationToken cancellationToken,
        Action<int, string>? progress = null)
    {
        progress?.Invoke(10, "Scan GitHub: récupération de l'arbre distant...");
        await logger.InfoAsync("Récupération du manifest distant via GitHub API (git tree). ");

        var json = await DownloadStringWithRetryAsync(RepoTreeApiUrl, cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("tree", out var treeElement) || treeElement.ValueKind != JsonValueKind.Array)
        {
            throw new InstallerException(InstallerExitCode.NetworkError, "Réponse GitHub invalide (tree manquant).");
        }

        var manifest = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in treeElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!item.TryGetProperty("type", out var typeElement) || !string.Equals(typeElement.GetString(), "blob", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!item.TryGetProperty("path", out var pathElement))
            {
                continue;
            }

            var path = pathElement.GetString();
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(RemotePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = path.Substring(RemotePrefix.Length).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var sha = item.TryGetProperty("sha", out var shaElement) ? shaElement.GetString() ?? string.Empty : string.Empty;
            var size = item.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)
                ? parsedSize
                : -1;

            manifest[relative] = new ManifestEntry
            {
                RelativePath = relative,
                GitBlobSha1 = sha,
                Size = size,
            };
        }

        if (manifest.Count == 0)
        {
            throw new InstallerException(InstallerExitCode.IntegrityError, "Aucun fichier distant trouvé dans Patches/Archipel.");
        }

        progress?.Invoke(25, $"Scan GitHub terminé: {manifest.Count} fichiers distants détectés.");
        await logger.InfoAsync($"Manifest distant chargé: {manifest.Count} fichiers.");
        return manifest;
    }

    public async Task<string> DownloadAndExtractArchipelAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        Action<int, string>? progress = null)
    {
        FileUtils.EnsureDirectory(workingDirectory);
        var zipPath = Path.Combine(workingDirectory, "main.zip");
        var extractRoot = Path.Combine(workingDirectory, "extracted");
        var outputArchipel = Path.Combine(workingDirectory, "Archipel");

        FileUtils.DeleteDirectoryIfExists(extractRoot);
        FileUtils.DeleteDirectoryIfExists(outputArchipel);
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        progress?.Invoke(10, "Téléchargement du ZIP GitHub...");
        await logger.InfoAsync("Téléchargement du ZIP GitHub en cours.");
        await DownloadFileWithRetryAsync(RepoZipUrl, zipPath, cancellationToken);

        var zipSize = new FileInfo(zipPath).Length;
        FileUtils.EnsureFreeDiskSpace(workingDirectory, zipSize * 3);

        progress?.Invoke(30, "Extraction du ZIP...");
        await logger.InfoAsync("Extraction du ZIP téléchargé.");
        ZipFile.ExtractToDirectory(zipPath, extractRoot);

        var archipelSource = Directory
            .EnumerateDirectories(extractRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(root => Path.Combine(root, "Patches", "Archipel"))
            .FirstOrDefault(Directory.Exists);

        if (archipelSource is null)
        {
            throw new InstallerException(
                InstallerExitCode.IntegrityError,
                "Le dossier Patches/Archipel est introuvable dans l'archive GitHub.");
        }

        progress?.Invoke(40, "Préparation des fichiers distants...");
        await logger.InfoAsync("Copie des fichiers distants Archipel dans le staging local.");
        FileUtils.CopyDirectory(archipelSource, outputArchipel);
        return outputArchipel;
    }

    private async Task DownloadFileWithRetryAsync(string url, string outputFile, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                {
                    if (attempt == maxRetries)
                    {
                        throw new InstallerException(
                            InstallerExitCode.NetworkError,
                            $"GitHub a refusé la requête (HTTP {(int)response.StatusCode}). Vérifiez rate-limit/proxy.");
                    }

                    var retryDelay = GetRetryDelay(response, attempt);
                    await logger.WarnAsync($"HTTP {(int)response.StatusCode} reçu, nouvelle tentative dans {retryDelay.TotalSeconds:F0}s.");
                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new InstallerException(
                        InstallerExitCode.NetworkError,
                        $"Échec du téléchargement ZIP (HTTP {(int)response.StatusCode}).");
                }

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await responseStream.CopyToAsync(fileStream, cancellationToken);
                return;
            }
            catch (InstallerException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                if (attempt == maxRetries)
                {
                    throw new InstallerException(
                        InstallerExitCode.NetworkError,
                        "Erreur réseau lors du téléchargement GitHub.",
                        exception);
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await logger.WarnAsync($"Tentative {attempt}/{maxRetries} échouée: {exception.Message}. Retry dans {delay.TotalSeconds:F0}s.");
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task<string> DownloadStringWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                {
                    if (attempt == maxRetries)
                    {
                        throw new InstallerException(
                            InstallerExitCode.NetworkError,
                            $"GitHub a refusé la requête API (HTTP {(int)response.StatusCode}). Vérifiez rate-limit/proxy.");
                    }

                    var retryDelay = GetRetryDelay(response, attempt);
                    await logger.WarnAsync($"API HTTP {(int)response.StatusCode} reçu, nouvelle tentative dans {retryDelay.TotalSeconds:F0}s.");
                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new InstallerException(
                        InstallerExitCode.NetworkError,
                        $"Échec de lecture du manifest GitHub (HTTP {(int)response.StatusCode}).");
                }

                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (InstallerException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                if (attempt == maxRetries)
                {
                    throw new InstallerException(
                        InstallerExitCode.NetworkError,
                        "Erreur réseau lors de la lecture du manifest GitHub.",
                        exception);
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await logger.WarnAsync($"Tentative API {attempt}/{maxRetries} échouée: {exception.Message}. Retry dans {delay.TotalSeconds:F0}s.");
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InstallerException(InstallerExitCode.NetworkError, "Échec de récupération du manifest GitHub.");
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var retryAfter = values.FirstOrDefault();
            if (int.TryParse(retryAfter, out var seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }
}
