@echo off
echo Building System Utilization Monitor with all dependencies...

REM Clean previous build
if exist .\publish rmdir /s /q .\publish
if exist .\Output rmdir /s /q .\Output

echo Step 1: Publishing application with all runtime dependencies...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\publish

if %ERRORLEVEL% neq 0 (
    echo ERROR: Publish failed!
    pause
    exit /b 1
)

echo Step 2: Checking if main executable exists...
if not exist .\publish\SystemUtilizationMonitorV2.exe (
    echo ERROR: Main executable not found!
    pause
    exit /b 1
)

echo Step 3: Checking for System.Runtime.dll...
if not exist .\publish\System.Runtime.dll (
    echo WARNING: System.Runtime.dll not found! Trying alternative publish...
    rmdir /s /q .\publish
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o .\publish
    
    if not exist .\publish\System.Runtime.dll (
        echo ERROR: Still missing System.Runtime.dll after alternative publish!
        echo Please check your .NET installation or project configuration.
        pause
        exit /b 1
    )
)

echo Step 4: Listing file count...
dir .\publish\*.* | find /c "File(s)"

echo Step 5: Creating MSI installer...
powershell -ExecutionPolicy Bypass -File Fixed-WiX-Installer-Generator.ps1

if %ERRORLEVEL% neq 0 (
    echo ERROR: MSI creation failed!
    pause
    exit /b 1
)

echo.
echo SUCCESS! Build completed.
echo MSI file should be in .\Output\
pause