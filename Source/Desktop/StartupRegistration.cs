using Microsoft.Win32;

namespace SubWake;

/// <summary>Start with Windows is one per-user Run value.</summary>
static class StartupRegistration
{
    public const string ValueName = "SubWake";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Read()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        return run?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) run.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --startup");
        else run.DeleteValue(ValueName, false);
    }
}
