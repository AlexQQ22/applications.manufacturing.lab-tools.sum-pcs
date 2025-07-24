@ECHO OFF
powershell Set-ExecutionPolicy Unrestricted -Scope CurrentUser
powershell -ExecutionPolicy Bypass -File \\amr\ec\proj\mdl\cr\intel\hdmx_db\mae\Releases\SystemUtilization\CreateTaskSUM.ps1
REM Run from \\amr\ec\proj\mdl\cr\intel\hdmx_db\mae\Releases\SystemUtilization\CreateTaskSUM.bat