<#
  Install.ps1 - copies the scripts to %USERPROFILE%\HubDisplaySwitch and registers a
  per-user scheduled task that starts the watcher (hidden) at sign-in.
  Run with -Uninstall to remove it.
#>
param([switch]$Uninstall)
$task = 'HubDisplaySwitch'
$dest = Join-Path $env:USERPROFILE 'HubDisplaySwitch'

# Stop any running copy.
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
    Where-Object { $_.CommandLine -like '*HubDisplayWatcher.ps1*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
    "Uninstalled."; return
}

$src = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Force $dest | Out-Null
foreach ($f in 'HubDisplayWatcher.ps1', 'DisplayOnly.ps1') { Copy-Item (Join-Path $src $f) $dest -Force }
if (-not (Test-Path (Join-Path $dest 'config.json'))) { Copy-Item (Join-Path $src 'config.json') $dest }

# Tiny VBScript launcher so PowerShell runs with no window at all.
$ps  = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$cmd = "`"$ps`" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$dest\HubDisplayWatcher.ps1`""
$q   = [char]34
$vbs = 'CreateObject("WScript.Shell").Run ' + $q + $cmd.Replace([string]$q, "$q$q") + $q + ', 0, False'
Set-Content -Path "$dest\launch.vbs" -Value $vbs -Encoding ASCII
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\wscript.exe" -Argument "`"$dest\launch.vbs`""
$trig   = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$set    = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) `
            -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
$prin   = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $task -Action $action -Trigger $trig -Settings $set -Principal $prin -Force | Out-Null
Start-ScheduledTask -TaskName $task
"Installed to $dest and started. Log: $dest\watcher.log"
