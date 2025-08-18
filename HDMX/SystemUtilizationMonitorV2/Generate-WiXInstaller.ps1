# Clean WiX Generator - No Syntax Errors
param(
    [string]$SourcePath = ".\publish",
    [string]$OutputPath = ".\Output"
)

Write-Host "Building System Utilization Monitor MSI..." -ForegroundColor Green

# Check WiX installation
$WixPath = "${env:ProgramFiles}\WiX Toolset v6.0\bin"
if (-not (Test-Path "$WixPath\wix.exe")) {
    Write-Host "WiX not found at: $WixPath" -ForegroundColor Red
    exit 1
}

# Check source directory
if (-not (Test-Path $SourcePath)) {
    Write-Host "Source directory not found: $SourcePath" -ForegroundColor Red
    exit 1
}

# Get files
$files = Get-ChildItem -Path $SourcePath -File
Write-Host "Found $($files.Count) files" -ForegroundColor Cyan

# Find main executable
$mainExe = $files | Where-Object { $_.Name -eq "SystemUtilizationMonitorV2.exe" }
if (-not $mainExe) {
    Write-Host "Main executable not found!" -ForegroundColor Red
    exit 1
}

# Create output directory
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Build WiX content
$wxsContent = '<?xml version="1.0" encoding="UTF-8"?>'
$wxsContent += "`n" + '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
$wxsContent += "`n" + '  <Package Name="System Utilization Monitor"'
$wxsContent += "`n" + '           Language="1033"'
$wxsContent += "`n" + '           Version="1.0.0.0"'
$wxsContent += "`n" + '           Manufacturer="Intel Corporation"'
$wxsContent += "`n" + '           UpgradeCode="12345678-1234-1234-1234-123456789012">'
$wxsContent += "`n" + '    <MajorUpgrade DowngradeErrorMessage="A newer version of [ProductName] is already installed." />'
$wxsContent += "`n" + '    <MediaTemplate EmbedCab="yes" />'
$wxsContent += "`n" + '    <StandardDirectory Id="ProgramFilesFolder">'
$wxsContent += "`n" + '      <Directory Id="IntelFolder" Name="Intel">'
$wxsContent += "`n" + '        <Directory Id="INSTALLFOLDER" Name="SystemUtilizationMonitor" />'
$wxsContent += "`n" + '      </Directory>'
$wxsContent += "`n" + '    </StandardDirectory>'
$wxsContent += "`n" + '    <StandardDirectory Id="ProgramMenuFolder">'
$wxsContent += "`n" + '      <Directory Id="ApplicationProgramsFolder" Name="System Utilization Monitor"/>'
$wxsContent += "`n" + '    </StandardDirectory>'
$wxsContent += "`n" + '    <DirectoryRef Id="INSTALLFOLDER">'

$componentRefs = @()
$componentIndex = 1

# Add main executable
$guid1 = [System.Guid]::NewGuid().ToString().ToUpper()
$wxsContent += "`n" + "      <Component Id=`"Component_$componentIndex`" Guid=`"$guid1`">"
$wxsContent += "`n" + "        <File Id=`"MainExe`" Source=`"$SourcePath\$($mainExe.Name)`" />"
$wxsContent += "`n" + "      </Component>"
$componentRefs += "Component_$componentIndex"
$componentIndex++

# Add other files
$otherFiles = $files | Where-Object { $_.Name -ne $mainExe.Name }
foreach ($file in $otherFiles) {
    $guid = [System.Guid]::NewGuid().ToString().ToUpper()
    $wxsContent += "`n" + "      <Component Id=`"Component_$componentIndex`" Guid=`"$guid`">"
    $wxsContent += "`n" + "        <File Id=`"File_$componentIndex`" Source=`"$SourcePath\$($file.Name)`" />"
    $wxsContent += "`n" + "      </Component>"
    $componentRefs += "Component_$componentIndex"
    $componentIndex++
}

# Close directory and add shortcuts
$wxsContent += "`n" + '    </DirectoryRef>'
$wxsContent += "`n" + '    <DirectoryRef Id="ApplicationProgramsFolder">'
$wxsContent += "`n" + '      <Component Id="ApplicationShortcut" Guid="CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC">'
$wxsContent += "`n" + '        <Shortcut Id="ApplicationStartMenuShortcut"'
$wxsContent += "`n" + '                  Name="System Utilization Monitor"'
$wxsContent += "`n" + '                  Description="Monitor system utilization and performance"'
$wxsContent += "`n" + '                  Target="[#MainExe]"'
$wxsContent += "`n" + '                  WorkingDirectory="INSTALLFOLDER"/>'
$wxsContent += "`n" + '        <RemoveFolder Id="ApplicationProgramsFolder" On="uninstall"/>'
$wxsContent += "`n" + '        <RegistryValue Root="HKCU"'
$wxsContent += "`n" + '                       Key="Software\Intel\SystemUtilizationMonitor"'
$wxsContent += "`n" + '                       Name="installed"'
$wxsContent += "`n" + '                       Type="integer"'
$wxsContent += "`n" + '                       Value="1" />'
$wxsContent += "`n" + '      </Component>'
$wxsContent += "`n" + '    </DirectoryRef>'
$wxsContent += "`n" + '    <Feature Id="ProductFeature" Title="System Utilization Monitor" Level="1">'

# Add component references
foreach ($componentRef in $componentRefs) {
    $wxsContent += "`n" + "      <ComponentRef Id=`"$componentRef`" />"
}

$wxsContent += "`n" + '      <ComponentRef Id="ApplicationShortcut" />'
$wxsContent += "`n" + '    </Feature>'
$wxsContent += "`n" + '  </Package>'
$wxsContent += "`n" + '</Wix>'

# Write file
$wxsFile = "Clean.SystemUtilization.Monitor.wxs"
$wxsContent | Out-File -FilePath $wxsFile -Encoding UTF8
Write-Host "Created WiX file: $wxsFile" -ForegroundColor Green
Write-Host "Components: $($componentRefs.Count)" -ForegroundColor Cyan

# Build MSI
try {
    Write-Host "Building MSI..." -ForegroundColor Yellow
    & "$WixPath\wix.exe" build -out "$OutputPath\SystemUtilization.Monitor.msi" $wxsFile
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "SUCCESS!" -ForegroundColor Green
        $msiFile = Get-Item "$OutputPath\SystemUtilization.Monitor.msi"
        Write-Host "Location: $($msiFile.FullName)" -ForegroundColor Cyan
        Write-Host "Size: $([math]::Round($msiFile.Length / 1MB, 2)) MB" -ForegroundColor Cyan
    } else {
        Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
    }
} catch {
    Write-Host "Build error: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    if (Test-Path $wxsFile) {
        Remove-Item $wxsFile -Force
    }
}

Write-Host "Script completed." -ForegroundColor Green