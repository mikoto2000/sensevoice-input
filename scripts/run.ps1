param([switch]$Settings, [ValidateSet('WhisperOnnx')][string]$Engine='WhisperOnnx', [ValidateSet('CUDA','CPU')][string]$Backend='CUDA')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$arguments = @('run', '--project', (Join-Path $repo 'src\SenseVoiceInput.App'), '--', '--engine', $Engine, '--backend', $Backend)
$localModel = Join-Path $repo 'models\whisper-large-v3-turbo'
if (Test-Path -LiteralPath $localModel) { $arguments += @('--model-dir', $localModel) }
if ($Settings) { $arguments += '--settings' }
& dotnet @arguments
exit $LASTEXITCODE
