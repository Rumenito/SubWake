namespace SubWake;

/// <summary>
/// When the next burst plays. Pure rules on the schedule clock, with no Windows calls, so the build tests them.
/// </summary>
sealed class Scheduler(DateTime now)
{
    public const int RetrySeconds = 60;

    public DateTime NextBurstAt { get; private set; } = now;   // First burst right after start.
    public DateTime? LastBurstAt { get; private set; }

    public bool Due(DateTime now) => now >= NextBurstAt;

    public void BurstNow(DateTime now) => NextBurstAt = now;

    /// <summary>Unlock, resume or return: brings the next burst forward, never pushes it back.</summary>
    public void WakeAt(DateTime at) { if (at < NextBurstAt) NextBurstAt = at; }

    /// <summary>Audio keeps the sub awake by itself: the next burst waits a full interval after the last signal.</summary>
    public void HoldForAudio(DateTime lastSignal, double intervalMinutes)
    {
        var after = lastSignal.AddMinutes(intervalMinutes);
        if (after > NextBurstAt) NextBurstAt = after;
    }

    /// <summary>A burst played. Calibration bursts don't move the schedule.</summary>
    public void Played(DateTime at, double intervalMinutes, bool reschedule = true)
    {
        LastBurstAt = at;
        if (reschedule) NextBurstAt = at.AddMinutes(intervalMinutes);
    }

    public void Failed(DateTime now) => NextBurstAt = now.AddSeconds(RetrySeconds);

    /// <summary>After an interval change, or wait-for-silence turned off: one interval after the last burst, never in the past.</summary>
    public void Restart(DateTime now, double intervalMinutes)
    {
        var next = (LastBurstAt ?? now).AddMinutes(intervalMinutes);
        NextBurstAt = next < now ? now : next;
    }

    public void After(DateTime now, double minutes) => NextBurstAt = now.AddMinutes(minutes);
}

/// <summary>Everything that can stop bursts for now, in the order the status names them.</summary>
readonly record struct PauseInputs(
    bool Enabled,
    string? SnoozedUntil,               // "14:30", or null when not snoozed.
    bool Locked,
    bool PauseWhenLocked,
    bool InQuietHours,
    string QuietEnd,
    bool Away,
    IReadOnlyList<string> Recording,    // Apps recording, when "hold while recording" is on.
    IReadOnlyList<string> OpenHoldApps,
    bool SetupNeeded = false);          // First-run setup isn't finished: no bursts yet.

static class Rules
{

    /// <summary>"" when bursts may play; otherwise the reason, as shown after "Paused · ".</summary>
    public static string PauseReason(PauseInputs p)
    {
        if (!p.Enabled) return "";
        if (p.SetupNeeded) return "finish setup";
        if (p.SnoozedUntil != null) return $"snoozed until {p.SnoozedUntil}";
        if (p.Locked && p.PauseWhenLocked) return "PC locked";
        if (p.InQuietHours) return $"quiet hours until {p.QuietEnd}";
        if (p.Away) return "you're away";
        if (p.Recording.Count > 0) return $"recording in {Names(p.Recording)}";
        if (p.OpenHoldApps.Count > 0) return $"{Names(p.OpenHoldApps)} {(p.OpenHoldApps.Count > 1 ? "are" : "is")} open";
        return "";
    }

    static string Names(IReadOnlyList<string> names) =>
        names.Count switch { 1 => names[0], 2 => $"{names[0]} and {names[1]}", _ => $"{names[0]} and {names.Count - 1} more" };

    /// <summary>Away: no keyboard or mouse input and no audio for the whole away time.</summary>
    public static bool IsAway(DateTime now, TimeSpan idle, DateTime? lastAudio, double awayMinutes)
    {
        var lastActivity = now - idle;
        if (lastAudio is DateTime audio && audio > lastActivity) lastActivity = audio;
        return now - lastActivity >= TimeSpan.FromMinutes(awayMinutes);
    }

    /// <summary>When the sub's own standby timer last restarted, as far as the app can tell: the later of the last burst and the last audio.</summary>
    public static DateTime? SubTimerFrom(DateTime? lastBurst, DateTime? lastSignal) =>
        lastBurst is DateTime b && lastSignal is DateTime s ? (b > s ? b : s) : lastBurst ?? lastSignal;
}
