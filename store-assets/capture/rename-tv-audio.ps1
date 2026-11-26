<#
Rename the second HD Audio playback endpoint to "LG TV" through the Sound control panel's own
Properties > General name box, as a user would (ASCII only). Run in the desktop session (app_test_ui),
in any display language.

Nothing is found by its translated text: the Sound dialog and the device's Properties dialog are the
#32770 dialogs of the rundll32 process this script starts (the first is the Sound dialog, the second
the Properties dialog it owns), the device list and the name box by their window class, and the OK
buttons by their control ID (IDOK, 1). The managed UI Automation client shows the list's items as
plain panes with no selection state, so the second device is selected with Home, Down and its
Properties opened from the list's context menu (Shift+F10, then Up to its last item, Properties, in
every language). Checked on the VM at 200%, November 26, 2026.
#>
$ErrorActionPreference = 'Stop'
. C:\apptest\ui-lib.ps1
Add-Type -AssemblyName System.Windows.Forms

$log = Join-Path $UiOut 'rename-audio.txt'
New-Item $UiOut -ItemType Directory -Force | Out-Null
Set-Content $log "rename $(Get-Date -Format o)"

<# Append a line to the log. #>
function Note([string]$Message) {
    Add-Content $log $Message
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

# Close any Sound dialog an earlier attempt left open.
Get-Process rundll32 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# The Sound dialog, Playback tab.
$proc = Start-Process rundll32.exe 'shell32.dll,Control_RunDLL mmsys.cpl,,0' -PassThru
$sound = (Get-ProcessDialogs $proc.Id 1)[0]
# Look the dialog up again once it has settled: found while it is still being created, it has no
# title yet and could not be made the foreground window.
Start-Sleep -Seconds 3
$sound = (Get-ProcessDialogs $proc.Id 1)[0]
Set-WindowActive $sound

# Select the second device (Home, Down in the list), then open its Properties with a double-click.
$list = Find-ByClass $sound 'SysListView32'
if (-not $list) { throw 'no device list' }
Invoke-Click $list
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{HOME}')
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
Start-Sleep -Milliseconds 500
# The managed client shows the list's items as plain panes with no selection state, so open the
# selected device's Properties from its context menu instead: Shift+F10, then Up wraps to the last
# item, which is Properties in every language.
[System.Windows.Forms.SendKeys]::SendWait('+{F10}')
Start-Sleep -Milliseconds 800
[System.Windows.Forms.SendKeys]::SendWait('{UP}')
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

# The Properties dialog: the process's other dialog. Its General tab opens with the name box first.
$dialogs = Get-ProcessDialogs $proc.Id 2
$props = $dialogs | Where-Object { $_.Current.NativeWindowHandle -ne $sound.Current.NativeWindowHandle } | Select-Object -First 1
Note "properties: $($props.Current.Name)"
Start-Sleep -Seconds 2
Set-WindowActive $props
$edit = Find-ByClass $props 'Edit'
if (-not $edit) { throw 'no name box' }
Invoke-Click $edit
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('^a')
[System.Windows.Forms.SendKeys]::SendWait('LG TV')
Start-Sleep -Milliseconds 500

Invoke-Click (Find-OkButton $props)
Start-Sleep -Seconds 2
Invoke-Click (Find-OkButton $sound)
Start-Sleep -Seconds 2
Note 'done'
