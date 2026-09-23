param([switch]$Settings, [ValidateSet('WhisperOnnx','SenseVoice')][string]$Engine='WhisperOnnx', [ValidateSet('CUDA','CPU')][string]$Backend='CUDA')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$modelName = if ($Engine -eq 'WhisperOnnx') { 'whisper-large-v3-turbo' } else { 'sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17' }
if ($Engine -eq 'SenseVoice') { $Backend = 'CPU' }
$localCuda = Join-Path $repo 'artifacts\cuda-runtime'
$originalPath = $env:PATH
try {
    if (Test-Path -LiteralPath $localCuda) { $env:PATH = $localCuda + ';' + $env:PATH }
    $arguments = @('run', '--project', (Join-Path $repo 'src\SenseVoiceInput.App'), '--', '--model-dir', (Join-Path $repo ('models\' + $modelName)), '--engine', $Engine, '--backend', $Backend)
    if ($Settings) { $arguments += '--settings' }
    & dotnet @arguments
    $result = $LASTEXITCODE
} finally { $env:PATH = $originalPath }
exit $result
