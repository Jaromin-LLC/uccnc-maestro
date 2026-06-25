#Requires -Version 5.1
<#
.SYNOPSIS
  Graphical installer for the (uc)CNC Maestro UCCNC plugin.

.DESCRIPTION
  Opens a setup window that lets you confirm the UCCNC folder and choose
  whether existing workflow data (projects.json / tools.json) is kept or
  overwritten with the bundled seed files. Copies the prebuilt plugin DLL
  into <UCCNC>\Plugins and seeds <UCCNC>\Maestro. No build tools, source,
  or internet connection are required - everything ships next to this script.

  Run with -Yes for a fully unattended (no window) install.

.PARAMETER UccncRoot
  Path to the UCCNC installation directory (the folder containing UCCNC.exe
  and Plugininterface.dll). If omitted, the installer auto-detects a likely
  location and pre-fills it in the window.

.PARAMETER Yes
  Headless install: no window, no prompts. Requires a valid -UccncRoot or a
  detectable default.

.PARAMETER OverwriteConfigs
  Headless mode only: replace existing projects.json / tools.json with the
  bundled seed files. Default is to keep existing workflow data.

.EXAMPLE
  .\Install.ps1
  Opens the setup window.

.EXAMPLE
  .\Install.ps1 -UccncRoot "D:\UCCNC" -Yes
  Unattended install into D:\UCCNC, keeping any existing workflow data.
#>
param(
    [string]$UccncRoot = "",
    [switch]$Yes,
    [switch]$OverwriteConfigs
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

$DllName = "UccncMaestro.dll"
$dllSource = Join-Path $here $DllName
$configSource = Join-Path $here "config\projects.json"
$toolsSource = Join-Path $here "config\tools.json"

function Test-UccncDir {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return (Test-Path (Join-Path $Path "Plugininterface.dll"))
}

function Find-UccncRoot {
    $guesses = @("C:\UCCNC", "$env:SystemDrive\UCCNC", "D:\UCCNC", "E:\UCCNC") | Select-Object -Unique
    $detected = $guesses | Where-Object { Test-UccncDir $_ } | Select-Object -First 1
    if ($detected) { return $detected }
    return "C:\UCCNC"
}

# Performs the install. $Log is a callback that receives one line at a time.
function Install-Maestro {
    param(
        [string]$Root,
        [bool]$Overwrite,
        [scriptblock]$Log
    )

    if (Get-Process -Name "UCCNC" -ErrorAction SilentlyContinue) {
        throw "UCCNC is currently running. Close UCCNC and try again."
    }

    # 1) Plugin DLL -> <Root>\Plugins
    $pluginsDir = Join-Path $Root "Plugins"
    New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null
    Copy-Item -Force $dllSource (Join-Path $pluginsDir $DllName)
    & $Log "[OK] Plugin -> $pluginsDir\$DllName"

    # Remove old (pre-rename) plugin DLLs if present so they cannot load alongside
    # the renamed UccncMaestro.dll. JarominMaestro.dll is the immediate predecessor;
    # JarominWizard.dll is the original name from even earlier builds.
    foreach ($legacyName in "JarominMaestro.dll", "JarominWizard.dll") {
        $legacyDll = Join-Path $pluginsDir $legacyName
        if (Test-Path $legacyDll) {
            Remove-Item -Force $legacyDll
            & $Log "[OK] Removed old plugin -> $legacyDll"
        }
    }

    # 2) Workflow data -> <Root>\Maestro
    $maestroRoot = Join-Path $Root "Maestro"
    New-Item -ItemType Directory -Force -Path $maestroRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $maestroRoot "Media") | Out-Null

    foreach ($pair in @($configSource, "projects.json"), @($toolsSource, "tools.json")) {
        $source = $pair[0]; $name = $pair[1]
        $dest = Join-Path $maestroRoot $name
        if (-not (Test-Path $source)) {
            & $Log "[INFO] No seed $name in package; the plugin will create defaults on first run."
        } elseif ((Test-Path $dest) -and -not $Overwrite) {
            & $Log "[SKIP] Existing $name kept: $dest"
        } else {
            if (Test-Path $dest) { & $Log "[WARN] Overwriting $dest" }
            Copy-Item -Force $source $dest
            & $Log "[OK] $name -> $dest"
        }
    }

    # The plugin reads its data from C:\UCCNC\Maestro. Warn if installing elsewhere.
    if ($Root.TrimEnd('\') -ne "C:\UCCNC") {
        & $Log "[WARN] The plugin currently reads workflow data from C:\UCCNC\Maestro."
        & $Log "[WARN] You installed into '$Root'; copy the Maestro folder to C:\UCCNC if the plugin cannot find its config."
    }

    & $Log ""
    & $Log "Done. Next steps:"
    & $Log "  1. Start UCCNC"
    & $Log "  2. Configuration -> Plugins -> enable 'UccncMaestro', check 'Call startup'"
    & $Log "  3. Restart UCCNC - the Maestro window opens"
}

if (-not $UccncRoot) { $UccncRoot = Find-UccncRoot }
$UccncRoot = $UccncRoot.Trim().Trim('"').TrimEnd('\')

$packageError = $null
if (-not (Test-Path $dllSource)) {
    $packageError = "$DllName was not found next to this installer.`n`n" +
        "This script is meant to run from inside a release package. " +
        "If you are working from the source repository, build one first:`n`n" +
        "    .\make.ps1 package`n`n" +
        "then run Install.bat from dist\UccncMaestro\ (or just use '.\make.ps1 install' for a local developer deploy)."
}

# --- Headless mode ---
if ($Yes) {
    if ($packageError) { throw ($packageError -replace "`n", " ") }
    if (-not (Test-UccncDir $UccncRoot)) {
        throw "'$UccncRoot' is not a valid UCCNC install (Plugininterface.dll not found). Provide a correct -UccncRoot."
    }
    Write-Host "(uc)CNC Maestro - unattended install -> $UccncRoot" -ForegroundColor Cyan
    Install-Maestro -Root $UccncRoot -Overwrite:$OverwriteConfigs.IsPresent -Log { param($line) Write-Host $line }
    exit 0
}

# --- GUI mode ---
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# Surface fatal problems in a dialog - the launching console is minimized,
# so anything written to stdout/stderr would vanish unseen.
if ($packageError) {
    [System.Windows.Forms.MessageBox]::Show($packageError, "(uc)CNC Maestro Setup",
        [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "(uc)CNC Maestro Setup"
$form.Size = New-Object System.Drawing.Size(620, 560)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = "(uc)CNC Maestro"
$titleLabel.Font = New-Object System.Drawing.Font("Segoe UI", 14, [System.Drawing.FontStyle]::Bold)
$titleLabel.Location = New-Object System.Drawing.Point(16, 12)
$titleLabel.AutoSize = $true
$form.Controls.Add($titleLabel)

$subLabel = New-Object System.Windows.Forms.Label
$subLabel.Text = "Installs the Maestro plugin into your UCCNC installation."
$subLabel.Location = New-Object System.Drawing.Point(18, 44)
$subLabel.AutoSize = $true
$form.Controls.Add($subLabel)

# --- UCCNC folder picker ---
$dirGroup = New-Object System.Windows.Forms.GroupBox
$dirGroup.Text = "UCCNC installation folder"
$dirGroup.Location = New-Object System.Drawing.Point(16, 74)
$dirGroup.Size = New-Object System.Drawing.Size(572, 84)
$form.Controls.Add($dirGroup)

$dirBox = New-Object System.Windows.Forms.TextBox
$dirBox.Location = New-Object System.Drawing.Point(12, 26)
$dirBox.Width = 450
$dirBox.Text = $UccncRoot
$dirGroup.Controls.Add($dirBox)

$browseBtn = New-Object System.Windows.Forms.Button
$browseBtn.Text = "Browse..."
$browseBtn.Location = New-Object System.Drawing.Point(472, 24)
$browseBtn.Width = 88
$dirGroup.Controls.Add($browseBtn)

$dirStatus = New-Object System.Windows.Forms.Label
$dirStatus.Location = New-Object System.Drawing.Point(12, 56)
$dirStatus.AutoSize = $true
$dirGroup.Controls.Add($dirStatus)

$updateDirStatus = {
    if (Test-UccncDir $dirBox.Text.Trim()) {
        $dirStatus.Text = "Verified: Plugininterface.dll found."
        $dirStatus.ForeColor = [System.Drawing.Color]::DarkGreen
    } else {
        $dirStatus.Text = "Not verified: Plugininterface.dll not found in this folder."
        $dirStatus.ForeColor = [System.Drawing.Color]::Firebrick
    }
}
$dirBox.Add_TextChanged($updateDirStatus)
& $updateDirStatus

$browseBtn.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select the UCCNC installation folder (contains UCCNC.exe)"
    if ($dirBox.Text -and (Test-Path $dirBox.Text)) { $dlg.SelectedPath = $dirBox.Text }
    if ($dlg.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
        $dirBox.Text = $dlg.SelectedPath
    }
})

# --- Workflow data choice ---
$dataGroup = New-Object System.Windows.Forms.GroupBox
$dataGroup.Text = "Workflow data (projects.json / tools.json)"
$dataGroup.Location = New-Object System.Drawing.Point(16, 168)
$dataGroup.Size = New-Object System.Drawing.Size(572, 84)
$form.Controls.Add($dataGroup)

$keepRadio = New-Object System.Windows.Forms.RadioButton
$keepRadio.Text = "Keep existing workflow data if present (recommended)"
$keepRadio.Location = New-Object System.Drawing.Point(12, 24)
$keepRadio.Width = 540
$keepRadio.Checked = $true
$dataGroup.Controls.Add($keepRadio)

$overwriteRadio = New-Object System.Windows.Forms.RadioButton
$overwriteRadio.Text = "Overwrite with the bundled seed files (existing projects and tools are LOST)"
$overwriteRadio.Location = New-Object System.Drawing.Point(12, 50)
$overwriteRadio.Width = 540
$dataGroup.Controls.Add($overwriteRadio)

# --- Log output ---
$logBox = New-Object System.Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ReadOnly = $true
$logBox.ScrollBars = "Vertical"
$logBox.Font = New-Object System.Drawing.Font("Consolas", 9)
$logBox.Location = New-Object System.Drawing.Point(16, 262)
$logBox.Size = New-Object System.Drawing.Size(572, 200)
$form.Controls.Add($logBox)

$appendLog = {
    param($line)
    $logBox.AppendText("$line`r`n")
}

# --- Buttons ---
$installBtn = New-Object System.Windows.Forms.Button
$installBtn.Text = "Install"
$installBtn.Location = New-Object System.Drawing.Point(408, 474)
$installBtn.Size = New-Object System.Drawing.Size(88, 30)
$form.Controls.Add($installBtn)

$closeBtn = New-Object System.Windows.Forms.Button
$closeBtn.Text = "Close"
$closeBtn.Location = New-Object System.Drawing.Point(500, 474)
$closeBtn.Size = New-Object System.Drawing.Size(88, 30)
$closeBtn.Add_Click({ $form.Close() })
$form.Controls.Add($closeBtn)

$installBtn.Add_Click({
    $root = $dirBox.Text.Trim().Trim('"').TrimEnd('\')
    if (-not $root) {
        [System.Windows.Forms.MessageBox]::Show($form, "Choose the UCCNC installation folder.", "Maestro Setup",
            [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
        return
    }
    if (-not (Test-UccncDir $root)) {
        $resp = [System.Windows.Forms.MessageBox]::Show($form,
            "Plugininterface.dll was not found in:`n$root`n`nThis may not be a UCCNC installation. Install anyway?",
            "Maestro Setup", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($resp -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    }
    if ($overwriteRadio.Checked) {
        $resp = [System.Windows.Forms.MessageBox]::Show($form,
            "This will REPLACE any existing projects.json and tools.json in`n$root\Maestro`nwith the bundled seed files. Existing projects and tools will be lost.`n`nContinue?",
            "Maestro Setup", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($resp -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    }

    $installBtn.Enabled = $false
    $logBox.Clear()
    try {
        Install-Maestro -Root $root -Overwrite $overwriteRadio.Checked -Log $appendLog
        & $appendLog ""
        & $appendLog "Installation completed."
    } catch {
        & $appendLog "[ERROR] $($_.Exception.Message)"
        & $appendLog "Installation FAILED."
    } finally {
        $installBtn.Enabled = $true
    }
})

[System.Windows.Forms.Application]::Run($form) | Out-Null
exit 0
