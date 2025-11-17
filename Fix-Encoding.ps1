# PowerShell Script to Fix Encoding Issues in C# Files
# This script converts all .cs files to UTF-8 with BOM

$ErrorActionPreference = "Stop"
$rootPath = "C:\Users\cagat\source\repos\RefikCodes\Raptorex-Controller-PC"

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Encoding Fix Tool for C# Files" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Get all .cs files recursively
$csFiles = Get-ChildItem -Path $rootPath -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue

Write-Host "Found $($csFiles.Count) C# files" -ForegroundColor Yellow
Write-Host ""

$fixedCount = 0
$errorCount = 0

foreach ($file in $csFiles) {
    try {
   Write-Host "Processing: $($file.FullName)" -ForegroundColor Gray
        
   # Read file with automatic encoding detection
        $content = Get-Content -Path $file.FullName -Raw -Encoding Default
        
        # Save with UTF-8 BOM
    $utf8BOM = New-Object System.Text.UTF8Encoding $true
        [System.IO.File]::WriteAllText($file.FullName, $content, $utf8BOM)
 
  $fixedCount++
  Write-Host "  ✓ Fixed" -ForegroundColor Green
    }
    catch {
 $errorCount++
        Write-Host "  ✗ Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Total files: $($csFiles.Count)" -ForegroundColor White
Write-Host "  Fixed: $fixedCount" -ForegroundColor Green
Write-Host "  Errors: $errorCount" -ForegroundColor Red
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Done! Press any key to exit..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
