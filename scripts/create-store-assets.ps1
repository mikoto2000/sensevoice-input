param(
    [string]$Source = (Join-Path $PSScriptRoot '../store/assets/app-source.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '../packaging/msix/Assets')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath = [IO.Path]::GetFullPath($Source)
$destinationPath = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory($destinationPath) | Out-Null
$original = [Drawing.Image]::FromFile($sourcePath)
try {
    # Resize the existing application artwork for the package; do not redesign it.
    $sizes = [ordered]@{ 'StoreLogo.png' = 50; 'Square44x44Logo.png' = 44; 'Square150x150Logo.png' = 150; 'AppTile300.png' = 300 }
    foreach ($entry in $sizes.GetEnumerator()) {
        $bitmap = [Drawing.Bitmap]::new($entry.Value, $entry.Value)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale = [Math]::Min($entry.Value / $original.Width, $entry.Value / $original.Height)
            $width = [int][Math]::Round($original.Width * $scale)
            $height = [int][Math]::Round($original.Height * $scale)
            $graphics.DrawImage($original, [int](($entry.Value - $width) / 2), [int](($entry.Value - $height) / 2), $width, $height)
            $bitmap.Save((Join-Path $destinationPath $entry.Key), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $original.Dispose() }
Write-Host "Store assets: $destinationPath"
