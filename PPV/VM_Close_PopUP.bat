@echo off
color 0C
echo.
echo Is somebody working there?
echo If yes please press any key within 5 seconds...
echo.

REM Create temp directory if it doesn't exist
if not exist "C:\Temp\" mkdir "C:\Temp\"

REM Wait for user input with 5 second timeout
timeout /t 5 /nobreak >nul
if %errorlevel% equ 0 (
    echo YES > "C:\Temp\user_has_activity.txt"
    echo User activity detected and recorded.
) else (
    echo No response detected within 5 seconds.
    echo Continuing with cleanup...
)

REM Clear the userconected.txt file
echo. > "C:\SUMInstall\userconected.txt"

echo File userconected.txt cleaned successfully.
exit