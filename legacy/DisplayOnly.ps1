<#
  DisplayOnly.ps1 - make Windows show the desktop on ONE monitor only, chosen by name.
  Usage:  .\DisplayOnly.ps1 -Monitor "LG TV"
          .\DisplayOnly.ps1 -List
  Uses the Windows CCD API (QueryDisplayConfig / SetDisplayConfig), the same one Win+P uses.
#>
param([string]$Monitor, [switch]$List)

if (-not ('Ccd.Display' -as [type])) {
Add-Type -Language CSharp @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace Ccd {
  [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }
  [StructLayout(LayoutKind.Sequential)] public struct SourceInfo { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
  [StructLayout(LayoutKind.Sequential)] public struct TargetInfo {
    public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling;
    public uint refreshNum; public uint refreshDen; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags; }
  [StructLayout(LayoutKind.Sequential)] public struct PathInfo { public SourceInfo source; public TargetInfo target; public uint flags; }
  [StructLayout(LayoutKind.Sequential)] public struct ModeInfo { public uint infoType; public uint id; public LUID adapterId;
    public ulong d0, d1, d2, d3, d4, d5; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct TargetName {
    public uint type; public uint size; public LUID adapterId; public uint id;
    public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string friendlyName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string devicePath; }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct SourceName {
    public uint type; public uint size; public LUID adapterId; public uint id;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdiName; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DEVMODE {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
    public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
    public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
    public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
    public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight; }

  public static class Display {
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SourceName req);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr param);

    // For every active display: switch to the highest refresh rate available at its current resolution.
    public static string MaxRefresh() {
      var log = new List<string>();
      uint np, nm; GetDisplayConfigBufferSizes(2, out np, out nm);           // QDC_ONLY_ACTIVE_PATHS
      var p = new PathInfo[np]; var m = new ModeInfo[nm];
      if (QueryDisplayConfig(2, ref np, p, ref nm, m, IntPtr.Zero) != 0) return "query failed";
      for (int k = 0; k < np; k++) {
        var sn = new SourceName(); sn.type = 1; sn.size = (uint)Marshal.SizeOf(typeof(SourceName));
        sn.adapterId = p[k].source.adapterId; sn.id = p[k].source.id;
        if (DisplayConfigGetDeviceInfo(ref sn) != 0) continue;
        var cur = new DEVMODE(); cur.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (!EnumDisplaySettings(sn.gdiName, -1, ref cur)) continue;           // ENUM_CURRENT_SETTINGS
        DEVMODE best = cur; bool found = false;
        var dm = new DEVMODE(); dm.dmSize = cur.dmSize;
        for (int i = 0; EnumDisplaySettings(sn.gdiName, i, ref dm); i++) {
          if (dm.dmPelsWidth != cur.dmPelsWidth || dm.dmPelsHeight != cur.dmPelsHeight) continue;
          if ((dm.dmDisplayFlags & 2) != 0) continue;                         // skip interlaced
          if (dm.dmDisplayFrequency > best.dmDisplayFrequency) { best = dm; found = true; }
        }
        string who = Name(p[k].target);
        if (!found) { log.Add(who + ": already " + cur.dmDisplayFrequency + " Hz"); continue; }
        best.dmFields = 0x80000 | 0x100000 | 0x400000;                        // width, height, frequency
        int r = ChangeDisplaySettingsEx(sn.gdiName, ref best, IntPtr.Zero, 1, IntPtr.Zero); // CDS_UPDATEREGISTRY
        log.Add(who + ": " + cur.dmDisplayFrequency + " -> " + best.dmDisplayFrequency + " Hz" + (r == 0 ? "" : " (error " + r + ")"));
      }
      return string.Join("; ", log);
    }
    const uint QDC_ALL_PATHS = 1, PATH_ACTIVE = 1, IDX_INVALID = 0xffffffff;
    const uint SDC_USE_SUPPLIED = 0x20, SDC_APPLY = 0x80, SDC_SAVE = 0x200, SDC_ALLOW_CHANGES = 0x400;
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint nPath, out uint nMode);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint nPath, [Out] PathInfo[] paths, ref uint nMode, [Out] ModeInfo[] modes, IntPtr topo);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint nPath, [In] PathInfo[] paths, uint nMode, [In] ModeInfo[] modes, uint flags);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TargetName req);

    static PathInfo[] Query() {
      uint np, nm; int r = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out np, out nm);
      if (r != 0) throw new Exception("GetDisplayConfigBufferSizes failed: " + r);
      var p = new PathInfo[np]; var m = new ModeInfo[nm];
      r = QueryDisplayConfig(QDC_ALL_PATHS, ref np, p, ref nm, m, IntPtr.Zero);
      if (r != 0) throw new Exception("QueryDisplayConfig failed: " + r);
      Array.Resize(ref p, (int)np); return p;
    }
    static string Name(TargetInfo t) {
      var n = new TargetName(); n.type = 2; n.size = (uint)Marshal.SizeOf(typeof(TargetName)); n.adapterId = t.adapterId; n.id = t.id;
      return DisplayConfigGetDeviceInfo(ref n) == 0 ? n.friendlyName : "";
    }
    // Returns "name|active" for every connected monitor.
    public static List<string> List() {
      var seen = new Dictionary<string, bool>();
      foreach (var p in Query()) {
        if (p.target.targetAvailable == 0) continue;
        string key = Name(p.target) + "#" + p.target.id;
        bool act = (p.flags & PATH_ACTIVE) != 0;
        if (!seen.ContainsKey(key)) seen[key] = act; else seen[key] |= act;
      }
      var res = new List<string>(); foreach (var kv in seen) res.Add(kv.Key.Split('#')[0] + "|" + kv.Value); return res;
    }
    // Activates only the monitor whose friendly name contains `match`. Returns 0 on success.
    public static int Only(string match) {
      PathInfo? chosen = null;
      foreach (var p in Query()) {
        if (p.target.targetAvailable == 0) continue;
        if (Name(p.target).IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;
        if ((p.flags & PATH_ACTIVE) != 0) { chosen = p; break; }   // prefer its current path
        if (chosen == null) chosen = p;
      }
      if (chosen == null) throw new Exception("Monitor '" + match + "' is not connected / available (is it switched on?)");
      var path = chosen.Value;
      path.flags = PATH_ACTIVE; path.source.modeInfoIdx = IDX_INVALID; path.target.modeInfoIdx = IDX_INVALID;
      return SetDisplayConfig(1, new[] { path }, 0, null, SDC_APPLY | SDC_USE_SUPPLIED | SDC_ALLOW_CHANGES | SDC_SAVE);
    }
  }
}
"@
}

if ($List) {
    [Ccd.Display]::List() | ForEach-Object { $n, $a = $_ -split '\|'; [pscustomobject]@{ Monitor = $n; Active = [bool]::Parse($a) } }
    return
}
if (-not $Monitor) { return }   # dot-sourced: just load the type
$r = [Ccd.Display]::Only($Monitor)
if ($r -ne 0) { throw "SetDisplayConfig failed with error $r" }
"Now showing only: $Monitor"
Start-Sleep -Seconds 2
"Refresh: " + [Ccd.Display]::MaxRefresh()
