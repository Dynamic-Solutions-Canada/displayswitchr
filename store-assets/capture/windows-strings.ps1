<#
Windows' own names for devices in a given display language, for the localized Store captures (ASCII
only). Dot-sourced by name-audio-devices.ps1 and shots.ps1 in the guest's desktop session.

Windows names a device once, when it installs it, in the install (system) language, and keeps that
name when the user's display language changes later. The test VM was installed in English, so in a
ja-JP session it still shows "Speakers" and "Generic USB Hub", where a PC set up in Japanese shows
Windows' Japanese names. The names come from the drivers' INF strings, and Windows keeps each
language's copy in C:\Windows\System32\DriverStore\<language>\<inf>_loc once that language pack is
installed. These helpers read the name from there, so the captures show exactly what Windows itself
calls the device in that language: nothing is translated by hand.
#>

<#
The [Strings] value $Token of $Inf (such as 'usbhub3.inf') in $Language's localized copy, or $null
when that language has no copy or no such string. Tokens match without regard to case, as in INFs.
#>
function Get-InfString([string]$Inf, [string]$Token, [string]$Language) {
    $path = "C:\Windows\System32\DriverStore\$Language\${Inf}_loc"
    if (-not (Test-Path $path)) { return $null }
    $pattern = '^\s*' + [regex]::Escape($Token) + '\s*=\s*"([^"]*)"'
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        if ($line -match $pattern) { return $Matches[1] }
    }
    return $null
}

<#
Resolve a device's stored description, which Windows writes as an indirect string
("@usbhub3.inf,%usbhub3.usb20hubdesc%;Generic USB Hub"), in $Language. Returns the INF string in that
language, or throws when the description is not in that form or the language has no such string, so
a capture never shows a silently wrong name.
#>
function Resolve-DeviceText([string]$Indirect, [string]$Language) {
    if ($Indirect -notmatch '^@([^,]+),%([^%]+)%;') { throw "not an INF string reference: '$Indirect'" }
    $text = Get-InfString $Matches[1] $Matches[2] $Language
    if (-not $text) { throw "no $Language string for $($Matches[2]) in $($Matches[1])" }
    $text
}

<# A device's stored description (its DeviceDesc registry value, in the indirect form above). #>
function Get-DeviceDescReference([string]$InstanceId) {
    (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Enum\$InstanceId" -Name DeviceDesc -ErrorAction Stop).DeviceDesc
}
