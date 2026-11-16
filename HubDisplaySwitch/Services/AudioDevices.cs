using System.Runtime.InteropServices;

namespace HubDisplaySwitch.Services;

public sealed record AudioDevice(string Id, string Name, bool Active, bool IsDefault);

public enum AudioResult { Done, AlreadySet, NotReady, NotFound }

/// <summary>Output = playback (speakers, headphones, HDMI); Input = recording (microphones).</summary>
public enum AudioFlow { Output = 0, Input = 1 }

/// <summary>
/// Playback/recording devices via Core Audio (IMMDeviceEnumerator), and switching the default device via
/// the undocumented-but-stable IPolicyConfig interface (used by Windows' own Sound control panel; same
/// approach as SoundSwitch / AudioDeviceCmdlets).
/// </summary>
public static class AudioDevices
{
    /// <summary>
    /// Real devices for a direction: active ones (Active = true) and unplugged ones (Active = false; the
    /// device exists but nothing is connected to it, e.g. a headphone jack). "Not present" endpoints are left
    /// out: they're mostly ghosts from old drivers and long-gone devices.
    /// </summary>
    public static List<AudioDevice> List(AudioFlow flow = AudioFlow.Output)
    {
        var result = new List<AudioDevice>();
        var en = CreateEnumerator();
        try
        {
            string? defaultId = DefaultId(en, flow);
            Check(en.EnumAudioEndpoints((EDataFlow)flow, DEVICE_STATE_ACTIVE | DEVICE_STATE_UNPLUGGED, out var coll));
            Check(coll.GetCount(out uint n));
            for (uint i = 0; i < n; i++)
            {
                if (coll.Item(i, out var dev) != 0)
                {
                    continue;
                }

                try
                {
                    dev.GetId(out var id);
                    dev.GetState(out var state);
                    result.Add(new AudioDevice(id, FriendlyName(dev) ?? id, state == DEVICE_STATE_ACTIVE, id == defaultId));
                }
                finally { Marshal.ReleaseComObject(dev); }
            }
            Marshal.ReleaseComObject(coll);
        }
        finally { Marshal.ReleaseComObject(en); }

        // Keep the best entry per name (a driver reinstall can leave a duplicate).
        return result.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.OrderByDescending(d => d.Active).First())
                     .OrderByDescending(d => d.Active).ThenBy(d => d.Name).ToList();
    }

    /// <summary>
    /// Make a device the default for its direction, for all roles (default and communications).
    /// Matches by id first, then by name. Returns NotReady if the device exists but isn't active yet.
    /// </summary>
    public static AudioResult SetDefault(AudioFlow flow, string id, string name)
    {
        var en = CreateEnumerator();
        try
        {
            string? currentDefault = DefaultId(en, flow);
            string? currentComms = DefaultId(en, flow, ERole.Communications);
            Check(en.EnumAudioEndpoints((EDataFlow)flow, DEVICE_STATE_ACTIVE | DEVICE_STATE_UNPLUGGED | DEVICE_STATE_NOTPRESENT, out var coll));
            Check(coll.GetCount(out uint n));
            var candidates = new List<(string id, bool active, bool byId)>();
            for (uint i = 0; i < n; i++)
            {
                if (coll.Item(i, out var dev) != 0)
                {
                    continue;
                }

                try
                {
                    dev.GetId(out var devId);
                    dev.GetState(out var state);
                    bool byId = !string.IsNullOrEmpty(id) && string.Equals(devId, id, StringComparison.OrdinalIgnoreCase);
                    bool byName = string.Equals(FriendlyName(dev), name, StringComparison.OrdinalIgnoreCase);
                    if (byId || byName)
                    {
                        candidates.Add((devId, state == DEVICE_STATE_ACTIVE, byId));
                    }
                }
                finally { Marshal.ReleaseComObject(dev); }
            }
            Marshal.ReleaseComObject(coll);

            if (candidates.Count == 0)
            {
                return AudioResult.NotFound;
            }
            // Prefer an active device: exact id first, then same name (the id can change after a driver reinstall).
            var pick = candidates.Where(c => c.active).OrderByDescending(c => c.byId).ToList();
            if (pick.Count == 0)
            {
                return AudioResult.NotReady;   // e.g. TV HDMI audio before the TV display is up
            }

            string matchId = pick[0].id;
            if (string.Equals(matchId, currentDefault, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(matchId, currentComms, StringComparison.OrdinalIgnoreCase))
            {
                return AudioResult.AlreadySet;
            }

            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                Check(policy.SetDefaultEndpoint(matchId, ERole.Console));
                Check(policy.SetDefaultEndpoint(matchId, ERole.Multimedia));
                Check(policy.SetDefaultEndpoint(matchId, ERole.Communications));
            }
            finally { Marshal.ReleaseComObject(policy); }
            return string.Equals(matchId, currentDefault, StringComparison.OrdinalIgnoreCase) ? AudioResult.AlreadySet : AudioResult.Done;
        }
        finally { Marshal.ReleaseComObject(en); }
    }

    public static AudioDevice? CurrentDefault(AudioFlow flow = AudioFlow.Output) => List(flow).FirstOrDefault(d => d.IsDefault);

    // ---- helpers -------------------------------------------------------------------------------
    static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumerator();

    static string? DefaultId(IMMDeviceEnumerator en, AudioFlow flow, ERole role = ERole.Multimedia)
    {
        if (en.GetDefaultAudioEndpoint((EDataFlow)flow, role, out var dev) != 0 || dev == null)
        {
            return null;
        }

        try { dev.GetId(out var id); return id; }
        finally { Marshal.ReleaseComObject(dev); }
    }

    static string? FriendlyName(IMMDevice dev)
    {
        if (dev.OpenPropertyStore(STGM_READ, out var store) != 0)
        {
            return null;
        }

        try
        {
            var key = PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out var pv) != 0)
            {
                return null;
            }

            try { return pv.vt == VT_LPWSTR ? Marshal.PtrToStringUni(pv.p) : null; }
            finally { PropVariantClear(ref pv); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    static void Check(int hr)
    {
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    // ---- COM interop ---------------------------------------------------------------------------
    const uint DEVICE_STATE_ACTIVE = 1, DEVICE_STATE_NOTPRESENT = 4, DEVICE_STATE_UNPLUGGED = 8;
    const int STGM_READ = 0;
    const ushort VT_LPWSTR = 31;
    static readonly PROPERTYKEY PKEY_Device_FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

    enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt; ushort _r1, _r2, _r3; public IntPtr p; public IntPtr p2; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pvar);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }

    // Method order matters (vtable). Only SetDefaultEndpoint is used.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
