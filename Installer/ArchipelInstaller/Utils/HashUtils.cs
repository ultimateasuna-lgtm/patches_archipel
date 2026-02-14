using System.Security.Cryptography;
using System.Text;

namespace ArchipelInstaller.Utils;

public static class HashUtils
{
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        using var sha = SHA256.Create();
        var hashBytes = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static async Task<string> ComputeGitBlobSha1Async(string filePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(filePath);
        var header = Encoding.UTF8.GetBytes($"blob {info.Length}\0");

        using var sha1 = SHA1.Create();
        sha1.TransformBlock(header, 0, header.Length, null, 0);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            sha1.TransformBlock(buffer, 0, read, null, 0);
        }

        sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha1.Hash!).ToLowerInvariant();
    }
}
