using System.Runtime.InteropServices;

namespace HubDisplaySwitch.Services;

/// <summary>A monitor Windows can see. Id is the connector's device path (stable across reboots).</summary>
public sealed record MonitorInfo(string Id, string Name, bool Active);

/// <summary>
/// Display layout control via the Windows CCD API (QueryDisplayConfig / SetDisplayConfig, the same API Win+P uses)
/// plus the legacy ChangeDisplaySettingsEx API for refresh rate and primary-display changes.
/// </summary>
public static class DisplayConfig
{
    // ---- Win32 structs -------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    struct SourceInfo { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct TargetInfo
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation;
        public uint scaling; public uint refreshNum; public uint refreshDen; public uint scanLineOrdering;
        public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PathInfo { public SourceInfo source; public TargetInfo target; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct ModeInfo { public uint infoType; public uint id; public LUID adapterId; public ulong d0, d1, d2, d3, d4, d5; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TargetName
    {
        public uint type; public uint size; public LUID adapterId; public uint id;
        public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string friendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string devicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SourceName
    {
        public uint type; public uint size; public LUID adapterId; public uint id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdiName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    // ---- Win32 functions -----------------------------------------------------------------------
    const uint QDC_ALL_PATHS = 1, QDC_ONLY_ACTIVE_PATHS = 2, PATH_ACTIVE = 1, IDX_INVALID = 0xffffffff;
    const uint SDC_TOPOLOGY_CLONE = 2, SDC_TOPOLOGY_EXTEND = 8, SDC_USE_SUPPLIED = 0x20, SDC_APPLY = 0x80,
               SDC_SAVE = 0x200, SDC_ALLOW_CHANGES = 0x400, SDC_ALLOW_PATH_ORDER_CHANGES = 0x2000;
    const int ENUM_CURRENT_SETTINGS = -1;
    const uint CDS_UPDATEREGISTRY = 1, CDS_SET_PRIMARY = 0x10, CDS_NORESET = 0x10000000;
    const int DM_POSITION = 0x20, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint nPath, out uint nMode);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint nPath, [Out] PathInfo[] paths, ref uint nMode, [Out] ModeInfo[] modes, IntPtr topo);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint nPath, [In] PathInfo[]? paths, uint nMode, [In] ModeInfo[]? modes, uint flags);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TargetName req);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SourceName req);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string? dev, IntPtr dm, IntPtr hwnd, uint flags, IntPtr param);

    // ---- helpers -------------------------------------------------------------------------------
    static PathInfo[] Query(uint flags)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int r = GetDisplayConfigBufferSizes(flags, out uint np, out uint nm);
            if (r != 0)
            {
                throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed ({r})");
            }

            var p = new PathInfo[np]; var m = new ModeInfo[nm];
            r = QueryDisplayConfig(flags, ref np, p, ref nm, m, IntPtr.Zero);
            if (r == 122)
            {
                continue; // ERROR_INSUFFICIENT_BUFFER: config changed between calls, retry
            }

            if (r != 0)
            {
                throw new InvalidOperationException($"QueryDisplayConfig failed ({r})");
            }

            Array.Resize(ref p, (int)np);
            return p;
        }
        throw new InvalidOperationException("QueryDisplayConfig kept changing");
    }

    static TargetName GetTargetName(TargetInfo t)
    {
        var n = new TargetName { type = 2, size = (uint)Marshal.SizeOf<TargetName>(), adapterId = t.adapterId, id = t.id };
        return DisplayConfigGetDeviceInfo(ref n) == 0 ? n : new TargetName { friendlyName = "", devicePath = "" };
    }

    static string? GetGdiName(SourceInfo s)
    {
        var n = new SourceName { type = 1, size = (uint)Marshal.SizeOf<SourceName>(), adapterId = s.adapterId, id = s.id };
        return DisplayConfigGetDeviceInfo(ref n) == 0 ? n.gdiName : null;
    }

    static string FriendlyName(TargetName n) => string.IsNullOrWhiteSpace(n.friendlyName) ? "Unknown display" : n.friendlyName;

    /// <summary>True if the target is the requested monitor: match by device path, else by name.</summary>
    static bool IsMonitor(TargetName n, string? id, string name, bool byIdOnly) =>
        byIdOnly ? !string.IsNullOrEmpty(id) && string.Equals(n.devicePath, id, StringComparison.OrdinalIgnoreCase)
                 : string.Equals(FriendlyName(n), name, StringComparison.OrdinalIgnoreCase);

    // ---- public API ----------------------------------------------------------------------------

    /// <summary>All monitors that are connected and available (on), active or not.</summary>
    public static List<MonitorInfo> GetMonitors()
    {
        var result = new Dictionary<string, MonitorInfo>();
        foreach (var p in Query(QDC_ALL_PATHS))
        {
            if (p.target.targetAvailable == 0)
            {
                continue;
            }

            var n = GetTargetName(p.target);
            string key = string.IsNullOrEmpty(n.devicePath) ? $"{p.target.adapterId.Low}:{p.target.id}" : n.devicePath;
            bool active = (p.flags & PATH_ACTIVE) != 0;
            result[key] = result.TryGetValue(key, out var existing)
                ? existing with { Active = existing.Active || active }
                : new MonitorInfo(key, FriendlyName(n), active);
        }
        return result.Values.ToList();
    }

    /// <summary>
    /// Turn on exactly these monitors (everything else off). With <paramref name="mirror"/>, they share one
    /// picture where the GPU allows it. Missing monitors throw <see cref="MonitorNotFoundException"/> unless
    /// <paramref name="skipMissing"/> is set (then the rest are applied, as long as at least one is available).
    /// </summary>
    public static void SetActive(IReadOnlyList<(string? id, string name)> monitors, bool mirror, bool skipMissing = false)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("No displays chosen");
        }

        if (monitors.Count == 1 && !mirror) { ShowOnly(monitors[0].id, monitors[0].name); return; }

        var all = Query(QDC_ALL_PATHS).Where(p => p.target.targetAvailable != 0)
                                      .Select(p => (path: p, name: GetTargetName(p.target))).ToList();
        var chosen = new List<PathInfo>();
        var usedSources = new HashSet<(uint, int, uint)>();
        var usedTargets = new HashSet<(uint, int, uint)>();
        (uint, int, uint)? mirrorSource = null;

        foreach (var (id, name) in monitors)
        {
            var c = all.Where(x => IsMonitor(x.name, id, name, true)).ToList();
            if (c.Count == 0)
            {
                c = all.Where(x => IsMonitor(x.name, id, name, false)).ToList();
            }

            c = c.Where(x => !usedTargets.Contains(Key(x.path.target.adapterId, x.path.target.id))).ToList();
            if (c.Count == 0)
            {
                if (skipMissing)
                {
                    continue;
                }

                throw new MonitorNotFoundException(name);
            }

            // Prefer the monitor's current path, then any path whose source is still free.
            var ordered = c.OrderByDescending(x => (x.path.flags & PATH_ACTIVE) != 0).Select(x => x.path).ToList();
            PathInfo? pick = null;
            if (mirror && mirrorSource is { } ms)
            {
                pick = ordered.Cast<PathInfo?>().FirstOrDefault(p => Key(p!.Value.source.adapterId, p.Value.source.id) == ms);
            }

            pick ??= ordered.Cast<PathInfo?>().FirstOrDefault(p => !usedSources.Contains(Key(p!.Value.source.adapterId, p.Value.source.id)));
            if (pick == null) { if (skipMissing) { continue; } throw new InvalidOperationException($"No free video output for '{name}'"); }

            var path = pick.Value;
            var src = Key(path.source.adapterId, path.source.id);
            usedSources.Add(src);
            usedTargets.Add(Key(path.target.adapterId, path.target.id));
            mirrorSource ??= src;
            path.flags = PATH_ACTIVE;
            path.source.modeInfoIdx = IDX_INVALID;
            path.target.modeInfoIdx = IDX_INVALID;
            chosen.Add(path);
        }
        if (chosen.Count == 0)
        {
            throw new MonitorNotFoundException(monitors[0].name);
        }

        int r = SetDisplayConfig((uint)chosen.Count, chosen.ToArray(), 0, null,
                                 SDC_APPLY | SDC_USE_SUPPLIED | SDC_ALLOW_CHANGES | SDC_SAVE | SDC_ALLOW_PATH_ORDER_CHANGES);
        if (r != 0)
        {
            throw new InvalidOperationException($"SetDisplayConfig failed ({r})");
        }
    }

    static (uint, int, uint) Key(LUID adapter, uint id) => (adapter.Low, adapter.High, id);

    /// <summary>Show the desktop on this monitor only. Throws if the monitor is not connected/on.</summary>
    public static void ShowOnly(string? id, string name)
    {
        var all = Query(QDC_ALL_PATHS).Where(p => p.target.targetAvailable != 0).ToList();
        // Prefer the monitor's currently active path, otherwise any path that drives it.
        var withNames = all.Select(p => (path: p, name: GetTargetName(p.target))).ToList();
        var candidates = withNames.Where(x => IsMonitor(x.name, id, name, true)).ToList();
        if (candidates.Count == 0)
        {
            candidates = withNames.Where(x => IsMonitor(x.name, id, name, false)).ToList();
        }

        if (candidates.Count == 0)
        {
            throw new MonitorNotFoundException(name);
        }

        var activeOnes = candidates.Where(x => (x.path.flags & PATH_ACTIVE) != 0).ToList();
        var path = (activeOnes.Count > 0 ? activeOnes[0] : candidates[0]).path;
        path.flags = PATH_ACTIVE;
        path.source.modeInfoIdx = IDX_INVALID;
        path.target.modeInfoIdx = IDX_INVALID;
        int r = SetDisplayConfig(1, new[] { path }, 0, null, SDC_APPLY | SDC_USE_SUPPLIED | SDC_ALLOW_CHANGES | SDC_SAVE);
        if (r != 0)
        {
            throw new InvalidOperationException($"SetDisplayConfig failed ({r})");
        }
    }

    /// <summary>Extend the desktop across all connected monitors, with this one as the main display.</summary>
    public static void ExtendWithPrimary(string? id, string name)
    {
        if (!GetMonitors().Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MonitorNotFoundException(name);
        }

        int r = SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_EXTEND);
        if (r != 0)
        {
            throw new InvalidOperationException($"SetDisplayConfig (extend) failed ({r})");
        }

        Thread.Sleep(800);
        SetPrimary(id, name);
    }

    /// <summary>Duplicate the desktop on all connected monitors.</summary>
    public static void Duplicate()
    {
        int r = SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_CLONE);
        if (r != 0)
        {
            throw new InvalidOperationException($"SetDisplayConfig (duplicate) failed ({r})");
        }
    }

    /// <summary>GDI device name (\\.\DISPLAYn) of an active monitor, or null.</summary>
    static string? ActiveGdiName(string? id, string name)
    {
        var active = Query(QDC_ONLY_ACTIVE_PATHS).Select(p => (path: p, name: GetTargetName(p.target)));
        var hit = active.FirstOrDefault(x => IsMonitor(x.name, id, name, true));
        if (hit.name.devicePath == null)
        {
            hit = active.FirstOrDefault(x => IsMonitor(x.name, id, name, false));
        }

        return hit.name.devicePath == null ? null : GetGdiName(hit.path.source);
    }

    /// <summary>Make an active monitor the main display by moving it to (0,0) and shifting the others.</summary>
    public static void SetPrimary(string? id, string name)
    {
        string? primary = ActiveGdiName(id, name) ?? throw new MonitorNotFoundException(name);
        var gdiNames = Query(QDC_ONLY_ACTIVE_PATHS).Select(p => GetGdiName(p.source)).OfType<string>().Distinct().ToList();

        var pm = NewDevMode();
        if (!EnumDisplaySettings(primary, ENUM_CURRENT_SETTINGS, ref pm))
        {
            return;
        }

        int dx = pm.dmPositionX, dy = pm.dmPositionY;
        if (dx == 0 && dy == 0)
        {
            return; // already primary
        }

        foreach (var gdi in gdiNames)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettings(gdi, ENUM_CURRENT_SETTINGS, ref dm))
            {
                continue;
            }

            dm.dmPositionX -= dx; dm.dmPositionY -= dy; dm.dmFields = DM_POSITION;
            uint flags = CDS_UPDATEREGISTRY | CDS_NORESET | (gdi == primary ? CDS_SET_PRIMARY : 0);
            ChangeDisplaySettingsEx(gdi, ref dm, IntPtr.Zero, flags, IntPtr.Zero);
        }
        ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero); // apply all pending changes
    }

    static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };

    /// <summary>Refresh rates available at the monitor's current resolution (empty if it's not active).</summary>
    public static List<int> GetRefreshRates(string? id, string name)
    {
        var gdi = ActiveGdiName(id, name);
        if (gdi == null)
        {
            return new();
        }

        var cur = NewDevMode();
        if (!EnumDisplaySettings(gdi, ENUM_CURRENT_SETTINGS, ref cur))
        {
            return new();
        }

        var rates = new SortedSet<int>();
        var dm = NewDevMode();
        for (int i = 0; EnumDisplaySettings(gdi, i, ref dm); i++)
        {
            if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight && (dm.dmDisplayFlags & 2) == 0)
            {
                rates.Add(dm.dmDisplayFrequency);
            }
        }

        return rates.Reverse().ToList();
    }

    /// <summary>Current refresh rate of an active monitor, or 0.</summary>
    public static int GetCurrentRefreshRate(string? id, string name)
    {
        var gdi = ActiveGdiName(id, name);
        var cur = NewDevMode();
        return gdi != null && EnumDisplaySettings(gdi, ENUM_CURRENT_SETTINGS, ref cur) ? cur.dmDisplayFrequency : 0;
    }

    /// <summary>
    /// Apply a refresh-rate preference to an active monitor at its current resolution.
    /// preferred: 0 = highest available, >0 = that rate (or the closest lower one). Returns a description if it changed.
    /// </summary>
    public static string? ApplyRefreshRate(string? id, string name, int preferred) => ApplyRefreshRate(id, name, preferred, dryRun: false);

    /// <summary>True if <see cref="ApplyRefreshRate(string?, string, int)"/> would change anything right now.</summary>
    public static bool RefreshRateNeedsChange(string? id, string name, int preferred) =>
        preferred >= 0 && ApplyRefreshRate(id, name, preferred, dryRun: true) != null;

    /// <summary>True if the monitor is active and is the main display (at 0,0).</summary>
    public static bool IsPrimary(string? id, string name)
    {
        var gdi = ActiveGdiName(id, name);
        var dm = NewDevMode();
        return gdi != null && EnumDisplaySettings(gdi, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmPositionX == 0 && dm.dmPositionY == 0;
    }

    private static string? ApplyRefreshRate(string? id, string name, int preferred, bool dryRun)
    {
        var gdi = ActiveGdiName(id, name);
        if (gdi == null)
        {
            return null;
        }

        var cur = NewDevMode();
        if (!EnumDisplaySettings(gdi, ENUM_CURRENT_SETTINGS, ref cur))
        {
            return null;
        }

        DEVMODE? best = null;
        var dm = NewDevMode();
        for (int i = 0; EnumDisplaySettings(gdi, i, ref dm); i++)
        {
            if (dm.dmPelsWidth != cur.dmPelsWidth || dm.dmPelsHeight != cur.dmPelsHeight || (dm.dmDisplayFlags & 2) != 0)
            {
                continue;
            }

            if (preferred > 0 && dm.dmDisplayFrequency > preferred)
            {
                continue;
            }

            if (best == null || dm.dmDisplayFrequency > best.Value.dmDisplayFrequency)
            {
                best = dm;
            }
        }
        if (best == null || best.Value.dmDisplayFrequency == cur.dmDisplayFrequency)
        {
            return null;
        }

        if (dryRun)
        {
            return $"{name}: {cur.dmDisplayFrequency} → {best.Value.dmDisplayFrequency} Hz";
        }

        var target = best.Value;
        target.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        int r = ChangeDisplaySettingsEx(gdi, ref target, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        return $"{name}: {cur.dmDisplayFrequency} → {target.dmDisplayFrequency} Hz" + (r == 0 ? "" : $" (error {r})");
    }
}

public sealed class MonitorNotFoundException(string name)
    : Exception($"'{name}' isn't connected or is switched off")
{
    public string DisplayName { get; } = name;
}
