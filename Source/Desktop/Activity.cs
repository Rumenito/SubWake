using System.Globalization;

namespace SubWake;

/// <summary>
/// Bursts, music and pauses over the last day, for the window's timeline. Kept in local wall-clock time
/// (display only) and rebuilt from the log at start, so it survives restarts.
/// </summary>
sealed class Activity
{
    public static readonly TimeSpan Keep = TimeSpan.FromHours(26);
    public static readonly TimeSpan Window = TimeSpan.FromHours(6);   // What the timeline shows.

    sealed class Span(DateTime start) { public DateTime Start = start; public DateTime? End; }

    readonly List<DateTime> bursts = [];
    readonly List<Span> music = [], paused = [];

    public IReadOnlyList<DateTime> Bursts => bursts;

    public void Burst(DateTime at) => bursts.Add(at);
    public void MusicStarted(DateTime at) => Open(music, at);
    public void MusicStopped(DateTime at) => Close(music, at);
    public void PauseStarted(DateTime at) => Open(paused, at);
    public void PauseEnded(DateTime at) => Close(paused, at);
    public void AppStopped(DateTime at) { Close(music, at); Close(paused, at); }

    static void Open(List<Span> spans, DateTime at) { if (spans.Count == 0 || spans[^1].End != null) spans.Add(new Span(at)); }
    static void Close(List<Span> spans, DateTime at)
    {
        if (spans.Count > 0 && spans[^1].End == null) spans[^1].End = at < spans[^1].Start ? spans[^1].Start : at;
    }

    public void Prune(DateTime now)
    {
        var cutoff = now - Keep;
        bursts.RemoveAll(b => b < cutoff);
        music.RemoveAll(s => s.End is DateTime e && e < cutoff);
        paused.RemoveAll(s => s.End is DateTime e && e < cutoff);
    }

    /// <summary>Bursts since midnight.</summary>
    public int BurstsToday(DateTime now) => bursts.Count(b => b >= now.Date && b <= now);

    /// <summary>Music since midnight.</summary>
    public TimeSpan MusicToday(DateTime now)
    {
        var total = TimeSpan.Zero;
        foreach (var s in music)
        {
            var start = s.Start < now.Date ? now.Date : s.Start;
            var end = s.End ?? now;
            if (end > start) total += end - start;
        }
        return total;
    }

    public object ToJson(DateTime now)
    {
        var from = now - Window;
        static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();
        object[] Spans(List<Span> list) => list
            .Where(s => (s.End ?? now) >= from)
            .Select(s => new long?[] { Ms(s.Start < from ? from : s.Start), s.End is DateTime e ? Ms(e) : null })
            .ToArray<object>();
        return new
        {
            from = Ms(from),
            to = Ms(now),
            bursts = bursts.Where(b => b >= from).Select(Ms).ToArray(),
            music = Spans(music),
            paused = Spans(paused),
            today = new { bursts = BurstsToday(now), musicMinutes = (int)MusicToday(now).TotalMinutes },
        };
    }

    /// <summary>Rebuilds the history from log lines ("yyyy-MM-dd HH:mm:ss  message").</summary>
    public static Activity FromLog(IEnumerable<string> lines, DateTime now, TimeSpan audioStoppedAfter)
    {
        var activity = new Activity();
        var cutoff = now - Keep;
        foreach (var line in lines)
        {
            if (line.Length < 21 || !DateTime.TryParseExact(line[..19], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
            if (at < cutoff || at > now) continue;
            string message = line[19..].TrimStart();
            if (message.StartsWith("Played ", StringComparison.Ordinal)) activity.Burst(at);
            else if (message.StartsWith("Audio detected", StringComparison.Ordinal)) activity.MusicStarted(at);
            else if (message.StartsWith("Audio stopped", StringComparison.Ordinal)) activity.MusicStopped(at - audioStoppedAfter);
            else if (message.StartsWith("Paused: ", StringComparison.Ordinal)) activity.PauseStarted(at);
            else if (message.StartsWith("Resumed.", StringComparison.Ordinal)) activity.PauseEnded(at);
            else if (message.StartsWith("Stopped.", StringComparison.Ordinal) || message.StartsWith("Started v", StringComparison.Ordinal)) activity.AppStopped(at);
        }
        return activity;
    }

    /// <summary>The history from the app's own log; empty if it can't be read.</summary>
    public static Activity Load(TimeSpan audioStoppedAfter)
    {
        try
        {
            if (!File.Exists(AppPaths.Log)) return new Activity();
            using var stream = new FileStream(AppPaths.Log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is string line) lines.Add(line);
            return FromLog(lines, DateTime.Now, audioStoppedAfter);
        }
        catch { return new Activity(); }
    }
}
