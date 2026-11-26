<#
Name the capture VM's two HD Audio playback devices as a user of the display language would see them
(ASCII only). Run in the desktop session (app_test_ui), in any display language, after the language
pack is installed and Windows is in that language:

- the HD Audio devices get Windows' own description in that language ("High Definition Audio Device",
  "High Definition Audio-Geraet" in German, ...), which Windows shows in brackets after each playback
  device's name;
- the first playback device gets Windows' own name for speakers in that language ("Speakers",
  "Lautsprecher", ...): it is the desk speakers;
- the second is renamed "LG TV", the TV's own name, the same in every language.

Windows names devices once, in the language it was installed in (English on the VM), so without this a
ja-JP capture would show the English names a Japanese PC never would. The localized names are read from
Windows' own driver strings (windows-strings.ps1), never translated by hand.

Nothing is found by its translated text: the Sound dialog and the device's Properties dialog are the
#32770 dialogs of the rundll32 process this script starts (the first is the Sound dialog, the second
the Properties dialog it owns), the device list and the name box by their window class, and the OK
buttons by their control ID (IDOK, 1). The managed UI Automation client shows the list's items as
plain panes with no selection state, so a device is selected with Home and Down, and its Properties
opened from the list's context menu (Shift+F10, then Up to its last item, Properties, in every
language). The name is set as the box's text (WM_SETTEXT), not typed, so any script arrives intact.
#>
$ErrorActionPreference = 'Stop'
. C:\apptest\ui-lib.ps1
. (Join-Path $PSScriptRoot 'windows-strings.ps1')
Add-Type -AssemblyName System.Windows.Forms

$log = Join-Path $UiOut 'name-audio.txt'
New-Item $UiOut -ItemType Directory -Force | Out-Null
Set-Content $log "name-audio $(Get-Date -Format o)" -Encoding UTF8

<# Append a line to the log. #>
function Note([string]$Message) {
    Add-Content $log $Message -Encoding UTF8
}

<# The first descendant of $Root with this window class, or $null. #>
function Find-ByClass($Root, [string]$Class) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UiA::ClassNameProperty, $Class)
    $Root.FindFirst('Descendants', $cond)
}

<# The OK button (control ID 1, IDOK) of a Win32 dialog, which UI Automation reports as its AutomationId. #>
function Find-OkButton($Dialog) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UiA::AutomationIdProperty, '1')
    $ok = $Dialog.FindFirst('Descendants', $cond)
    if (-not $ok) { throw "no OK button in '$($Dialog.Current.Name)'" }
    $ok
}

Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class SoundDialogs {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

  // The visible top-level #32770 dialogs of one process, owned ones included, in Z order (topmost first).
  public static IntPtr[] Of(int processId) {
    var found = new List<IntPtr>();
    EnumWindows((h, l) => {
      uint pid;
      GetWindowThreadProcessId(h, out pid);
      var name = new StringBuilder(16);
      GetClassName(h, name, 16);
      if (pid == (uint)processId && name.ToString() == "#32770" && IsWindowVisible(h)) { found.Add(h); }
      return true;
    }, IntPtr.Zero);
    return found.ToArray();
  }
}

// Sets a device's friendly name (SPDRP_FRIENDLYNAME) through SetupAPI, the documented way to name a
// device instance; needs an elevated process, which every process on the VM is (UAC is off there).
public static class DeviceNames {
  [StructLayout(LayoutKind.Sequential)]
  struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwnd);
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern bool SetupDiOpenDeviceInfo(IntPtr set, string instanceId, IntPtr hwnd, int flags, ref SP_DEVINFO_DATA data);
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern bool SetupDiSetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA data, int property, byte[] buffer, int size);
  [DllImport("setupapi.dll", SetLastError = true)]
  static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, StringBuilder l);

  // An edit control's text (WM_GETTEXT) and setting it (WM_SETTEXT).
  public static string GetText(IntPtr edit) { var b = new StringBuilder(512); SendMessage(edit, 0x000D, (IntPtr)512, b); return b.ToString(); }
  public static void SetText(IntPtr edit, string text) { SendMessage(edit, 0x000C, IntPtr.Zero, text); }

  public static void SetFriendlyName(string instanceId, string name) {
    IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
    try {
      var data = new SP_DEVINFO_DATA(); data.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
      if (!SetupDiOpenDeviceInfo(set, instanceId, IntPtr.Zero, 0, ref data)) throw new System.ComponentModel.Win32Exception();
      byte[] bytes = Encoding.Unicode.GetBytes(name + "\0");
      if (!SetupDiSetDeviceRegistryProperty(set, ref data, 0x0C /* SPDRP_FRIENDLYNAME */, bytes, bytes.Length)) throw new System.ComponentModel.Win32Exception();
    } finally { SetupDiDestroyDeviceInfoList(set); }
  }
}
"@

<#
The visible #32770 dialogs of process $ProcessId as UI Automation elements, waiting until at least
$Count are open (up to 20 s). Found through Win32 (EnumWindows), because the managed client's walk of
the desktop's children did not list the owned Properties dialog when this was written for English.
#>
function Get-ProcessDialogs([int]$ProcessId, [int]$Count) {
    for ($i = 0; $i -lt 20; $i++) {
        $handles = [SoundDialogs]::Of($ProcessId)
        if ($handles.Count -ge $Count) {
            return @($handles | ForEach-Object { [System.Windows.Automation.AutomationElement]::FromHandle($_) })
        }
        Start-Sleep -Seconds 1
    }
    throw "process $ProcessId has $($handles.Count) dialog(s), expected $Count"
}

<#
Give each present HD Audio function device Windows' description in $Language as its friendly name,
then restart it, so the playback devices' bracketed names (read when the device starts) follow.
Skipped for a device whose name already matches.
#>
function Set-AudioDeviceNames([string]$Language) {
    $devices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'HDAUDIO\FUNC_01*' })
    if ($devices.Count -lt 2) { throw "expected two HD Audio devices, found $($devices.Count)" }
    foreach ($device in $devices) {
        $name = Resolve-DeviceText (Get-DeviceDescReference $device.InstanceId) $Language
        # Compare with the stored friendly name, not Get-PnpDevice's, which falls back to the
        # description resolved in the session's own language and would hide an unset name.
        $stored = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Enum\$($device.InstanceId)" -Name FriendlyName -ErrorAction SilentlyContinue).FriendlyName
        if ($stored -eq $name) {
            Note "device $($device.InstanceId): already '$name'"
            continue
        }
        [DeviceNames]::SetFriendlyName($device.InstanceId, $name)
        & pnputil.exe /restart-device "$($device.InstanceId)" | Out-Null
        Note "device $($device.InstanceId): '$($device.FriendlyName)' -> '$name' (restart exit $LASTEXITCODE)"
    }
    # Let the audio endpoints come back after the restarts.
    Start-Sleep -Seconds 8
}

<#
Rename the playback device at list position $Index (0 is the first) to $Name through the Sound
dialog's Properties > General name box.
#>
function Rename-PlaybackDevice([int]$Index, [string]$Name) {
    # Close any Sound dialog an earlier attempt left open.
    Get-Process rundll32 -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    # The Sound dialog, Playback tab. Look it up again once it has settled: found while it is still
    # being created, it has no title yet and could not be made the foreground window.
    $proc = Start-Process rundll32.exe 'shell32.dll,Control_RunDLL mmsys.cpl,,0' -PassThru
    $sound = (Get-ProcessDialogs $proc.Id 1)[0]
    Start-Sleep -Seconds 3
    $sound = (Get-ProcessDialogs $proc.Id 1)[0]
    Set-WindowActive $sound

    # Select the device by position, then open its Properties from its context menu (Shift+F10, then
    # Up wraps to the last item, Properties, in every language).
    $list = Find-ByClass $sound 'SysListView32'
    if (-not $list) { throw 'no device list' }
    Invoke-Click $list
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait('{HOME}')
    Start-Sleep -Milliseconds 300
    for ($i = 0; $i -lt $Index; $i++) {
        [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait('+{F10}')
    Start-Sleep -Milliseconds 800
    [System.Windows.Forms.SendKeys]::SendWait('{UP}')
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

    # The Properties dialog: the process's other dialog. Its General tab opens with the name box first.
    $dialogs = Get-ProcessDialogs $proc.Id 2
    $props = $dialogs | Where-Object { $_.Current.NativeWindowHandle -ne $sound.Current.NativeWindowHandle } | Select-Object -First 1
    Start-Sleep -Seconds 2
    Set-WindowActive $props
    $edit = Find-ByClass $props 'Edit'
    if (-not $edit) { throw 'no name box' }
    # The box offers no UI Automation value pattern, so set its text with WM_SETTEXT, which a
    # single-line edit control reports to the dialog as a change, like typing.
    $handle = [IntPtr]$edit.Current.NativeWindowHandle
    $old = [DeviceNames]::GetText($handle)
    Invoke-Click $edit
    [DeviceNames]::SetText($handle, $Name)
    Start-Sleep -Milliseconds 500
    $now = [DeviceNames]::GetText($handle)
    if ($now -ne $Name) { throw "the name box reads '$now', not '$Name'" }
    Note "playback device ${Index}: '$old' -> '$Name'"

    Invoke-Click (Find-OkButton $props)
    Start-Sleep -Seconds 2
    Invoke-Click (Find-OkButton $sound)
    Start-Sleep -Seconds 2
}

# --- Run -----------------------------------------------------------------------------------------

$language = Get-DisplayLanguage
$speakers = Get-InfString 'ks.inf' 'Pin.Speaker' $language
if (-not $speakers) { throw "no $language name for speakers in ks.inf" }
Note "language $language; speakers '$speakers'"

Set-AudioDeviceNames $language
Rename-PlaybackDevice 0 $speakers
Rename-PlaybackDevice 1 'LG TV'
Note 'done'
