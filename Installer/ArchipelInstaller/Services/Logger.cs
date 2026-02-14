using System.Text;
using ArchipelInstaller.Utils;

namespace ArchipelInstaller.Services;

public sealed class Logger
{
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly string logFilePath;

    public Logger()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logDir = Path.Combine(appData, "ArchipelInstaller", "logs");
        FileUtils.EnsureDirectory(logDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        logFilePath = Path.Combine(logDir, $"installer-{timestamp}.log");
    }

    public event Action<string>? LogReceived;

    public string LogFilePath => logFilePath;

    public async Task InfoAsync(string message)
    {
        await WriteAsync("INFO", message);
    }

    public async Task WarnAsync(string message)
    {
        await WriteAsync("WARN", message);
    }

    public async Task ErrorAsync(string message)
    {
        await WriteAsync("ERROR", message);
    }

    public async Task ErrorAsync(string message, Exception exception)
    {
        var fullMessage = new StringBuilder()
            .AppendLine(message)
            .AppendLine(exception.ToString())
            .ToString();

        await WriteAsync("ERROR", fullMessage);
    }

    private async Task WriteAsync(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        LogReceived?.Invoke(line);

        await writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(logFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        finally
        {
            writeLock.Release();
        }
    }
}
