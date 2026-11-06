using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SubWake;

/// <summary>What the monitor last saw. Times are on the schedule clock (<see cref="Clock"/>).</summary>
sealed record MonitorState(
    bool Ready,
    IReadOnlyList<AudioDevice> Devices,
    AudioDevice? Target,
    IReadOnlyList<string> Watching,
    IReadOnlyList<string> Active,
    IReadOnlyList<string> Recording,
    string? Error,
    DateTime? LastSignal,
    DateTime? LastAny)
{
    public static readonly MonitorState Empty = new(false, [], null, [], [], [], null, null, null);
}

/// <summary>
/// Watches Windows' peak meters on the watched playback outputs (by default every output on the burst output's
/// device, for "Out 1/2 (CONNECT 6)" every "(CONNECT 6)" output) and the recording sessions on every input.
/// Runs on its own background thread. If metering fails it reports no signal, and bursts run on schedule.
/// </summary>
sealed class AudioMonitor : IDisposable
{
    public const double FloorDb = -70;           // Anything above this is "some sound" (calibration, away).
    const double SilentDb = -100;
    const int SampleMs = 50, CaptureCheckMs = 1000;
    const int FallbackRefreshMs = 60_000, PollRefreshMs = 5_000;   // Device changes come as notifications; this is the safety net.

    sealed class Watched(string name, IAudioMeterInformation meter, IAudioSessionManager2? sessions)
    {
        public string Name = name;
        public IAudioMeterInformation Meter = meter;
        public IAudioSessionManager2? Sessions = sessions;   // Only for the burst output: lets our own sound be left out.
    }

    readonly Thread thread;
    readonly int ownProcess = Environment.ProcessId;
    readonly object gate = new();
    volatile bool stopping, refreshNeeded = true;

    // Inputs, set from the UI thread.
    string? targetId;
    string targetName;
    List<string>? watchIds;
    double thresholdDb;
    bool watchCapture;
    long ownUntilTicks;

    // Outputs, read by the UI thread.
    MonitorState state = MonitorState.Empty;
    readonly Dictionary<string, long> lastSignalByOutput = [];
    readonly Dictionary<string, double> levels = [];
    long lastSignalTicks, lastAnyTicks;

    readonly Dictionary<uint, string> processNames = [];

    public AudioMonitor(string? deviceId, string deviceName, List<string>? watchIds, double thresholdDb, bool watchCapture)
    {
        targetId = deviceId;
        targetName = deviceName;
        this.watchIds = watchIds?.ToList();
        this.thresholdDb = thresholdDb;
        this.watchCapture = watchCapture;
        thread = new Thread(Run) { IsBackground = true, Name = "Audio monitor" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public void SetTarget(string? id, string name)
    {
        lock (gate) { targetId = id; targetName = name; }
        refreshNeeded = true;
    }

    /// <summary>Which outputs to watch besides the burst output. Null: every output on the same device.</summary>
    public void SetWatch(List<string>? ids)
    {
        lock (gate) watchIds = ids?.ToList();
        refreshNeeded = true;
    }

    /// <summary>True when Windows reports device changes; otherwise the monitor re-scans every few seconds.</summary>
    public bool Notified { get; private set; }

    public void SetThreshold(double db) { lock (gate) thresholdDb = db; }

    public void SetWatchCapture(bool on) { lock (gate) watchCapture = on; }

    /// <summary>Our own burst is starting: on the burst output, only other apps' sound counts until it ends.</summary>
    public void OwnPlaybackStarting() => Interlocked.Exchange(ref ownUntilTicks, DateTime.MaxValue.Ticks);

    /// <summary>Our own burst ended; its tail can take a moment to leave the meter.</summary>
    public void OwnPlaybackEnded() => Interlocked.Exchange(ref ownUntilTicks, Clock.Now.AddMilliseconds(500).Ticks);

    public MonitorState State()
    {
        lock (gate)
        {
            long signal = Interlocked.Read(ref lastSignalTicks), any = Interlocked.Read(ref lastAnyTicks);
            return state with
            {
                LastSignal = signal > 0 ? new DateTime(signal, DateTimeKind.Utc) : null,
                LastAny = any > 0 ? new DateTime(any, DateTimeKind.Utc) : null,
            };
        }
    }

    /// <summary>Outputs above the threshold within the window.</summary>
    public IReadOnlyList<string> ActiveWithin(TimeSpan window)
    {
        var now = Clock.Now;
        lock (gate)
            return state.Watching.Where(n => lastSignalByOutput.TryGetValue(n, out long t) && now - new DateTime(t, DateTimeKind.Utc) < window).ToList();
    }

    /// <summary>The loudest level (dBFS) on each watched output since the last call, for the window's level bars.</summary>
    public Dictionary<string, double> TakeLevels()
    {
        lock (gate)
        {
            var result = new Dictionary<string, double>(levels);
            foreach (var key in levels.Keys.ToList()) levels[key] = SilentDb;
            return result;
        }
    }

    public static double ToDb(float peak) => peak > 0 ? Math.Max(SilentDb, 20 * Math.Log10(peak)) : SilentDb;

    void Run()
    {
        IMMDeviceEnumerator? enumerator = null;
        DeviceNotifications? notifications = null;
        var watched = new List<Watched>();
        var inputs = new List<(string Name, IAudioSessionManager2 Sessions)>();
        var refreshAt = DateTime.MinValue;
        var captureAt = DateTime.MinValue;
        int refreshMs = PollRefreshMs;
        while (!stopping)
        {
            var now = Clock.Now;
            try
            {
                if (enumerator == null)
                {
                    enumerator = CoreAudio.CreateEnumerator();
                    try
                    {
                        notifications = new DeviceNotifications(() => refreshNeeded = true);
                        CoreAudio.Check(enumerator.RegisterEndpointNotificationCallback(notifications));
                        refreshMs = FallbackRefreshMs;
                        Notified = true;
                    }
                    catch { notifications = null; refreshMs = PollRefreshMs; Notified = false; }
                }
                if (refreshNeeded || now >= refreshAt)
                {
                    refreshNeeded = false;
                    Release(watched, inputs);
                    Open(enumerator, watched, inputs);
                    refreshAt = now.AddMilliseconds(refreshMs);
                }
                Sample(now, watched);
                bool capture;
                lock (gate) capture = watchCapture;
                if (now >= captureAt)
                {
                    var recording = capture ? Recording(inputs) : [];
                    lock (gate) state = state with { Recording = recording };
                    captureAt = now.AddMilliseconds(CaptureCheckMs);
                }
            }
            catch (Exception ex)
            {
                lock (gate) state = state with { Error = ex.Message, Ready = true };
                Release(watched, inputs);
                refreshAt = now.AddMilliseconds(PollRefreshMs);
            }
            Thread.Sleep(SampleMs);
        }
        Release(watched, inputs);
        if (enumerator != null && notifications != null)
            try { enumerator.UnregisterEndpointNotificationCallback(notifications); } catch { }
        CoreAudio.Release(enumerator);
    }

    void Open(IMMDeviceEnumerator enumerator, List<Watched> watched, List<(string, IAudioSessionManager2)> inputs)
    {
        string? id;
        string name;
        List<string>? chosen;
        lock (gate) { id = targetId; name = targetName; chosen = watchIds; }

        var outputs = CoreAudio.Endpoints(enumerator, CoreAudio.Render);
        var devices = outputs.Select(o => o.Info).ToList();
        var target = Devices.Resolve(devices, id, name);
        foreach (var (info, device) in outputs)
        {
            try
            {
                if (!Devices.Watched(info, target, name, chosen)) continue;
                var meter = CoreAudio.Activate<IAudioMeterInformation>(device);
                IAudioSessionManager2? sessions = null;
                if (info == target)
                    try { sessions = CoreAudio.Activate<IAudioSessionManager2>(device); } catch { }
                watched.Add(new Watched(info.Name, meter, sessions));
            }
            catch { }
            finally { CoreAudio.Release(device); }
        }

        foreach (var (info, device) in CoreAudio.Endpoints(enumerator, CoreAudio.Capture))
        {
            try { inputs.Add((info.Name, CoreAudio.Activate<IAudioSessionManager2>(device))); }
            catch { }
            finally { CoreAudio.Release(device); }
        }

        lock (gate)
        {
            state = state with
            {
                Ready = true,
                Devices = devices,
                Target = target,
                Watching = watched.Select(w => w.Name).ToList(),
                Error = watched.Count == 0 ? "No matching output to watch." : null,
            };
            foreach (var w in watched) levels.TryAdd(w.Name, SilentDb);
            foreach (var gone in levels.Keys.Where(k => !watched.Any(w => w.Name == k)).ToList()) levels.Remove(gone);
        }
    }

    void Sample(DateTime now, List<Watched> watched)
    {
        bool own = now.Ticks < Interlocked.Read(ref ownUntilTicks);
        double threshold;
        lock (gate) threshold = thresholdDb;
        foreach (var w in watched)
        {
            float peak;
            if (own && w.Sessions == null) continue;   // Burst output without session meters: skip it during our burst.
            if (own) peak = OtherAppsPeak(w.Sessions!);
            else if (w.Meter.GetPeakValue(out peak) != 0) continue;
            double db = ToDb(peak);
            lock (gate)
            {
                if (!levels.TryGetValue(w.Name, out double level) || db > level) levels[w.Name] = db;
                if (db >= threshold) lastSignalByOutput[w.Name] = now.Ticks;
            }
            if (db > FloorDb) Interlocked.Exchange(ref lastAnyTicks, now.Ticks);
            if (db >= threshold) Interlocked.Exchange(ref lastSignalTicks, now.Ticks);
        }
    }

    /// <summary>The loudest app on an output, leaving out this app's own burst.</summary>
    float OtherAppsPeak(IAudioSessionManager2 manager)
    {
        float loudest = 0;
        if (manager.GetSessionEnumerator(out var sessions) != 0) return 0;
        try
        {
            if (sessions.GetCount(out int count) != 0) return 0;
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) != 0) continue;
                try
                {
                    if (session.GetProcessId(out uint pid) >= 0 && pid == ownProcess) continue;
                    if (session is IAudioMeterInformation meter && meter.GetPeakValue(out float peak) == 0) loudest = Math.Max(loudest, peak);
                }
                finally { CoreAudio.Release(session); }
            }
        }
        finally { CoreAudio.Release(sessions); }
        return loudest;
    }

    /// <summary>Apps with a running recording stream on any input (microphone, interface input).</summary>
    List<string> Recording(List<(string Name, IAudioSessionManager2 Sessions)> inputs)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, manager) in inputs)
        {
            if (manager.GetSessionEnumerator(out var sessions) != 0) continue;
            try
            {
                if (sessions.GetCount(out int count) != 0) continue;
                for (int i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var session) != 0) continue;
                    try
                    {
                        if (session.GetState(out int s) != 0 || s != 1 /* active */) continue;
                        if (session.GetProcessId(out uint pid) < 0 || pid == 0 || pid == ownProcess) continue;
                        names.Add(ProcessName(pid));
                    }
                    finally { CoreAudio.Release(session); }
                }
            }
            finally { CoreAudio.Release(sessions); }
        }
        return [.. names];
    }

    string ProcessName(uint pid)
    {
        if (processNames.TryGetValue(pid, out var known)) return known;
        string name = "an app";
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
            try { if (process.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } description) name = description; } catch { }
        }
        catch { }
        if (processNames.Count > 200) processNames.Clear();
        processNames[pid] = name;
        return name;
    }

    static void Release(List<Watched> watched, List<(string, IAudioSessionManager2)> inputs)
    {
        foreach (var w in watched) { CoreAudio.Release(w.Meter); CoreAudio.Release(w.Sessions); }
        foreach (var (_, sessions) in inputs) CoreAudio.Release(sessions);
        watched.Clear();
        inputs.Clear();
    }

    public void Dispose()
    {
        stopping = true;
        thread.Join(1000);
    }

    /// <summary>Windows calls this from its own threads; it only flags a refresh.</summary>
    [ComVisible(true)]
    sealed class DeviceNotifications(Action changed) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(IntPtr id, int state) { changed(); return 0; }
        public int OnDeviceAdded(IntPtr id) { changed(); return 0; }
        public int OnDeviceRemoved(IntPtr id) { changed(); return 0; }
        public int OnDefaultDeviceChanged(int flow, int role, IntPtr id) => 0;
        public int OnPropertyValueChanged(IntPtr id, PropertyKey key)
        {
            // Only a rename matters (PKEY_Device_FriendlyName); other properties change often.
            if (key.Id == 14 && key.FormatId == FriendlyNameKey) changed();
            return 0;
        }
        static readonly Guid FriendlyNameKey = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    }
}
