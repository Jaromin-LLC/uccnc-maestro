#Requires -Version 5.1
<#
.SYNOPSIS
  Build and deploy the Jaromin Maestro UCCNC plugin (developer loop).

.PARAMETER SkipBuild
  Skip running build-plugin.ps1 before deploy.
#>
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$PluginRoot = Join-Path $Root "plugin"
$BuildRoot = Join-Path $PluginRoot "build"
$ConfigRoot = Join-Path $PluginRoot "config"
$UccncRoot = "C:\UCCNC"
$MaestroRoot = Join-Path $UccncRoot "Maestro"
$PluginsDir = Join-Path $UccncRoot "Plugins"
$GcodeDest = Join-Path $MaestroRoot "GCode"
$MediaDest = Join-Path $MaestroRoot "Media"
$DllName = "JarominMaestro.dll"

Write-Host "Jaromin Maestro plugin installer" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $UccncRoot)) {
    throw "UCCNC not found at $UccncRoot"
}

if (-not $SkipBuild) {
    Write-Host "Running build-plugin.ps1..."
    & (Join-Path $PluginRoot "build-plugin.ps1")
    Write-Host ""
}

$dllPath = Join-Path $BuildRoot $DllName
$configPath = Join-Path $ConfigRoot "projects.json"
$toolsConfigPath = Join-Path $ConfigRoot "tools.json"
if (-not (Test-Path $dllPath)) { throw "Build output missing: $dllPath" }
if (-not (Test-Path $configPath)) { throw "Config missing: $configPath" }
if (-not (Test-Path $toolsConfigPath)) { throw "Config missing: $toolsConfigPath" }

New-Item -ItemType Directory -Force -Path $PluginsDir | Out-Null
Copy-Item -Force $dllPath (Join-Path $PluginsDir $DllName)
Write-Host "[OK] Plugin -> $PluginsDir\$DllName"

New-Item -ItemType Directory -Force -Path $MaestroRoot | Out-Null
New-Item -ItemType Directory -Force -Path $MediaDest | Out-Null
Copy-Item -Force $configPath (Join-Path $MaestroRoot "projects.json")
Write-Host "[OK] Config -> $MaestroRoot\projects.json"
Copy-Item -Force $toolsConfigPath (Join-Path $MaestroRoot "tools.json")
Write-Host "[OK] Tool library -> $MaestroRoot\tools.json"

New-Item -ItemType Directory -Force -Path $GcodeDest | Out-Null
Write-Host "[OK] G-code folder ready -> $GcodeDest (copy your .nc files here)"

Write-Host ""
Write-Host "Deployment checklist:" -ForegroundColor Green
Write-Host "  1. Close UCCNC before installing; reopen after install"
Write-Host "  2. One-time machine setup: probing + tool change positions - see docs/M6_SETUP.md"
Write-Host "  3. UCCNC -> Configuration -> Plugins -> enable JarominMaestro, check Call startup"
Write-Host "  4. Restart UCCNC - Maestro window opens; your existing UCCNC screenset is left unchanged"
Write-Host "  5. Operator tab: select project, RUN a step or Run All"
Write-Host "  6. Admin tab: edit workflows, photos/videos, machine settings; Save All"
Write-Host "  7. Enable Test mode in Admin for demo (skips probing) - never use when cutting"
Write-Host ""
Write-Host "Config lives at $MaestroRoot\projects.json and $MaestroRoot\tools.json (editable in Admin tab)."
