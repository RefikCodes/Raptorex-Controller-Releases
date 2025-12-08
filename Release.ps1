# Raptorex Controller - Release Script
# Bu script versiyon günceller, build eder, installer oluşturur ve GitHub'a release yükler

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = "Stop"

# Paths
$RootPath = $PSScriptRoot
$AssemblyInfoPath = Join-Path $RootPath "Properties\AssemblyInfo.cs"
$InnoSetupPath = Join-Path $RootPath "Installer\RaptorexSetup.iss"
$ProjectPath = Join-Path $RootPath "CncControlApp.csproj"
$MSBuildPath = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
$InnoSetupCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$PublicRepo = "RefikCodes/Raptorex-Controller-Releases"

# Version format: "4.0" -> "4.0.0.0" for assembly
$AssemblyVersion = $Version
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    $parts = $Version -split '\.'
    while ($parts.Count -lt 4) { $parts += "0" }
    $AssemblyVersion = $parts -join '.'
}

# Short version for Inno Setup (e.g., "4.0")
$ShortVersion = ($Version -split '\.')[0..1] -join '.'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Raptorex Controller Release Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Assembly Version: $AssemblyVersion" -ForegroundColor Yellow
Write-Host "Short Version: $ShortVersion" -ForegroundColor Yellow
Write-Host ""

# Step 1: Update AssemblyInfo.cs
Write-Host "[1/7] Updating AssemblyInfo.cs..." -ForegroundColor Green
$assemblyContent = Get-Content $AssemblyInfoPath -Raw
$assemblyContent = $assemblyContent -replace 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$AssemblyVersion`")"
$assemblyContent = $assemblyContent -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$AssemblyVersion`")"
Set-Content $AssemblyInfoPath $assemblyContent -NoNewline
Write-Host "   AssemblyInfo.cs updated." -ForegroundColor Gray

# Step 2: Update Inno Setup script
Write-Host "[2/7] Updating RaptorexSetup.iss..." -ForegroundColor Green
$innoContent = Get-Content $InnoSetupPath -Raw
$innoContent = $innoContent -replace '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$ShortVersion`""
Set-Content $InnoSetupPath $innoContent -NoNewline
Write-Host "   RaptorexSetup.iss updated." -ForegroundColor Gray

# Step 3: Build Release
Write-Host "[3/7] Building Release..." -ForegroundColor Green
& $MSBuildPath $ProjectPath /p:Configuration=Release /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED!" -ForegroundColor Red
    exit 1
}
Write-Host "   Build successful." -ForegroundColor Gray

# Step 4: Create Installer
Write-Host "[4/7] Creating Installer..." -ForegroundColor Green
& $InnoSetupCompiler $InnoSetupPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "INSTALLER CREATION FAILED!" -ForegroundColor Red
    exit 1
}
$InstallerPath = Join-Path $RootPath "bin\Installer\RaptorexController_Setup_$ShortVersion.exe"
Write-Host "   Installer created: $InstallerPath" -ForegroundColor Gray

# Step 5: Git commit and push
Write-Host "[5/7] Committing changes..." -ForegroundColor Green
git add -A
git commit -m "Release v$Version"
git push origin main
Write-Host "   Changes pushed to main." -ForegroundColor Gray

# Step 6: Create and push tag
Write-Host "[6/7] Creating tag v$Version..." -ForegroundColor Green
git tag -a "v$Version" -m "Version $Version"
git push origin "v$Version"
Write-Host "   Tag pushed." -ForegroundColor Gray

# Step 7: Create GitHub Release on public repo
Write-Host "[7/7] Creating GitHub Release..." -ForegroundColor Green

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = @"
## Version $Version

### Download:
Download and run RaptorexController_Setup_$ShortVersion.exe
"@
}

gh release create "v$Version" $InstallerPath --repo $PublicRepo --title "Raptorex Controller v$Version" --notes $ReleaseNotes

if ($LASTEXITCODE -ne 0) {
    Write-Host "GITHUB RELEASE FAILED!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RELEASE COMPLETE!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: v$Version" -ForegroundColor Yellow
Write-Host "Public Release: https://github.com/$PublicRepo/releases/tag/v$Version" -ForegroundColor Yellow
Write-Host ""
