#Requires -Version 5.1
<#
.SYNOPSIS
  Deploy Jaromin Maestro UCCNC plugin and retire the legacy screenset.

.PARAMETER ProfileName
  UCCNC profile name without extension (Default, etc.). Used only to locate the
  profile's macro folder for legacy cleanup; the active screenset is left as-is.

.PARAMETER ProfileMacroFolder
  Macro folder for the active profile. Defaults from ProfileName.

.PARAMETER SkipBuild
  Skip running build-plugin.ps1 before deploy.
#>
param(
    [string]$ProfileName = "Default",
    [string]$ProfileMacroFolder = "",
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
$ScreensDir = Join-Path $UccncRoot "Screens"
$BmpDir = Join-Path $UccncRoot "Flashscreen\BMP\Jaromin"
$DllName = "JarominMaestro.dll"

if (-not $ProfileMacroFolder) {
    $ProfileMacroFolder = Join-Path $UccncRoot "Profiles\Macro_$ProfileName"
}

Write-Host "Jaromin Maestro plugin installer" -ForegroundColor Cyan
Write-Host "  Profile: $ProfileName"
Write-Host "  Macros:  $ProfileMacroFolder"
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

# Retire legacy screenset artifacts
$jarominSsf = Join-Path $ScreensDir "Jaromin.ssf"
if (Test-Path $jarominSsf) {
    Remove-Item -Force $jarominSsf
    Write-Host "[OK] Removed screenset $jarominSsf"
}
if (Test-Path $BmpDir) {
    Remove-Item -Recurse -Force $BmpDir
    Write-Host "[OK] Removed tab images $BmpDir"
}

$macroMin = 20797
$macroMax = 20886
$macroFolders = @(
    $ProfileMacroFolder,
    (Join-Path $UccncRoot "Profiles\Macro"),
    (Join-Path $UccncRoot "Macro")
) | Select-Object -Unique

foreach ($folder in $macroFolders) {
    if (-not (Test-Path $folder)) { continue }
    for ($n = $macroMin; $n -le $macroMax; $n++) {
        $macroFile = Join-Path $folder ("M{0}.txt" -f $n)
        if (Test-Path $macroFile) {
            Remove-Item -Force $macroFile
            Write-Host "[OK] Removed macro $macroFile"
        }
    }
}

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
