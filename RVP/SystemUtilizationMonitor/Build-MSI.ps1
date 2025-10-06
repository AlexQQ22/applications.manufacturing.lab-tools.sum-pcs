# PowerShell script to build MSI using WiX v6
param(
    [string]$SourcePath = ".\bin\Release\net8.0\win-x64\publish",
    [string]$OutputPath = ".\Output"
)

Write-Host "Building System Utilization Monitor MSI using WiX v6..." -ForegroundColor Green

# Check if WiX v6 is installed
$WixPath = "${env:ProgramFiles}\WiX Toolset v6.0\bin"

if (-not (Test-Path "$WixPath\wix.exe")) {
    Write-Host "WiX Toolset v6.0 not found at: $WixPath" -ForegroundColor Red
    Write-Host "Please install WiX v6 from https://wixtoolset.org/" -ForegroundColor Yellow
    exit 1
}

Write-Host "Found WiX v6 at: $WixPath" -ForegroundColor Green

# Check if source directory exists
if (-not (Test-Path $SourcePath)) {
    Write-Host "Source directory not found: $SourcePath" -ForegroundColor Red
    Write-Host "Please build and publish your application first:" -ForegroundColor Yellow
    Write-Host "  dotnet publish -c Release -r win-x64 --self-contained true -o `"$SourcePath`"" -ForegroundColor Cyan
    exit 1
}

# Create output directory
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force
}

# Build MSI using WiX v6
try {
    Write-Host "Building MSI with WiX v6..." -ForegroundColor Yellow
    
    $wixArgs = @(
        "build",
        "-define", "SourceDir=$SourcePath",
        "-out", "$OutputPath\SystemUtilization.Monitor.Installer.msi",
        "SystemUtilization.Monitor.Installer.wxs"
    )
    
    & "$WixPath\wix.exe" @wixArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "WiX v6 build failed with exit code $LASTEXITCODE"
    }
    
    Write-Host "Success! MSI created at: $OutputPath\SystemUtilization.Monitor.Installer.msi" -ForegroundColor Green
    
    # Show file info
    $msiFile = Get-Item "$OutputPath\SystemUtilization.Monitor.Installer.msi"
    Write-Host "File size: $([math]::Round($msiFile.Length / 1MB, 2)) MB" -ForegroundColor Cyan
    Write-Host "Created: $($msiFile.CreationTime)" -ForegroundColor Cyan
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}