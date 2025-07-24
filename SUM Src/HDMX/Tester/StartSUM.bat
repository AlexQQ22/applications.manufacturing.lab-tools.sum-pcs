


@ECHO OFF

echo "Checking if SystemUtilizationMonitor is running\n"

tasklist /nh /fi "IMAGENAME eq SystemUtilizationMonitor.exe" | find /i "SystemUtilizationMonitor" >nul
if %errorlevel% equ 0 (
    echo "SystemUtilizationMonitor" is currently running. \n"
) else (


	if exist "C:\Program Files\Intel\SystemUtilizationMonitor\SystemUtilizationMonitor.exe" (
			echo  SystemUtilizationMonitor was Not not Running, it will be execute
			c:
			cd "C:\Program Files\Intel\SystemUtilizationMonitor\"
			start SystemUtilizationMonitor.exe
			
		) else (
			echo SystemUtilizationMonitor.exe dont exist , so it will delete the Rev.txt to install againg the sum
			del "C:\SUMInstall\Rev.txt"
			
		)

)




echo "Checking if task Log monitor is running\n"
	
schtasks /query /tn "MoniSUM" >nul 2>&1
if %errorlevel% equ 0 (
	echo The task MoniSUM exists.
) else (
	echo "Task MoniSUM is not created, checking if create Moni script(createMonitoringTask) exist be reinstaled. \r\n
	if exist "C:\SUMInstall\createMonitoringTask.ps1" (
	    echo Crearing the Task Scheduler MoniSUM.\r\n
		Powershell.exe -executionpolicy Bypass -File c:\SUMInstall\createMonitoringTask.ps1
	) else (
		echo createMonitoringTask script dont exist , so it will delete the Rev.txt to install againg the sum
		del "C:\SUMInstall\Rev.txt"
		
	)
)





echo "Checking if  killPSexcec is running\n"
	
tasklist /nh /fi "IMAGENAME eq PSEXESVC.exe" | find /i "PSEXESVC" >nul
if %errorlevel% equ 0 (
    echo PSEXESVC is running. It will be closed to avoid issues with data collection of SUM.
    TASKKILL /F /IM PSEXESVC.exe
) else (
    echo PSEXESVC is NOT running.
)





EXIT /B 0




