namespace ArchipelInstaller.Models;

public sealed class InstallerOptions
{
    public bool Silent { get; init; }
    public bool DryRun { get; init; }
    public bool NoClose { get; init; }
    public string? ForcedPath { get; init; }

    public static InstallerOptions Parse(string[] args)
    {
        string? forcedPath = null;
        var silent = false;
        var dryRun = false;
        var noClose = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index].Trim();
            switch (arg.ToLowerInvariant())
            {
                case "--silent":
                    silent = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--no-close":
                    noClose = true;
                    break;
                case "--path":
                    if (index + 1 < args.Length)
                    {
                        forcedPath = args[++index].Trim('"');
                    }
                    break;
            }
        }

        return new InstallerOptions
        {
            Silent = silent,
            DryRun = dryRun,
            NoClose = noClose,
            ForcedPath = string.IsNullOrWhiteSpace(forcedPath) ? null : forcedPath,
        };
    }
}
