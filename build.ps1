# 配布用 zip を作る:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

[xml]$proj = Get-Content TsuruNoHitokoe.csproj -Encoding utf8
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

$out = Join-Path $PSScriptRoot 'publish'
$dist = Join-Path $PSScriptRoot 'dist'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null

dotnet publish TsuruNoHitokoe.csproj -c Release -o $out
if ($LASTEXITCODE -ne 0) {
    # ウイルス対策ソフトのスキャンで一時的に失敗することがあるので 1 回だけやり直す
    Start-Sleep -Seconds 3
    dotnet publish TsuruNoHitokoe.csproj -c Release -o $out
    if ($LASTEXITCODE -ne 0) { throw 'publish に失敗しました' }
}

$stage = Join-Path $out 'stage'
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $out 'TsuruNoHitokoe.exe') $stage
Copy-Item dist-readme.txt (Join-Path $stage 'はじめにお読みください.txt')

$zip = Join-Path $dist "TsuruNoHitokoe_v$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "作成しました: $zip"
