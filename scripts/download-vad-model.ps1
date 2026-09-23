param([string]$Destination = (Join-Path $PSScriptRoot '..\models'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot '..\licenses\Silero-VAD-*.txt') -Destination $Destination -Force
$model = Join-Path $Destination 'silero_vad.onnx'
if (-not (Test-Path -LiteralPath $model)) {
    Invoke-WebRequest 'https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx' -OutFile $model
}
if ((Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash -ne '9E2449E1087496D8D4CABA907F23E0BD3F78D91FA552479BB9C23AC09CBB1FD6') {
    throw 'Silero VAD SHA256 mismatch. Check the downloaded file before retrying.'
}
Write-Host "VAD model ready: $model"
