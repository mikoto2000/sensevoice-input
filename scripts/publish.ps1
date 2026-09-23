param([string]$Destination = (Join-Path $PSScriptRoot '..\artifacts\release'))
$ErrorActionPreference = 'Stop'
$destinationPath = [IO.Path]::GetFullPath($Destination)
if ((Test-Path -LiteralPath $destinationPath) -and (Get-ChildItem -LiteralPath $destinationPath -Force | Select-Object -First 1)) {
    throw 'Choose a new or empty destination so old DLLs or models cannot enter the release.'
}
$repo = Split-Path $PSScriptRoot -Parent
& dotnet publish (Join-Path $repo 'src\SenseVoiceInput.App') -c Release --artifacts-path (Join-Path $repo 'artifacts\publish-build') --self-contained false -p:PublishSingleFile=false -p:RestoreLockedMode=true -o $destinationPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($file in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'licenses\Whisper-LICENSE.txt', 'licenses\Silero-VAD-LICENSE.txt', 'licenses\ONNXRuntime-ThirdPartyNotices.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $destinationPath $file))) { throw "Missing release notice: $file" }
}
$unexpected = Get-ChildItem -LiteralPath $destinationPath -Recurse -File | Where-Object { $_.Name -match '^sherpa|^org\.k2fsa|\.onnx$|^cudart|^cublas|^cufft|^cudnn|^nvrtc' -and $_.Extension -ne '.txt' -and $_.Name -notmatch '-LICENSE$' }
if ($unexpected) { throw 'Unexpected model, sherpa or NVIDIA binary in standard release.' }
Write-Host "Release ready: $destinationPath"
