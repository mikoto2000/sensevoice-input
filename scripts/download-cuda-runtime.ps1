param([string]$Destination = (Join-Path $PSScriptRoot '..\artifacts\cuda-runtime'), [switch]$AcceptNvidiaLicense)
$ErrorActionPreference = 'Stop'
if (-not $AcceptNvidiaLicense) { throw 'Read licenses/nvidia and THIRD_PARTY_NOTICES.md, then pass -AcceptNvidiaLicense to accept the NVIDIA terms. CPU does not require these files.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$downloadRoot = Join-Path $PSScriptRoot '..\artifacts\cuda-downloads'
New-Item -ItemType Directory -Force -Path $Destination, $downloadRoot | Out-Null
$entries = Get-Content (Join-Path $PSScriptRoot 'cuda-runtime-manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $entries) {
    $zipPath = Join-Path $downloadRoot ($entry.name + '.zip')
    if (!(Test-Path -LiteralPath $zipPath) -or (Get-FileHash -LiteralPath $zipPath).Hash -ne $entry.sha256) {
        $partial = $zipPath + '.partial'
        try {
            Invoke-WebRequest $entry.url -OutFile $partial
            if ((Get-FileHash -LiteralPath $partial).Hash -ne $entry.sha256) { throw "SHA256 mismatch: $($entry.name)" }
            Move-Item -LiteralPath $partial -Destination $zipPath -Force
        } finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    try {
        foreach ($member in $zip.Entries) {
            $name = [IO.Path]::GetFileName($member.FullName.Replace('\', '/'))
            $dll = $name -in $entry.dlls
            if (-not $dll -and $name -notmatch 'LICENSE|EULA|NOTICE|COPYING|COPYRIGHT') { continue }
            $outputName = if ($dll) { $name } else { $entry.name + '-' + $name }
            if (-not $names.Add($outputName)) { throw "Duplicate archive filename: $outputName" }
            $target = Join-Path $Destination $outputName
            $partial = $target + '.partial'
            try {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($member, $partial, $true)
                Move-Item -LiteralPath $partial -Destination $target -Force
            } finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
        }
    } finally { $zip.Dispose() }
    foreach ($required in (@($entry.dlls) + @($entry.name + '-LICENSE'))) {
        if (-not $names.Contains($required)) { throw "Missing required archive member: $required" }
    }
}
Write-Host "CUDA 13 + cuDNN 9 DLLs and notices ready: $Destination"
