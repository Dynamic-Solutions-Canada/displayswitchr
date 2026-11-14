# Generates the MSIX visual assets AND the app .ico from one source: a monitor with a
# blue screen and a green "switch" badge (swap arrows), on a light rounded tile.
# Run with Windows PowerShell: powershell.exe -NoProfile -File generate-msix-images.ps1
# ASCII only (Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI).
Add-Type -AssemblyName System.Drawing

$repoRoot  = Split-Path $PSScriptRoot -Parent
$imagesDir = Join-Path $PSScriptRoot "Images"
$icoPath   = Join-Path $repoRoot "HubDisplaySwitch\Resources\HubDisplaySwitch.ico"
New-Item -ItemType Directory -Force $imagesDir | Out-Null
New-Item -ItemType Directory -Force (Split-Path $icoPath -Parent) | Out-Null

$tile   = [System.Drawing.Color]::FromArgb(255, 233, 240, 250)  # E9F0FA  light tile
$frame  = [System.Drawing.Color]::FromArgb(255, 45, 50, 59)     # 2D323B  monitor frame
$screen = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)    # 2563EB  screen
$glare  = [System.Drawing.Color]::FromArgb(70, 255, 255, 255)
$badge  = [System.Drawing.Color]::FromArgb(255, 16, 185, 129)   # 10B981  hub badge
$white  = [System.Drawing.Color]::White

function Rounded($x, $y, $w, $h, $r) {
    $d = [single]($r * 2)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc([single]$x, [single]$y, $d, $d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, $d, $d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Fill($g, $color, $path) {
    $b = New-Object System.Drawing.SolidBrush($color)
    $g.FillPath($b, $path); $b.Dispose(); $path.Dispose()
}

# Monitor + hub badge centered in a square of side S at (ox, oy).
function Draw-Mark($g, $S, $ox, $oy) {
    $mw = [single]($S * 0.66); $mh = [single]($S * 0.44)
    $mx = [single]($ox + ($S - $mw) / 2); $my = [single]($oy + $S * 0.2)
    Fill $g $frame (Rounded $mx $my $mw $mh ([single]($S * 0.05)))
    $bz = [single]($S * 0.035)
    Fill $g $screen (Rounded ($mx + $bz) ($my + $bz) ($mw - 2 * $bz) ($mh - 2 * $bz) ([single]($S * 0.025)))
    # glare stripe
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddPolygon(@(
        (New-Object System.Drawing.PointF([single]($mx + $mw * 0.55), [single]($my + $bz))),
        (New-Object System.Drawing.PointF([single]($mx + $mw - $bz), [single]($my + $bz))),
        (New-Object System.Drawing.PointF([single]($mx + $mw - $bz), [single]($my + $mh * 0.35))),
        (New-Object System.Drawing.PointF([single]($mx + $mw * 0.30), [single]($my + $mh - $bz)))))
    Fill $g $glare $gp
    # stand
    $sw = [single]($S * 0.08); $sh = [single]($S * 0.09)
    Fill $g $frame (Rounded ([single]($ox + ($S - $sw) / 2)) ($my + $mh - 1) $sw $sh ([single]($S * 0.01)))
    $bw = [single]($S * 0.30); $bh = [single]($S * 0.045)
    Fill $g $frame (Rounded ([single]($ox + ($S - $bw) / 2)) ($my + $mh + $sh - 2) $bw $bh ([single]($bh / 2)))
    # hub badge (bottom-right): green circle with a white USB trident
    $cr = [single]($S * 0.17)
    $cx = [single]($mx + $mw - $cr * 0.55); $cy = [single]($my + $mh - $cr * 0.35)
    $ring = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ring.AddEllipse([single]($cx - $cr - $S * 0.02), [single]($cy - $cr - $S * 0.02), [single](2 * $cr + $S * 0.04), [single](2 * $cr + $S * 0.04))
    Fill $g $tile $ring
    $dot = New-Object System.Drawing.Drawing2D.GraphicsPath
    $dot.AddEllipse([single]($cx - $cr), [single]($cy - $cr), [single](2 * $cr), [single](2 * $cr))
    Fill $g $badge $dot
    $pen = New-Object System.Drawing.Pen($white, [single][Math]::Max(1.0, $S * 0.022))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    # swap arrows: top one points right, bottom one points left
    $u = [single]($cr * 0.5); $h = [single]($cr * 0.28); $a = [single]($cr * 0.24)
    $g.DrawLine($pen, [single]($cx - $u), [single]($cy - $h), [single]($cx + $u), [single]($cy - $h))
    $g.DrawLine($pen, [single]($cx + $u), [single]($cy - $h), [single]($cx + $u - $a), [single]($cy - $h - $a))
    $g.DrawLine($pen, [single]($cx + $u), [single]($cy - $h), [single]($cx + $u - $a), [single]($cy - $h + $a))
    $g.DrawLine($pen, [single]($cx + $u), [single]($cy + $h), [single]($cx - $u), [single]($cy + $h))
    $g.DrawLine($pen, [single]($cx - $u), [single]($cy + $h), [single]($cx - $u + $a), [single]($cy + $h - $a))
    $g.DrawLine($pen, [single]($cx - $u), [single]($cy + $h), [single]($cx - $u + $a), [single]($cy + $h + $a))
    $pen.Dispose()
}

function New-Master([int]$M) {
    $bmp = New-Object System.Drawing.Bitmap($M, $M, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    Fill $g $tile (Rounded 0 0 $M $M ([single]($M * 0.18)))
    Draw-Mark $g $M 0 0
    $g.Dispose()
    return $bmp
}

function New-Resized($src, [int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $w, $h)
    $g.Dispose()
    return $bmp
}

# Render each asset at its own size with 4x supersampling (render large, shrink once).
$SS = 4
function New-Crisp([int]$size) {
    $m = New-Master ($size * $SS)
    $r = New-Resized $m $size $size
    $m.Dispose()
    return $r
}

function Save-Square([int]$size, [string]$name) {
    $b = New-Crisp $size
    $b.Save((Join-Path $imagesDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    Write-Output "  $name ($size x $size)"
}

function Save-Splash([int]$w, [int]$h, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [int]($h * 0.5); $bx = [int](($w - $s) / 2); $by = [int](($h - $s) / 2)
    $bb = New-Crisp $s
    $g.DrawImage($bb, $bx, $by, $s, $s)
    $bb.Dispose(); $g.Dispose()
    $bmp.Save((Join-Path $imagesDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output "  $name ($w x $h)"
}

function Save-Ico([int[]]$sizes, [string]$path) {
    $blobs = @()
    foreach ($s in $sizes) {
        $b = New-Crisp $s
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $blobs += , ($ms.ToArray())
        $ms.Dispose(); $b.Dispose()
    }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $len = $blobs[$i].Length
        $wb = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$wb); $bw.Write([byte]$wb); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
        $offset += $len
    }
    foreach ($b in $blobs) { $bw.Write($b) }
    $bw.Flush(); $bw.Close(); $fs.Close()
    Write-Output "  HubDisplaySwitch.ico ($($sizes -join ', '))"
}

Write-Output "Generating MSIX images:"
# Every scale and target size Windows asks for, so the taskbar / Start / Settings never upscale a
# smaller asset (which is what makes packaged app icons look blurry at 125-300% scaling).
Remove-Item (Join-Path $imagesDir "*.png") -ErrorAction SilentlyContinue
foreach ($s in @(@(100, 1.0), @(125, 1.25), @(150, 1.5), @(200, 2.0), @(400, 4.0))) {
    $scale = $s[0]; $f = $s[1]
    $sfx = if ($scale -eq 100) { "" } else { ".scale-$scale" }
    Save-Square ([int](50 * $f))  "StoreLogo$sfx.png"
    Save-Square ([int](44 * $f))  "Square44x44Logo$sfx.png"
    Save-Square ([int](150 * $f)) "Square150x150Logo$sfx.png"
    Save-Splash ([int](620 * $f)) ([int](300 * $f)) "SplashScreen$sfx.png"
}
# Square44x44Logo is the taskbar / title bar / Start list icon: provide exact pixel sizes.
foreach ($t in @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)) {
    Save-Square $t "Square44x44Logo.targetsize-$t.png"
    Save-Square $t "Square44x44Logo.targetsize-${t}_altform-unplated.png"
    Save-Square $t "Square44x44Logo.targetsize-${t}_altform-lightunplated.png"
}

Write-Output "Generating app icon:"
Save-Ico @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256) $icoPath
$png = New-Crisp 256
$png.Save((Join-Path (Split-Path $icoPath -Parent) "HubDisplaySwitch.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
Write-Output "  HubDisplaySwitch.png (256 x 256)"

Write-Output "Done."
