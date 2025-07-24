System Utilization Monitor -SUM- Installer Scripts:
InstallSUM_toTester.bat   	--> Installs and configures SUM to tester from DPC/TC to a single tester, needs tester IP to run.
				--> Runs Tester.bat which is module type specific
		          	Run from a command prompt, usage:
				InstallSUM_toTester.bat <IP Address>
				Example:
				InstallSUM_toTester.bat 10.250.0.1
Tester.bat			--> Installs and configures SUM when called by InstallSUM_toTester.bat
 