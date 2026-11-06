using System.Globalization;
using System.Text.Json;
using SubWake;

// Build-time tests for SubWake's rules. Exit code 0 = all passed.
//   (no arguments)   Run the tests.
//   --hardware       Also watch the real audio devices for a few seconds and print what the monitor sees.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var t = new TestRun();

var start = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
DateTime At(double minutes) => start.AddMinutes(minutes);

// --- Scheduler ---------------------------------------------------------------------------------
t.Group("Schedule");
{
    var s = new Scheduler(start);
    t.Check(s.Due(start), "first burst is due straight away");
    s.Played(At(0), 8);
    t.Equal(At(8), s.NextBurstAt, "a burst schedules the next one an interval later");
    t.Check(!s.Due(At(7.9)) && s.Due(At(8)), "due exactly at the interval");

    s.HoldForAudio(At(5), 8);
    t.Equal(At(13), s.NextBurstAt, "audio pushes the next burst to one interval after the last signal");
    s.HoldForAudio(At(1), 8);
    t.Equal(At(13), s.NextBurstAt, "older audio never pulls the next burst earlier");

    s.WakeAt(At(6));
    t.Equal(At(6), s.NextBurstAt, "unlock or wake brings the burst forward");
    s.WakeAt(At(20));
    t.Equal(At(6), s.NextBurstAt, "a wake never pushes the burst back");

    s.Failed(At(6));
    t.Equal(At(6).AddSeconds(60), s.NextBurstAt, "a failed burst retries after 60 s");

    s.Played(At(10), 8, reschedule: false);
    t.Equal(At(10), s.LastBurstAt, "calibration bursts count as the last burst");
    t.Equal(At(6).AddSeconds(60), s.NextBurstAt, "calibration bursts don't move the schedule");

    s.Restart(At(12), 14);
    t.Equal(At(24), s.NextBurstAt, "interval change: one new interval after the last burst");
    s.Restart(At(40), 5);
    t.Equal(At(40), s.NextBurstAt, "interval change never schedules in the past");
    var fresh = new Scheduler(start);
    fresh.Restart(At(3), 10);
    t.Equal(At(13), fresh.NextBurstAt, "interval change before any burst counts from now");
}

// --- Pause rules -------------------------------------------------------------------------------
t.Group("Pause rules");
{
    PauseInputs Inputs(bool enabled = true, string? snooze = null, bool locked = false, bool pauseLocked = false, bool quiet = false,
                       bool away = false, string[]? recording = null, string[]? apps = null) =>
        new(enabled, snooze, locked, pauseLocked, quiet, "07:00", away, recording ?? [], apps ?? []);

    t.Equal("", Rules.PauseReason(Inputs()), "nothing set: no pause");
    t.Equal("", Rules.PauseReason(Inputs(enabled: false, snooze: "14:30", away: true)), "keep-awake off isn't a pause");
    t.Equal("snoozed until 14:30", Rules.PauseReason(Inputs(snooze: "14:30", locked: true, pauseLocked: true)), "snooze comes first");
    t.Equal("", Rules.PauseReason(Inputs(locked: true)), "locked only pauses when that option is on");
    t.Equal("PC locked", Rules.PauseReason(Inputs(locked: true, pauseLocked: true, quiet: true)), "locked before quiet hours");
    t.Equal("quiet hours until 07:00", Rules.PauseReason(Inputs(quiet: true, away: true)), "quiet hours before away");
    t.Equal("you're away", Rules.PauseReason(Inputs(away: true, recording: ["Discord"])), "away before recording");
    t.Equal("recording in Discord", Rules.PauseReason(Inputs(recording: ["Discord"], apps: ["Live"])), "recording before apps");
    t.Equal("recording in OBS and Discord", Rules.PauseReason(Inputs(recording: ["OBS", "Discord"])), "two recording apps");
    t.Equal("Live is open", Rules.PauseReason(Inputs(apps: ["Live"])), "one hold app");
    t.Equal("Live and Reaper are open", Rules.PauseReason(Inputs(apps: ["Live", "Reaper"])), "two hold apps");
    t.Equal("Live and 2 more are open", Rules.PauseReason(Inputs(apps: ["Live", "Reaper", "Cubase"])), "three hold apps");
    t.Equal("finish setup", Rules.PauseReason(Inputs(snooze: "14:30") with { SetupNeeded = true }), "setup comes before every other pause");
    t.Equal("", Rules.PauseReason(Inputs(enabled: false) with { SetupNeeded = true }), "off stays off, even before setup");

    t.Check(Rules.IsAway(At(60), TimeSpan.FromMinutes(31), null, 30), "away after 30 min without input or audio");
    t.Check(!Rules.IsAway(At(60), TimeSpan.FromMinutes(29), null, 30), "not away before the away time");
    t.Check(!Rules.IsAway(At(60), TimeSpan.FromMinutes(45), At(50), 30), "recent audio means you're not away");
    t.Check(Rules.IsAway(At(60), TimeSpan.FromMinutes(45), At(20), 30), "old audio doesn't keep you present");

    t.Equal(At(5), Rules.SubTimerFrom(At(5), At(3)), "sub timer: later of burst and audio (burst)");
    t.Equal(At(7), Rules.SubTimerFrom(At(5), At(7)), "sub timer: later of burst and audio (audio)");
    t.Equal(At(5), Rules.SubTimerFrom(At(5), null), "sub timer: burst only");
    t.Equal(null, Rules.SubTimerFrom(null, null), "sub timer: nothing yet");
}

// --- Settings ----------------------------------------------------------------------------------
t.Group("Settings");
{
    Settings Parse(string json) { var s = JsonSerializer.Deserialize<Settings>(json, Json.Options)!; s.Normalize(); return s; }

    var s = Parse("""{"toneLow":false,"tone20":false,"tone20k":false,"levelDb":-80,"signalThresholdDb":5,"intervalMinutes":9,"durationSeconds":7,"awayMinutes":45}""");
    t.Check(s.ToneLow, "a burst always keeps at least one tone");
    t.Equal(-60.0, s.LevelDb, "volume clamps to -60");
    t.Equal(-10.0, s.SignalThresholdDb, "threshold clamps to -10");
    t.Equal(8.0, s.IntervalMinutes, "interval snaps to an allowed value");
    t.Equal(2.0, s.DurationSeconds, "duration snaps to an allowed value");
    t.Equal(30.0, s.AwayMinutes, "away time snaps to an allowed value");
    t.Equal(-6.0, Parse("""{"levelDb":-1}""").LevelDb, "volume clamps to -6");

    var defaults = new Settings();
    t.Equal(-50.0, defaults.SignalThresholdDb, "audio counts from -50 dBFS by default");
    t.Check(defaults.SkipWhileAudio && defaults.HoldWhileRecording && defaults.SleepWhenAway, "listening options are on by default");
    t.Equal(null, defaults.DeviceId, "no output ID until one is found");
    t.Equal("", defaults.DeviceName, "no output is assumed: setup picks one");
    t.Check(!defaults.SetupDone, "a new install starts with setup");
    t.Equal(-40.0, defaults.LevelDb, "new installs start fairly quiet");
    t.Equal(15.0, defaults.SubStandbyMinutes, "standby time defaults to 15 min");
    t.Equal(35.0, defaults.LowToneHz, "low tone defaults to 35 Hz");
    t.Check(defaults.CheckForUpdates, "update check is on unless Setup or the window turns it off");
    t.Equal(null, defaults.WatchOutputIds, "watched outputs are automatic by default");

    // Standby time and the interval choices under it.
    foreach (var standby in Settings.StandbyOptions)
        t.Check(Settings.IntervalsFor(standby).All(i => i > 0 && i < standby), $"every interval stays under a {standby}-minute standby");
    t.Equal("5,8,10,12,14", string.Join(",", Settings.IntervalsFor(15)), "interval choices for a 15-minute sub");
    var longSleep = Parse("""{"subStandbyMinutes":60,"intervalMinutes":10}""");
    t.Equal(15.0, longSleep.IntervalMinutes, "interval snaps into the new standby's choices");
    var shortSleep = Parse("""{"subStandbyMinutes":6,"intervalMinutes":14}""");
    t.Equal(5.0, shortSleep.SubStandbyMinutes, "standby snaps to an allowed value");
    t.Equal(4.0, shortSleep.IntervalMinutes, "an interval never reaches the standby time");
    using var standbyChange = JsonDocument.Parse("""{"subStandbyMinutes":10}""");
    t.Equal(9.0, defaults.With(standbyChange.RootElement).IntervalMinutes, "changing standby moves the interval under it");
    t.Equal(40.0, Parse("""{"lowToneHz":41}""").LowToneHz, "low tone snaps to an allowed frequency");
    t.Equal("a|b", string.Join("|", Parse("""{"watchOutputIds":["a","","a","b"]}""").WatchOutputIds!), "watched outputs: no blanks or duplicates");

    // The installer's update-check checkbox.
    var install = new Settings();
    t.Check(install.ApplyInstallerChoice(0) && !install.CheckForUpdates, "Setup can turn the update check off");
    install.CheckForUpdates = true;
    t.Check(!install.ApplyInstallerChoice(0) && install.CheckForUpdates, "the same Setup choice doesn't undo a change made in the window");
    t.Check(install.ApplyInstallerChoice(1) && install.CheckForUpdates, "a new Setup choice applies");
    t.Check(!install.ApplyInstallerChoice(null), "no Setup choice: nothing changes");

    var apps = Parse("""{"holdApps":["C:\\Apps\\Ableton Live 12 Suite.exe","ableton live 12 suite"," Reaper ",""]}""");
    t.Equal("Ableton Live 12 Suite|Reaper", string.Join("|", apps.HoldApps), "hold apps: no folder, no .exe, no duplicates or blanks");

    var times = Parse("""{"quietStart":"25:00","quietEnd":"7"}""");
    t.Equal("00:00|07:00", times.QuietStart + "|" + times.QuietEnd, "bad quiet-hour times reset to defaults");
    t.Equal(null, Parse("""{"windowBounds":[1,2,3]}""").WindowBounds, "bad window bounds are dropped");
    t.Equal(-6.0, Parse("""{"calibratedWakeDb":-2}""").CalibratedWakeDb, "calibration result is clamped too");

    using var change = JsonDocument.Parse("""{"intervalMinutes":12,"deviceId":"{0.0.0.00000000}.{abc}","noSuchKey":1}""");
    var changed = defaults.With(change.RootElement);
    t.Equal(12.0, changed.IntervalMinutes, "window changes apply");
    t.Equal("{0.0.0.00000000}.{abc}", changed.DeviceId, "output ID is a setting");
    t.Equal(10.0, defaults.IntervalMinutes, "With() returns a copy");

    var quiet = Parse("""{"quietHours":true,"quietStart":"22:00","quietEnd":"07:00"}""");
    t.Check(quiet.InQuietHours(new DateTime(2026, 1, 1, 23, 0, 0)), "quiet hours across midnight: 23:00");
    t.Check(quiet.InQuietHours(new DateTime(2026, 1, 1, 6, 59, 0)), "quiet hours across midnight: 06:59");
    t.Check(!quiet.InQuietHours(new DateTime(2026, 1, 1, 7, 0, 0)), "quiet hours end at 07:00");
    t.Check(!quiet.InQuietHours(new DateTime(2026, 1, 1, 12, 0, 0)), "not quiet at noon");
    t.Check(!Parse("""{"quietHours":true,"quietStart":"09:00","quietEnd":"09:00"}""").InQuietHours(new DateTime(2026, 1, 1, 9, 0, 0)), "same start and end: never quiet");

    t.Equal("35 Hz at -40 dBFS for 1.5 s", Settings.Describe(defaults.Tones(), 1.5), "log text for one tone");
    t.Equal("50 Hz at -30 dBFS for 1 s", Settings.Describe(Parse("""{"lowToneHz":50,"levelDb":-30}""").Tones(), 1), "log text for another frequency");
    var all = Parse("""{"toneLow":true,"tone20":true,"tone20k":true,"levelDb":-6}""");
    t.Equal("35 Hz at -6 dBFS + 20 Hz at -6 dBFS + 20 kHz at -40 dBFS for 2 s", Settings.Describe(all.Tones(), 2), "log text for a mix");
    t.Equal(-40.0, all.Tones().Single(x => x.Frequency == 20000).LevelDb, "20 kHz stays at -40 dBFS");
    var highOnly = Parse("""{"toneLow":false,"tone20k":true,"lowToneHz":40}""");
    t.Equal("40", string.Join(",", highOnly.Tones(includeHigh: false).Select(x => x.Frequency)), "calibration with only 20 kHz on uses the low tone's frequency");
}

// --- Burst sound -------------------------------------------------------------------------------
t.Group("Burst sound");
{
    double Peak(float[] samples) => samples.Max(x => Math.Abs((double)x));
    double target = Math.Pow(10, -6 / 20.0);

    var one = AudioOut.MakeBurst(2, [new Tone(35, -6)], 48000);
    t.Equal(96000, one.Length, "2 s at 48 kHz");
    t.Near(target, Peak(one), 0.002, "one tone peaks at its level");
    t.Near(0, one[0], 1e-6, "fades in from silence");
    t.Near(0, one[^1], 1e-3, "fades out to silence");

    var two = AudioOut.MakeBurst(2, [new Tone(35, -6), new Tone(20, -6)], 48000);
    t.Check(Peak(two) > 0.97, $"two low tones each keep their own level, as in 1.0 (peak {20 * Math.Log10(Peak(two)):0.00} dBFS)");
    t.Check(Peak(two) <= AudioOut.MaxPeak + 1e-6, "but the sum stays under 0 dBFS");
    var quiet2 = AudioOut.MakeBurst(2, [new Tone(35, -30), new Tone(20, -30)], 48000);
    t.Check(Peak(quiet2) > Math.Pow(10, -30 / 20.0) * 1.9, "quiet tones add up unscaled");

    var mix = AudioOut.MakeBurst(2, [new Tone(35, -6), new Tone(20, -6), new Tone(20000, -40)], 44100);
    t.Equal(88200, mix.Length, "follows the output's sample rate");
    t.Check(Peak(mix) <= AudioOut.MaxPeak + 1e-6, "the full mix stays under 0 dBFS");
    t.Check(mix.All(x => Math.Abs(x) < 0.99f), "never clips");
}

// --- Devices -----------------------------------------------------------------------------------
t.Group("Devices");
{
    List<AudioDevice> list = [new("id-12", "Out 1/2 (CONNECT 6)"), new("id-78", "Out 7/8 (CONNECT 6)"), new("id-rt", "Speakers (Realtek(R) Audio)")];
    t.Equal("id-12", Devices.Resolve(list, "id-12", "Old name")?.Id, "found by ID even after a rename");
    t.Equal("id-78", Devices.Resolve(list, null, "Out 7/8 (CONNECT 6)")?.Id, "found by exact name when there's no ID");
    t.Equal("id-12", Devices.Resolve(list, "gone", "Out 1/2 (CONNECT 6)")?.Id, "falls back to the name when the ID is gone");
    t.Equal("id-rt", Devices.Resolve(list, null, "Speakers (Realtek(R) Au")?.Id, "a name cut to 31 characters still matches");
    t.Equal(null, Devices.Resolve(list, "gone", "Out 9/10 (CONNECT 6)")?.Id, "missing output");
    t.Equal(null, Devices.Resolve(list, null, " ")?.Id, "blank name matches nothing");

    t.Equal("(CONNECT 6", Devices.GroupKey("Out 1/2 (CONNECT 6)"), "group key");
    t.Equal("(Realtek(R) Audio", Devices.GroupKey("Speakers (Realtek(R) Audio)"), "group key with brackets inside the device name");
    t.Check(Devices.InGroup("Headphones (Realtek(R) Audio)", "Speakers (Realtek(R) Audio)"), "same device, other output");
    t.Check(!Devices.InGroup("Out 1/2 (CONNECT 6)", ""), "no output chosen: nothing is in its group");

    var burstOut = list[0];
    t.Check(Devices.Watched(list[1], burstOut, burstOut.Name, null), "automatic: other outputs on the device are watched");
    t.Check(!Devices.Watched(list[2], burstOut, burstOut.Name, null), "automatic: other devices aren't");
    t.Check(Devices.Watched(burstOut, burstOut, burstOut.Name, []), "the burst output is always watched");
    t.Check(!Devices.Watched(list[1], burstOut, burstOut.Name, []), "chosen list: unchosen outputs aren't watched");
    t.Check(Devices.Watched(list[2], burstOut, burstOut.Name, ["id-rt"]), "chosen list: a chosen output on another device is");
    t.Check(Devices.InGroup("Out 7/8 (CONNECT 6)", "Out 1/2 (CONNECT 6)"), "outputs on the same interface are watched");
    t.Check(!Devices.InGroup("Speakers (Realtek(R) Audio)", "Out 1/2 (CONNECT 6)"), "other interfaces aren't");
    t.Check(Devices.InGroup("Monitors", "Monitors"), "a name without brackets matches itself");

    t.Equal(PlaybackProblem.Busy, AudioOut.Problem(unchecked((int)0x8889000A), "Out", null).Problem, "exclusive mode means busy");
    t.Equal(PlaybackProblem.Missing, AudioOut.Problem(unchecked((int)0x88890004), "Out", null).Problem, "an unplugged output is missing");
    t.Equal(PlaybackProblem.Failed, AudioOut.Problem(unchecked((int)0x80004005), "Out", null).Problem, "anything else is a playback problem");
    t.Equal("Output busy", PlaybackException.StatusText(PlaybackProblem.Busy), "status names the real reason");

    t.Equal(-100.0, AudioMonitor.ToDb(0), "silence");
    t.Near(-20, AudioMonitor.ToDb(0.1f), 1e-4, "0.1 is -20 dBFS");
}

// --- Activity history --------------------------------------------------------------------------
t.Group("Activity");
{
    var now = new DateTime(2026, 11, 6, 10, 0, 0);
    string L(int hour, int minute, string message) => $"{now.Date.AddHours(hour).AddMinutes(minute):yyyy-MM-dd HH:mm:ss}  {message}";
    string[] lines =
    [
        $"{now.AddDays(-2):yyyy-MM-dd HH:mm:ss}  Played 35 Hz at -30 dBFS for 1.5 s on 'Out' (scheduled).",
        "garbage line",
        L(1, 0, "Played 35 Hz at -30 dBFS for 1.5 s on 'Out' (scheduled)."),
        L(1, 10, "Audio detected on Out 7/8 (CONNECT 6)."),
        L(2, 10, "Audio stopped."),
        L(3, 0, "Paused: you're away."),
        L(4, 0, "Resumed."),
        L(9, 0, "Audio detected on Out 7/8 (CONNECT 6)."),
        L(9, 30, "Stopped."),
        L(9, 31, "Started v1.1.0: x"),
        L(9, 40, "Played 35 Hz at -30 dBFS for 1.5 s on 'Out' (manual)."),
    ];
    var a = Activity.FromLog(lines, now, TimeSpan.FromSeconds(15));
    t.Equal(2, a.BurstsToday(now), "bursts today from the log (old ones skipped)");
    t.Equal(90, (int)Math.Round(a.MusicToday(now).TotalMinutes), "music today: 60 min + 30 min");
    var json = JsonSerializer.Serialize(a.ToJson(now), Json.Options);
    using var doc = JsonDocument.Parse(json);
    t.Equal(1, doc.RootElement.GetProperty("bursts").GetArrayLength(), "timeline shows the last 6 hours only");
    t.Equal(1, doc.RootElement.GetProperty("music").GetArrayLength(), "music span in the last 6 hours");

    a.MusicStarted(now);
    a.MusicStarted(now.AddMinutes(1));
    t.Equal(91, (int)Math.Round(a.MusicToday(now.AddMinutes(1)).TotalMinutes), "an open span runs to now, opened once");
    a.Prune(now.AddDays(3));
    t.Equal(0, a.Bursts.Count, "old history is pruned");
}

// --- Update check ------------------------------------------------------------------------------
t.Group("Updates");
{
    t.Equal(new Version(2, 1, 0), Updates.ParseVersion("v2.1.0"), "tag with v");
    t.Equal(new Version(2, 1, 0), Updates.ParseVersion("2.1"), "short version");
    t.Equal(new Version(2, 0, 1), Updates.ParseVersion("v2.0.1-beta"), "pre-release suffix is ignored");
    t.Equal(null, Updates.ParseVersion("latest"), "not a version");
    t.Check(Updates.IsNewer("v2.0.1", "2.0.0"), "a newer patch is newer");
    t.Check(!Updates.IsNewer("v2.0.0", "2.0.0"), "the same version isn't");
    t.Check(!Updates.IsNewer("v1.9.9", "2.0.0"), "an older one isn't");
    t.Check(!Updates.IsNewer("nonsense", "2.0.0"), "a bad tag isn't");
    var now = new DateTime(2026, 11, 6, 12, 0, 0);
    t.Check(Updates.Due(null, now), "never checked: due");
    t.Check(!Updates.Due(now.AddHours(-23), now), "checked today: not due");
    t.Check(Updates.Due(now.AddHours(-24), now), "a day later: due");
    t.Check(Updates.Due(now.AddHours(2), now), "clock went back: due");
    t.Equal("https://github.com/me/SubWake/releases/tag/v2.1.0", Updates.SafeUrl("https://github.com/me/SubWake/releases/tag/v2.1.0", "me/SubWake"), "release page opens");
    t.Equal("https://github.com/me/SubWake/releases/latest", Updates.SafeUrl("https://evil.example/x", "me/SubWake"), "other sites never open");
    t.Check(Updates.Repo.Contains('/'), "the update check knows its GitHub repository");
}

// --- Clock -------------------------------------------------------------------------------------
t.Group("Clock");
{
    long wall = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    t.Check(Math.Abs(Clock.ToWallMs(Clock.Now) - wall) < 200, "schedule times convert to wall-clock time for the window");
    t.Check(Clock.Now.Kind == DateTimeKind.Utc, "the schedule clock doesn't use local time");
}

if (args.Contains("--record-briefly")) { RecordBriefly(); return 0; }
if (args.Length == 4 && args[0] == "--tone")
{
    // --tone "<output name>" <dBFS> <seconds>: a 35 Hz test tone from another process, which the app's monitor should hear.
    var played = AudioOut.Play(null, args[1], rate => AudioOut.MakeBurst(double.Parse(args[3]), [new Tone(35, double.Parse(args[2]))], rate));
    Console.WriteLine($"Played on {played.Name}");
    return 0;
}
if (args.Contains("--hardware")) Hardware();
return t.Finish();

static void Hardware()
{
    Console.WriteLine();
    Console.WriteLine("Hardware check (not a test):");
    foreach (var d in CoreAudio.List(CoreAudio.Render)) Console.WriteLine($"  output  {d.Name}  [{d.Id}]");
    foreach (var d in CoreAudio.List(CoreAudio.Capture)) Console.WriteLine($"  input   {d.Name}  [{d.Id}]");
    var output = CoreAudio.List(CoreAudio.Render).FirstOrDefault(d => d.Id == CoreAudio.DefaultOutputId());
    Console.WriteLine($"  Windows default output: {output?.Name ?? "(none)"}");
    using var monitor = new AudioMonitor(output?.Id, output?.Name ?? "", null, -50, watchCapture: true);
    Thread.Sleep(3000);
    var state = monitor.State();
    Console.WriteLine($"  target: {state.Target?.Name ?? "(not found)"}; watching: {string.Join(", ", state.Watching)}");
    Console.WriteLine($"  device notifications: {(monitor.Notified ? "on" : "off, re-scanning every 5 s")}");
    Console.WriteLine($"  keyboard/mouse idle: {UserInput.Idle().TotalSeconds:0} s");
    Console.WriteLine($"  levels: {string.Join(", ", monitor.TakeLevels().Select(l => $"{l.Key} {l.Value:0} dBFS"))}");
    Console.WriteLine($"  recording before: {(state.Recording.Count > 0 ? string.Join(", ", state.Recording) : "nothing")}; error: {state.Error ?? "none"}");

    // A second copy of this program opens an input for a few seconds; the monitor should name it.
    using var recorder = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--record-briefly") { UseShellExecute = false })!;
    Thread.Sleep(3000);
    string during = string.Join(", ", monitor.State().Recording);
    Console.WriteLine($"  recording during test: {(during.Length > 0 ? during : "nothing (FAIL)")}");
    recorder.WaitForExit(10000);
    Thread.Sleep(1500);
    string after = string.Join(", ", monitor.State().Recording);
    Console.WriteLine($"  recording after: {(after.Length > 0 ? after : "nothing")}");
}

// Opens the first input in shared mode for 5 seconds without reading it: enough for Windows to report a recording session.
static void RecordBriefly()
{
    var enumerator = CoreAudio.CreateEnumerator();
    var inputs = CoreAudio.Endpoints(enumerator, CoreAudio.Capture);
    var client = CoreAudio.Activate<IAudioClient>(inputs[0].Device);
    CoreAudio.Check(client.GetMixFormat(out IntPtr format));
    CoreAudio.Check(client.Initialize(0, 0, 2_000_000, 0, format, IntPtr.Zero));
    CoreAudio.Check(client.Start());
    Thread.Sleep(5000);
    client.Stop();
    System.Runtime.InteropServices.Marshal.FreeCoTaskMem(format);
}

sealed class TestRun
{
    int passed, failed;
    string group = "";

    public void Group(string name) => group = name;

    public void Check(bool ok, string what)
    {
        if (ok) { passed++; return; }
        failed++;
        Console.WriteLine($"FAIL  {group}: {what}");
    }

    public void Equal<T>(T expected, T actual, string what) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{what} (expected {expected?.ToString() ?? "null"}, got {actual?.ToString() ?? "null"})");

    public void Near(double expected, double actual, double tolerance, string what) =>
        Check(Math.Abs(expected - actual) <= tolerance, $"{what} (expected {expected}, got {actual})");

    public int Finish()
    {
        Console.WriteLine(failed == 0 ? $"All {passed} tests passed." : $"{failed} of {passed + failed} tests failed.");
        return failed == 0 ? 0 : 1;
    }
}
