param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '../store/store-config.json'),
    [string]$OutputDirectory,
    [string]$MakeAppxPath,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($name in @('identityName', 'publisher', 'publisherDisplayName', 'displayName', 'version', 'minWindowsVersion', 'maxWindowsVersionTested')) {
    if (-not $config.PSObject.Properties[$name] -or [string]::IsNullOrWhiteSpace([string]$config.$name)) {
        throw "Fill '$name' in $ConfigPath using the reserved product identity."
    }
    if ([string]$config.$name -match '\{\{|\}\}') { throw "Unresolved placeholder: $name" }
}
if ($config.identityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'Invalid package identity name.' }
if ($config.publisher -notmatch '^CN=') { throw 'Publisher must be the complete CN=... value from Partner Center.' }
if ($config.version -notmatch '^[1-9][0-9]*\.[0-9]+\.[0-9]+\.0$') { throw 'Store version must be Major.Minor.Build.0 (Major > 0).' }
foreach ($field in @('version', 'minWindowsVersion', 'maxWindowsVersionTested')) {
    if ([string]$config.$field -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "Invalid version: $field" }
    $parsed = [version]$config.$field
    if (@($parsed.Major, $parsed.Minor, $parsed.Build, $parsed.Revision | Where-Object { $_ -gt 65535 }).Count) { throw "Version component too large: $field" }
}
if ([version]$config.minWindowsVersion -lt [version]'10.0.22000.0') { throw 'This application targets Windows 11 or later.' }
if ([version]$config.maxWindowsVersionTested -lt [version]$config.minWindowsVersion) { throw 'MaxVersionTested is lower than MinVersion.' }

$templatePath = Join-Path $repo 'packaging/msix/AppxManifest.template.xml'
$manifest = Get-Content -LiteralPath $templatePath -Raw -Encoding UTF8
$tokens = @{
    IDENTITY_NAME = $config.identityName; PUBLISHER = $config.publisher; VERSION = $config.version
    DISPLAY_NAME = $config.displayName; PUBLISHER_DISPLAY_NAME = $config.publisherDisplayName
    MIN_WINDOWS_VERSION = $config.minWindowsVersion; MAX_WINDOWS_VERSION_TESTED = $config.maxWindowsVersionTested
}
foreach ($key in $tokens.Keys) { $manifest = $manifest.Replace("{{$key}}", [Security.SecurityElement]::Escape([string]$tokens[$key])) }
if ($manifest -match '\{\{') { throw 'Unresolved manifest placeholder.' }
[xml]$parsedManifest = $manifest
$assets = Join-Path $repo 'packaging/msix/Assets'
Add-Type -AssemblyName System.Drawing
foreach ($entry in @{ 'StoreLogo.png' = 50; 'Square44x44Logo.png' = 44; 'Square150x150Logo.png' = 150 }.GetEnumerator()) {
    $asset = [Drawing.Image]::FromFile((Join-Path $assets $entry.Key))
    try { if ($asset.Width -ne $entry.Value -or $asset.Height -ne $entry.Value) { throw "Wrong asset dimensions: $($entry.Key)" } }
    finally { $asset.Dispose() }
}
Write-Warning 'This builds a testable package, not a submission approval. Complete store/submission-checklist.md, including installed-package startup tests.'
if ($ValidateOnly) { Write-Host 'Package configuration, XML and base assets validated.'; return }

if (-not $MakeAppxPath) {
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $candidates = @(Get-ChildItem -LiteralPath $sdkBin -Directory | Where-Object { $_.Name -match '^10\.0\.\d+\.0$' } | Sort-Object { [version]$_.Name } -Descending)
    foreach ($candidate in $candidates) {
        $tool = Join-Path $candidate.FullName 'x64/makeappx.exe'
        if (Test-Path -LiteralPath $tool) { $MakeAppxPath = $tool; break }
    }
}
if (-not $MakeAppxPath -or -not (Test-Path -LiteralPath $MakeAppxPath -PathType Leaf)) { throw 'Install Windows SDK or supply -MakeAppxPath.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "artifacts/store/$($config.version)" }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw 'Output directory already exists. Choose a new output directory.' }
$payload = Join-Path $outputPath 'payload'
$buildPath = Join-Path $outputPath 'build'
[IO.Directory]::CreateDirectory($payload) | Out-Null
# The app project already targets win-x64. Do not pass -r globally: doing so
# would change the RID of its library projects and invalidate their lockfiles.
& dotnet publish (Join-Path $repo 'src/SenseVoiceInput.App') -c Release --self-contained true --artifacts-path $buildPath -p:PublishSingleFile=false -p:PublishTrimmed=false -p:RestoreLockedMode=true "-p:Version=$($config.version)" -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Store publish failed.' }
foreach ($file in @('SenseVoiceInput.App.exe', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'licenses/Whisper-LICENSE.txt', 'licenses/nvidia/cudnn-LICENSE')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $file))) { throw "Missing payload file: $file" }
}
$unexpected = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object { $_.Name -match '\.onnx$|^(sherpa|org\.k2fsa).*\.(dll|exe)$|^(cudart|cublas|cufft|cudnn|nvrtc).*\.dll$' })
if ($unexpected.Count) { throw 'Unexpected model or NVIDIA binary in standard Store package.' }

# Preserve the notices from the actual runtime packs resolved by this build.
$runtimeConfig = Get-Content -LiteralPath (Join-Path $payload 'SenseVoiceInput.App.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$noticePath = Join-Path $payload 'licenses/dotnet'
[IO.Directory]::CreateDirectory($noticePath) | Out-Null
foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
    $packageId = "$($framework.name.ToLowerInvariant()).runtime.win-x64"
    $packPath = Join-Path $repo ".packages/$packageId/$($framework.version)"
    $notices = @(Get-ChildItem -LiteralPath $packPath -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
    if (-not ($notices | Where-Object { $_.Name -match '^LICENSE' })) { throw "Runtime license missing: $packPath" }
    if ($framework.name -eq 'Microsoft.NETCore.App' -and -not ($notices | Where-Object { $_.Name -eq 'THIRD-PARTY-NOTICES.TXT' })) { throw 'Core runtime third-party notices missing.' }
    foreach ($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $noticePath "$($framework.name)-$($notice.Name)")
    }
}
[IO.File]::WriteAllText((Join-Path $noticePath 'README.txt'), "This self-contained Store package includes the .NET runtimes listed in SenseVoiceInput.App.runtimeconfig.json. The accompanying runtime licenses and notices apply in addition to THIRD_PARTY_NOTICES.md.`n", [Text.UTF8Encoding]::new($false))
[IO.Directory]::CreateDirectory((Join-Path $payload 'Assets')) | Out-Null
foreach ($name in @('StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png')) {
    Copy-Item -LiteralPath (Join-Path $assets $name) -Destination (Join-Path $payload "Assets/$name")
}
[IO.File]::WriteAllText((Join-Path $payload 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
$packagePath = Join-Path $outputPath "SenseVoiceInput-$($config.version)-x64.msix"
& $MakeAppxPath pack /d $payload /p $packagePath /h SHA256
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx validation/packaging failed.' }
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$packagePath.sha256", "$hash  $([IO.Path]::GetFileName($packagePath))`n", [Text.UTF8Encoding]::new($false))
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'Could not record .NET SDK version.' }
$revision = & git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not record Git revision.' }
$dirty = & git -C $repo status --porcelain
$metadata = [ordered]@{ createdUtc = [DateTime]::UtcNow.ToString('o'); gitCommit = $revision; hasUncommittedChanges = [bool]$dirty; sdk = $sdkVersion; package = [IO.Path]::GetFileName($packagePath); sha256 = $hash; identity = $config.identityName; version = $config.version; signed = $false }
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'build-info.json') -Encoding utf8
Write-Host "Unsigned MSIX: $packagePath"
