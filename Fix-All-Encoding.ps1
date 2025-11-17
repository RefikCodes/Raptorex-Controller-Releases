# PowerShell Script to Fix ALL Encoding Issues (CS, XAML, RESX, etc.)
# This script converts all text files to UTF-8 with BOM

$ErrorActionPreference = "Stop"
$rootPath = "C:\Users\cagat\source\repos\RefikCodes\Raptorex-Controller-PC"

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Complete Encoding Fix Tool" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# File extensions to fix
$extensions = @("*.cs", "*.xaml", "*.resx", "*.config", "*.txt")
$fixedCount = 0
$errorCount = 0

foreach ($ext in $extensions) {
    Write-Host "`nProcessing $ext files..." -ForegroundColor Yellow
    
    # Get all files with this extension (exclude obj and bin folders)
    $files = Get-ChildItem -Path $rootPath -Filter $ext -Recurse -ErrorAction SilentlyContinue | 
         Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' }

    Write-Host "Found $($files.Count) $ext files" -ForegroundColor Gray

 foreach ($file in $files) {
        try {
 # Read file with automatic encoding detection
            $content = Get-Content -Path $file.FullName -Raw -Encoding Default
      
   # Save with UTF-8 BOM
            $utf8BOM = New-Object System.Text.UTF8Encoding $true
  [System.IO.File]::WriteAllText($file.FullName, $content, $utf8BOM)
     
            $fixedCount++
        Write-Host "  ✓ Fixed: $($file.Name)" -ForegroundColor Green
 }
        catch {
   $errorCount++
            Write-Host "  ✗ Error: $($file.Name) - $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Total files fixed: $fixedCount" -ForegroundColor Green
Write-Host "  Errors: $errorCount" -ForegroundColor Red
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Now clean and rebuild
Write-Host "Cleaning old binaries..." -ForegroundColor Yellow
Get-ChildItem -Path $rootPath -Include "obj","bin" -Recurse -Directory -ErrorAction SilentlyContinue | 
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "✓ Cleaned obj and bin folders" -ForegroundColor Green
Write-Host ""
Write-Host "Now rebuilding project..." -ForegroundColor Yellow

# Rebuild project
$projectFile = Join-Path $rootPath "CncControlApp.csproj"
if (Test-Path $projectFile) {
 try {
        & msbuild $projectFile /t:Rebuild /p:Configuration=Debug /v:minimal
  Write-Host "✓ Project rebuilt successfully" -ForegroundColor Green
    }
    catch {
 Write-Host "✗ Build failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done! Press any key to exit..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
