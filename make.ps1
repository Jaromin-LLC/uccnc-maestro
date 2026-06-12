#Requires -Version 5.1
<#
.SYNOPSIS
  Single entry point for building, installing, and packaging Jaromin CNC Maestro.

.DESCRIPTION
  Make-style targets:

    build     Compile plugin\src\*.cs -> plugin\build\JarominMaestro.dll  (default)
    install   build, then deploy the DLL to <UccncRoot>\Plugins and seed the
              Maestro config folder. Existing projects.json / tools.json are
              NEVER overwritten.
    package   build, then stage the end-user installer payload under dist\
              and zip it for handoff (dist\JarominMaestro-<Version>.zip).
    clean     Delete build output (plugin\build) and dist\.

.PARAMETER Target
  One of: build, install, package, clean. Defaults to build.

.PARAMETER UccncRoot
  UCCNC installation folder. Used by build (for Plugininterface.dll) and
  install (deploy destination). Defaults to C:\UCCNC.

.PARAMETER Version
  Version label for the package zip name. Defaults to 1.0.0.

.PARAMETER SkipBuild
  For install/package: reuse an existing plugin\build\JarominMaestro.dll
  instead of recompiling.

.EXAMPLE
  .\make.ps1                      # build
  .\make.ps1 install              # build + deploy to C:\UCCNC
  .\make.ps1 package -Version 1.2.0
  .\make.ps1 clean
#>
param(
    [Parameter(Position = 0)]
    [ValidateSet("build", "install", "package", "clean")]
    [string]$Target = "build",
    [string]$UccncRoot = "C:\UCCNC",
    [string]$Version = "1.0.0",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$PluginRoot = Join-Path $RepoRoot "plugin"
$SrcDir = Join-Path $PluginRoot "src"
$BuildDir = Join-Path $PluginRoot "build"
$ConfigDir = Join-Path $PluginRoot "config"
$InstallerDir = Join-Path $RepoRoot "installer"
$DistRoot = Join-Path $RepoRoot "dist"
$DllName = "JarominMaestro.dll"

function Invoke-Build {
    $pluginInterface = Join-Path $UccncRoot "Plugininterface.dll"
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

    if (-not (Test-Path $pluginInterface)) { throw "Plugininterface.dll not found at $pluginInterface (set -UccncRoot)" }
    if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

    New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null

    $sourceFiles = Get-ChildItem -Path $SrcDir -Filter "*.cs" -Recurse | Sort-Object FullName
    if ($sourceFiles.Count -eq 0) { throw "No C# source files found under $SrcDir" }

    $refs = @(
        $pluginInterface,
        "System.dll",
        "System.Core.dll",
        "System.Drawing.dll",
        "System.Windows.Forms.dll",
        "System.Web.Extensions.dll"
    )
    $refArgs = $refs | ForEach-Object { "/reference:`"$_`"" }
    $srcArgs = $sourceFiles | ForEach-Object { "`"$($_.FullName)`"" }

    $outPath = Join-Path $BuildDir $DllName
    if (Test-Path $outPath) { Remove-Item $outPath -Force }

    Write-Host "Compiling $DllName ..."
    & $csc /nologo /target:library /platform:anycpu /optimize+ `
        /out:"$outPath" `
        @refArgs `
        @srcArgs
    if ($LASTEXITCODE -ne 0) { throw "Plugin build failed." }

    Write-Host "[OK] Built $outPath" -ForegroundColor Green
}

function Invoke-Install {
    if (-not $SkipBuild) { Invoke-Build; Write-Host "" }

    $dllPath = Join-Path $BuildDir $DllName
    if (-not (Test-Path $dllPath)) { throw "Build output missing: $dllPath (run '.\make.ps1 build')" }
    if (-not (Test-Path $UccncRoot)) { throw "UCCNC not found at $UccncRoot" }

    if (Get-Process -Name "UCCNC" -ErrorAction SilentlyContinue) {
        throw "UCCNC is currently running. Close UCCNC and run '.\make.ps1 install' again."
    }

    $pluginsDir = Join-Path $UccncRoot "Plugins"
    $maestroRoot = Join-Path $UccncRoot "Maestro"

    New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null
    Copy-Item -Force $dllPath (Join-Path $pluginsDir $DllName)
    Write-Host "[OK] Plugin -> $pluginsDir\$DllName"

    New-Item -ItemType Directory -Force -Path $maestroRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $maestroRoot "Media") | Out-Null

    # Seed configs only on first install - never overwrite live workflow data.
    foreach ($name in "projects.json", "tools.json") {
        $dest = Join-Path $maestroRoot $name
        if (Test-Path $dest) {
            Write-Host "[SKIP] Existing $name kept: $dest"
        } else {
            Copy-Item (Join-Path $ConfigDir $name) $dest
            Write-Host "[OK] Seeded $name -> $dest"
        }
    }

    Write-Host ""
    Write-Host "Deployed. If this is a first install:" -ForegroundColor Green
    Write-Host "  1. Start UCCNC -> Configuration -> Plugins -> enable JarominMaestro, check Call startup"
    Write-Host "  2. Restart UCCNC - the Maestro window opens"
    Write-Host "  3. One-time machine setup (probing / tool change): docs\M6_SETUP.md"
}

function Invoke-Package {
    if (-not $SkipBuild) { Invoke-Build; Write-Host "" }

    $dllPath = Join-Path $BuildDir $DllName
    if (-not (Test-Path $dllPath)) { throw "Build output missing: $dllPath (run '.\make.ps1 build')" }

    $stageDir = Join-Path $DistRoot "JarominMaestro"
    $zipPath = Join-Path $DistRoot "JarominMaestro-$Version.zip"

    if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
    New-Item -ItemType Directory -Force -Path (Join-Path $stageDir "config") | Out-Null

    Copy-Item -Force $dllPath (Join-Path $stageDir $DllName)
    Copy-Item -Force (Join-Path $ConfigDir "projects.json") (Join-Path $stageDir "config\projects.json")
    Copy-Item -Force (Join-Path $ConfigDir "tools.json") (Join-Path $stageDir "config\tools.json")
    Copy-Item -Force (Join-Path $InstallerDir "Install.ps1") (Join-Path $stageDir "Install.ps1")
    Copy-Item -Force (Join-Path $InstallerDir "Install.bat") (Join-Path $stageDir "Install.bat")
    Copy-Item -Force (Join-Path $InstallerDir "PACKAGE_README.txt") (Join-Path $stageDir "README.txt")
    Write-Host "[OK] Staged payload -> $stageDir"

    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath
    Write-Host "[OK] Package -> $zipPath" -ForegroundColor Green
    Write-Host ""
    Write-Host "Hand the zip to the target machine, unzip it, and run Install.bat."
}

function Invoke-Clean {
    foreach ($dir in $BuildDir, $DistRoot) {
        if (Test-Path $dir) {
            Remove-Item -Recurse -Force $dir
            Write-Host "[OK] Removed $dir"
        }
    }
}

switch ($Target) {
    "build"   { Invoke-Build }
    "install" { Invoke-Install }
    "package" { Invoke-Package }
    "clean"   { Invoke-Clean }
}
exit 0
