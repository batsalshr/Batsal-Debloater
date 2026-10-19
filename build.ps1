<#
.SYNOPSIS
  Builds, tests and packages ULTIMATE PC OPTIMIZER PRO.
.DESCRIPTION
  1. Restores and builds the solution (Release)
  2. Runs the unit/integration tests
  3. Publishes a self-contained single-file win-x64 executable to publish\win-x64
  4. Creates a portable ZIP in publish\
  5. Compiles the Inno Setup installer if ISCC.exe (Inno Setup 6) is installed
.PARAMETER SkipTests
  Skip the test step.
#>
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$cmd = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($cmd) { $cmd.Source } else { $null }
if (-not $dotnet -and (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe")) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
if (-not $dotnet) { throw ".NET 8 SDK not found. Install it from https://dot.net/download" }

& $dotnet build UltimatePCOptimizerPro.sln -c Release -nologo
if ($LASTEXITCODE) { throw "Build failed" }

if (-not $SkipTests) {
    & $dotnet test tests\UltimatePCOptimizerPro.Tests -c Release --no-build -nologo
    if ($LASTEXITCODE) { throw "Tests failed" }
}

& $dotnet publish src\UltimatePCOptimizerPro\UltimatePCOptimizerPro.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o publish\win-x64 -nologo
if ($LASTEXITCODE) { throw "Publish failed" }

$zip = "publish\UltimatePCOptimizerPro-1.0.0-win-x64-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path publish\win-x64\UltimatePCOptimizerPro.exe, README.md, docs\USER_GUIDE.md -DestinationPath $zip
Write-Host "Portable package: $zip"

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc installer\UltimatePCOptimizerPro.iss
    if ($LASTEXITCODE) { throw "Installer build failed" }
} else {
    Write-Warning "Inno Setup 6 not found - installer not built. Install it from https://jrsoftware.org/isinfo.php and re-run."
}
