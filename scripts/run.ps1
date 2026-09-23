param([switch]$Settings)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$model = Join-Path $repo 'models\sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17'
$arguments = @('run', '--project', (Join-Path $repo 'src\SenseVoiceInput.App'), '--', '--model-dir', $model)
if ($Settings) { $arguments += '--settings' }
& dotnet @arguments
exit $LASTEXITCODE
