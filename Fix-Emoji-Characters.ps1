# PowerShell Script to Fix Corrupted Emoji Characters in XAML Files
# This script replaces "?" placeholders with correct emoji characters

$ErrorActionPreference = "Stop"
$rootPath = "C:\Users\cagat\source\repos\RefikCodes\Raptorex-Controller-PC"

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Emoji Character Fix Tool" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

$fixedCount = 0
$errorCount = 0

# Define emoji replacements with their context patterns
$emojiReplacements = @(
    @{
        Pattern = 'Tag="?" Content="OPEN FILE"'
        Replacement = 'Tag="📁" Content="OPEN FILE"'
        Description = "File button icon"
    },
    @{
   Pattern = 'Tag="\?\?" Content="RUN"'
        Replacement = 'Tag="▶️" Content="RUN"'
    Description = "Run button icon"
    },
  @{
    Pattern = 'Tag="\?\?" Content="STOP / PAUSE"'
        Replacement = 'Tag="⏹️" Content="STOP / PAUSE"'
        Description = "Stop button icon"
    },
    @{
   Pattern = '<TextBlock Text="\?\?" FontSize="14" Margin="0,0,0,2" HorizontalAlignment="Center"/>\s*<TextBlock Text="G-CODE"'
        Replacement = '<TextBlock Text="📄" FontSize="14" Margin="0,0,0,2" HorizontalAlignment="Center"/><TextBlock Text="G-CODE"'
        Description = "G-Code tab icon"
    },
    @{
     Pattern = '<TextBlock Text="\?\?" FontSize="14" Margin="0,0,0,2" HorizontalAlignment="Center"/>\s*<TextBlock Text="MULTI-VIEW"'
        Replacement = '<TextBlock Text="🎯" FontSize="14" Margin="0,0,0,2" HorizontalAlignment="Center"/><TextBlock Text="MULTI-VIEW"'
        Description = "Multi-view tab icon"
    },
    @{
      Pattern = 'Content="\?" .*?ToolTip="Maximize top view"'
        Replacement = 'Content="⛶" ToolTip="Maximize top view"'
        Description = "Maximize button icon"
  },
    @{
     Pattern = '<TextBlock .*?Text="\?" FontSize="14" Foreground="#FF2196F3".*?Visibility="Collapsed"/>'
    Replacement = '<TextBlock Text="➤" FontSize="14" Foreground="#FF2196F3" VerticalAlignment="Center" Visibility="Collapsed"/>'
        Description = "Current line icon in GCode list"
    },
    @{
        Pattern = '<TextBlock .*?Text="\?" FontSize="14" Foreground="#FF4CAF50".*?Visibility="Collapsed"/>'
        Replacement = '<TextBlock Text="✔" FontSize="14" Foreground="#FF4CAF50" VerticalAlignment="Center" Visibility="Collapsed"/>'
        Description = "Executed line icon in GCode list"
    }
)

# Get all XAML files
Write-Host "Searching for XAML files..." -ForegroundColor Yellow
$xamlFiles = Get-ChildItem -Path $rootPath -Filter "*.xaml" -Recurse -ErrorAction SilentlyContinue | 
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' }

Write-Host "Found $($xamlFiles.Count) XAML files to check`n" -ForegroundColor Gray

foreach ($file in $xamlFiles) {
    try {
        # Read file content
        $content = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
   $originalContent = $content
        $fileModified = $false

        # Apply each replacement
        foreach ($replacement in $emojiReplacements) {
            if ($content -match $replacement.Pattern) {
            $content = $content -replace $replacement.Pattern, $replacement.Replacement
                Write-Host "  ✓ Fixed: $($replacement.Description) in $($file.Name)" -ForegroundColor Green
                $fileModified = $true
       }
     }

# Save if modified
        if ($fileModified) {
      $utf8BOM = New-Object System.Text.UTF8Encoding $true
 [System.IO.File]::WriteAllText($file.FullName, $content, $utf8BOM)
         $fixedCount++
       Write-Host "  Saved: $($file.Name)" -ForegroundColor Cyan
        }
    }
    catch {
        $errorCount++
     Write-Host "  ✗ Error processing $($file.Name): $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Files modified: $fixedCount" -ForegroundColor Green
Write-Host "  Errors: $errorCount" -ForegroundColor Red
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Done! Press any key to exit..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
