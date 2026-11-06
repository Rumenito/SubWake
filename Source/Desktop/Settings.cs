using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SubWake;

static class AppPaths
{
    // SUBWAKE_DATA points a test copy at its own folder (screenshots, manual tests), so the real settings stay untouched.
    public static readonly string Data = Environment.GetEnvironmentVariable("SUBWAKE_DATA") is { Length: > 0 } custom ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SubWake");
    public static string Log => Path.Combine(Data, "subwake.log");
    public static string Settings => Path.Combine(Data, "settings.json");
    public static string WebView => Path.Combine(Data, "WebView2");
}

static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

/// <summary>Everything the window can change. Saved to %LOCALAPPDATA%\SubWake\settings.json.</summary>
sealed class Settings
{
    public const double MinLevelDb = -60, MaxLevelDb = -6, HighToneDb = -40;
    public const double MinThresholdDb = -70, MaxThresholdDb = -10;
    public static readonly double[] StandbyOptions = [5, 10, 15, 20, 30, 60];   // Minutes until the sub's auto-standby.
    public static readonly double[] LowTones = [25, 30, 35, 40, 50, 60];       // Hz for the main low tone.
    public static readonly double[] Durations = [1, 1.5, 2];                    // Seconds.
    public static readonly double[] AwayOptions = [15, 30, 60, 120];            // Minutes without input or audio.
    const int MaxHoldApps = 20, MaxWatchOutputs = 32;

    /// <summary>Burst intervals for a standby time: every choice stays under it.</summary>
    public static double[] IntervalsFor(double standbyMinutes) => standbyMinutes switch
    {
        <= 5 => [2, 3, 4],
        <= 10 => [3, 5, 7, 9],
        <= 15 => [5, 8, 10, 12, 14],
        <= 20 => [5, 10, 15, 18],
        <= 30 => [10, 15, 20, 25, 28],
        _ => [15, 30, 45, 55],
    };

    public bool Enabled { get; set; } = true;
    public bool SetupDone { get; set; }                                 // First-run setup finished; no bursts before that.
    public double SubStandbyMinutes { get; set; } = 15;                 // The sub sleeps after this long without signal.
    public bool ToneLow { get; set; } = true;                           // The main low tone, at LowToneHz.
    public double LowToneHz { get; set; } = 35;
    public bool Tone20 { get; set; }
    public bool Tone20k { get; set; }
    public double LevelDb { get; set; } = -40;
    public double IntervalMinutes { get; set; } = 10;
    public double DurationSeconds { get; set; } = 1.5;
    public string? DeviceId { get; set; }                                // Windows endpoint ID; survives renames.
    public string DeviceName { get; set; } = "";                        // Shown, and the fallback if the ID is gone.
    public bool SkipWhileAudio { get; set; } = true;                    // Audio on the watched outputs restarts the countdown.
    public List<string>? WatchOutputIds { get; set; }                   // Null: every output on the burst output's device.
    public double SignalThresholdDb { get; set; } = -50;                // Audio counts only from this peak level.
    public bool HoldWhileRecording { get; set; } = true;                // No bursts while an app records from an input.
    public List<string> HoldApps { get; set; } = [];                    // Process names: no bursts while one is running.
    public bool SleepWhenAway { get; set; } = true;
    public double AwayMinutes { get; set; } = 30;
    public bool BurstAfterWake { get; set; } = true;                    // Unlock, resume from sleep, or return from away.
    public bool PauseWhenLocked { get; set; }
    public bool QuietHours { get; set; }
    public string QuietStart { get; set; } = "00:00";
    public string QuietEnd { get; set; } = "07:00";
    public bool NotifyDeviceMissing { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;                   // Once a day, asks GitHub for the latest release.
    public int? InstallerUpdateChoice { get; set; }                     // The installer's update-check choice that was last applied.
    public DateTime? LastUpdateCheck { get; set; }                      // Local time of the last automatic check.
    public string? UpdateNotifiedVersion { get; set; }                  // The newer version a balloon was shown for.
    public double? CalibratedWakeDb { get; set; }                       // Where the sub woke in the last calibration.
    public bool TrayHintShown { get; set; }                             // "Still running in the tray" is shown once, on the first close.
    public int[]? WindowBounds { get; set; }                            // x, y, width, height of the window when last closed.

    [System.Text.Json.Serialization.JsonIgnore]
    public double[] Intervals => IntervalsFor(SubStandbyMinutes);

    public static Settings Load()
    {
        Settings settings = new();
        try
        {
            if (File.Exists(AppPaths.Settings))
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.Settings), Json.Options) ?? new();
        }
        catch (Exception ex) { Log.Write("Settings could not be read, using defaults: " + ex.Message); }
        if (settings.ApplyInstallerChoice(InstallOptions.CheckForUpdates()))
            Log.Write("Update check " + (settings.CheckForUpdates ? "on" : "off") + ", as chosen in Setup.");
        settings.Normalize();
        return settings;
    }

    /// <summary>The installer's "check for updates" checkbox wins whenever it changes; the window can change it in between.</summary>
    internal bool ApplyInstallerChoice(int? choice)
    {
        if (choice is not int value || value == InstallerUpdateChoice) return false;
        InstallerUpdateChoice = value;
        CheckForUpdates = value != 0;
        return true;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Data);
        string temp = AppPaths.Settings + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json.Options));
        File.Move(temp, AppPaths.Settings, true);
    }

    /// <summary>Returns a copy with the window's changed fields applied and validated.</summary>
    public Settings With(JsonElement changes)
    {
        var node = JsonSerializer.SerializeToNode(this, Json.Options)!.AsObject();
        foreach (var property in changes.EnumerateObject())
            if (node.ContainsKey(property.Name)) node[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        var updated = node.Deserialize<Settings>(Json.Options) ?? this;
        updated.Normalize();
        return updated;
    }

    public Settings Copy() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, Json.Options), Json.Options)!;

    internal void Normalize()
    {
        LevelDb = Math.Round(Math.Clamp(LevelDb, MinLevelDb, MaxLevelDb));
        SignalThresholdDb = Math.Round(Math.Clamp(SignalThresholdDb, MinThresholdDb, MaxThresholdDb));
        if (CalibratedWakeDb is double wake) CalibratedWakeDb = Math.Round(Math.Clamp(wake, MinLevelDb, MaxLevelDb));
        SubStandbyMinutes = Closest(StandbyOptions, SubStandbyMinutes);
        IntervalMinutes = Closest(Intervals, IntervalMinutes);
        LowToneHz = Closest(LowTones, LowToneHz);
        DurationSeconds = Closest(Durations, DurationSeconds);
        AwayMinutes = Closest(AwayOptions, AwayMinutes);
        if (!ToneLow && !Tone20 && !Tone20k) ToneLow = true;   // A burst always has at least one tone.
        DeviceName = (DeviceName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(DeviceId)) DeviceId = null;
        if (!TryParseTime(QuietStart, out _)) QuietStart = "00:00";
        if (!TryParseTime(QuietEnd, out _)) QuietEnd = "07:00";
        HoldApps = (HoldApps ?? [])
            .Select(CleanAppName).Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxHoldApps).ToList();
        if (WatchOutputIds != null)
            WatchOutputIds = WatchOutputIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(MaxWatchOutputs).ToList();
        if (WindowBounds is { Length: not 4 }) WindowBounds = null;
    }

    /// <summary>A process name as Windows reports it: no folder, no ".exe".</summary>
    public static string CleanAppName(string? name)
    {
        string clean = Path.GetFileName((name ?? "").Trim());
        return clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? clean[..^4] : clean;
    }

    static double Closest(double[] allowed, double value) => allowed.MinBy(a => Math.Abs(a - value));

    static bool TryParseTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>Uses local wall-clock time. The window may cross midnight.</summary>
    public bool InQuietHours(DateTime localNow)
    {
        if (!QuietHours || !TryParseTime(QuietStart, out var start) || !TryParseTime(QuietEnd, out var end) || start == end) return false;
        var time = TimeOnly.FromDateTime(localNow);
        return start < end ? time >= start && time < end : time >= start || time < end;
    }

    /// <summary>Low tones at the set level (their combined peak); 20 kHz at a fixed -40 dBFS.</summary>
    public List<Tone> Tones(double? lowLevelDb = null, bool includeHigh = true)
    {
        double low = lowLevelDb ?? LevelDb;
        var tones = new List<Tone>();
        if (ToneLow) tones.Add(new Tone(LowToneHz, low));
        if (Tone20) tones.Add(new Tone(20, low));
        if (includeHigh && Tone20k) tones.Add(new Tone(20000, HighToneDb));
        if (tones.Count == 0) tones.Add(new Tone(LowToneHz, low));   // Calibration with only 20 kHz on still needs a low tone.
        return tones;
    }

    /// <summary>"35 Hz at -30 dBFS for 1.5 s", or "35 Hz + 20 Hz at -6 dBFS peak + 20 kHz at -40 dBFS for 2 s".</summary>
    public static string Describe(IReadOnlyList<Tone> tones, double seconds)
    {
        var parts = new List<string>();
        var low = tones.Where(t => t.IsLow).ToList();
        if (low.Count > 0)
            parts.Add(string.Join(" + ", low.Select(t => $"{t.Frequency} Hz")) + $" at {low.Max(t => t.LevelDb):0} dBFS" + (low.Count > 1 ? " peak" : ""));
        parts.AddRange(tones.Where(t => !t.IsLow).Select(t => $"{t.Frequency / 1000} kHz at {t.LevelDb:0} dBFS"));
        return string.Join(" + ", parts) + $" for {seconds.ToString(CultureInfo.InvariantCulture)} s";
    }
}

/// <summary>Choices Setup leaves in HKCU\Software\SubWake for the app to pick up.</summary>
static class InstallOptions
{
    public static int? CheckForUpdates()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\SubWake");
            return key?.GetValue("CheckForUpdates") is int value ? value : null;
        }
        catch { return null; }
    }
}
