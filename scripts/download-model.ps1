param([string]$Destination = (Join-Path $PSScriptRoot '..\models'))
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$name = 'sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17'
$archive = Join-Path $Destination 'sensevoice.tar.bz2'
$expected = '7D1EFA2138A65B0B488DF37F8B89E3D91A60676E416F515B952358D83DFD347E'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/$name.tar.bz2" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
    throw "Model archive SHA256 mismatch: $archive. Remove this archive manually and retry."
}
$members = & tar -tf $archive
if ($LASTEXITCODE -ne 0) { throw 'Could not list model archive.' }
foreach ($member in $members) {
    if ($member -notmatch "^$name(/|$)" -or $member -match '(^|/)\.\.(/|$)') { throw 'Unexpected model archive path.' }
}
& tar -xf $archive -C $Destination
if ($LASTEXITCODE -ne 0) { throw 'Model extraction failed.' }
Write-Host "Model ready: $(Join-Path $Destination $name)"
Write-Host 'Review the model LICENSE and upstream model terms before redistribution.'
