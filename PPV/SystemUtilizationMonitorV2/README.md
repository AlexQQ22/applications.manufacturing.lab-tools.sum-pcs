dotnet publish -c Release -r win-x64 --self-contained true -o .\bin\Release\net10.0\win-x64\publish
powershell -ExecutionPolicy Bypass -File Generate-WiXInstaller.ps1

    // VALIDATE: Read delete files rate 20 days example
    // VALIDATE: In the fusion json add shouldReadLogFiles = false or true to activate the whole txt logs thighy
    // VALIDATE: Shows as SystemUtilizationMonitor.exe en task manager
    // VALIDATE: Json generated from 00 day a to 00 day b, view CR03THST4711:22 C:\Users\SysC\AppData\Local\Intel\SystemUtilizationMonitor

-- Ready
// TODO: Dont show console when executing
// TODO: add json read in %LOCALAPPDATA%/Intel/SystemUtilizationMonitor/SystemUtilizationTimeFrames.json
// TODO: When generating json follow the format of the samples
// TODO: The json tells which txt files to read, if one changes then doesnt read the rest, reports to json files changed only 1 per new line
// TODO: Read path to drop json
// TODO: Add to the json the keyboard and mouse constants
