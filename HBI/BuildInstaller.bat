@echo off
echo Building System Utilization Monitor Installer using WiX v6...

REM Set WiX v6 path
set WIX_PATH="C:\Program Files\WiX Toolset v6.0\bin"
set SOURCE_DIR=".\bin\Release\net10.0\win-x64\publish"
set OUTPUT_DIR=".\Output"

REM Check if WiX v6 exists
if not exist %WIX_PATH%\wix.exe (
    echo Error: WiX Toolset v6.0 not found at %WIX_PATH%
    echo Please install WiX v6 from https://wixtoolset.org/
    pause
    exit /b 1
)

echo Found WiX v6 at: %WIX_PATH%

REM Create output directory
if not exist %OUTPUT_DIR% mkdir %OUTPUT_DIR%

REM Check if source directory exists
if not exist %SOURCE_DIR% (
    echo Error: Source directory not found: %SOURCE_DIR%
    echo Please build and publish your application first:
    echo   dotnet publish -c Release -r win-x64 --self-contained true -o %SOURCE_DIR%
    pause
    exit /b 1
)

REM Build MSI using WiX v6
echo Building MSI with WiX v6...
%WIX_PATH%\wix.exe build -define SourceDir=%SOURCE_DIR% -out %OUTPUT_DIR%\SystemUtilization.Monitor.Installer.msi SystemUtilization.Monitor.Installer.wxs

if %ERRORLEVEL% neq 0 (
    echo Error: WiX v6 build failed
    pause
    exit /b 1
)

echo Success! MSI created at %OUTPUT_DIR%\SystemUtilization.Monitor.Installer.msi
pause