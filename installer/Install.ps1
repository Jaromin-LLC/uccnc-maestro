#Requires -Version 5.1
<#
.SYNOPSIS
  Install the Jaromin CNC Maestro UCCNC plugin onto this machine.

.DESCRIPTION
  Copies the prebuilt plugin DLL into the UCCNC "Plugins" folder and seeds the
  Maestro workflow config. No build tools, source, or internet connection are
  required - everything needed ships next to this script.

.PARAMETER UccncRoot
  Path to the UCCNC installation directory (the folder that contains UCCNC.exe
  and Plugininterface.dll). If omitted, the installer auto-detects a likely
  location and asks you to confirm it.

.PARAMETER Force
  Overwrite an existing projects.json in the Maestro data folder. By default an
  existing config is preserved so operator edits are not lost.

.PARAMETER Yes
  Skip the interactive directory confirmation (for unattended/automated installs).
  Requires a valid -UccncRoot or a detectable default.

.EXAMPLE
  .\Install.ps1
  Auto-detects UCCNC and asks you to confirm the folder before installing.

.EXAMPLE
  .\Install.ps1 -UccncRoot "D:\UCCNC" -Yes
  Installs into D:\UCCNC without prompting.
#>
param(
    [string]$UccncRoot = "",
    [switch]$Force,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

$DllName = "JarominMaestro.dll"
$dllSource = Join-Path $here $DllName
$configSource = Join-Path $here "config\projects.json"

function Test-UccncDir {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return (Test-Path (Join-Path $Path "Plugininterface.dll"))
}

function Resolve-UccncRoot {
    param([string]$Initial, [switch]$NoPrompt)

    # Start from the supplied path, else auto-detect a common location.
    $candidate = $Initial.Trim().Trim('"').TrimEnd('\')
    if (-not $candidate) {
        $guesses = @("C:\UCCNC", "$env:SystemDrive\UCCNC", "D:\UCCNC", "E:\UCCNC") |
            Select-Object -Unique
        $detected = $guesses | Where-Object { Test-UccncDir $_ } | Select-Object -First 1
        $candidate = if ($detected) { $detected } else { "C:\UCCNC" }
    }

    if ($NoPrompt) {
        if (-not (Test-UccncDir $candidate)) {
            throw "'$candidate' is not a valid UCCNC install (Plugininterface.dll not found). Provide a correct -UccncRoot."
        }
        return $candidate
    }

    while ($true) {
        $verified = Test-UccncDir $candidate
        Write-Host ""
        Write-Host "UCCNC installation directory:" -ForegroundColor Cyan
        Write-Host "  $candidate"
        if ($verified) {
            Write-Host "  (verified: Plugininterface.dll found)" -ForegroundColor DarkGray
        } else {
            Write-Warning "Plugininterface.dll NOT found here - this may not be a UCCNC install."
        }

        $resp = Read-Host "Press ENTER to use this folder, type a different path, or Q to quit"
        if ($resp -match '^\s*(q|quit)\s*$') { throw "Installation cancelled by user." }

        if ([string]::IsNullOrWhiteSpace($resp)) {
            if ($verified) { return $candidate }
            $ok = Read-Host "Folder is not verified. Type Y to use it anyway, or ENTER to choose again"
            if ($ok -match '^\s*(y|yes)\s*$') { return $candidate }
            continue
        }

        $candidate = $resp.Trim().Trim('"').TrimEnd('\')
    }
}

Write-Host "Jaromin CNC Maestro - plugin installer" -ForegroundColor Cyan

# --- Validate the package ---
if (-not (Test-Path $dllSource)) {
    throw "Missing $DllName next to this installer. Re-download the package."
}

# --- Confirm / resolve the target UCCNC install ---
$UccncRoot = Resolve-UccncRoot -Initial $UccncRoot -NoPrompt:$Yes
if (-not (Test-Path $UccncRoot)) {
    throw "UCCNC directory not found: $UccncRoot"
}
if (-not (Test-UccncDir $UccncRoot)) {
    Write-Warning "Proceeding with an unverified UCCNC folder: $UccncRoot"
}

# --- Make sure UCCNC is closed (a loaded DLL cannot be replaced) ---
$running = Get-Process -Name "UCCNC" -ErrorAction SilentlyContinue
if ($running) {
    throw "UCCNC is currently running. Close UCCNC and run this installer again."
}

# --- 1) Plugin DLL -> <UccncRoot>\Plugins ---
$pluginsDir = Join-Path $UccncRoot "Plugins"
New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null
Copy-Item -Force $dllSource (Join-Path $pluginsDir $DllName)
Write-Host "[OK] Plugin -> $pluginsDir\$DllName"

# Remove the old (pre-rename) plugin if present so it cannot load alongside.
$legacyDll = Join-Path $pluginsDir "JarominWizard.dll"
if (Test-Path $legacyDll) {
    Remove-Item -Force $legacyDll
    Write-Host "[OK] Removed old plugin -> $legacyDll"
}

# --- 2) Seed config + data folders -> <UccncRoot>\Maestro ---
$maestroRoot = Join-Path $UccncRoot "Maestro"
$configDest = Join-Path $maestroRoot "projects.json"
New-Item -ItemType Directory -Force -Path $maestroRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $maestroRoot "GCode") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $maestroRoot "Media") | Out-Null

if ((Test-Path $configDest) -and -not $Force) {
    Write-Host "[SKIP] Existing config kept: $configDest (use -Force to overwrite)"
} elseif (Test-Path $configSource) {
    Copy-Item -Force $configSource $configDest
    Write-Host "[OK] Config -> $configDest"
} else {
    Write-Host "[INFO] No seed config in package; the plugin will create defaults on first run."
}

# The plugin reads its data from C:\UCCNC\Maestro. Warn if installing elsewhere.
if ($UccncRoot.TrimEnd('\') -ne "C:\UCCNC") {
    Write-Warning "The plugin currently reads workflow data from 'C:\UCCNC\Maestro'."
    Write-Warning "You installed into '$UccncRoot'; copy the Maestro folder to C:\UCCNC if the plugin cannot find its config."
}

Write-Host ""
Write-Host "Done. Next steps:" -ForegroundColor Green
Write-Host "  1. Start UCCNC"
Write-Host "  2. Configuration -> Plugins -> enable 'JarominMaestro', check 'Call startup'"
Write-Host "  3. Restart UCCNC - the Maestro window opens"
Write-Host ""
exit 0
