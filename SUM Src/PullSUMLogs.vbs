Set oShell = CreateObject("Shell.Application")
Set oWScript = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

Dim strTitle, pidFilePath, pid, processFound, processRunTime
strTitle = "SUM_cmd"
pidFilePath = "C:\SUMInstall\SUM_PID.txt"

' Check if PID file exists and read PID
If fso.FileExists(pidFilePath) Then
    Set pidFile = fso.OpenTextFile(pidFilePath, 1) ' 1 = ForReading
    If Not pidFile.AtEndOfStream Then
        pid = Trim(pidFile.ReadLine())
        pidFile.Close
        
        ' Check if process with this PID is still running
        Set objWMI = GetObject("winmgmts:\\.\root\cimv2")
        Set processes = objWMI.ExecQuery("SELECT * FROM Win32_Process WHERE ProcessId = " & pid)
        
        processFound = False
        For Each process In processes
            processFound = True
            
            ' Get process creation time to calculate runtime
            Set processInfo = objWMI.ExecQuery("SELECT CreationDate FROM Win32_Process WHERE ProcessId = " & pid)
            For Each procInfo In processInfo
                creationDate = procInfo.CreationDate
                ' Convert WMI datetime to VBScript datetime
                creationDateTime = CDate(Mid(creationDate, 1, 4) & "/" & Mid(creationDate, 5, 2) & "/" & Mid(creationDate, 7, 2) & " " & Mid(creationDate, 9, 2) & ":" & Mid(creationDate, 11, 2) & ":" & Mid(creationDate, 13, 2))
                
                ' Calculate runtime in minutes
                processRunTime = DateDiff("n", creationDateTime, Now())
                
                ' WScript.Echo "Process found with PID: " & pid
                ' WScript.Echo "Process running for: " & processRunTime & " minutes"
                
                If processRunTime > 60 Then
                    ' Kill the process if running more than 60 minutes
                    ' WScript.Echo "Process running for more than 60 minutes. Terminating..."
                    oWScript.Run "taskkill /PID " & pid & " /F", 0, True
                    
                    ' Delete the PID file
                    If fso.FileExists(pidFilePath) Then
                        fso.DeleteFile pidFilePath
                    End If
                    
                    ' Launch new service
                    LaunchSUMService()
                Else
                    ' Process is running but less than 60 minutes, do nothing
                    ' WScript.Echo "Process running for less than 60 minutes. No action needed."
                End If
                Exit For
            Next
            Exit For
        Next
        
        If Not processFound Then
            ' PID exists in file but process is not running
            ' WScript.Echo "PID found in file but process not running. Cleaning up and starting new service..."
            
            ' Delete the stale PID file
            If fso.FileExists(pidFilePath) Then
                fso.DeleteFile pidFilePath
            End If
            
            ' Launch new service
            LaunchSUMService()
        End If
    Else
        pidFile.Close
        ' PID file is empty, launch service
        ' WScript.Echo "PID file is empty. Starting new service..."
        LaunchSUMService()
    End If
Else
    ' No PID file exists, launch service
    ' WScript.Echo "No PID file found. Starting new service..."
    LaunchSUMService()
End If

' Subroutine to launch the SUM service
Sub LaunchSUMService()
    ' WScript.Echo "Launching SUM service..."
    
    ' Launch the process with built-in commands
    oShell.ShellExecute "cmd.exe", _
        "/c title " & strTitle & " && ""\\amr\ec\proj\mdl\cr\intel\hdmx_db\mae\Releases\SystemUtilization\HDMX\RunPullSUMLogs.bat""", _
        "", "runas", 0
    
    ' Wait for process to start
    WScript.Sleep 3000
    
    Set objWMI = GetObject("winmgmts:\\.\root\cimv2")
    Set processes = objWMI.ExecQuery("SELECT * FROM Win32_Process WHERE Name = 'cmd.exe' AND CommandLine LIKE '%" & strTitle & "%'")
    
    Dim pidFound
    pidFound = False
    
    ' Find the new process and save its PID
    For Each process In processes
        ' WScript.Echo "New process started - PID: " & process.ProcessId
        Set pidFile = fso.CreateTextFile(pidFilePath, True)
        pidFile.WriteLine process.ProcessId
        pidFile.Close
        ' WScript.Echo "PID saved to " & pidFilePath
        pidFound = True
        Exit For
    Next
    
    If Not pidFound Then
        ' WScript.Echo "Warning: Could not find the launched process to save PID"
    End If
End Sub