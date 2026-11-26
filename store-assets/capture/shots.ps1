<#
DisplaySwitchr Store captures, one listing locale per run (ASCII only; the guest's Windows PowerShell
5.1 reads a file without a byte order mark as ANSI).

Run in the guest's desktop session with the windows-build-tools app-test helpers, after
screenshot-desktop.ps1 -Mode Prepare, with this folder copied to C:\apptest\capture and Windows already in
the locale's display language. app_test_ui passes no arguments, so run it through app_test_run, which
does: app_test_run '& C:\apptest\capture\shots.ps1 -Locale de-DE -Phase base' (store-assets/README.md,
"Localized captures", has every step). One run captures one locale, because a Windows display language
change needs a sign-in, which the host does with a cold VM cycle between locales. Each phase saves its
captures to C:\apptest\out\<Locale>\, named as in store-assets/manifest.json:

  base   writes the synthetic settings for the locale, starts the app, and captures the tray flyout
         (00-flyout-dark) and the Settings page (03-general-dark)
  popup  started just before the host plugs in the virtual hub: captures the switching popup as a burst
         of frames (02-popup-dark-f<n>); keep the one that shows the spinner and the full text
  setup  once the switch to the TV is done: captures the TV setup's page (01-setup-dark)

Every element is found by its automation ID, the app's own window title or the tray tooltip's app name,
never by translated text, so the same script captures every language. Lengths are written in logical
pixels (the 1920 x 1080 desktop) and multiplied by the display scale, because UI Automation, the cursor
and the window helpers all work in physical pixels.

Run on the VM at 200% in en-US and ja-JP (November 26, 2026); the setup phase scrolls until the
playback device is in view, so a longer translation moves the page rather than hiding the row.
#>
param(
    # The listing locale to capture, such as de-DE: picks fixtures\<Locale>.json and the output folder.
    [Parameter(Mandatory)] [string]$Locale,

    [Parameter(Mandatory)] [ValidateSet('base', 'popup', 'setup')] [string]$Phase,

    # The display scale the guest runs at, in percent. Store captures use 200 (3840 x 2160).
    [ValidateSet(100, 200)] [int]$DisplayScale = 200
)
$ErrorActionPreference = 'Stop'
. C:\apptest\ui-lib.ps1
. (Join-Path $PSScriptRoot 'windows-strings.ps1')

# This script's folder: settings.base.json and fixtures\ sit beside it, as in the repo.
$here = $PSScriptRoot
$outDir = Join-Path $UiOut $Locale
New-Item $outDir -ItemType Directory -Force | Out-Null
$log = Join-Path $outDir 'shots.txt'

<# Append a timestamped line to this locale's log. #>
function Note([string]$Message) {
    Add-Content $log "$(Get-Date -Format HH:mm:ss) [$Phase] $Message"
}

# Physical pixels per logical pixel.
$px = $DisplayScale / 100

<# A logical length or coordinate in physical pixels. #>
function P([double]$Logical) {
    [int]($Logical * $px)
}

# The helpers gained Find-ById for the localized captures; keep the script usable with an older copy.
if (-not (Get-Command Find-ById -ErrorAction SilentlyContinue)) {
    function Find-ById($Element, [string]$AutomationId, [string]$Type = $null) {
        $cond = New-Object System.Windows.Automation.PropertyCondition($UiA::AutomationIdProperty, $AutomationId)
        $Element.FindAll('Descendants', $cond) |
            Where-Object { -not $Type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$Type" } |
            Select-Object -First 1
    }
}

# ---- The app and its synthetic data ------------------------------------------------------------------

# The dev and Store packages have different identity names; both contain HubDisplaySwitch.
$pkg = Get-AppxPackage '*HubDisplaySwitch*' | Select-Object -First 1
if (-not $pkg) { throw 'DisplaySwitchr is not installed' }
$app = "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
$dataDir = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)\LocalCache\Roaming\HubDisplaySwitch"

# The setup ids are fixed in settings.base.json; the setup page is selected by id, not by its name.
$tvSetupId = '7d1e4b0c2f9a4e6b8c3d5a1f0e2b4c6d'

<#
The active playback endpoints as objects with Id (the app's form, "{0.0.0.00000000}.{guid}"), Name
(Windows' name for the endpoint, such as "Speakers" or the renamed "LG TV") and FullName (the name with
its interface, as the app stores it: "Speakers (2- High Definition Audio Device)"). Windows writes these
names in its display language, which is why they are read here instead of kept in a fixture.
#>
function Get-PlaybackEndpoints {
    Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render' | ForEach-Object {
        if ((Get-ItemProperty $_.PSPath).DeviceState -ne 1) { return }   # DEVICE_STATE_ACTIVE
        $p = Get-ItemProperty (Join-Path $_.PSPath 'Properties')
        $name = $p.'{a45c254e-df1c-4efd-8020-67d146a850e0},2'
        $interface = $p.'{b3f8fa53-0004-438e-9003-51a46e139bfc},6'
        [pscustomobject]@{ Id = "{0.0.0.00000000}.$($_.PSChildName)"; Name = $name; FullName = "$name ($interface)" }
    }
}

<#
Write the app's settings.json for this locale, as a user of that language would have it:

- settings.base.json gives the capture VM's displays (their EDID names are the same in every language),
  the hub's device ID and the setups' ids, glyphs and options;
- the locale's fixture gives the app's language and the setup names, the synthetic text a user typed;
- Windows gives the names it shows in its own language: the playback endpoints (named by
  name-audio-devices.ps1: the desk speakers, and the TV's renamed LG TV) and the hub, which must have
  been plugged in once this session so Windows knows it (see store-assets/README.md). The hub's name
  is Windows' own description for it in the display language (windows-strings.ps1), since the VM's
  Windows keeps the English name it gave the device when it was installed in English.

The app's language is set explicitly, so it does not depend on Windows' own preferences.
#>
function Write-LocaleSettings {
    $utf8 = New-Object Text.UTF8Encoding $false
    $settings = [IO.File]::ReadAllText((Join-Path $here 'settings.base.json'), $utf8) | ConvertFrom-Json
    $fixture = [IO.File]::ReadAllText((Join-Path $here "fixtures\$Locale.json"), $utf8) | ConvertFrom-Json
    $settings.Language = $fixture.language

    # The setup names, from the fixture.
    foreach ($setup in $settings.Setups) {
        $name = $fixture.setupNames.$($setup.Id)
        if (-not $name) { throw "fixtures\$Locale.json has no name for setup $($setup.Id)" }
        $setup.Name = $name
    }

    # The playback endpoints, from Windows: LG TV for the TV, the other one for the desk.
    $endpoints = @(Get-PlaybackEndpoints)
    $tvAudio = $endpoints | Where-Object { $_.Name -eq 'LG TV' } | Select-Object -First 1
    $deskAudio = $endpoints | Where-Object { $_.Name -ne 'LG TV' } | Select-Object -First 1
    if (-not $tvAudio -or -not $deskAudio) {
        throw "expected two playback endpoints, one renamed LG TV (found: $(($endpoints | ForEach-Object FullName) -join ', '))"
    }

    foreach ($setup in $settings.Setups) {
        $audio = if ($setup.Id -eq $tvSetupId) { $tvAudio } else { $deskAudio }
        $setup.AudioId = $audio.Id
        $setup.AudioName = $audio.FullName

        # The hub's name, as Windows names it in this language (also for a device that is unplugged now).
        foreach ($hub in $setup.Hubs) {
            $device = Get-PnpDevice -InstanceId $hub.DeviceId -ErrorAction SilentlyContinue
            if (-not $device) { throw "Windows does not know the hub $($hub.DeviceId) yet: plug the virtual hub in and out once first" }
            $hub.Name = Resolve-DeviceText (Get-DeviceDescReference $hub.DeviceId) (Get-DisplayLanguage)
        }
    }

    New-Item $dataDir -ItemType Directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $dataDir 'settings.json'), ($settings | ConvertTo-Json -Depth 6), $utf8)
    Note "settings written: language $($settings.Language), setups $(($settings.Setups | ForEach-Object Name) -join ', ')"
    Note "audio: desk '$($deskAudio.FullName)', TV '$($tvAudio.FullName)'; hub '$(($settings.Setups | ForEach-Object { $_.Hubs } | ForEach-Object Name) -join ', ')'"
}

# ---- Screen size -------------------------------------------------------------------------------------

# DisplaySwitchr applies the Desk layout when it starts (and on every switch), which can put the guest
# back to its saved mode; set the capture size again before each capture, as screenshot-desktop.ps1 does.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class ShotsNative {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct DEVMODE {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
    public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
    public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
    public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
    public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
  }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);
}
"@

<# Set the primary display to the capture size (1920 x 1080 logical at the chosen scale) if it is not. #>
function Set-ScreenSize {
    $width = P 1920
    $height = P 1080
    $dm = New-Object ShotsNative+DEVMODE
    $dm.dmSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type][ShotsNative+DEVMODE])
    [ShotsNative]::EnumDisplaySettings($null, -1, [ref]$dm) | Out-Null
    if ($dm.dmPelsWidth -eq $width -and $dm.dmPelsHeight -eq $height) { return }

    $dm.dmPelsWidth = $width
    $dm.dmPelsHeight = $height
    $dm.dmFields = 0x80000 -bor 0x100000   # DM_PELSWIDTH | DM_PELSHEIGHT
    $result = [ShotsNative]::ChangeDisplaySettings([ref]$dm, 0)
    if ($result -ne 0) { throw "ChangeDisplaySettings returned $result" }
    Note "screen set to $width x $height"
    Start-Sleep -Seconds 4
}

<#
Check that this process sees physical pixels at the expected scale. A process that is not DPI aware
sees a virtualized 1920 x 1080 screen, and every click and window size here would be off by the scale.
#>
function Assert-Scale {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    if ($bounds.Width -ne (P 1920)) {
        throw "The screen reads $($bounds.Width) wide, expected $(P 1920) at $DisplayScale%: set the scale first, or the process is not DPI aware"
    }
}

# ---- Windows and input -------------------------------------------------------------------------------

<# The app's top-level windows that are on the screen (the popup is parked off-screen between uses). #>
function Get-VisibleWindows {
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    Get-AppWindows HubDisplaySwitch | Where-Object {
        $r = $_.Current.BoundingRectangle
        $r.Width -gt 0 -and $r.X -ge 0 -and $r.Y -ge 0 -and $r.X -lt $screen.Width -and $r.Y -lt $screen.Height
    }
}

<# The settings window if it is on the screen: the only app window wider than 600 logical pixels. #>
function Get-SettingsWindow {
    Get-VisibleWindows | Where-Object { $_.Current.BoundingRectangle.Width -gt (P 600) } | Select-Object -First 1
}

<# Move the pointer to the top right corner, away from anything with a hover state. #>
function Park-Pointer {
    [UiNative]::SetCursorPos((P 1900), (P 20)) | Out-Null
    Start-Sleep -Milliseconds 500
}

<# Press and release one virtual key. #>
function Send-Key([byte]$VirtualKey) {
    [UiNative]::keybd_event($VirtualKey, 0, 0, [UIntPtr]::Zero)
    [UiNative]::keybd_event($VirtualKey, 0, 2, [UIntPtr]::Zero)
}

<# Turn the mouse wheel by $Delta (120 is one notch; positive scrolls up), then pause. #>
function Send-Wheel([int]$Delta, [int]$PauseMs = 200) {
    [UiNative]::mouse_event(0x0800, 0, 0, [uint32][BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$Delta), 0), [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $PauseMs
}

<#
Clear the keyboard focus rectangle that automation leaves, with a plain click on blank page space to
the right of a section heading (found by its automation ID); nothing there reacts to a click.
#>
function Clear-Focus($Window, [string]$HeadingId) {
    $heading = Find-ById $Window $HeadingId
    if (-not $heading) { throw "no heading $HeadingId" }
    $r = $heading.Current.BoundingRectangle
    if ($r.IsEmpty -or [double]::IsInfinity($r.X) -or [double]::IsNaN($r.X)) {
        # Scrolled out of view (UI Automation gives it no rectangle): click blank space near the
        # window's right edge, level with the top of the visible page, instead.
        $w = $Window.Current.BoundingRectangle
        Note "heading $HeadingId is out of view; clearing focus at the window's right edge"
        [UiNative]::SetCursorPos([int]($w.Right - (P 40)), [int]($w.Y + (P 120))) | Out-Null
    }
    else {
        [UiNative]::SetCursorPos([int]($r.X + $r.Width + (P 300)), [int]($r.Y + $r.Height / 2)) | Out-Null
    }
    Start-Sleep -Milliseconds 200
    [UiNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [UiNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}

<# Open the settings window (a second launch hands "show settings" to the running copy), centred. #>
function Open-Settings {
    Set-ScreenSize
    Start-Process $app
    $win = $null
    for ($i = 0; $i -lt 20 -and -not $win; $i++) {
        Start-Sleep -Seconds 1
        $win = Get-SettingsWindow
    }
    if (-not $win) { throw 'no settings window' }
    Start-Sleep -Seconds 2
    Set-WindowCentered $win (P 1100) (P 700)
    Set-WindowActive $win
    $win
}

<# Select a navigation item by its automation ID, through its selection pattern or a click. #>
function Select-Nav($Window, [string]$AutomationId) {
    $item = Find-ById $Window $AutomationId
    if (-not $item) { throw "no nav item $AutomationId" }
    try {
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    }
    catch {
        Invoke-Click $item
    }
    Start-Sleep -Seconds 3
}

<# Save a capture under this locale's folder, and log the window's frame. #>
function Save-LocaleCapture([string]$Name, $Window) {
    Save-DesktopCapture "$Locale\$Name" $Window | Out-Null
    $f = Get-WindowFrame $Window
    Note "$Name frame $($f.X),$($f.Y) $($f.Width)x$($f.Height)"
}

<#
The Playback (output) box on the setup page: the first combo box below the Audio heading (found by its
automation ID), or $null while either is scrolled out of view. Its own name is translated text.
#>
function Get-PlaybackBox($Window) {
    $header = Find-ById $Window 'AudioHeader'
    if (-not $header) { return $null }
    $top = $header.Current.BoundingRectangle.Y
    if ($header.Current.IsOffscreen -or [double]::IsInfinity($top)) { return $null }
    $combo = New-Object System.Windows.Automation.PropertyCondition($UiA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
    $Window.FindAll('Descendants', $combo) |
        Where-Object { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Y -gt $top } |
        Sort-Object { $_.Current.BoundingRectangle.Y } |
        Select-Object -First 1
}

# ---- Phases ------------------------------------------------------------------------------------------

switch ($Phase) {
    'base' {
        Set-Content $log "shots $Locale $(Get-Date -Format o)"
        Get-Process HubDisplaySwitch -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
        Write-LocaleSettings

        # Show the tray icon in the notification area rather than the overflow.
        Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' | ForEach-Object {
            $p = Get-ItemProperty $_.PSPath
            if ($p.ExecutablePath -like '*HubDisplaySwitch*') {
                Set-ItemProperty $_.PSPath IsPromoted 1
                Note "promoted $($_.PSChildName)"
            }
        }

        Start-Process $app
        # Let the start-up switch (layout, then settle) finish before setting the screen size.
        Start-Sleep -Seconds 30
        Set-ScreenSize
        Assert-Scale

        # Close any settings window, so the flyout capture shows only the desktop and the flyout.
        $open = Get-SettingsWindow
        if ($open) {
            Note 'closed a settings window'
            $open.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        }
        Start-Sleep -Seconds 2

        # 00: the tray flyout. The tooltip starts with the app's name in every language.
        $icon = Find-TrayIcon 'DisplaySwitchr'
        if (-not $icon) { throw 'no tray icon' }
        Note "tray icon: $($icon.Current.Name)"
        Invoke-Click $icon
        Start-Sleep -Seconds 3
        $fly = Get-VisibleWindows | Select-Object -First 1
        if (-not $fly) { throw 'no flyout' }
        Park-Pointer
        Save-LocaleCapture '00-flyout-dark' $fly
        Send-Key 0x1B   # Esc closes the flyout
        Start-Sleep -Seconds 2

        # 03: Settings (the General page). The built-in item's ID comes from WinUI's NavigationView.
        $win = Open-Settings
        Select-Nav $win 'SettingsItem'
        Clear-Focus $win 'BehaviourHeader'
        Park-Pointer
        Save-LocaleCapture '03-general-dark' $win
        $win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Start-Sleep -Seconds 2
    }

    'popup' {
        # The popup window always exists (parked off-screen between uses), so it counts as shown only
        # while its frame is on the screen. Its title is the app's name, which is not translated.
        function Get-ShownPopup {
            $w = Get-AppWindows HubDisplaySwitch | Where-Object { $_.Current.Name -eq 'DisplaySwitchr Popup' } | Select-Object -First 1
            if (-not $w) { return $null }
            $f = Get-WindowFrame $w
            $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
            if ($f.X -ge 0 -and $f.Y -ge 0 -and $f.X + $f.Width -le $screen.Width -and $f.Y + $f.Height -le $screen.Height) {
                return $w
            }
            return $null
        }

        Assert-Scale
        Park-Pointer
        Note 'waiting for the popup'
        $osd = $null
        for ($i = 0; $i -lt 300 -and -not $osd; $i++) {
            Start-Sleep -Milliseconds 100
            $osd = Get-ShownPopup
        }
        if (-not $osd) { throw 'no popup shown within 30 s' }

        # The popup never activates by design (WS_EX_NOACTIVATE), so skip the helper's activation step.
        function Set-WindowActive($Window, [int]$Attempts = 5) { }

        # A burst of frames while the switch runs (about two seconds); the best one is kept by hand.
        $n = 0
        $missing = 0
        while ($n -lt 20 -and $missing -lt 8) {
            $osd = Get-ShownPopup
            if (-not $osd) {
                $missing++
                Start-Sleep -Milliseconds 300
                continue
            }
            $missing = 0
            $n++
            try {
                Save-LocaleCapture "02-popup-dark-f$n" $osd
            }
            catch {
                Note "f$n failed: $_"
            }
            Start-Sleep -Milliseconds 250
        }
        Note "captured $n frames"
    }

    'setup' {
        # Dismiss anything a previous run left open (Esc cancels a dialog), then close the window.
        Send-Key 0x1B
        Start-Sleep -Seconds 1
        $open = Get-SettingsWindow
        if ($open) { $open.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
        Start-Sleep -Seconds 2

        Assert-Scale
        $win = Open-Settings
        Select-Nav $win "SetupNavItem_$tvSetupId"

        # Scroll to the top, then down in small steps until the playback device (named in the alt
        # text) sits just inside the window's bottom edge, as in the approved English shot: the hub,
        # the displays and the playback device are all in view. Anchored on the playback box rather
        # than the hub trigger, because translated captions wrap to different heights.
        $b = $win.Current.BoundingRectangle
        [UiNative]::SetCursorPos([int]($b.X + (P 1000)), [int]($b.Y + (P 400))) | Out-Null
        for ($i = 0; $i -lt 20; $i++) { Send-Wheel 120 }
        Start-Sleep -Seconds 1

        # Just inside the frame: the Recording box's label, 12 px below, stays out of view.
        $limit = $b.Bottom - (P 10)
        $playback = $null
        for ($i = 0; $i -lt 60; $i++) {
            $playback = Get-PlaybackBox $win
            if ($playback -and $playback.Current.BoundingRectangle.Bottom -le $limit) { break }
            $gap = if ($playback) { $playback.Current.BoundingRectangle.Bottom - $limit } else { [double]::PositiveInfinity }
            # Coarse, then finer as the box nears the edge, so it stops within a few pixels of it and
            # the Recording box's label below stays hidden.
            if ($gap -gt (P 100)) { Send-Wheel -120 700 }
            elseif ($gap -gt (P 30)) { Send-Wheel -20 500 }
            else { Send-Wheel -10 500 }
        }
        if (-not $playback) { throw 'the playback device never came into view' }
        $hubY = (Find-ById $win 'HubTrigger').Current.BoundingRectangle.Y
        Note "playback bottom $($playback.Current.BoundingRectangle.Bottom), limit $limit; hub trigger y $hubY, window y $($b.Y)"
        # The playback row comes first (the alt text names it); a long translation can push the hub
        # trigger under the title bar, which is reported for a look rather than failing the shot.
        if ([double]::IsInfinity($hubY) -or $hubY -lt $b.Y + (P 48)) { Note 'WARNING: the hub trigger is partly under the title bar' }

        Clear-Focus $win 'AudioHeader'
        Park-Pointer
        Save-LocaleCapture '01-setup-dark' $win
    }
}
Note 'done'
