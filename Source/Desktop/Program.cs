namespace SubWake;

static class AppIds
{
    // A test copy (SUBWAKE_DATA set) gets its own names, so it runs beside the installed app.
    static readonly string Suffix = Environment.GetEnvironmentVariable("SUBWAKE_DATA") is { Length: > 0 } ? "Test" : "";
    public static readonly string Mutex = @"Local\SubWake" + Suffix;
    public static readonly string ShowEvent = @"Local\SubWakeShow" + Suffix;
    public static readonly string StopEvent = @"Local\SubWakeStop" + Suffix;
}

static class Program
{
    // Arguments:
    //   --startup                     Launched by Windows at sign-in: stay in the tray, no window (unless setup isn't done).
    //   --stop                        Ask the running copy to exit (used by Setup and the uninstaller).
    //   --configure-startup=on|off    Set Start with Windows and exit (used by the uninstaller).
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Contains("--stop")) { StopRunningCopy(); return; }

        var startupArg = args.FirstOrDefault(a => a.StartsWith("--configure-startup=", StringComparison.Ordinal));
        if (startupArg != null)
        {
            try { StartupRegistration.Set(startupArg.EndsWith("=on", StringComparison.Ordinal)); }
            catch (Exception ex) { Log.Write("Start with Windows could not be changed: " + ex.Message); Environment.ExitCode = 1; }
            return;
        }

        using var mutex = new Mutex(true, AppIds.Mutex, out bool first);
        if (!first)
        {
            // Already running: bring its window forward, unless this is the sign-in launch.
            if (!args.Contains("--startup"))
                try { using var show = EventWaitHandle.OpenExisting(AppIds.ShowEvent); show.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
            return;
        }

        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var app = new SubWakeApp(showWindow: !args.Contains("--startup"));
        Application.Run(app);
    }

    static void StopRunningCopy()
    {
        try
        {
            using var stop = EventWaitHandle.OpenExisting(AppIds.StopEvent);
            stop.Set();
            // Wait until it has exited so Setup can replace its files.
            using var running = Mutex.OpenExisting(AppIds.Mutex);
            try { if (running.WaitOne(10000)) running.ReleaseMutex(); } catch (AbandonedMutexException) { }
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }
}
