param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'

$solutionRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $solutionRoot

Write-Host "Building Raptorex Controller ($Configuration)" -ForegroundColor Cyan

$msbuild = "C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\MSBuild\\Current\\Bin\\MSBuild.exe"
if (-not (Test-Path $msbuild)) { throw "MSBuild not found at $msbuild" }

& $msbuild "$solutionRoot\CncControlApp.csproj" /t:Build /p:Configuration=$Configuration /v:minimal

$iss = "C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe"
if (-not (Test-Path $iss)) {
    $iss = "C:\\Program Files\\Inno Setup 6\\ISCC.exe"
}

if (Test-Path $iss) {
    Write-Host "Building installer via Inno Setup..." -ForegroundColor Cyan
    & $iss "$solutionRoot\Installer\RaptorexController.iss"
    Write-Host "Installer built: $(Resolve-Path "$solutionRoot\dist")" -ForegroundColor Green
} else {
    Write-Warning "Inno Setup not found. Install Inno Setup 6 or run manually via ISCC. Script skipped installer generation."
}

Pop-Location
