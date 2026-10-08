# Draws the Stream Deck plugin's pictures (PNG, normal and @2x) in LaunchStage's colors.
# Run it again only if you want to change them: powershell -ExecutionPolicy Bypass -File make-images.ps1
Add-Type -AssemblyName System.Drawing

$root = Join-Path $PSScriptRoot "com.bones84.launchstage.sdPlugin\imgs"
$dark = [System.Drawing.Color]::FromArgb(30, 30, 30)       # #1E1E1E
$teal = [System.Drawing.Color]::FromArgb(0, 173, 181)      # #00ADB5
$white = [System.Drawing.Color]::FromArgb(255, 255, 255)
$clear = [System.Drawing.Color]::Transparent

function Save-Glyph([string]$file, [int]$size, [string]$glyph, $background, $foreground, [double]$glyphScale, [double]$lift) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear($background)
    $font = New-Object System.Drawing.Font "Segoe Fluent Icons", ([float]($size * $glyphScale)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush $foreground
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $area = New-Object System.Drawing.RectangleF 0, ([float](-$size * $lift)), $size, $size
    $g.DrawString($glyph, $font, $brush, $area, $format)
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bitmap.Dispose(); $font.Dispose(); $brush.Dispose()
}

function Save-Pair([string]$name, [int]$size, [string]$glyph, $background, $foreground, [double]$glyphScale, [double]$lift) {
    Save-Glyph (Join-Path $root "$name.png") $size $glyph $background $foreground $glyphScale $lift
    Save-Glyph (Join-Path $root "$name@2x.png") ($size * 2) $glyph $background $foreground $glyphScale $lift
}

$monitor = [string][char]0xE7F4   # screen: a workspace profile
$resnap = [string][char]0xE72C    # arrows going round: put back
$game = [string][char]0xE7FC      # game controller
$launch = [string][char]0xE768    # play

# Action list icons (white on clear, as Elgato asks) and key pictures (title sits at the bottom, so the glyph is lifted).
Save-Pair "actions/profile/icon" 20 $monitor $clear $white 0.8 0
Save-Pair "actions/profile/key" 72 $monitor $dark $teal 0.42 0.1
Save-Pair "actions/profile/key-open" 72 $monitor $teal $dark 0.42 0.1
Save-Pair "actions/resnap/icon" 20 $resnap $clear $white 0.8 0
Save-Pair "actions/resnap/key" 72 $resnap $dark $teal 0.42 0.1
Save-Pair "actions/games/icon" 20 $game $clear $white 0.8 0
Save-Pair "actions/games/key" 72 $game $dark $teal 0.42 0.1

# Plugin pictures.
Save-Pair "plugin/category-icon" 28 $launch $clear $white 0.75 0
Save-Pair "plugin/marketplace" 288 $launch $dark $teal 0.5 0

Write-Output "Pictures saved in $root"
