using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Net.NetworkInformation;
using Newtonsoft.Json;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Services;
using SystemUtilizationMonitor.Utilities;

namespace SystemUtilizationMonitor
{

    public class Program
    {
        private static readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private static readonly Dictionary<string, uint> basicFileChanges = new Dictionary<string, uint>();
        private static readonly object lockObj = new object();
        private static MonitorConfiguration config;
        private static DateTime lastVmConnectKill = DateTime.UtcNow;

        private static bool shouldStop = false;
        private static string outputDirectory;
        private static string currentOutputFile;
        private static DateTime currentDay;
        private static ConfigurationModel appConfig;
        private static readonly Dictionary<string, string> tempCopyPaths = new Dictionary<string, string>();
        private static readonly object copyLockObj = new object();
        private static string logInfo;

        private static InputHookManager inputHook;

        private static volatile bool fileChangeDetected = false;
        private static string firstChangedFile = string.Empty;

        private static DateTime lastPsExecRun = DateTime.MinValue;

        private static DateTime lastEndTime = DateTime.UtcNow;

        // Constants for the new VM killing logic
        private const string KILLING_PENDINGS_FILE = @"C:\SUMInstall\KillingPendings.txt";

        // Thread-safe logging
        private static readonly object logFileLock = new object();
        private static string monitoringLogPath;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                LoadConfiguration();

                if (!appConfig.SumPOR.Debug)
                {
                    HideConsoleWindow();
                }
                else
                {
                    Console.WriteLine("=== SystemUtilizationMonitor Debug Mode ===");
                    Console.WriteLine($"Debug Mode: {appConfig.SumPOR.Debug}");
                    Console.WriteLine($"Should Read Log Files: {appConfig.SumPOR.ShouldReadLogFiles}");
                    Console.WriteLine($"Retained File Count Limit: {appConfig.SumPOR.Args.RetainedFileCountLimit}");
                    Console.WriteLine($"Rolling Interval: {appConfig.SumPOR.Args.RollingInterval}");
                    Console.WriteLine($"Monitoring {appConfig.Jose.Count} file configurations");
                    Console.WriteLine($"JSON Output Path: {(!string.IsNullOrEmpty(appConfig.JsonOutputPath) ? appConfig.JsonOutputPath : "Default LocalAppData")}");
                    Console.WriteLine($"Hook Constants: Keyboard={appConfig.Hook.WH_KEYBOARD_LL}, Mouse={appConfig.Hook.WH_MOUSE_LL}");
                    Console.WriteLine($"Mouse Constants: LButton={appConfig.Mouse.WM_LBUTTONDOWN}, RButton={appConfig.Mouse.WM_RBUTTONDOWN}, Move={appConfig.Mouse.WM_MOUSEMOVE}");
                    Console.WriteLine($"Keyboard Constants: KeyDown={appConfig.Keyboard.WM_KEYDOWN}, SysKeyDown={appConfig.Keyboard.WM_SYSKEYDOWN}");
                    Console.WriteLine($"VM Configuration: Username={appConfig.VM.Username}, Password=***");
                    Console.WriteLine($"Monitoring Configuration: RecordInterval={appConfig.Monitoring.RecordIntervalMinutes} minutes");
                    Console.WriteLine("Press Ctrl+C to stop...");
                    Console.WriteLine("==========================================");
                }

                SetupOutputDirectory();

                InitializeForCurrentDay();

                SetupMonitoringConfiguration();

                SetupCancellation();

                InitializeInputHooks();

                StartFileCleanupTask();

                MonitoringLoop();
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Main execution error: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
            finally
            {
                Cleanup();
            }
        }

        private static void HideConsoleWindow()
        {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SW_HIDE);
            }

            try
            {
                FreeConsole();
            }
            catch
            {
                // Intentionally empty - hide console window errors are not critical
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private static void LoadConfiguration()
        {
            string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Intel", "SystemUtilizationMonitor", "SystemUtilizationConfig.json");

            // Initialize monitoring log path
            monitoringLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Intel", "SystemUtilizationMonitor", "Monitoring_logs.txt");

            if (!File.Exists(configPath))
            {
                CreateDefaultConfiguration(configPath);
            }

            string jsonContent = File.ReadAllText(configPath);
            appConfig = JsonConvert.DeserializeObject<ConfigurationModel>(jsonContent);
        }

        /// <summary>
        /// Thread-safe method to write to monitoring log file
        /// </summary>
        private static void WriteToMonitoringLog(string message)
        {
            try
            {
                lock (logFileLock)
                {
                    string logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                    File.AppendAllText(monitoringLogPath, logEntry);
                }
            }
            catch (Exception ex)
            {
                if (appConfig?.SumPOR?.Debug == true)
                {
                    Console.WriteLine($"Failed to write to monitoring log: {ex.Message}");
                }
            }
        }

        private static void CreateDefaultConfiguration(string configPath)
        {
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));

            var defaultConfig = new ConfigurationModel
            {
                Jose = new Dictionary<string, MonitorTxtConfig>
                {
                    ["montior_txt_priority"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\STHI\\logs\\strut_detail_log_yyyy/MM/dd.txt",
                        NoContent = "RmqEventsListener",
                        Skip = "GetStatus;RetrieveHWConfigInfo =;CommandType =;CommandSource =;UniqueCommandId =;SysCClientUniqueCommandId =;SiteId =;AdditionalParameters =;TesterInfo.get_VMImageVersion - VMImageVersion:;TpCache.GetCachedTps - Test package caching is not currently implemented.;NetworkConfigurator.get_IpAddressToSpacialLocation - IP location mapping:;localhost: 1;HwConfig.CollectHwConfig;HwConfig.CreateSocketEntities;HwConfig.parseCMMSList;HwConfig.SerializeXml;xml version=;HWConfiguration;</;/>;<SocketEntity;<TesterExternalEntity;<TesterExternalEntity;<BoardBLT;<TesterCoreEntity;TesterHWConfigAsXMLString",
                        FormatDate = "yyyy/MM/dd",
                        LastlineContent = "EventManager.SendEvent - Send SiteInformationEvent Event to Supervisor for command UndefinedSiteCommand, uniqueCommandId 8888888888888888888, SysCClientUniqueCommandId:"
                    },
                    ["montior_txt_normal_1"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\Logs\\Aguila\\Sequencer 1\\TraceLog.txt"
                    },
                    ["montior_txt_normal_2"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\Logs\\Aguila\\Sequencer 2\\TraceLog.txt"
                    },
                    ["montior_txt_normal_3"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\Logs\\Aguila\\Sequencer 3\\TraceLog.txt"
                    },
                    ["montior_txt_normal_4"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\Logs\\Aguila\\Sequencer 4\\TraceLog.txt"
                    }
                },
                SumPOR = new SumPORConfig
                {
                    ShouldReadLogFiles = true,
                    Debug = false,
                    Args = new ArgsConfig
                    {
                        RollingInterval = "Day",
                        RetainedFileCountLimit = 15,
                        OutputTemplate = "{Message:l}{NewLine}"
                    }
                },
                Mouse = new MouseConfig
                {
                    WM_LBUTTONDOWN = 0x0201,
                    WM_RBUTTONDOWN = 0x0204,
                    WM_MBUTTONDOWN = 0x0207,
                    WM_MOUSEMOVE = 0x0200,
                    WM_MOUSEWHEEL = 0x020A,
                    MouseMoveThrottleMs = 100
                },
                Keyboard = new KeyboardConfig
                {
                    WM_KEYDOWN = 0x0100,
                    WM_SYSKEYDOWN = 0x0104
                },
                Hook = new HookConfig
                {
                    WH_KEYBOARD_LL = 13,
                    WH_MOUSE_LL = 14
                },
                VM = new VMConfig
                {
                    Username = "cc3user",
                    Password = "sthi"
                },
                Monitoring = new MonitoringConfig
                {
                    RecordIntervalMinutes = 5
                },
                JsonOutputPath = ""
            };

            string jsonContent = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            File.WriteAllText(configPath, jsonContent);
        }

        private static void SetupOutputDirectory()
        {
            if (!string.IsNullOrEmpty(appConfig.JsonOutputPath))
            {
                outputDirectory = Environment.ExpandEnvironmentVariables(appConfig.JsonOutputPath);

                if (!Directory.Exists(outputDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDirectory);
                        WriteToMonitoringLog($"INFO: Created output directory: {outputDirectory}");
                    }
                    catch (Exception ex)
                    {
                        WriteToMonitoringLog($"ERROR: Failed to create output directory '{outputDirectory}': {ex.Message}");
                        outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Intel", "SystemUtilizationMonitor");
                        WriteToMonitoringLog($"INFO: Falling back to default output directory: {outputDirectory}");
                    }
                }
            }
            else
            {
                outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor");
            }

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            WriteToMonitoringLog($"INFO: Output directory set to: {outputDirectory}");
        }

        private static void InitializeForCurrentDay()
        {
            currentDay = DateTime.UtcNow.Date;
            currentOutputFile = Path.Combine(outputDirectory,
                $"SystemUtilizationTimeFrames{currentDay:yyyyMMdd}.json");
        }

        private static void SetupMonitoringConfiguration()
        {
            config = new MonitorConfiguration();
            config.RecordInterval = TimeSpan.FromMinutes(appConfig.Monitoring.RecordIntervalMinutes);
        }

        private static void SetupCancellation()
        {
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                shouldStop = true;
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, e) => { shouldStop = true; Cleanup(); };
        }

        private static void InitializeInputHooks()
        {
            try
            {
                inputHook = new InputHookManager(appConfig);
                inputHook.Start();
                WriteToMonitoringLog("INFO: Input monitoring initialized successfully");
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Could not initialize input hooks: {ex.Message}");
            }
        }

        private static void StartFileCleanupTask()
        {
            Task.Factory.StartNew(delegate ()
            {
                while (!shouldStop)
                {
                    try
                    {
                        CleanupOldFiles();
                        Thread.Sleep(TimeSpan.FromHours(1));
                    }
                    catch (Exception ex)
                    {
                        WriteToMonitoringLog($"ERROR: File cleanup error: {ex.Message}");
                        Thread.Sleep(TimeSpan.FromHours(1));
                    }
                }
            });
        }

        private static void CleanupOldFiles()
        {
            try
            {
                var files = Directory.GetFiles(outputDirectory, "SystemUtilizationTimeFrames*.json")
                    .Select(f => new FileInfo(f))
                    .Where(f => !f.Name.Equals("SystemUtilizationTimeFrames.json", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                var filesToDelete = files.Skip(appConfig.SumPOR.Args.RetainedFileCountLimit).ToList();

                foreach (var file in filesToDelete)
                {
                    try
                    {
                        file.Delete();
                        WriteToMonitoringLog($"INFO: Deleted old file: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        WriteToMonitoringLog($"ERROR: Could not delete file {file.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error during file cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Pings a host to check if it's reachable
        /// </summary>
        private static bool PingHost(string hostname)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(hostname, 500);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error pinging {hostname}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Alternative method using schtasks instead of PsExec
        /// This bypasses the "Log on as a service" requirement
        /// </summary>
        private static void ExecuteScheduledTask(string ipAddress)
        {
            try
            {
                // Step 1: Create a scheduled task on the remote machine
                string taskName = $"VM_Close_Notification_{DateTime.Now:HHmmss}";
                string createTaskCommand = $@"schtasks /create /s {ipAddress} /u {appConfig.VM.Username} /p {appConfig.VM.Password} /tn ""{taskName}"" /tr ""c:\Users\{appConfig.VM.Username}\Desktop\VM_Close_PopUP.bat"" /sc once /st {DateTime.Now.AddSeconds(5):HH:mm} /sd {DateTime.Now:MM/dd/yyyy} /ru {appConfig.VM.Username} /rp {appConfig.VM.Password} /f";

                ProcessStartInfo createInfo = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c \"{createTaskCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                WriteToMonitoringLog($"INFO: Creating scheduled task on {ipAddress}");

                using (Process createProcess = Process.Start(createInfo))
                {
                    bool finished = createProcess.WaitForExit(10000);

                    if (finished && createProcess.ExitCode == 0)
                    {
                        WriteToMonitoringLog($"INFO: Successfully created scheduled task on {ipAddress}");

                        // Step 2: Run the task immediately
                        string runTaskCommand = $@"schtasks /run /s {ipAddress} /u {appConfig.VM.Username} /p {appConfig.VM.Password} /tn ""{taskName}""";

                        ProcessStartInfo runInfo = new ProcessStartInfo
                        {
                            FileName = "cmd",
                            Arguments = $"/c \"{runTaskCommand}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };

                        using (Process runProcess = Process.Start(runInfo))
                        {
                            runProcess.WaitForExit(10000);

                            if (runProcess.ExitCode == 0)
                            {
                                WriteToMonitoringLog($"INFO: Successfully executed scheduled task on {ipAddress}");
                            }
                            else
                            {
                                string runError = runProcess.StandardError.ReadToEnd();
                                WriteToMonitoringLog($"ERROR: Failed to run scheduled task on {ipAddress}, exit code: {runProcess.ExitCode}, error: {runError}");
                            }
                        }

                        // Step 3: Clean up - delete the task after a delay
                        Task.Delay(30000).ContinueWith(_ => CleanupScheduledTask(ipAddress, taskName));
                    }
                    else
                    {
                        string error = createProcess.StandardError.ReadToEnd();
                        WriteToMonitoringLog($"ERROR: Failed to create scheduled task on {ipAddress}, exit code: {createProcess.ExitCode}, error: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error executing scheduled task on {ipAddress}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean up the temporary scheduled task
        /// </summary>
        private static void CleanupScheduledTask(string ipAddress, string taskName)
        {
            try
            {
                string deleteCommand = $@"schtasks /delete /s {ipAddress} /u {appConfig.VM.Username} /p {appConfig.VM.Password} /tn ""{taskName}"" /f";

                ProcessStartInfo deleteInfo = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c \"{deleteCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process deleteProcess = Process.Start(deleteInfo))
                {
                    deleteProcess.WaitForExit(5000);
                    WriteToMonitoringLog($"INFO: Cleaned up scheduled task {taskName} on {ipAddress}");
                }
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Failed to cleanup scheduled task on {ipAddress}: {ex.Message}");
            }
        }

        /// <summary>
        /// Test scheduled task authentication
        /// </summary>
        private static bool TestScheduledTaskAuthentication(string ipAddress)
        {
            try
            {
                string testCommand = $@"schtasks /query /s {ipAddress} /u {appConfig.VM.Username} /p {appConfig.VM.Password}";

                ProcessStartInfo testInfo = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c \"{testCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process testProcess = Process.Start(testInfo))
                {
                    bool finished = testProcess.WaitForExit(10000);

                    if (finished && testProcess.ExitCode == 0)
                    {
                        WriteToMonitoringLog($"INFO: Scheduled task authentication test successful for {ipAddress}");
                        return true;
                    }
                    else
                    {
                        string error = testProcess.StandardError.ReadToEnd();
                        WriteToMonitoringLog($"ERROR: Scheduled task authentication test failed for {ipAddress}: {error}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Scheduled task authentication test exception for {ipAddress}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Improved VM Close and Kill scheduling with sequential processing to avoid file conflicts
        /// </summary>
        private static void ScheduleVmCloseAndKill()
        {
            try
            {
                WriteToMonitoringLog("INFO: Scheduling VM close operations and kill - checking VMs");

                // Process VMs sequentially instead of in parallel to avoid file access conflicts
                for (int i = 1; i <= 4; i++)
                {
                    string ipvm = $"10.0.0.{i}";
                    ProcessSingleVM(ipvm);

                    // Small delay between VM operations to prevent resource conflicts
                    Thread.Sleep(1000);
                }

                // Schedule the kill operation
                ScheduleKillOperation();
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error scheduling VM close and kill: {ex.Message}");
            }
        }

        /// <summary>
        /// Process a single VM with comprehensive error handling using scheduled tasks
        /// </summary>
        private static void ProcessSingleVM(string ipAddress)
        {
            try
            {
                WriteToMonitoringLog($"INFO: Processing VM {ipAddress}");

                if (!PingHost(ipAddress))
                {
                    WriteToMonitoringLog($"INFO: VM {ipAddress} is not reachable, skipping");
                    return;
                }

                WriteToMonitoringLog($"INFO: VM {ipAddress} is reachable, proceeding with close operation");

                // Try to copy the batch file first
                if (!EnsureBatchFileExists(ipAddress))
                {
                    WriteToMonitoringLog($"ERROR: Failed to ensure batch file exists on {ipAddress}, skipping execution");
                    return;
                }

                // Test scheduled task authentication before attempting execution
                if (!TestScheduledTaskAuthentication(ipAddress))
                {
                    WriteToMonitoringLog($"ERROR: Scheduled task authentication test failed for {ipAddress}, skipping execution");
                    return;
                }

                // Execute the command using scheduled tasks
                ExecuteScheduledTask(ipAddress);
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error processing VM {ipAddress}: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensure the batch file exists on the target VM
        /// </summary>
        private static bool EnsureBatchFileExists(string ipAddress)
        {
            try
            {
                string remotePath = $@"\\{ipAddress}\c$\Users\{appConfig.VM.Username}\Desktop\VM_Close_PopUP.bat";
                string localPath = @"C:\SUMInstall\VM_Close_PopUP.bat";

                if (!File.Exists(localPath))
                {
                    WriteToMonitoringLog($"ERROR: Local batch file not found: {localPath}");
                    return false;
                }

                if (!File.Exists(remotePath))
                {
                    WriteToMonitoringLog($"INFO: Batch file not found on {ipAddress}, copying...");

                    // Ensure remote directory exists
                    string remoteDir = Path.GetDirectoryName(remotePath);
                    if (!Directory.Exists(remoteDir))
                    {
                        Directory.CreateDirectory(remoteDir);
                    }

                    File.Copy(localPath, remotePath, true);
                    WriteToMonitoringLog($"INFO: Successfully copied VM_Close_PopUP.bat to {ipAddress}");
                }
                else
                {
                    WriteToMonitoringLog($"INFO: Batch file already exists on {ipAddress}");
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Failed to ensure batch file exists on {ipAddress}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Schedule the kill operation
        /// </summary>
        private static void ScheduleKillOperation()
        {
            try
            {
                DateTime killTime = DateTime.UtcNow.AddMinutes(15);
                string killTimeString = killTime.ToString("yy:MM:dd:HH:mm:ss");

                string directory = Path.GetDirectoryName(KILLING_PENDINGS_FILE);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    WriteToMonitoringLog($"INFO: Created directory: {directory}");
                }

                string killEntry = $"Killing vmconnect at {killTimeString}";
                File.AppendAllText(KILLING_PENDINGS_FILE, killEntry + Environment.NewLine);

                WriteToMonitoringLog($"INFO: Added kill entry: {killEntry}");
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error scheduling kill operation: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if any VMs have user activity by reading the userconected.txt files
        /// </summary>
        private static bool CheckVMUserActivity()
        {
            try
            {
                WriteToMonitoringLog("INFO: Checking VM user activity...");

                for (int i = 1; i <= 4; i++)
                {
                    string ipvm = $"10.0.0.{i}";

                    if (PingHost(ipvm))
                    {
                        string remoteUserFile = $@"\\{ipvm}\c$\SUMInstall\userconected.txt";

                        try
                        {
                            if (File.Exists(remoteUserFile))
                            {
                                string content = File.ReadAllText(remoteUserFile).Trim();

                                // If file has content other than empty lines, user is active
                                if (!string.IsNullOrWhiteSpace(content) && content != "user is here")
                                {
                                    WriteToMonitoringLog($"INFO: User activity detected on VM {ipvm}: {content}");
                                    return true;
                                }
                                else
                                {
                                    WriteToMonitoringLog($"INFO: No user activity on VM {ipvm}");
                                }
                            }
                            else
                            {
                                WriteToMonitoringLog($"INFO: User activity file not found on VM {ipvm}");
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteToMonitoringLog($"ERROR: Error reading user activity file from VM {ipvm}: {ex.Message}");
                        }
                    }
                }

                return false; // No user activity detected
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error checking VM user activity: {ex.Message}");
                return false; // Assume no activity on error
            }
        }

        /// <summary>
        /// Checks for overdue kills in the pendings file and executes them
        /// </summary>
        private static void ProcessPendingKills()
        {
            try
            {
                if (!File.Exists(KILLING_PENDINGS_FILE))
                {
                    return; // No pending kills file, nothing to do
                }

                List<string> remainingLines = new List<string>();
                string[] lines = File.ReadAllLines(KILLING_PENDINGS_FILE);
                bool killExecuted = false;

                DateTime currentUtc = DateTime.UtcNow;

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    // Parse the kill time from the line
                    // Expected format: "Killing vmconnect at yy:MM:dd:HH:mm:ss"
                    var match = Regex.Match(line, @"Killing vmconnect at (\d{2}:\d{2}:\d{2}:\d{2}:\d{2}:\d{2})");
                    if (match.Success)
                    {
                        string timeString = match.Groups[1].Value;

                        if (TryParseKillTime(timeString, out DateTime killTime))
                        {
                            if (currentUtc >= killTime)
                            {
                                // Kill time is overdue - check for user activity before killing
                                WriteToMonitoringLog($"INFO: Processing overdue kill from: {line}");

                                if (!CheckVMUserActivity())
                                {
                                    KillVmConnectProcesses();
                                    killExecuted = true;
                                    WriteToMonitoringLog("INFO: VM kill executed - no user activity detected");
                                }
                                else
                                {
                                    WriteToMonitoringLog("INFO: VM kill skipped - user activity detected");
                                    // Keep the line to retry later
                                    remainingLines.Add(line);
                                }
                                // Don't add this line to remainingLines if kill was executed
                            }
                            else
                            {
                                // Kill time is still in the future - keep the line
                                remainingLines.Add(line);
                            }
                        }
                        else
                        {
                            WriteToMonitoringLog($"ERROR: Could not parse kill time from line: {line}");
                            // Keep malformed lines to avoid losing data
                            remainingLines.Add(line);
                        }
                    }
                    else
                    {
                        WriteToMonitoringLog($"ERROR: Invalid kill entry format: {line}");
                        // Keep malformed lines to avoid losing data
                        remainingLines.Add(line);
                    }
                }

                // Rewrite the file with only the remaining pending kills
                if (killExecuted || remainingLines.Count != lines.Length)
                {
                    if (remainingLines.Count > 0)
                    {
                        File.WriteAllLines(KILLING_PENDINGS_FILE, remainingLines);
                        WriteToMonitoringLog($"INFO: Updated pendings file with {remainingLines.Count} remaining entries");
                    }
                    else
                    {
                        File.Delete(KILLING_PENDINGS_FILE);
                        WriteToMonitoringLog("INFO: Deleted empty pendings file");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error processing pending kills: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to parse the kill time string in format "yy:MM:dd:HH:mm:ss"
        /// </summary>
        private static bool TryParseKillTime(string timeString, out DateTime killTime)
        {
            killTime = DateTime.MinValue;

            try
            {
                // Parse format: yy:MM:dd:HH:mm:ss
                string[] parts = timeString.Split(':');
                if (parts.Length != 6)
                {
                    return false;
                }

                int year = 2000 + int.Parse(parts[0]); // Convert yy to yyyy
                int month = int.Parse(parts[1]);
                int day = int.Parse(parts[2]);
                int hour = int.Parse(parts[3]);
                int minute = int.Parse(parts[4]);
                int second = int.Parse(parts[5]);

                killTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Modified method to kill vmconnect processes
        /// </summary>
        private static void KillVmConnectProcesses()
        {
            try
            {
                Process[] vmConnectProcesses = Process.GetProcessesByName("vmconnect");

                if (vmConnectProcesses.Length > 0)
                {
                    WriteToMonitoringLog($"INFO: Found {vmConnectProcesses.Length} vmconnect process(es) to terminate");

                    foreach (Process process in vmConnectProcesses)
                    {
                        try
                        {
                            WriteToMonitoringLog($"INFO: Attempting to kill vmconnect process with PID: {process.Id}");
                            process.Kill();
                            process.WaitForExit(5000); // Wait up to 5 seconds for graceful exit
                            WriteToMonitoringLog($"INFO: Successfully killed vmconnect process with PID: {process.Id}");
                        }
                        catch (Exception ex)
                        {
                            WriteToMonitoringLog($"ERROR: Failed to kill vmconnect process with PID: {process.Id}. Error: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                else
                {
                    WriteToMonitoringLog("INFO: No vmconnect processes found to terminate");
                }
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error while searching for vmconnect processes: {ex.Message}");
            }
        }

        private static void MonitoringLoop()
        {
            while (!shouldStop)
            {
                var startTime = lastEndTime;
                logInfo = string.Empty;

                if (DateTime.UtcNow.Date != currentDay)
                {
                    InitializeForCurrentDay();
                    WriteToMonitoringLog($"INFO: Switched to new daily file: {currentOutputFile}");
                }

                // Process any pending kills first
                ProcessPendingKills();

                // Check if 2 minutes have passed since last VM operation scheduling
                if (DateTime.UtcNow.Subtract(lastVmConnectKill).TotalMinutes >= 2)
                {
                    ScheduleVmCloseAndKill();
                    lastVmConnectKill = DateTime.UtcNow;
                }

                var endTime = startTime.Add(config.RecordInterval);
                ResetCounters();
                try
                {
                    Thread.Sleep(config.RecordInterval);
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }

                if (shouldStop) break;

                WriteToMonitoringLog($"INFO: Saved JSON to: {currentOutputFile} \n                                           with startTime: {startTime.ToString("yyyy-MM-ddTHH:mm:ssZ")}");

                var timeFrame = CollectUtilizationData(startTime, endTime);
                lastEndTime = endTime;
                WriteToFile(currentOutputFile, timeFrame);
            }
        }

        private static UtilizationTimeFrame CollectUtilizationData(DateTime startTime, DateTime endTime)
        {
            var timeFrame = new UtilizationTimeFrame();
            timeFrame.StartTime = startTime;
            timeFrame.EndTime = endTime;
            timeFrame.MachineName = Environment.MachineName;
            timeFrame.Product = GetProductPartNumber();

            if (inputHook != null)
            {
                timeFrame.MouseEvents = inputHook.GetMouseEventCount();
                timeFrame.KeyboardEvents = inputHook.GetKeyboardEventCount();
            }

            // Create MonitoringVMs instance with configuration
            MonitoringVMs monitoringVMs = new MonitoringVMs(appConfig);
            bool vmInUse = false;

            try
            {
                // Set a timeout for the VM check to prevent hanging
                var vmCheckTask = Task.Run(() => monitoringVMs.CheckVMsAsync());

                // Wait for the task to complete with a timeout (e.g., 5 seconds)
                if (vmCheckTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    vmInUse = vmCheckTask.Result;
                    WriteToMonitoringLog($"INFO: VM check completed successfully. VMs in use: {vmInUse}");
                }
                else
                {
                    WriteToMonitoringLog("ERROR: VM check timed out after 5 seconds, continuing with file monitoring");
                    vmInUse = false;
                }
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: VM monitoring failed with error: {ex.Message}, continuing with file monitoring");
                vmInUse = false;
            }

            // Continue with the logic regardless of VM check results
            if (vmInUse)
            {
                timeFrame.FileChanges = string.Empty;
                timeFrame.FileChanges = "VMs In Use By VMC or hyperV";
                WriteToMonitoringLog("INFO: VMs detected in use, skipping file monitoring");
            }
            else
            {
                timeFrame = MonitoringSUM.MonitoringFiles(timeFrame, appConfig, logInfo);
                WriteToMonitoringLog("INFO: No VMs in use or VM check failed, proceeding with file monitoring");
            }

            return timeFrame;
        }

        private static void ResetCounters()
        {
            lock (lockObj)
            {
                basicFileChanges.Clear();
                fileChangeDetected = false;
                firstChangedFile = string.Empty;
            }
            if (inputHook != null)
            {
                inputHook.ResetCounters();
            }
        }

        private static void WriteToFile(string fileName, UtilizationTimeFrame timeFrame)
        {
            try
            {
                var json = CustomJsonSerializer.Serialize(timeFrame);
                File.AppendAllText(fileName, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error writing to file: {ex.Message}");
            }
        }

        private static void LogInfo(string message)
        {
            if (appConfig?.SumPOR?.Debug == true)
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] INFO: {message}");
            }

            try
            {
                logInfo = logInfo + $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine} \n";
            }
            catch { }
        }

        private static void LogError(string message)
        {
            if (appConfig?.SumPOR?.Debug == true)
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] ERROR: {message}");
            }

            try
            {
                logInfo = logInfo + $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR: {message}{Environment.NewLine} \n";
            }
            catch { }
        }

        private static void Cleanup()
        {
            if (inputHook != null)
            {
                inputHook.Dispose();
            }

            foreach (var watcher in watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch { }
            }

            foreach (var tempPath in tempCopyPaths.Values)
            {
                try
                {
                    if (Directory.Exists(tempPath))
                    {
                        Directory.Delete(tempPath, true);
                        WriteToMonitoringLog($"INFO: Cleaned up temp directory: {tempPath}");
                    }
                }
                catch (Exception ex)
                {
                    WriteToMonitoringLog($"ERROR: Could not clean up temp directory {tempPath}: {ex.Message}");
                }
            }

            WriteToMonitoringLog("INFO: Cleanup completed.");
        }

        private static string GetProductPartNumber()
        {
            try
            {
                WriteToMonitoringLog("INFO: Starting enhanced product detection logic...");

                string hdmxPath = @"D:\HDMT3\HdmtOutputFiles";
                if (Directory.Exists(hdmxPath))
                {
                    WriteToMonitoringLog("INFO: HDMX machine detected - checking TesterHwConfig.xml");
                    string product = GetProductFromHDMX(hdmxPath);
                    if (!string.IsNullOrEmpty(product))
                    {
                        WriteToMonitoringLog($"INFO: Successfully retrieved product from HDMX: {product}");
                        return product;
                    }
                    WriteToMonitoringLog("INFO: HDMX path exists but no product found, falling back to HST methods");
                }
                else
                {
                    WriteToMonitoringLog("INFO: HDMX path not found, assuming HST machine");
                }

                string hstMethod1Product = GetProductFromHSTMethod1();
                if (!string.IsNullOrEmpty(hstMethod1Product))
                {
                    WriteToMonitoringLog($"INFO: Successfully retrieved product from HST Method 1: {hstMethod1Product}");
                    return hstMethod1Product;
                }

                string hstMethod2Product = GetProductFromHSTMethod2();
                if (!string.IsNullOrEmpty(hstMethod2Product))
                {
                    WriteToMonitoringLog($"INFO: Successfully retrieved product from HST Method 2: {hstMethod2Product}");
                    return hstMethod2Product;
                }

                WriteToMonitoringLog("ERROR: All product detection methods failed");
                return "PRODUCT_NOT_FOUND";
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error in GetProductPartNumber: {ex.Message}");
                return "ERROR_GETTING_PRODUCT";
            }
        }

        private static string GetProductFromHDMX(string hdmxPath)
        {
            try
            {
                string configFilePath = Path.Combine(hdmxPath, "TesterHwConfig.xml");
                if (!File.Exists(configFilePath))
                {
                    WriteToMonitoringLog($"INFO: TesterHwConfig.xml not found at: {configFilePath}");
                    return "";
                }

                WriteToMonitoringLog($"INFO: Reading TesterHwConfig.xml from: {configFilePath}");
                string xmlContent = File.ReadAllText(configFilePath);

                // Nuevo regex para buscar DUTSocketSerialNumber0
                var dutSocketSerialRegex = new System.Text.RegularExpressions.Regex(
                    @"<SupplementalData\s+Name=""DUTSocketSerialNumber0""\s+Value=""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
                );

                var match = dutSocketSerialRegex.Match(xmlContent);
                if (match.Success)
                {
                    string dutSerialNumber = match.Groups[1].Value.Trim();
                    WriteToMonitoringLog($"INFO: Found DUTSocketSerialNumber0 in HDMX config: {dutSerialNumber}");
                    return dutSerialNumber;
                }
                else
                {
                    WriteToMonitoringLog("INFO: DUTSocketSerialNumber0 pattern not found in TesterHwConfig.xml");
                    return "";
                }
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error reading HDMX config: {ex.Message}");
                return "";
            }
        }

        private static string GetProductFromHSTMethod1()
        {
            try
            {
                string hstCachePath = @"c:\hst\tpcache\o\D7";

                if (!Directory.Exists(hstCachePath))
                {
                    WriteToMonitoringLog($"INFO: HST cache path not found: {hstCachePath}");
                    return "";
                }

                WriteToMonitoringLog($"INFO: Checking HST cache path: {hstCachePath}");

                string[] firstLevelDirs = Directory.GetDirectories(hstCachePath);

                if (firstLevelDirs.Length == 0)
                {
                    WriteToMonitoringLog("INFO: No directories found in HST cache D7 folder");
                    return "";
                }

                string firstDir = firstLevelDirs[0];
                WriteToMonitoringLog($"INFO: Found first level directory: {Path.GetFileName(firstDir)}");

                string[] secondLevelDirs = Directory.GetDirectories(firstDir);

                if (secondLevelDirs.Length == 0)
                {
                    WriteToMonitoringLog("INFO: No second level directories found in HST cache");
                    return "";
                }

                string secondDir = secondLevelDirs[0];
                string productName = Path.GetFileName(secondDir);

                WriteToMonitoringLog($"INFO: Found second level directory (product): {productName}");
                return productName;
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error in HST Method 1: {ex.Message}");
                return "";
            }
        }

        private static string GetProductFromHSTMethod2()
        {
            try
            {
                string hstLoopsPath = @"D:\HST\TP_ENG_Loops\TP";

                if (!Directory.Exists(hstLoopsPath))
                {
                    WriteToMonitoringLog($"INFO: HST loops path not found: {hstLoopsPath}");
                    return "";
                }

                WriteToMonitoringLog($"INFO: Checking HST loops path: {hstLoopsPath}");

                string[] zipFiles = Directory.GetFiles(hstLoopsPath, "*.zip");

                if (zipFiles.Length == 0)
                {
                    WriteToMonitoringLog("INFO: No ZIP files found in HST loops directory");
                    return "";
                }

                WriteToMonitoringLog($"INFO: Found {zipFiles.Length} ZIP files in HST loops directory");

                Array.Sort(zipFiles, (x, y) => File.GetLastWriteTime(y).CompareTo(File.GetLastWriteTime(x)));

                string mostRecentZip = zipFiles[0];
                string productName = Path.GetFileNameWithoutExtension(mostRecentZip);

                WriteToMonitoringLog($"INFO: Most recent ZIP file: {Path.GetFileName(mostRecentZip)}");
                WriteToMonitoringLog($"INFO: Product name from ZIP: {productName}");

                return productName;
            }
            catch (Exception ex)
            {
                WriteToMonitoringLog($"ERROR: Error in HST Method 2: {ex.Message}");
                return "";
            }
        }
    }
}