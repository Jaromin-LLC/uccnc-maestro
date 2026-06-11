#Requires -Version 5.1
<#
.SYNOPSIS
  Build and bundle a self-contained Jaromin CNC Maestro installer package.

.DESCRIPTION
  Compiles the plugin (via plugin/build-plugin.ps1), then stages a distributable
  folder under dist/ containing the prebuilt DLL, the seed config, the standalone
  installer (Install.ps1 / Install.bat), and a README. Finally zips it for handoff.

  Run this on a development machine that has csc.exe and UCCNC's Plugininterface.dll
  available (the build step needs them). The resulting zip needs none of that on
  the target machine.

.PARAMETER Version
  Version label used in the zip file name. Defaults to 1.0.0.

.PARAMETER SkipBuild
  Skip recompiling and reuse an existing plugin/build/JarominMaestro.dll.
#>
param(
    [string]$Version = "1.0.0",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $repoRoot "plugin"

$DllName = "JarominMaestro.dll"
$builtDll = Join-Path $pluginRoot "build\$DllName"
$seedConfig = Join-Path $pluginRoot "config\projects.json"

$distRoot = Join-Path $repoRoot "dist"
$stageDir = Join-Path $distRoot "JarominMaestro"
$zipPath = Join-Path $distRoot "JarominMaestro-$Version.zip"

Write-Host "Packaging Jaromin CNC Maestro $Version" -ForegroundColor Cyan
Write-Host ""

# --- 1) Build (unless skipped) ---
if (-not $SkipBuild) {
    Write-Host "Building plugin..."
    & (Join-Path $pluginRoot "build-plugin.ps1")
    Write-Host ""
}
if (-not (Test-Path $builtDll)) { throw "Built DLL missing: $builtDll (run without -SkipBuild)." }
if (-not (Test-Path $seedConfig)) { throw "Seed config missing: $seedConfig." }

# --- 2) Stage the payload ---
if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stageDir "config") | Out-Null

Copy-Item -Force $builtDll (Join-Path $stageDir $DllName)
Copy-Item -Force $seedConfig (Join-Path $stageDir "config\projects.json")
Copy-Item -Force (Join-Path $PSScriptRoot "Install.ps1") (Join-Path $stageDir "Install.ps1")
Copy-Item -Force (Join-Path $PSScriptRoot "Install.bat") (Join-Path $stageDir "Install.bat")
Copy-Item -Force (Join-Path $PSScriptRoot "PACKAGE_README.txt") (Join-Path $stageDir "README.txt")
Write-Host "[OK] Staged payload -> $stageDir"

# --- 3) Zip it ---
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath
Write-Host "[OK] Package -> $zipPath" -ForegroundColor Green
Write-Host ""
Write-Host "Hand the zip to the target machine, unzip it, and run Install.bat."
exit 0
