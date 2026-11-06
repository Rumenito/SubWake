using System.Runtime.InteropServices;

namespace SubWake;

/// <summary>A Windows playback or recording device: its endpoint ID (stable across renames) and its name.</summary>
sealed record AudioDevice(string Id, string Name);

static class Devices
{
    /// <summary>The saved output: by endpoint ID first, then exact name, then partial name. Null if missing.</summary>
    public static AudioDevice? Resolve(IReadOnlyList<AudioDevice> devices, string? id, string name)
    {
        if (!string.IsNullOrEmpty(id) && devices.FirstOrDefault(d => d.Id == id) is AudioDevice byId) return byId;
        if (string.IsNullOrWhiteSpace(name)) return null;
        return devices.FirstOrDefault(d => d.Name == name)
            ?? devices.FirstOrDefault(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The text that names the interface: "(CONNECT 6" from "Out 1/2 (CONNECT 6)", "(Realtek(R) Audio" from
    /// "Speakers (Realtek(R) Audio)". Windows puts the device after the first " (". Null: match the name itself.
    /// </summary>
    public static string? GroupKey(string name)
    {
        int open = name.IndexOf(" (", StringComparison.Ordinal);
        open = open >= 0 ? open + 1 : name.IndexOf('(');
        if (open < 0) return null;
        string key = name[open..].Trim();
        if (key.EndsWith(')')) key = key[..^1];
        return key.Length > 1 ? key : null;
    }

    /// <summary>True if an output is on the same interface as the burst output.</summary>
    public static bool InGroup(string outputName, string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return false;
        string? key = GroupKey(deviceName);
        return key != null
            ? outputName.Contains(key, StringComparison.OrdinalIgnoreCase)
            : outputName.Contains(deviceName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether wait-for-silence watches an output. The burst output always counts. With no list chosen (null),
    /// every output on the same device does; otherwise only the chosen ones.
    /// </summary>
    public static bool Watched(AudioDevice output, AudioDevice? target, string targetName, IReadOnlyCollection<string>? chosenIds)
    {
        if (target != null && output.Id == target.Id) return true;
        return chosenIds == null ? InGroup(output.Name, target?.Name ?? targetName) : chosenIds.Contains(output.Id);
    }
}

/// <summary>Windows Core Audio: device lists, activation and the COM interfaces the app uses.</summary>
static class CoreAudio
{
    public const int Render = 0, Capture = 1;
    const int StateActive = 1, ClsctxAll = 23;

    public static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorObject();

    /// <summary>Active endpoints with their names. The caller releases the devices.</summary>
    public static List<(AudioDevice Info, IMMDevice Device)> Endpoints(IMMDeviceEnumerator enumerator, int flow)
    {
        var result = new List<(AudioDevice, IMMDevice)>();
        Check(enumerator.EnumAudioEndpoints(flow, StateActive, out var collection));
        try
        {
            Check(collection.GetCount(out int count));
            for (int i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0) continue;
                string? id = device.GetId(out string value) == 0 ? value : null;
                string? name = id == null ? null : FriendlyName(device);
                if (id != null && name != null) result.Add((new AudioDevice(id, name), device));
                else Release(device);
            }
        }
        finally { Release(collection); }
        return result.OrderBy(e => e.Item1.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Active endpoint names and IDs, with nothing left open.</summary>
    public static List<AudioDevice> List(int flow)
    {
        var enumerator = CreateEnumerator();
        try
        {
            var endpoints = Endpoints(enumerator, flow);
            foreach (var (_, device) in endpoints) Release(device);
            return endpoints.Select(e => e.Info).ToList();
        }
        finally { Release(enumerator); }
    }

    static string? FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(0, out var store) != 0) return null;
        try
        {
            var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };   // PKEY_Device_FriendlyName
            if (store.GetValue(ref key, out var value) != 0) return null;
            try { return value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(store); }
    }

    /// <summary>Windows' default playback output, or null.</summary>
    public static string? DefaultOutputId()
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = CreateEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(Render, 0 /* eConsole */, out var device) != 0) return null;
            try { return device.GetId(out string id) == 0 ? id : null; }
            finally { Release(device); }
        }
        catch { return null; }
        finally { Release(enumerator); }
    }

    public static T Activate<T>(IMMDevice device) where T : class
    {
        var iid = typeof(T).GUID;
        Check(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object instance));
        return (T)instance;
    }

    public static void Release(object? instance)
    {
        if (instance != null && Marshal.IsComObject(instance))
            try { Marshal.ReleaseComObject(instance); } catch { }
    }

    public static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorObject { }
}

// --- COM interfaces (only the methods the app calls are typed; the rest keep the vtable order) ----------

[StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid FormatId; public int Id; }
[StructLayout(LayoutKind.Sequential)] struct PropVariant { public ushort Type, Reserved1, Reserved2, Reserved3; public IntPtr Pointer, Pointer2; }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int Item(int index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetAt(int index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
}

/// <summary>Windows calls this when outputs are added, removed, enabled, disabled or renamed.</summary>
[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged(IntPtr id, int state);
    [PreserveSig] int OnDeviceAdded(IntPtr id);
    [PreserveSig] int OnDeviceRemoved(IntPtr id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, IntPtr id);
    [PreserveSig] int OnPropertyValueChanged(IntPtr id, PropertyKey key);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation { [PreserveSig] int GetPeakValue(out float peak); }

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioRenderClient
{
    [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
    [PreserveSig] int ReleaseBuffer(uint frames, int flags);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
}

[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
    [PreserveSig] int GetGroupingParam(out Guid grouping);
    [PreserveSig] int SetGroupingParam(IntPtr grouping, IntPtr context);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
    // IAudioSessionControl2
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
    [PreserveSig] int GetProcessId(out uint processId);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference(bool optOut);
}
