# Requires PowerShell 7 (ConvertFrom-Markdown).
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/pages'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'store/privacy-policy.ja.md'
$markdown = Get-Content -LiteralPath $source -Raw -Encoding utf8
if ($markdown -match '\{\{[^}]+\}\}') { throw 'Privacy policy has unresolved placeholders.' }
if ($markdown -notmatch '施行日: \d{4}年\d{1,2}月\d{1,2}日') { throw 'Privacy policy effective date is missing.' }
$config = Get-Content -LiteralPath (Join-Path $repo 'store/store-config.json') -Raw -Encoding utf8 | ConvertFrom-Json
$url = [uri]$config.privacyPolicyUrl
if (-not $url.IsAbsoluteUri -or $url.Scheme -ne 'https') { throw 'A public HTTPS privacyPolicyUrl is required.' }
$template = Get-Content -LiteralPath (Join-Path $repo 'site/template.html') -Raw -Encoding utf8
$html = $template.Replace('{{CANONICAL_URL}}', [Net.WebUtility]::HtmlEncode($url.AbsoluteUri)).Replace('{{CONTENT}}', (ConvertFrom-Markdown -InputObject $markdown).Html)
if ($html -match '\{\{[^}]+\}\}') { throw 'Page template has unresolved placeholders.' }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw 'Output directory already exists. Choose a new directory.' }
[IO.Directory]::CreateDirectory((Join-Path $outputPath 'privacy')) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $outputPath 'privacy/index.html'), $html, $utf8)
Copy-Item -LiteralPath (Join-Path $repo 'site/style.css') -Destination (Join-Path $outputPath 'style.css')
$index = '<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>SenseVoice Input</title><body><h1>SenseVoice Input</h1><p><a href="privacy/">プライバシーポリシー</a></p><p><a href="https://github.com/mikoto2000/sensevoice-input">アプリの説明・ソースコード</a></p></body></html>'
[IO.File]::WriteAllText((Join-Path $outputPath 'index.html'), $index, $utf8)
[IO.File]::WriteAllText((Join-Path $outputPath '.nojekyll'), '', $utf8)
Write-Host "Pages output: $outputPath"
