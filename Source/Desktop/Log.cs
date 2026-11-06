namespace SubWake;

/// <summary>Same file and line format as the PowerShell version: "yyyy-MM-dd HH:mm:ss  message".</summary>
static class Log
{
    static readonly object Gate = new();

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Data);
                var file = new FileInfo(AppPaths.Log);
                if (file.Exists && file.Length > 1024 * 1024) File.Move(AppPaths.Log, AppPaths.Log + ".old", true);
                File.AppendAllText(AppPaths.Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch { }   // Logging must never stop the keep-awake bursts.
        }
    }
}
