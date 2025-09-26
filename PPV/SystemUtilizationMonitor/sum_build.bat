@echo off
echo Starting SystemUtilizationMonitor build process...

echo.
echo Step 1: Building and publishing .NET application...
dotnet publish -c Release -r win-x64 --self-contained true -o .\bin\Release\net10.0\win-x64\publish
if %errorlevel% neq 0 (
    echo Error: dotnet publish failed
    pause
    exit /b %errorlevel%
)

echo.
echo Step 2: Copying files to publish directory...
xcopy ".\bin\Release\net10.0\win-x64\publish\*" ".\publish\" /E /I /Y
if %errorlevel% neq 0 (
    echo Error: File copy failed
    pause
    exit /b %errorlevel%
)

echo.
echo Step 3: Generating WiX installer...
powershell -ExecutionPolicy Bypass -File Generate-WiXInstaller.ps1
if %errorlevel% neq 0 (
    echo Error: WiX installer generation failed
    pause
    exit /b %errorlevel%
)

echo.
echo Build process completed successfully!
pause