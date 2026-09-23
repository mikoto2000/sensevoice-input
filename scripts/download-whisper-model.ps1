param([string]$Destination = (Join-Path $PSScriptRoot '..\models\whisper-large-v3-turbo'), [switch]$SkipVad)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content (Join-Path $PSScriptRoot 'whisper-model-manifest.json') -Raw | ConvertFrom-Json
Write-Host "Model: $($manifest.model), revision: $($manifest.revision), license: $($manifest.license)"
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot '..\licenses\Whisper-*.txt') -Destination $Destination -Force
foreach ($file in $manifest.files) {
    $target = Join-Path $Destination $file.name
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $file.sha256) { continue }
    $partial = $target + '.partial'
    Invoke-WebRequest $file.url -OutFile $partial
    if ((Get-FileHash -LiteralPath $partial).Hash -ne $file.sha256) { throw "SHA256 mismatch: $($file.name)" }
    Move-Item -LiteralPath $partial -Destination $target -Force
}
if (-not $SkipVad) { & (Join-Path $PSScriptRoot 'download-vad-model.ps1') -Destination (Split-Path ([IO.Path]::GetFullPath($Destination)) -Parent) }
Write-Host "Whisper FP16 model ready: $Destination"
