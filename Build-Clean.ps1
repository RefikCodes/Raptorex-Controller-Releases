<#!
.SYNOPSIS
Performs a clean (optionally deep) restore + build of the solution.

.PARAMETER Configuration
Build configuration (Debug|Release). Defaults to Release.

.PARAMETER DeepClean
Removes all bin/obj folders before restore/build.

.PARAMETER UseDotnet
Uses `dotnet` CLI instead of `nuget.exe` + `msbuild` where possible.

.PARAMETER RestoreOnly
Runs only restore (skips clean/build).

.PARAMETER BuildOnly
Skips restore (assumes already restored).

.PARAMETER Verbosity
MSBuild verbosity (quiet|minimal|normal|detailed|diagnostic). Default: minimal.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\Build-Clean.ps1 -DeepClean

#>
[CmdletBinding()]param(
    [ValidateSet("Debug","Release")] [string]$Configuration = "Release",
    [switch]$DeepClean,
    [switch]$UseDotnet,
    [switch]$RestoreOnly,
    [switch]$BuildOnly,
    [ValidateSet("quiet","minimal","normal","detailed","diagnostic")] [string]$Verbosity = "minimal"
)

$ErrorActionPreference = "Stop"

function Write-Section($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

$solution = "Raptorex Controller.sln"
if (-not (Test-Path $solution)) { throw "Solution file '$solution' not found in $(Get-Location)" }

# Detect assembly name from csproj (first match)
$csproj = "CncControlApp.csproj"
$assemblyName = "Rptx01"
if (Test-Path $csproj) {
    $match = Select-String -Path $csproj -Pattern "<AssemblyName>(.+)</AssemblyName>" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($match) { $assemblyName = $match.Matches[0].Groups[1].Value }
}

# Try to locate msbuild if not explicitly using dotnet
function Get-MsBuildPath {
    param()
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$Env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($p in $candidates) { if (Test-Path $p) { return $p } }
    return $null
}

if (-not $UseDotnet) {
    $msbuildExe = Get-MsBuildPath
    if ($msbuildExe) {
        Write-Section "Found msbuild: $msbuildExe"
    } else {
        Write-Warning "msbuild not found; switching to dotnet toolchain"
        $UseDotnet = $true
    }
}

Write-Section "Config=$Configuration DeepClean=$($DeepClean.IsPresent) UseDotnet=$($UseDotnet.IsPresent)"

if ($DeepClean) {
    Write-Section "Deep clean removing bin/obj"
    Get-ChildItem -Path . -Recurse -Include bin,obj -Directory | ForEach-Object {
        Write-Host "Removing $($_.FullName)" -ForegroundColor DarkGray
        Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $BuildOnly) {
    Write-Section "Restore packages"
    if ($UseDotnet) {
        & dotnet restore "$solution"
    } else {
        $nuget = Get-Command nuget -ErrorAction SilentlyContinue
        if ($nuget) { & nuget restore "$solution" } else { Write-Warning "nuget.exe not found; using dotnet restore"; & dotnet restore "$solution" }
    }
    if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE" }
}

if ($RestoreOnly) { Write-Section "Restore-only requested, exiting"; exit 0 }

Write-Section "Clean target"
if ($UseDotnet) { & dotnet clean "$solution" --configuration $Configuration } else { & $msbuildExe "$solution" /t:Clean /p:Configuration=$Configuration /verbosity:$Verbosity }
if ($LASTEXITCODE -ne 0) { throw "Clean failed with exit code $LASTEXITCODE" }

Write-Section "Build target"
if ($UseDotnet) {
    & dotnet build "$solution" --configuration $Configuration --no-incremental --verbosity $Verbosity
} else {
    & $msbuildExe "$solution" /t:Build /p:Configuration=$Configuration /m /verbosity:$Verbosity /p:UseSharedCompilation=false
}
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

Write-Section "Post-build verification"
$expectedExeCandidates = @(
    "CncControlApp\bin\$Configuration\$assemblyName.exe",
    "bin\$Configuration\$assemblyName.exe"
)
$found = $null
foreach ($c in $expectedExeCandidates) { if (Test-Path $c) { $found = $c; break } }
if ($found) {
    $info = Get-Item $found
    $hash = (Get-FileHash $found -Algorithm SHA256).Hash.Substring(0,16)
    Write-Host "OK: $found Size=$([Math]::Round($info.Length/1KB,2))KB SHA256(16)=$hash" -ForegroundColor Green
    exit 0
} else {
    Write-Host "FAILED: Expected exe not found. Tried: $($expectedExeCandidates -join ', ')" -ForegroundColor Red
    exit 1
}