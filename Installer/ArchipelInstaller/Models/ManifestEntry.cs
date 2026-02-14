namespace ArchipelInstaller.Models;

public sealed class ManifestEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string GitBlobSha1 { get; init; } = string.Empty;
    public long Size { get; init; }
}
