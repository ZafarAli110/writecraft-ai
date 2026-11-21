<#
.SYNOPSIS
Build and package WriteCraft.AI for distribution

.DESCRIPTION
Creates self-contained Windows executable and ZIP package ready for distribution.
Outputs to 'release/' folder with installer files and portable ZIP.

.EXAMPLE
.\build-release.ps1
.\build-release.ps1 -Version "1.0.0" -SkipZip
#>

param(
    [string]$Version = "1.0.0",
    [switch]$SkipZip,
    [switch]$CleanFirst
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WriteCraft.AI Build & Package Script" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Paths
$ProjectRoot = $PSScriptRoot
$PublishDir = Join-Path $ProjectRoot "publish\win-x64"
$ReleaseDir = Join-Path $ProjectRoot "release"
$ZipPath = Join-Path $ReleaseDir "WriteCraftAI-v$Version-Windows-x64.zip"

# Clean old builds
if ($CleanFirst -and (Test-Path $PublishDir)) {
    Write-Host "🧹 Cleaning old publish directory..." -ForegroundColor Yellow
    Remove-Item $PublishDir -Recurse -Force
}

if (-not (Test-Path $ReleaseDir)) {
    New-Item -ItemType Directory -Path $ReleaseDir | Out-Null
}

# Step 1: Publish self-contained executable
Write-Host "📦 Publishing self-contained executable..." -ForegroundColor Green
dotnet publish -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:Version=$Version `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Build failed!"
    exit 1
}

Write-Host "✅ Build completed successfully!" -ForegroundColor Green
Write-Host ""

# Step 2: Copy essential files
Write-Host "📋 Copying configuration and assets..." -ForegroundColor Green

# Ensure config files are in publish folder
$ConfigFiles = @(
    "config.json",
    "urdu-vocabulary.json"
)

foreach ($file in $ConfigFiles) {
    $source = Join-Path $ProjectRoot $file
    $dest = Join-Path $PublishDir $file
    if (Test-Path $source) {
        Copy-Item $source $dest -Force
        Write-Host "  ✓ $file" -ForegroundColor Gray
    }
}

# Create AIModels folder with README
$AIModelsDir = Join-Path $PublishDir "AIModels"
if (-not (Test-Path $AIModelsDir)) {
    New-Item -ItemType Directory -Path $AIModelsDir | Out-Null
}

$ModelsReadme = @"
# AI Models Directory

Place your GGUF model files here.

## Recommended Models:

1. **Qwen2.5-7B-Instruct-Q4_K_M.gguf** (4.6 GB)
   Download: https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF/resolve/main/qwen2.5-7b-instruct-q4_k_m.gguf

2. **Qwen2.5-14B-Instruct-Q4_K_M.gguf** (8.5 GB)
   Download: https://huggingface.co/Qwen/Qwen2.5-14B-Instruct-GGUF/resolve/main/qwen2.5-14b-instruct-q4_k_m.gguf

After downloading, place the .gguf file in this folder and update config.json
to point to the correct model file.
"@

Set-Content -Path (Join-Path $AIModelsDir "README.txt") -Value $ModelsReadme

Write-Host "  ✓ AIModels/README.txt" -ForegroundColor Gray
Write-Host ""

# Step 3: Create README for distribution
Write-Host "📝 Creating distribution README..." -ForegroundColor Green

$DistReadme = @"
# WriteCraft.AI - Private & Local AI Writing Studio

Version: $Version
Built: $(Get-Date -Format "yyyy-MM-dd")

## Quick Start

1. **Download AI Model** (first time only)
   - Go to: https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF
   - Download: qwen2.5-7b-instruct-q4_k_m.gguf (4.6 GB)
   - Place in: AIModels\ folder

2. **Run the Application**
   - Double-click: WriteCraftAI.exe
   - Browser will open automatically to: http://localhost:5555
   - If browser doesn't open, navigate to: http://localhost:5555

3. **Install as PWA** (Optional)
   - Click the install icon in browser address bar
   - App will be available offline

## Running as a Background Service (Optional)

To run WriteCraft.AI automatically at startup:

**Option A: Run at Login (Simple)**
- Right-click WriteCraftAI.exe → Create Shortcut
- Move shortcut to: %APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup

**Option B: Windows Service (Advanced, requires Admin)**
```powershell
# Run PowerShell as Administrator
cd "C:\path\to\WriteCraftAI"
.\scripts\install-service.ps1 -Install -ExePath "$PWD\WriteCraftAI.exe"
```

To uninstall service:
```powershell
.\scripts\install-service.ps1 -Uninstall
```

## Configuration

Edit `config.json` to customize:
- Model path and settings
- Port number (default: 5555)
- GPU acceleration options

## Features

✍️ **Grammar Checker** - Fix grammar and improve writing
🌐 **Roman Urdu Translation** - Translate Roman Urdu to English
🧹 **Clean Text** - Remove formatting and clean up text
✉️ **Email Writer** - Draft professional emails
📝 **Writing Assistant** - Get help with content creation

## System Requirements

- Windows 10/11 (x64)
- 8GB RAM minimum (16GB recommended)
- 10GB free disk space (for model files)
- GPU optional (CPU mode works, but slower)

## Privacy

✅ All processing happens locally on your machine
✅ No data sent to external servers
✅ No internet required (after initial model download)
✅ Your writing stays private

## Troubleshooting

**App won't start?**
- Check if port 5555 is already in use
- Make sure you have downloaded the AI model

**Slow performance?**
- Use GPU acceleration (edit config.json: "AutoDetectBackend": true)
- Try smaller model (7B instead of 14B)
- Close other applications

**PWA not working offline?**
- Make sure WriteCraftAI.exe is running
- The UI caches locally, but needs the server for AI features

## Support & Updates

- GitHub: [Your Repo URL]
- Report Issues: [Your Issues URL]

---
Built with ❤️ using ASP.NET Core & LLamaSharp
"@

Set-Content -Path (Join-Path $PublishDir "README.txt") -Value $DistReadme
Write-Host "  ✓ README.txt" -ForegroundColor Gray
Write-Host ""

# Step 4: Create portable ZIP
if (-not $SkipZip) {
    Write-Host "🗜️  Creating portable ZIP package..." -ForegroundColor Green
    
    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }
    
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal
    
    $ZipSize = (Get-Item $ZipPath).Length / 1MB
    Write-Host "✅ ZIP created: $ZipPath" -ForegroundColor Green
    Write-Host "   Size: $([math]::Round($ZipSize, 2)) MB" -ForegroundColor Gray
    Write-Host ""
}

# Step 5: Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  📦 Build Complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Distribution files:" -ForegroundColor White
Write-Host "  📁 Executable: $PublishDir\WriteCraftAI.exe" -ForegroundColor Gray

$ExeSize = (Get-Item "$PublishDir\WriteCraftAI.exe").Length / 1MB
Write-Host "     Size: $([math]::Round($ExeSize, 2)) MB" -ForegroundColor Gray

if (-not $SkipZip) {
    Write-Host "  📦 ZIP Package: $ZipPath" -ForegroundColor Gray
    Write-Host "     Size: $([math]::Round($ZipSize, 2)) MB" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Download AI model to AIModels/ folder" -ForegroundColor White
Write-Host "  2. Test the executable: .\publish\win-x64\WriteCraftAI.exe" -ForegroundColor White
Write-Host "  3. Distribute the ZIP file or publish/ folder" -ForegroundColor White
Write-Host ""
Write-Host "Optional: Create installer with Inno Setup" -ForegroundColor Yellow
Write-Host "  Download Inno Setup: https://jrsoftware.org/isdl.php" -ForegroundColor Gray
Write-Host ""
