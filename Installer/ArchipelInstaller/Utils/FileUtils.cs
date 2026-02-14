namespace ArchipelInstaller.Utils;

public static class FileUtils
{
    public static void EnsureDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    public static void DeleteDirectoryIfExists(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    public static long GetDirectorySize(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        long size = 0;
        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            size += new FileInfo(file).Length;
        }

        return size;
    }

    public static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var source = new DirectoryInfo(sourceDir);
        EnsureDirectory(destinationDir);

        foreach (var directory in source.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source.FullName, directory.FullName);
            var destinationPath = Path.Combine(destinationDir, relative);
            EnsureDirectory(destinationPath);
        }

        foreach (var file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source.FullName, file.FullName);
            var destinationPath = Path.Combine(destinationDir, relative);
            var destinationParent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                EnsureDirectory(destinationParent);
            }

            file.CopyTo(destinationPath, overwrite: true);
        }
    }

    public static void EnsureFreeDiskSpace(string targetPath, long minimumBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(targetPath));
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < minimumBytes)
        {
            throw new IOException($"Espace disque insuffisant sur {drive.Name}. Requis: {minimumBytes} octets.");
        }
    }
}
