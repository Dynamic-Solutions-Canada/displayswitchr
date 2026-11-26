<#
Set every active display to one display scale, as Settings > Display > Scale does, through the
DPI-scale request Settings itself sends (DisplayConfigSetDeviceInfo). Language-neutral: no UI is
involved. Run in the desktop session (ASCII only). The change lasts for the throwaway -snapshot
session the captures run in.

Windows expresses the scale relative to each display's recommended one, in steps of the list below;
the minimum relative step is 100%. Not yet run on the VM at 200%: a display whose largest step is
below 200% (too few pixels) is left at its largest step and reported in the log.
#>
param(
    # The scale in percent: 200 for Store captures (3840 x 2160 for the 1920 x 1080 logical desktop).
    [ValidateSet(100, 125, 150, 175, 200)] [int]$Percent = 200
)
$ErrorActionPreference = 'Stop'

$log = 'C:\apptest\out\scale.txt'
New-Item C:\apptest\out -ItemType Directory -Force | Out-Null

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Dpi {
  [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }
  [StructLayout(LayoutKind.Sequential)] public struct HEADER { public int type; public int size; public LUID adapterId; public uint id; }
  [StructLayout(LayoutKind.Sequential)] public struct GET { public HEADER header; public int minRel; public int curRel; public int maxRel; }
  [StructLayout(LayoutKind.Sequential)] public struct SET { public HEADER header; public int rel; }
  [StructLayout(LayoutKind.Sequential)] public struct PATH_SOURCE { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
  [StructLayout(LayoutKind.Sequential)] public struct PATH_TARGET { public LUID adapterId; public uint id; public uint modeInfoIdx; public int outputTechnology; public int rotation; public int scaling; public uint rrNum; public uint rrDen; public int scanLineOrdering; public bool targetAvailable; public uint statusFlags; }
  [StructLayout(LayoutKind.Sequential)] public struct PATH { public PATH_SOURCE source; public PATH_TARGET target; public uint flags; }
  [StructLayout(LayoutKind.Sequential)] public struct MODE { public int infoType; public uint id; public LUID adapterId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] data; }
  [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
  [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint np, [Out] PATH[] p, ref uint nm, [Out] MODE[] m, IntPtr topo);
  [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref GET g);
  [DllImport("user32.dll")] public static extern int DisplayConfigSetDeviceInfo(ref SET s);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
}
"@

# The scale steps Windows offers, in order; the relative values count steps from the recommended one.
$steps = @(100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500)
$targetIndex = [Array]::IndexOf($steps, $Percent)

# Per-monitor aware, so the requests address the real (unscaled) displays.
[Dpi]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$np = 0
$nm = 0
[Dpi]::GetDisplayConfigBufferSizes(2, [ref]$np, [ref]$nm) | Out-Null   # QDC_ONLY_ACTIVE_PATHS
$paths = New-Object Dpi+PATH[] $np
$modes = New-Object Dpi+MODE[] $nm
$r = [Dpi]::QueryDisplayConfig(2, [ref]$np, $paths, [ref]$nm, $modes, [IntPtr]::Zero)
if ($r -ne 0) { throw "QueryDisplayConfig $r" }

Set-Content $log "scale $Percent% $(Get-Date -Format o)"
for ($i = 0; $i -lt $np; $i++) {
    # Read the display's range: minRel is 100%, so the recommended step is at index -minRel.
    $g = New-Object Dpi+GET
    $g.header.type = -3   # DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE
    $g.header.size = [Runtime.InteropServices.Marshal]::SizeOf([type][Dpi+GET])
    $g.header.adapterId = $paths[$i].source.adapterId
    $g.header.id = $paths[$i].source.id
    $r = [Dpi]::DisplayConfigGetDeviceInfo([ref]$g)
    Add-Content $log "source $($paths[$i].source.id): get $r min $($g.minRel) cur $($g.curRel) max $($g.maxRel)"
    if ($r -ne 0) { continue }

    $rel = $targetIndex + $g.minRel
    if ($rel -gt $g.maxRel) {
        Add-Content $log "source $($paths[$i].source.id): $Percent% is above this display's largest step; using it"
        $rel = $g.maxRel
    }
    if ($rel -eq $g.curRel) { continue }

    $s = New-Object Dpi+SET
    $s.header.type = -4   # DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE
    $s.header.size = [Runtime.InteropServices.Marshal]::SizeOf([type][Dpi+SET])
    $s.header.adapterId = $paths[$i].source.adapterId
    $s.header.id = $paths[$i].source.id
    $s.rel = $rel
    $r = [Dpi]::DisplayConfigSetDeviceInfo([ref]$s)
    Add-Content $log "source $($paths[$i].source.id): set rel $rel -> $r"
}

Add-Type -AssemblyName System.Windows.Forms
Start-Sleep -Seconds 3
Add-Content $log "screen now: $([System.Windows.Forms.Screen]::PrimaryScreen.Bounds)"
