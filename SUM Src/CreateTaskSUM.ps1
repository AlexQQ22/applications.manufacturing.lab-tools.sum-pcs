$time = [DateTime]::Now.AddMinutes(2)
$hourMinute=$time.ToString("HH:mm")
$Username=$Env:UserName

# SCHTASKS /Create /RU AMR\$Username /SC MINUTE /MO 35  /TN PullSUMLogs /TR \\amr\ec\proj\mdl\cr\intel\hdmx_db\mae\Releases\SystemUtilization\RunPullSUMLogs.bat  /ST $hourMinute /F
SCHTASKS /Create /RU AMR\$Username /SC MINUTE /MO 35  /TN PullSUMLogs /TR "wscript.exe c:\SUMInstall\PullSUMLogs.vbs"  /ST $hourMinute /F
