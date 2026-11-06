using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace SubWake;

enum CalibrationPhase { None, Waiting, Stepping, Done }

/// <summary>
/// The tray app: owns the burst schedule, the tray icon and (while open) the window.
/// Everything runs on the UI thread from a one-second timer; only playback and the audio monitor run on other threads.
/// Schedule times use <see cref="Clock"/>; quiet hours, the log and the activity history use local time.
/// </summary>
sealed class SubWakeApp : ApplicationContext
{
    const double CalibrationStartDb = -60, CalibrationStepDb = 3, CalibrationMaxDb = Settings.MaxLevelDb, CalibrationMarginDb = 6;
    const int CalibrationGapSeconds = 12;   // The sub can take several seconds to wake.
    static readonly TimeSpan AudioActiveWindow = TimeSpan.FromSeconds(3);    // "Playing" in the window.
    static readonly TimeSpan AudioStoppedAfter = TimeSpan.FromSeconds(15);   // Silence this long logs "Audio stopped" (track gaps don't).
    static readonly (double Minutes, string Label)[] SnoozeChoices = [(30, "30 minutes"), (60, "1 hour"), (120, "2 hours")];

    Settings settings = Settings.Load();
    readonly AudioMonitor monitor;
    readonly Scheduler schedule = new(Clock.Now);
    readonly Activity activity = Activity.Load(AudioStoppedAfter);
    MonitorState audio = MonitorState.Empty;
    bool audioPlaying, away;
    IReadOnlyList<string> openHoldApps = [];
    DateTime? snoozeUntil;

    readonly NotifyIcon tray;
    readonly ToolStripMenuItem enabledItem, resumeItem;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer levelTimer = new() { Interval = 250 };   // Level bars; runs only while the window is open.
    readonly EventWaitHandle showSignal = new(false, EventResetMode.AutoReset, AppIds.ShowEvent);
    readonly EventWaitHandle stopSignal = new(false, EventResetMode.AutoReset, AppIds.StopEvent);
    readonly string version = Application.ProductVersion.Split('+')[0];
    MainWindow? window;
    string? defaultOutputId;   // Suggested in first-run setup.

    UpdateInfo? update;
    bool checkingUpdate;
    string updateStatus = "";

    string lastBurstText = "";
    string? lastError;
    PlaybackProblem? problem;
    bool playing, exiting, quitRequested, deviceMissingNotified;
    string lastPause = "";
    int ticks;

    // Set from SystemEvents threads; handled on the next timer tick.
    volatile bool locked;
    long wakeBurstAtTicks;

    CalibrationPhase calibration;
    DateTime calibrationWaitUntil, calibrationNextStepAt, calibrationQuietSince;
    double calibrationNextLevel;
    double? calibrationLastPlayed, calibrationSuggested;
    string calibrationMessage = "", calibrationNote = "";

    TimeSpan StandbyWait => TimeSpan.FromMinutes(settings.SubStandbyMinutes + 1);   // Long enough for the sub to fall asleep.

    public SubWakeApp(bool showWindow)
    {
        enabledItem = new ToolStripMenuItem("Keep subwoofer awake", null, (_, _) => SetEnabled(!settings.Enabled));
        resumeItem = new ToolStripMenuItem("Resume now", null, (_, _) => Resume()) { Enabled = false };
        var pauseMenu = new ToolStripMenuItem("Pause for");
        foreach (var (minutes, label) in SnoozeChoices) pauseMenu.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => Snooze(minutes)));
        pauseMenu.DropDownItems.Add(new ToolStripSeparator());
        pauseMenu.DropDownItems.Add(resumeItem);
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open SubWake", null, (_, _) => ShowWindow()) { Font = new Font(menu.Font, FontStyle.Bold) };
        menu.Items.AddRange([
            open,
            new ToolStripSeparator(),
            enabledItem,
            new ToolStripMenuItem("Play burst now", null, (_, _) => PlayNow()),
            pauseMenu,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        ]);
        tray = new NotifyIcon { Icon = AppIcon.Active, Text = "SubWake", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
        tray.BalloonTipClicked += (_, _) => ShowWindow();

        SystemEvents.SessionSwitch += SessionSwitched;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        monitor = new AudioMonitor(settings.DeviceId, settings.DeviceName, settings.WatchOutputIds, settings.SignalThresholdDb, settings.HoldWhileRecording);
        openHoldApps = OpenHoldApps();

        Log.Write($"Started v{version}: {Settings.Describe(settings.Tones(), settings.DurationSeconds)} every {settings.IntervalMinutes} min on '{settings.DeviceName}'."
                  + (settings.Enabled ? "" : " Keep-awake is off.") + (settings.SetupDone ? "" : " Waiting for first-run setup."));

        timer.Tick += (_, _) => Tick();
        levelTimer.Tick += (_, _) => SendLevels();
        timer.Start();
        UpdateTray(Clock.Now);
        if (showWindow || !settings.SetupDone) ShowWindow();
    }

    // --- Schedule ------------------------------------------------------------------------------

    bool Calibrating => calibration is CalibrationPhase.Waiting or CalibrationPhase.Stepping;

    void Tick()
    {
        try
        {
            if (stopSignal.WaitOne(0) || quitRequested) { ExitThread(); return; }
            if (showSignal.WaitOne(0)) ShowWindow();
            var now = Clock.Now;
            var wall = DateTime.Now;
            audio = monitor.State();
            RememberOutput();
            if (ticks % 5 == 0) openHoldApps = OpenHoldApps();

            long wakeTicks = Interlocked.Exchange(ref wakeBurstAtTicks, 0);
            if (wakeTicks != 0 && settings.Enabled && settings.BurstAfterWake && !Calibrating)
                schedule.WakeAt(new DateTime(wakeTicks, DateTimeKind.Utc));

            // Away: no input and no audio for a while. Coming back counts as a wake.
            bool awayNow = settings.Enabled && settings.SleepWhenAway && Rules.IsAway(now, UserInput.Idle(), audio.LastAny, settings.AwayMinutes);
            if (away && !awayNow && settings.BurstAfterWake && !Calibrating) schedule.WakeAt(now);
            away = awayNow;

            // Audio on any watched output keeps the sub awake by itself: the next burst waits a full interval after it.
            if (settings.SkipWhileAudio && !Calibrating && audio.LastSignal is DateTime heard)
                schedule.HoldForAudio(heard, settings.IntervalMinutes);
            bool audioNow = audio.LastSignal is DateTime last && now - last < (audioPlaying ? AudioStoppedAfter : AudioActiveWindow);
            if (audioNow != audioPlaying)
            {
                audioPlaying = audioNow;
                if (audioNow)
                {
                    var active = monitor.ActiveWithin(AudioActiveWindow);
                    Log.Write($"Audio detected on {(active.Count > 0 ? string.Join(", ", active) : "a watched output")}.");
                    activity.MusicStarted(wall);
                }
                else
                {
                    Log.Write("Audio stopped.");
                    activity.MusicStopped(audio.LastSignal is DateTime l ? Clock.ToWall(l) : wall);
                }
                Push();
            }

            if (snoozeUntil is DateTime until && now >= until) { snoozeUntil = null; Log.Write("Snooze ended."); }

            string pause = PauseReason();
            if (pause != lastPause)
            {
                Log.Write(pause != "" ? $"Paused: {pause}." : "Resumed.");
                if (pause != "") activity.PauseStarted(wall); else activity.PauseEnded(wall);
                lastPause = pause;
                Push();
            }

            if (Calibrating) CalibrationTick(now);
            else if (settings.Enabled && pause == "" && !playing && schedule.Due(now)) _ = Burst("scheduled");

            if (settings.CheckForUpdates && !checkingUpdate && ticks >= 60 && Updates.Due(settings.LastUpdateCheck, wall))
                _ = CheckForUpdates(manual: false);

            UpdateTray(now);
            if (++ticks % 2 == 0) Push();   // Keeps the output, audio and timer status in the window current.
            if (ticks % 600 == 0) activity.Prune(wall);
        }
        catch (Exception ex) { Log.Write("Unexpected error: " + ex.Message); }
    }

    string PauseReason() => Rules.PauseReason(new PauseInputs(
        settings.Enabled,
        snoozeUntil is DateTime s ? Clock.ToWall(s).ToString("HH:mm") : null,
        locked,
        settings.PauseWhenLocked,
        settings.InQuietHours(DateTime.Now),
        settings.QuietEnd,
        away,
        settings.HoldWhileRecording ? audio.Recording : [],
        openHoldApps,
        SetupNeeded: !settings.SetupDone));

    IReadOnlyList<string> OpenHoldApps()
    {
        var open = new List<string>();
        foreach (var name in settings.HoldApps)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0) open.Add(name);
                foreach (var p in processes) p.Dispose();
            }
            catch { }
        }
        return open;
    }

    /// <summary>Keeps the saved output's ID and name current, so renames and older name-only settings keep working.</summary>
    void RememberOutput()
    {
        if (!audio.Ready || audio.Target is not AudioDevice target) return;
        if (target.Id == settings.DeviceId && target.Name == settings.DeviceName) return;
        bool newId = target.Id != settings.DeviceId;
        settings.DeviceId = target.Id;
        settings.DeviceName = target.Name;
        try { settings.Save(); } catch (Exception ex) { Log.Write("Settings could not be saved: " + ex.Message); }
        Log.Write(newId ? $"Output remembered by its Windows ID: '{target.Name}'." : $"Output renamed to '{target.Name}'.");
        monitor.SetTarget(target.Id, target.Name);
    }

    async Task Burst(string reason, double? levelDb = null, bool includeHighTone = true)
    {
        playing = true;
        Push();
        var current = settings;
        var tones = current.Tones(levelDb, includeHighTone);
        string description = Settings.Describe(tones, current.DurationSeconds);
        monitor.OwnPlaybackStarting();
        try
        {
            var device = await Task.Run(() => AudioOut.Play(current.DeviceId, current.DeviceName,
                rate => AudioOut.MakeBurst(current.DurationSeconds, tones, rate)));
            schedule.Played(Clock.Now, current.IntervalMinutes, reschedule: !Calibrating);
            activity.Burst(DateTime.Now);
            lastBurstText = description;
            lastError = null;
            problem = null;
            deviceMissingNotified = false;
            Log.Write($"Played {description} on '{device.Name}' ({reason}).");
        }
        catch (Exception ex)
        {
            var failure = ex as PlaybackException ?? new PlaybackException(PlaybackProblem.Failed, "Playback failed: " + ex.Message, ex);
            if (failure.Message != lastError) Log.Write("Skipped: " + failure.Message);   // Log each distinct problem once.
            lastError = failure.Message;
            problem = failure.Problem;
            if (!Calibrating) schedule.Failed(Clock.Now);
            if (failure.Problem == PlaybackProblem.Missing && current.NotifyDeviceMissing && !deviceMissingNotified && !exiting)
            {
                deviceMissingNotified = true;
                tray.ShowBalloonTip(6000, "SubWake", $"'{current.DeviceName}' isn't connected. Bursts resume when it's back.", ToolTipIcon.Warning);
            }
        }
        finally
        {
            monitor.OwnPlaybackEnded();
            playing = false;
            Push();
        }
    }

    void PlayNow() { if (!playing && !Calibrating) _ = Burst("manual"); }

    void SetEnabled(bool enabled) => Apply(new { enabled });

    void Apply(object changes)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(changes, Json.Options));
        ApplySettings(doc.RootElement);
    }

    void Snooze(double minutes)
    {
        snoozeUntil = Clock.Now.AddMinutes(minutes);
        Log.Write($"Snoozed for {SnoozeChoices.First(c => c.Minutes == minutes).Label}, until {Clock.ToWall(snoozeUntil.Value):HH:mm}.");
        Tick();
    }

    void Resume()
    {
        if (snoozeUntil == null) return;
        snoozeUntil = null;
        Log.Write("Snooze ended.");
        Tick();
    }

    void ApplySettings(JsonElement changes)
    {
        var old = settings;
        settings = settings.With(changes);
        try { settings.Save(); } catch (Exception ex) { Log.Write("Settings could not be saved: " + ex.Message); }
        var now = Clock.Now;

        if (!old.SetupDone && settings.SetupDone)
        {
            Log.Write($"Setup finished: '{settings.DeviceName}', sub sleeps after {settings.SubStandbyMinutes} min, {settings.LevelDb:0} dBFS.");
            schedule.BurstNow(now);
        }
        if (old.Enabled != settings.Enabled)
        {
            Log.Write(settings.Enabled ? "Keep-awake turned on." : "Keep-awake turned off.");
            if (settings.Enabled) schedule.BurstNow(now);
        }
        bool deviceChanged = old.DeviceId != settings.DeviceId || old.DeviceName != settings.DeviceName;
        bool burstChanged = old.ToneLow != settings.ToneLow || old.LowToneHz != settings.LowToneHz || old.Tone20 != settings.Tone20 || old.Tone20k != settings.Tone20k
                            || old.LevelDb != settings.LevelDb || old.DurationSeconds != settings.DurationSeconds;
        if (old.SubStandbyMinutes != settings.SubStandbyMinutes) Log.Write($"The sub sleeps after {settings.SubStandbyMinutes} min.");
        if (burstChanged || old.IntervalMinutes != settings.IntervalMinutes || deviceChanged)
            Log.Write($"Settings: {Settings.Describe(settings.Tones(), settings.DurationSeconds)} every {settings.IntervalMinutes} min on '{settings.DeviceName}'.");
        if (old.IntervalMinutes != settings.IntervalMinutes || (old.SkipWhileAudio && !settings.SkipWhileAudio))
            schedule.Restart(now, settings.IntervalMinutes);   // Also drops any wait-for-silence delay when that option is turned off.
        if (deviceChanged)
        {
            lastError = null;
            problem = null;
            deviceMissingNotified = false;
            schedule.BurstNow(now);   // Check the new output straight away.
            monitor.SetTarget(settings.DeviceId, settings.DeviceName);
        }
        if (!SameList(old.WatchOutputIds, settings.WatchOutputIds))
        {
            monitor.SetWatch(settings.WatchOutputIds);
            Log.Write(settings.WatchOutputIds == null ? "Watching every output on the same device." : $"Watching {settings.WatchOutputIds.Count + 1} chosen outputs.");
        }
        if (old.CheckForUpdates != settings.CheckForUpdates) Log.Write("Update check " + OnOff(settings.CheckForUpdates) + ".");
        if (old.SignalThresholdDb != settings.SignalThresholdDb)
        {
            monitor.SetThreshold(settings.SignalThresholdDb);
            Log.Write($"Audio counts from {settings.SignalThresholdDb:0} dBFS.");
        }
        if (old.HoldWhileRecording != settings.HoldWhileRecording) monitor.SetWatchCapture(settings.HoldWhileRecording);
        if (!old.HoldApps.SequenceEqual(settings.HoldApps))
        {
            openHoldApps = OpenHoldApps();
            Log.Write("Hold while open: " + (settings.HoldApps.Count > 0 ? string.Join(", ", settings.HoldApps) : "no apps") + ".");
        }
        if (old.PauseWhenLocked != settings.PauseWhenLocked || old.QuietHours != settings.QuietHours
            || old.QuietStart != settings.QuietStart || old.QuietEnd != settings.QuietEnd || old.BurstAfterWake != settings.BurstAfterWake
            || old.NotifyDeviceMissing != settings.NotifyDeviceMissing || old.SkipWhileAudio != settings.SkipWhileAudio
            || old.HoldWhileRecording != settings.HoldWhileRecording || old.SleepWhenAway != settings.SleepWhenAway || old.AwayMinutes != settings.AwayMinutes)
            Log.Write($"Options: wait for silence {OnOff(settings.SkipWhileAudio)}, hold while recording {OnOff(settings.HoldWhileRecording)}, "
                      + $"sleep when away {(settings.SleepWhenAway ? $"after {settings.AwayMinutes} min" : "off")}, burst after wake {OnOff(settings.BurstAfterWake)}, "
                      + $"pause when locked {OnOff(settings.PauseWhenLocked)}, quiet hours {(settings.QuietHours ? settings.QuietStart + "-" + settings.QuietEnd : "off")}, "
                      + $"missing-output alert {OnOff(settings.NotifyDeviceMissing)}.");
        UpdateTray(now);
        Push();
    }

    static string OnOff(bool value) => value ? "on" : "off";

    static bool SameList(List<string>? a, List<string>? b) => a == null ? b == null : b != null && a.SequenceEqual(b);

    // --- Update check --------------------------------------------------------------------------

    async Task CheckForUpdates(bool manual)
    {
        if (checkingUpdate) return;
        checkingUpdate = true;
        updateStatus = manual ? "Checking…" : updateStatus;
        Push();
        try
        {
            update = await Updates.LatestAsync(version);
            settings.LastUpdateCheck = DateTime.Now;
            updateStatus = update == null ? (manual ? "You have the latest version." : "") : "";
            if (update != null)
            {
                Log.Write($"Update available: SubWake {update.Version}.");
                if (!manual && settings.UpdateNotifiedVersion != update.Version && !exiting)
                {
                    tray.ShowBalloonTip(8000, $"SubWake {update.Version} is available", "Click to open SubWake and download it.", ToolTipIcon.Info);
                    settings.UpdateNotifiedVersion = update.Version;
                }
            }
        }
        catch (Exception ex)
        {
            // Try again in an hour rather than every tick; log once per kind of failure.
            settings.LastUpdateCheck = DateTime.Now - Updates.Every + Updates.RetryAfterFailure;
            string message = "Update check failed: " + ex.Message;
            if (message != lastUpdateError) Log.Write(message);
            lastUpdateError = message;
            updateStatus = manual ? "Couldn't reach GitHub. Check your connection and try again." : "";
        }
        finally
        {
            checkingUpdate = false;
            try { settings.Save(); } catch (Exception ex) { Log.Write("Settings could not be saved: " + ex.Message); }
            Push();
        }
    }

    string? lastUpdateError;

    // --- Calibration ---------------------------------------------------------------------------

    void Calibrate(string action)
    {
        var now = Clock.Now;
        switch (action)
        {
            case "start":
                calibration = CalibrationPhase.Waiting;
                calibrationWaitUntil = now + StandbyWait;
                calibrationQuietSince = now;
                calibrationLastPlayed = null;
                calibrationSuggested = null;
                calibrationMessage = "";
                calibrationNote = "";
                Log.Write("Calibration started: waiting for the sub to go into standby.");
                break;
            case "skip-wait" when calibration == CalibrationPhase.Waiting:
                StartStepping(now);
                break;
            case "woke" when calibration == CalibrationPhase.Stepping && calibrationLastPlayed is double played:
                // Only a suggestion: the volume changes only if the user clicks "Use".
                calibrationSuggested = Math.Min(played + CalibrationMarginDb, Settings.MaxLevelDb);
                calibration = CalibrationPhase.Done;
                calibrationMessage = $"The sub woke at or just before {played:0} dBFS. Suggested volume, with a {CalibrationMarginDb:0} dB safety margin:";
                Log.Write($"Calibration: sub woke at or just before {played:0} dBFS; suggested {calibrationSuggested:0} dBFS.");
                schedule.After(now, settings.IntervalMinutes);   // The sub has just woken up.
                Apply(new { calibratedWakeDb = played });         // Remembered for the slider marks; changes no volume.
                break;
            case "apply" when calibration == CalibrationPhase.Done && calibrationSuggested is double suggested:
                calibration = CalibrationPhase.None;
                calibrationSuggested = null;
                Apply(new { levelDb = suggested });
                break;
            case "cancel" when Calibrating:
                calibration = CalibrationPhase.None;
                Log.Write("Calibration cancelled.");
                schedule.BurstNow(now);   // Wake the sub back up.
                break;
            case "close":
                if (!Calibrating) { calibration = CalibrationPhase.None; calibrationSuggested = null; }
                break;
        }
        Push();
    }

    void StartStepping(DateTime now)
    {
        calibration = CalibrationPhase.Stepping;
        calibrationNextLevel = CalibrationStartDb;
        calibrationNextStepAt = now;
        calibrationQuietSince = now;
        calibrationNote = "";
    }

    void CalibrationTick(DateTime now)
    {
        // Other sound could keep the sub awake or wake it, so the wait and the test both need silence.
        // The app's own calibration bursts are left out by the monitor.
        if (audio.LastAny is DateTime heard && heard > calibrationQuietSince)
        {
            bool first = calibrationNote == "";
            calibrationNote = calibration == CalibrationPhase.Stepping
                ? "Audio played during the test, so it went back to waiting. Keep everything silent."
                : "Audio played, so the wait started over. Keep everything silent.";
            if (first || calibration == CalibrationPhase.Stepping) Log.Write("Calibration: audio played, waiting for silence again.");
            calibration = CalibrationPhase.Waiting;
            calibrationQuietSince = heard;
            calibrationWaitUntil = heard + StandbyWait;
            calibrationLastPlayed = null;
            Push();
            return;
        }
        if (calibration == CalibrationPhase.Waiting && now >= calibrationWaitUntil) StartStepping(now);
        if (calibration != CalibrationPhase.Stepping || playing || now < calibrationNextStepAt) return;

        if (calibrationNextLevel > CalibrationMaxDb)
        {
            calibration = CalibrationPhase.Done;
            calibrationMessage = $"Reached {CalibrationMaxDb:0} dBFS without the sub waking. Check that this output really feeds the sub and that the sub's volume or gain isn't turned down, then try again.";
            Log.Write("Calibration: no wake-up up to " + CalibrationMaxDb + " dBFS.");
            schedule.BurstNow(now);
            Push();
            return;
        }
        calibrationLastPlayed = calibrationNextLevel;
        calibrationNextLevel += CalibrationStepDb;
        calibrationNextStepAt = now.AddSeconds(settings.DurationSeconds + CalibrationGapSeconds);
        _ = Burst("calibration", calibrationLastPlayed, includeHighTone: false);   // Calibrates the low tones only.
    }

    // --- Window and tray -----------------------------------------------------------------------

    void ShowWindow()
    {
        if (window == null || window.IsDisposed)
        {
            window = new MainWindow(settings.WindowBounds);
            window.MessageReceived += OnWindowMessage;
            window.FormClosing += (_, _) =>
            {
                if (window == null) return;
                settings.WindowBounds = window.SavedBounds;
                try { settings.Save(); } catch { }
            };
            window.FormClosed += (_, _) =>
            {
                window = null;
                levelTimer.Stop();
                if (!exiting && !settings.TrayHintShown)
                {
                    tray.ShowBalloonTip(5000, "SubWake is still running", "It keeps the sub awake from the tray. Click the icon to open it again.", ToolTipIcon.Info);
                    settings.TrayHintShown = true;
                    try { settings.Save(); } catch { }
                }
            };
            window.Show();
            levelTimer.Start();
        }
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.Activate();
    }

    void OnWindowMessage(JsonElement message)
    {
        switch (message.GetProperty("type").GetString())
        {
            case "ready": Push(); break;
            case "set": ApplySettings(message.GetProperty("settings")); break;
            case "startup":
                bool enabled = message.GetProperty("value").GetBoolean();
                try { StartupRegistration.Set(enabled); Log.Write("Start with Windows " + OnOff(enabled) + "."); }
                catch (Exception ex) { Log.Write("Start with Windows could not be changed: " + ex.Message); }
                Push();
                break;
            case "play-now": PlayNow(); break;
            case "check-updates": _ = CheckForUpdates(manual: true); break;
            case "open-update":
                try { Process.Start(new ProcessStartInfo(Updates.SafeUrl(update?.Url, Updates.Repo)) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Write("Release page could not be opened: " + ex.Message); }
                break;
            case "snooze":
                double minutes = message.GetProperty("minutes").GetDouble();
                if (SnoozeChoices.Any(c => c.Minutes == minutes)) Snooze(minutes);
                break;
            case "resume": Resume(); break;
            case "list-apps": window?.Send(new { type = "apps", apps = RunningApps() }); break;
            case "calibrate": Calibrate(message.GetProperty("action").GetString() ?? ""); break;
            case "open-log":
                if (!File.Exists(AppPaths.Log)) Log.Write("Log opened.");
                try { Process.Start(new ProcessStartInfo(AppPaths.Log) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Write("Log could not be opened: " + ex.Message); }
                break;
            case "quit": quitRequested = true; break;   // Exits on the next tick, outside the browser callback.
        }
    }

    /// <summary>Apps with a window, for "hold while these apps are open".</summary>
    static List<object> RunningApps()
    {
        var apps = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int own = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id != own && p.MainWindowHandle != IntPtr.Zero && !apps.ContainsKey(p.ProcessName))
                    apps[p.ProcessName] = p.MainWindowTitle;
            }
            catch { }
            finally { p.Dispose(); }
        }
        return apps.Select(a => (object)new { name = a.Key, title = a.Value }).ToList();
    }

    void SendLevels()
    {
        if (window == null || window.IsDisposed || !window.Ready) return;
        window.Send(new { type = "levels", levels = monitor.TakeLevels() });
    }

    void Push()
    {
        if (window == null || window.IsDisposed || !window.Ready) return;
        var now = Clock.Now;
        string pause = PauseReason();
        var target = Devices.Resolve(audio.Devices, settings.DeviceId, settings.DeviceName);
        if (!settings.SetupDone) defaultOutputId ??= CoreAudio.DefaultOutputId();
        window.Send(new
        {
            type = "state",
            version,
            settings,
            startup = StartupRegistration.Read(),
            status = new { mode = Mode(pause), text = StatusText(pause) },
            nextBurstAt = settings.Enabled && pause == "" && !Calibrating ? Clock.ToWallMs(schedule.NextBurstAt) : (long?)null,
            lastBurstAt = schedule.LastBurstAt is DateTime last ? Clock.ToWallMs(last) : (long?)null,
            lastBurstText,
            lastError,
            playing,
            devices = audio.Devices.Select(d => new { id = d.Id, name = d.Name, watched = audio.Watching.Contains(d.Name) }),
            defaultOutputId = settings.SetupDone ? null : defaultOutputId,
            intervals = settings.Intervals,
            update = new { available = update?.Version, checking = checkingUpdate, status = updateStatus },
            deviceFound = !audio.Ready || target != null,
            audio = new
            {
                watching = audio.Watching,
                active = monitor.ActiveWithin(AudioActiveWindow),
                recording = audio.Recording,
                holding = WaitingForSilence,
                error = audio.Error,
            },
            subTimerFrom = Rules.SubTimerFrom(schedule.LastBurstAt, audio.LastSignal) is DateTime from ? Clock.ToWallMs(from) : (long?)null,
            subStandbyMs = (long)TimeSpan.FromMinutes(settings.SubStandbyMinutes).TotalMilliseconds,
            snoozedUntil = snoozeUntil is DateTime until ? Clock.ToWallMs(until) : (long?)null,
            holdAppsOpen = openHoldApps,
            history = activity.ToJson(DateTime.Now),
            calibration = new
            {
                phase = calibration.ToString().ToLowerInvariant(),
                waitUntil = calibration == CalibrationPhase.Waiting ? Clock.ToWallMs(calibrationWaitUntil) : (long?)null,
                level = calibrationLastPlayed,
                suggestedDb = calibrationSuggested,
                startDb = CalibrationStartDb,
                maxDb = CalibrationMaxDb,
                waitMinutes = settings.SubStandbyMinutes + 1,
                message = calibrationMessage,
                note = calibrationNote,
            },
        });
    }

    bool WaitingForSilence => settings.SkipWhileAudio && audioPlaying;

    string Mode(string pause) =>
        Calibrating ? "calibrating" : !settings.Enabled ? "off" : pause != "" ? "paused" : problem != null ? "warning"
        : WaitingForSilence ? "holding" : "active";

    string StatusText(string pause) => Mode(pause) switch
    {
        "calibrating" => "Calibrating",
        "off" => "Off",
        "paused" => "Paused · " + pause,
        "warning" => PlaybackException.StatusText(problem ?? PlaybackProblem.Failed),
        "holding" => "Active · holding for music",
        _ => "Active",
    };

    void UpdateTray(DateTime now)
    {
        string pause = PauseReason();
        string mode = Mode(pause);
        var icon = mode switch
        {
            "active" or "calibrating" => AppIcon.Active,
            "holding" => AppIcon.Holding,
            "warning" => AppIcon.Warning,
            _ => AppIcon.Inactive,
        };
        if (tray.Icon != icon) tray.Icon = icon;
        enabledItem.Checked = settings.Enabled;
        resumeItem.Enabled = snoozeUntil != null;
        string detail = mode switch
        {
            "holding" => "music playing, burst waits",
            "active" => $"next burst in {Math.Max(1, (int)Math.Ceiling((schedule.NextBurstAt - now).TotalMinutes))} min",
            _ => StatusText(pause),
        };
        string text = "SubWake · " + detail;
        if (text.Length > 63) text = text[..63];
        if (tray.Text != text) tray.Text = text;
    }

    // --- System events (other threads) ---------------------------------------------------------

    void SessionSwitched(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) locked = true;
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            locked = false;
            Interlocked.Exchange(ref wakeBurstAtTicks, Clock.Now.Ticks);
        }
    }

    void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // Give the audio interface a few seconds to come back after sleep.
        if (e.Mode == PowerModes.Resume) Interlocked.Exchange(ref wakeBurstAtTicks, Clock.Now.AddSeconds(5).Ticks);
    }

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
        timer.Stop();
        levelTimer.Stop();
        SystemEvents.SessionSwitch -= SessionSwitched;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        monitor.Dispose();
        Log.Write("Stopped.");
        window?.Close();
        tray.Visible = false;
        tray.Dispose();
        timer.Dispose();
        levelTimer.Dispose();
        showSignal.Dispose();
        stopSignal.Dispose();
        base.ExitThreadCore();
    }
}
