@echo off
color 0C
echo.
echo Is somebody working there?
echo If yes please press Y within 300 seconds...
echo.

REM Create temp directory if it doesn't exist
if not exist "C:\Temp\" mkdir "C:\Temp\"

REM Wait for user input with timeout using choice
choice /c YN /t 300 /d N /m "Press Y to confirm activity"

if %errorlevel% equ 1 (
    echo YES > "C:\Temp\user_has_activity.txt"
    echo User activity detected and recorded.
    echo Exiting...
    exit
) else (
    echo No response detected within 300 seconds.
    echo Continuing with cleanup...
)

REM Clear the userconected.txt file
echo. > "C:\SUMInstall\userconected.txt"
echo File userconected.txt cleaned successfully.
exit