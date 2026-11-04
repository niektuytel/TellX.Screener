namespace WorkflowTracker.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkflowTracker", "logs");

    public static void Write(string message, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, $"tracker-{DateTime.Now:yyyy-MM-dd}.log");
                // Alleen eigen foutteksten en fouttype; geen API-responses, URLs, tokens of screenshots.
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}" +
                    (error is null ? "" : $" [{error.GetType().Name}]" +
                        (error is HttpRequestException { StatusCode: { } code } ? $" HTTP {(int)code}" : "")) +
                    Environment.NewLine);
                foreach (var old in new DirectoryInfo(DirectoryPath).EnumerateFiles("tracker-*.log")
                             .Where(file => file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-14)))
                    old.Delete();
            }
        }
        catch { /* Logproblemen mogen de capture-loop niet beëindigen. */ }
    }
}
