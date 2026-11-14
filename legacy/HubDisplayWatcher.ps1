<#
  HubDisplayWatcher.ps1 - runs in the background.
    Hub connected    -> show desktop on $HubMonitor only (LG TV)
    Hub disconnected -> show desktop on $DefaultMonitor only (BenQ)
  Settings live in config.json next to this script.
#>
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$cfg  = Get-Content (Join-Path $here 'config.json') -Raw | ConvertFrom-Json
$log  = Join-Path $here 'watcher.log'
. (Join-Path $here 'DisplayOnly.ps1')   # loads [Ccd.Display] (no args -> just lists, output discarded)

function Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg"
    Add-Content -Path $log -Value $line -Encoding utf8
    if ((Get-Item $log).Length -gt 512KB) { Get-Content $log -Tail 500 | Set-Content $log -Encoding utf8 }
}

# Only one copy at a time.
$mutex = New-Object System.Threading.Mutex($false, 'Local\HubDisplayWatcher')
if (-not $mutex.WaitOne(0)) { exit }

# HubIdPrefix like "USB\VID_1A40&PID_0101" matches that hub model on any port.
$likeId = ($cfg.HubIdPrefix -replace '\\', '\\') + '\\%'
function HubPresent { [bool](Get-CimInstance Win32_PnPEntity -Filter "DeviceID LIKE '$likeId'" -ErrorAction SilentlyContinue) }

function Switch-To($name) {
    # The TV can take a few seconds to wake / be detected, so retry for up to a minute.
    for ($i = 0; $i -lt 12; $i++) {
        try {
            $r = [Ccd.Display]::Only($name)
            if ($r -eq 0) {
                Log "Switched to '$name' only"
                # A TV waking from standby can re-sync its HDMI link a few seconds later and fall
                # back to 60 Hz, so keep re-checking the refresh rate for ~45 s after a switch.
                $stable = 0
                for ($t = 0; $t -lt 15 -and $stable -lt 4; $t++) {
                    Start-Sleep -Seconds 3
                    $res = [Ccd.Display]::MaxRefresh()
                    if ($res -match '->') { Log "Refresh: $res"; $stable = 0 } else { $stable++ }
                }
                return
            }
            Log "SetDisplayConfig error $r for '$name'"
        } catch { Log "Attempt $($i+1): $($_.Exception.Message)" }
        Start-Sleep -Seconds 5
    }
    Log "Gave up switching to '$name'"
}

function Apply($present) {
    if ($present) { Switch-To $cfg.HubMonitor } else { Switch-To $cfg.DefaultMonitor }
}

Log "Watcher started. Hub: $($cfg.HubIdPrefix)"
$state = HubPresent
Log "Hub present at start: $state"
if ($cfg.ApplyOnStart) { Apply $state }

Register-CimIndicationEvent -ClassName Win32_DeviceChangeEvent -SourceIdentifier HubDevChange | Out-Null
try {
    while ($true) {
        # Wake on any device change, or every 30 s as a safety net.
        $evt = Wait-Event -SourceIdentifier HubDevChange -Timeout 30
        if ($evt) {
            Start-Sleep -Seconds 2   # let the burst of events from the hub's children settle
            Get-Event -SourceIdentifier HubDevChange -ErrorAction SilentlyContinue | Remove-Event
        }
        $now = HubPresent
        if ($now -ne $state) {
            Log "Hub $(if ($now) {'connected'} else {'disconnected'})"
            $state = $now
            Apply $now
        }
    }
} finally {
    Unregister-Event -SourceIdentifier HubDevChange -ErrorAction SilentlyContinue
    $mutex.ReleaseMutex()
}
