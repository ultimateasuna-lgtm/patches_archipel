namespace ArchipelInstaller.Models;

public sealed class ManifestEntry
{
    public required string RelativePath { get; init; }
    public required string Sha256 { get; init; }
    public required long Size { get; init; }
}
