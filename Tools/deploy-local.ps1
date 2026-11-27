# Copies the current build into Stacy's own install, without any release (mode local, 2026-11-27).
# Usage: powershell -File Tools\deploy-local.ps1   (the meter must be closed first)
$ErrorActionPreference = 'Stop'
$install = Join-Path $env:LOCALAPPDATA 'AionDpsMeterStacysView\current'
if (-not (Test-Path $install)) { throw "Install folder not found: $install" }
if (Get-Process -Name 'Aion DPS' -ErrorAction SilentlyContinue) { throw "Close the meter first (it is running)." }
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $env:TEMP 'aion-local-publish'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $repo 'Client\AionDPS.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $out
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
Copy-Item (Join-Path $out '*') $install -Recurse -Force
Write-Host "Local install updated: $install"
