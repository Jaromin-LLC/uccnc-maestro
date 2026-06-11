#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $PSScriptRoot 'src'
$outDir = Join-Path $PSScriptRoot 'build'
$dllName = 'JarominMaestro.dll'

$uccncRoot = 'C:\UCCNC'
$pluginInterface = Join-Path $uccncRoot 'Plugininterface.dll'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $pluginInterface)) {
    throw "Plugininterface.dll not found at $pluginInterface"
}
if (-not (Test-Path $csc)) {
    throw "csc.exe not found at $csc"
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sourceFiles = Get-ChildItem -Path $srcDir -Filter '*.cs' -Recurse | Sort-Object FullName
if ($sourceFiles.Count -eq 0) {
    throw "No C# source files found under $srcDir"
}

$refs = @(
    $pluginInterface,
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.Extensions.dll'
)

$refArgs = $refs | ForEach-Object { "/reference:`"$_`"" }
$srcArgs = $sourceFiles | ForEach-Object { "`"$($_.FullName)`"" }

$outPath = Join-Path $outDir $dllName
if (Test-Path $outPath) { Remove-Item $outPath -Force }

Write-Host "Compiling $dllName ..."
& $csc /nologo /target:library /platform:anycpu /optimize+ `
    /out:"$outPath" `
    @refArgs `
    @srcArgs

if ($LASTEXITCODE -ne 0) {
    throw "Plugin build failed."
}

Write-Host "Built $outPath"
exit 0
