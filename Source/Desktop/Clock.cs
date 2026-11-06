using System.Runtime.InteropServices;

namespace SubWake;

/// <summary>
/// The schedule's clock. It counts from the system tick counter, so daylight saving and time syncs can't
/// make a burst late. Only quiet hours, the log and the activity history use local wall-clock time.
/// </summary>
static class Clock
{
    static readonly DateTime Start = DateTime.UtcNow;
    static readonly long StartTicks = Environment.TickCount64;

    public static DateTime Now => Start.AddMilliseconds(Environment.TickCount64 - StartTicks);

    /// <summary>A schedule time as Unix milliseconds on the wall clock, for the window.</summary>
    public static long ToWallMs(DateTime time) =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(time - Now).TotalMilliseconds;

    /// <summary>A schedule time as local wall-clock time.</summary>
    public static DateTime ToWall(DateTime time) => DateTime.Now + (time - Now);
}

/// <summary>Keyboard and mouse activity, for "let the sub sleep when you're away".</summary>
static class UserInput
{
    [StructLayout(LayoutKind.Sequential)] struct LastInputInfo { public uint Size; public uint Time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static TimeSpan Idle()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }
}
