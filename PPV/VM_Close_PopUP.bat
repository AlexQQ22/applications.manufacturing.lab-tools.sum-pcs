@echo off
setlocal enabledelayedexpansion
 
:: Set text color to red and display the question
color 0C
echo Is there someone working there?
echo Press Y/N to respond...
 
:: Use choice command to wait for Y/N input with 300 second timeout
choice /c YN /t 300 /d N /m "Waiting for response..."
 
:: Check the result
if errorlevel 2 goto timeout_or_no
if errorlevel 1 goto activity_detected
 
:timeout_or_no
:: This handles both timeout (default N) and explicit N choice
:: For our purposes, any timeout means no activity, so delete file
if exist "C:\Temp\user_has_activity.txt" (
    del "C:\Temp\user_has_activity.txt"
)
color 0E
echo.
echo No activity detected. File removed.
goto end
 
:activity_detected
:: Y was pressed - write YES to desktop file
echo YES > "C:\Temp\user_has_activity.txt"
color 0A
echo.
echo Activity detected! Written to desktop\user_activity.txt
goto end
 
:end
:: Reset color and pause
color 07
echo.
exit