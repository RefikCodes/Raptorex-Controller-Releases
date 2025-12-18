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

    # Create legacy-named copies for backward compatibility
    $latest = Get-ChildItem "$solutionRoot\dist\RaptorexController-*-Setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -ne $latest) {
        if ($latest.Name -match 'RaptorexController-(?<ver>[^-]+)-Setup\.exe') {
            $ver = $Matches['ver']
            # Build short version by trimming trailing .0 segments
            $short = $ver
            while ($short -match '\\.0$') { $short = $short -replace '\\.0$', '' }

            $legacyFull = Join-Path $solutionRoot "dist/RaptorexController_Setup_$ver.exe"
            $legacyShort = Join-Path $solutionRoot "dist/RaptorexController_Setup_$short.exe"

            Copy-Item $latest.FullName $legacyFull -Force
            if ($legacyShort -ne $legacyFull) { Copy-Item $latest.FullName $legacyShort -Force }

            Write-Host "Legacy assets created:" -ForegroundColor Yellow
            Write-Host " - $(Split-Path $legacyFull -Leaf)" -ForegroundColor Yellow
            if ($legacyShort -ne $legacyFull) { Write-Host " - $(Split-Path $legacyShort -Leaf)" -ForegroundColor Yellow }
        }
    }
} else {
    Write-Warning "Inno Setup not found. Install Inno Setup 6 or run manually via ISCC. Script skipped installer generation."
}

Pop-Location
