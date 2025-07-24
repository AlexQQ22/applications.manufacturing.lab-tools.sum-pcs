@ECHO OFF
IF "%~1"=="" GOTO USAGE
ECHO Installing SUM on: %1
NET USE \\%1\c$ /USER:SysC tr@nsf3r

RMDIR \\%1\c$\SUMInstall /s /q

IF NOT EXIST \\%1\c$\SUMInstall  MKDIR \\%1\c$\SUMInstall
IF NOT EXIST \\%1\c$\SUMInstall\\MonitoringSUM MKDIR \\%1\c$\SUMInstall\\MonitoringSUM
IF NOT EXIST \\%1\c$\SUMInstall\\MonitoringSUM\\Release MKDIR \\%1\c$\SUMInstall\\MonitoringSUM\\Release

COPY /Y c:\SUMInstall\*.* \\%1\c$\SUMInstall\
COPY /Y c:\SUMInstall\MonitoringSUM\*.* \\%1\c$\SUMInstall\MonitoringSUM\
COPY /Y c:\SUMInstall\MonitoringSUM\Release\*.* \\%1\c$\SUMInstall\MonitoringSUM\Release\


ECHO Copied System Utilization Monitor to tester...

c:\SUMInstall\PsExec64.exe \\%1 -u SysC -p tr@nsf3r cmd /c c:\SUMInstall\Tester.bat

NET USE /DELETE \\%1\c$


ECHO Completed installation and configuration of System Utilization Monitor
GOTO :eof
:USAGE
ECHO           Run from a command prompt, usage:
ECHO           InstallSUM_toTester.bat "<IP Address>"
ECHO           Example:
ECHO           InstallSUM_toTester.bat 10.250.0.1
