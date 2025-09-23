@echo off
REM VM_Close_PopUP.bat - Improved version for remote execution
REM Works both interactively and when run via PsExec

setlocal enabledelayedexpansion

REM Create SUMInstall directory if it doesn't exist
if not exist "C:\SUMInstall" mkdir "C:\SUMInstall"

REM Check if running interactively or remotely
set "INTERACTIVE=true"
if "%SESSIONNAME%"=="" set "INTERACTIVE=false"
if "%USERDOMAIN%"=="NT AUTHORITY" set "INTERACTIVE=false"

if "!INTERACTIVE!"=="false" (
    REM Running non-interactively (via PsExec)
    echo Running in remote mode - creating notification
    
    REM Show notification to logged-in users if any
    for /f "tokens=2" %%i in ('query session ^| findstr "Active"') do (
        msg %%i "VM CONNECTION WILL CLOSE IN 5 MINUTES - If you are working, run this command: echo user is here > C:\SUMInstall\userconected.txt" /time:30
    )
    
    REM Create a background PowerShell task to handle the timeout
    powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -Command "& { Start-Sleep 300; if (!(Test-Path 'C:\SUMInstall\userconected.txt') -or (Get-Content 'C:\SUMInstall\userconected.txt' -Raw -ErrorAction SilentlyContinue).Trim() -eq '') { '' | Out-File 'C:\SUMInstall\userconected.txt' -Encoding ASCII } }" &
    
    echo VM close notification sent to users
    exit /b 0
    
) else (
    REM Running interactively - show full interface
    color 0C
    echo.
    echo ============================================
    echo    VM CONNECTION WILL CLOSE IN 5 MINUTES
    echo ============================================
    echo.
    echo Are you currently working on this VM?
    echo.
    echo If YES - Press ANY KEY to indicate you are here
    echo If NO  - Just wait and the VM will disconnect automatically
    echo.
    echo This message will timeout in 300 seconds...
    echo.
    
    REM Wait for user input with timeout
    timeout /t 300 /nobreak >nul 2>&1
    
    REM Check if user pressed a key (timeout returns errorlevel 1 if key was pressed)
    if errorlevel 1 (
        REM User pressed a key - indicate they are present
        echo user is here > "C:\SUMInstall\userconected.txt"
        echo.
        echo Thank you! Your presence has been recorded.
        echo The VM disconnection has been postponed.
        echo.
        pause
    ) else (
        REM No user input - clear the file to indicate no user present
        echo. > "C:\SUMInstall\userconected.txt"
        echo.
        echo No response detected. VM will disconnect as scheduled.
        echo.
    )
)

exit /b 0