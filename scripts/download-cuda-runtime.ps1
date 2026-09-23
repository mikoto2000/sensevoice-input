param([string]$Destination = (Join-Path $PSScriptRoot '..\artifacts\cuda-runtime'))
$ErrorActionPreference = 'Stop'
# Local redistribution only; no system installation, driver modification or global PATH change.
# Terms: https://docs.nvidia.com/cuda/eula/index.html and https://docs.nvidia.com/deeplearning/cudnn/backend/latest/reference/eula.html
$downloadRoot = Join-Path $PSScriptRoot '..\artifacts\cuda-downloads'
New-Item -ItemType Directory -Force -Path $Destination, $downloadRoot | Out-Null
$entries = Get-Content (Join-Path $PSScriptRoot 'cuda-runtime-manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $entries) {
    $zip = Join-Path $downloadRoot ($entry.name + '.zip')
    if (!(Test-Path -LiteralPath $zip) -or (Get-FileHash -LiteralPath $zip).Hash -ne $entry.sha256) { Invoke-WebRequest $entry.url -OutFile $zip }
    if ((Get-FileHash -LiteralPath $zip).Hash -ne $entry.sha256) { throw "SHA256 mismatch: $($entry.name)" }
    $expanded = Join-Path $downloadRoot $entry.name
    Expand-Archive -LiteralPath $zip -DestinationPath $expanded -Force
    Get-ChildItem -LiteralPath $expanded -Filter '*.dll' -Recurse | Copy-Item -Destination $Destination -Force
    # Retain NVIDIA notices alongside local binaries.
    Get-ChildItem -LiteralPath $expanded -File -Recurse | Where-Object { $_.Name -match 'LICENSE|EULA' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $Destination ($entry.name + '-' + $_.Name)) -Force }
}
Write-Host "CUDA 13 + cuDNN 9 DLLs ready: $Destination"
