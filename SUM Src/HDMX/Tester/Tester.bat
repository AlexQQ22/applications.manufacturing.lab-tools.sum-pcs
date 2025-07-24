c:
cd c:\SUMInstall


SCHTASKS.exe /delete /tn "StartSUMLogOut" /f
SCHTASKS.exe /delete /tn "StartSUMLogOn" /f
SCHTASKS.exe /delete /tn "MoniSUM" /f
SCHTASKS.exe /delete /tn "StartSUMLogging" /f
SCHTASKS.exe /delete /tn "VMCloseVNC" /f


SCHTASKS.exe /delete /tn "StartSUM" /f rem esta liena se puede eliminar una ves que ya este todo estable se puede eliminar 
SCHTASKS.exe /delete /tn "TaskKillPs" /f rem esta liena se puede eliminar una ves que ya este todo estable se puede eliminar 
rem ambas lineas lo que hacen es eliminar un task que ya no se usa, pero puede que una maquina alla queddo con esos tasks, para asegurarnos de que se van a elimnar se dejan un tiempo una vez que se verifique que no existan en ningun terster esos task se pueden eliminar estas lineas

ECHO Creating Monitoring Task...\r\n
Powershell.exe -executionpolicy Bypass -File  "C:\SUMInstall\MonitoringSUM\createMonitoringTask.ps1"




ECHO Configuring System Utilization Monitor...\r\n
taskkill /IM SystemUtilizationMonitor.exe /T /F
copy /Y c:\SUMInstall\appsettings.json "C:\Program Files\Intel\SystemUtilizationMonitor\"


ECHO Installing System Utilization Monitor...\r\n
SystemUtilization.Monitor.Installer.msi
powershell -command "Start-Sleep -Seconds 10"




ECHO Running the script CheckRunningAndLoggin.bat This script start the SystemUtilization thepending if the user is loggeg or not ...\r\n
call "c:\SUMInstall\checkRunningAndLoggin.bat"


ECHO Creating the TaskScheduler "StartSUMLogging" it execute each time that user do the log on in to the tester ...\r\n
Powershell.exe -executionpolicy Bypass -File c:\SUMInstall\CreateSumTask.ps1 

REM powershell Set-ExecutionPolicy Unrestricted -Scope CurrentUser--> code original
REM powershell c:\SUMInstall\CreateSumTask.ps1 --> code original




