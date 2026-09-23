param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\packages')
)
$ErrorActionPreference = 'Stop'
$packageName = "SenseVoiceInput-$Version-win-x64"
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$publishPath = Join-Path $outputPath $packageName
$zipPath = Join-Path $outputPath "$packageName.zip"
$checksumPath = "$zipPath.sha256"
if ((Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath $checksumPath)) {
    throw 'Release files already exist. Choose a new output directory or version.'
}
& (Join-Path $PSScriptRoot 'publish.ps1') -Destination $publishPath

# Include a top-level folder so extracting the ZIP does not scatter files.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publishPath, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($file in @('SenseVoiceInput.App.exe', 'README.md', 'DEVELOP.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'licenses/Whisper-LICENSE.txt', 'licenses/Silero-VAD-LICENSE.txt', 'licenses/ONNXRuntime-ThirdPartyNotices.txt')) {
        if ($null -eq $zip.GetEntry("$packageName/$file")) { throw "Missing ZIP entry: $file" }
    }
} finally { $zip.Dispose() }
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksumPath, "$hash  $packageName.zip`n", [Text.UTF8Encoding]::new($false))
Write-Host "ZIP: $zipPath"
Write-Host "SHA256: $checksumPath"
