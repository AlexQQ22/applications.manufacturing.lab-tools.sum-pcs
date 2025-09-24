using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private static MonitorConfiguration config;
        private static bool shouldStop = false;
        private static string outputDirectory;
        private static string currentOutputFile;
        private static DateTime currentDay;
        private static ConfigurationModel appConfig;
        private static string logInfo;
        private static InputHookManager inputHook;
        private static DateTime lastEndTime = DateTime.UtcNow;
        private static DateTime lastVmConnectedDetection = DateTime.MinValue;

        // Constants for VM management
        private const string KILLING_PENDINGS_FILE = @"C:\SUMInstall\KillingPendings.txt";
        private const int VM_TIMEOUT_MINUTES = 40;
        private const int KILL_DELAY_MINUTES = 5;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                LoadConfiguration();

                if (!appConfig.SumPOR.Debug)
                    HideConsoleWindow();
                else
                {
                    Console.WriteLine("=== SystemUtilizationMonitor Debug Mode ===");
                    Console.WriteLine("Press Ctrl+C to stop...");
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
                LogError("Main execution error: " + ex.Message);
            }
            finally
            {
                Cleanup();
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
                    LogInfo($"Switched to new daily file: {currentOutputFile}");
                }

                // 1. Check if vmconnect instances exist
                bool vmConnectExists = CheckVmConnectProcesses();

                // Only update lastVmConnectedDetection when we FIRST detect VMConnect
                // or when VMConnect wasn't detected in the previous cycle but is now detected
                if (vmConnectExists && lastVmConnectedDetection == DateTime.MinValue)
                {
                    lastVmConnectedDetection = DateTime.UtcNow;
                    LogInfo("VMConnect processes detected for the first time, starting timeout timer");
                }
                else if (vmConnectExists)
                {
                    LogInfo("VMConnect processes still running");
                }
                else
                {
                    // If VMConnect is no longer running, reset the detection time
                    if (lastVmConnectedDetection != DateTime.MinValue)
                    {
                        LogInfo("VMConnect processes no longer detected, resetting timer");
                        lastVmConnectedDetection = DateTime.MinValue;
                    }
                }

                // 2. Process any pending kills first
                ProcessPendingKills();
                LogInfo("Killing Pendings Processed");

                // 3. Check if VM_TIMEOUT_MINUTES have passed since FIRST VM connection detection
                // AND vmconnect still exists
                if (vmConnectExists && ShouldScheduleVmClose())
                {
                    ScheduleVmCloseAndKill();
                }

                LogInfo("VMClosure checked");

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
                LogInfo("5 minutes finished");

                var timeFrame = CollectUtilizationData(startTime, endTime);
                lastEndTime = endTime;
                WriteToFile(currentOutputFile, timeFrame);
            }
        }

        private static bool CheckVmConnectProcesses()
        {
            try
            {
                Process[] vmConnectProcesses = Process.GetProcessesByName("vmconnect");
                return vmConnectProcesses.Length > 0;
            }
            catch (Exception ex)
            {
                LogError($"Error checking vmconnect processes: {ex.Message}");
                return false;
            }
        }

        private static bool ShouldScheduleVmClose()
        {
            if (lastVmConnectedDetection == DateTime.MinValue)
            {
                LogInfo("lastVmConnectedDetection is minvalue");
                return false;
            }
            var timeSinceLastDetection = DateTime.UtcNow - lastVmConnectedDetection;
            LogInfo($"timeSinceLastDetection >= VM_TIMEOUT_MINUTES: {timeSinceLastDetection.TotalMinutes >= VM_TIMEOUT_MINUTES}, {timeSinceLastDetection}, {VM_TIMEOUT_MINUTES}");
            return timeSinceLastDetection.TotalMinutes >= VM_TIMEOUT_MINUTES;
        }

        private static void ProcessPendingKills()
        {
            try
            {
                if (!File.Exists(KILLING_PENDINGS_FILE))
                    return;

                List<string> remainingLines = new List<string>();
                string[] lines = File.ReadAllLines(KILLING_PENDINGS_FILE);
                bool killExecuted = false;
                DateTime currentUtc = DateTime.UtcNow;

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var match = Regex.Match(line, @"Killing vmconnect at (\d{2}:\d{2}:\d{2}:\d{2}:\d{2}:\d{2})");
                    if (match.Success)
                    {
                        string timeString = match.Groups[1].Value;

                        if (TryParseKillTime(timeString, out DateTime killTime))
                        {
                            if (currentUtc >= killTime)
                            {
                                LogInfo($"Processing overdue kill from: {line}");

                                // Check for user activity across all VMs
                                if (!CheckVMUserActivity())
                                {
                                    KillVmConnectProcesses();
                                    killExecuted = true;
                                    LogInfo("VM kill executed - no user activity detected");
                                }
                                else
                                {
                                    LogInfo("VM kill skipped - user activity detected");
                                    remainingLines.Add(line);
                                }
                            }
                            else
                            {
                                remainingLines.Add(line);
                            }
                        }
                        else
                        {
                            LogError($"Could not parse kill time from line: {line}");
                            remainingLines.Add(line);
                        }
                    }
                    else
                    {
                        remainingLines.Add(line);
                    }
                }

                // Update the pendings file
                if (killExecuted || remainingLines.Count != lines.Length)
                {
                    if (remainingLines.Count > 0)
                    {
                        File.WriteAllLines(KILLING_PENDINGS_FILE, remainingLines);
                    }
                    else
                    {
                        File.Delete(KILLING_PENDINGS_FILE);
                        LogInfo("Deleted empty pendings file");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing pending kills: {ex.Message}");
            }
        }

        private static bool CheckVMUserActivity()
        {
            try
            {
                for (int i = 1; i <= 4; i++)
                {
                    string ipvm = $"10.0.0.{i}";

                    if (PingHost(ipvm))
                    {
                        string remoteActivityFile = $@"\\{ipvm}\c$\Temp\user_has_activity.txt";

                        try
                        {
                            if (File.Exists(remoteActivityFile))
                            {
                                string content = File.ReadAllText(remoteActivityFile).Trim();

                                if (content.Equals("YES", StringComparison.OrdinalIgnoreCase))
                                {
                                    LogInfo($"User activity detected on VM {ipvm}");
                                    // Clean the activity file
                                    File.WriteAllText(remoteActivityFile, "");
                                    return true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError($"Error checking user activity on VM {ipvm}: {ex.Message}");
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogError($"Error checking VM user activity: {ex.Message}");
                return false;
            }
        }

        private static bool TryParseKillTime(string timeString, out DateTime killTime)
        {
            killTime = DateTime.MinValue;

            try
            {
                string[] parts = timeString.Split(':');
                if (parts.Length != 6) return false;

                int year = 2000 + int.Parse(parts[0]);
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

        private static void KillVmConnectProcesses()
        {
            try
            {
                Process[] vmConnectProcesses = Process.GetProcessesByName("vmconnect");

                if (vmConnectProcesses.Length > 0)
                {
                    LogInfo($"Found {vmConnectProcesses.Length} vmconnect process(es) to terminate");

                    foreach (Process process in vmConnectProcesses)
                    {
                        try
                        {
                            LogInfo($"Killing vmconnect process with PID: {process.Id}");
                            process.Kill();
                            process.WaitForExit(5000);
                            LogInfo($"Successfully killed vmconnect process with PID: {process.Id}");
                        }
                        catch (Exception ex)
                        {
                            LogError($"Failed to kill vmconnect process with PID: {process.Id}. Error: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                else
                {
                    LogInfo("No vmconnect processes found to terminate");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error while searching for vmconnect processes: {ex.Message}");
            }
        }

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
                LogError($"Error pinging {hostname}: {ex.Message}");
                return false;
            }
        }

        private static void ScheduleVmCloseAndKill()
        {
            try
            {
                LogInfo("Scheduling VM close operations and kill after VM connection timeout");

                // Calculate kill time (current UTC + 5 minutes)
                DateTime killTime = DateTime.UtcNow.AddMinutes(KILL_DELAY_MINUTES);
                string killTimeString = killTime.ToString("yy:MM:dd:HH:mm:ss");

                // Ensure directory exists
                string directory = Path.GetDirectoryName(KILLING_PENDINGS_FILE);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Add the kill entry to the pendings file
                string killEntry = $"Killing vmconnect at {killTimeString}";
                File.AppendAllText(KILLING_PENDINGS_FILE, killEntry + Environment.NewLine);
                LogInfo($"Added kill entry: {killEntry}");

                // Execute VM close operations on available VMs with timeout
                Task.Run(() =>
                {
                    try
                    {
                        for (int i = 1; i <= 4; i++)
                        {
                            string ipvm = $"10.0.0.{i}";

                            // Use a timeout for the ping operation
                            if (PingHostWithTimeout(ipvm, 1000))
                            {
                                LogInfo($"VM {ipvm} is reachable, executing close script");
                                ExecuteVmCloseScriptAsync(ipvm);
                            }
                            else
                            {
                                LogInfo($"VM {ipvm} is not reachable, skipping");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error in VM close task: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                LogError($"Error scheduling VM close and kill: {ex.Message}");
            }
        }

        private static bool PingHostWithTimeout(string hostname, int timeoutMs)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(hostname, timeoutMs);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch (Exception ex)
            {
                LogError($"Error pinging {hostname}: {ex.Message}");
                return false;
            }
        }

        private static void ExecuteVmCloseScriptAsync(string ipAddress)
        {
            Task.Run(() =>
            {
                try
                {
                    // Check if VM_Close_PopUP.bat exists on the VM desktop, with timeout
                    string remoteBatPath = $@"\\{ipAddress}\c$\Users\cc3user\Desktop\VM_Close_PopUP.bat";
                    string localBatPath = @"C:\SUMInstall\VM_Close_PopUP.bat";

                    // Use a timeout for file operations
                    var copyTask = Task.Run(() =>
                    {
                        try
                        {
                            if (!File.Exists(remoteBatPath))
                            {
                                if (File.Exists(localBatPath))
                                {
                                    File.Copy(localBatPath, remoteBatPath, true);
                                    LogInfo($"Copied VM_Close_PopUP.bat to {ipAddress} desktop");
                                    return true;
                                }
                                else
                                {
                                    LogError($"Source bat file not found at {localBatPath}");
                                    return false;
                                }
                            }
                            else
                            {
                                LogInfo($"VM_Close_PopUP.bat already exists on {ipAddress} desktop");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError($"Failed to copy VM_Close_PopUP.bat to {ipAddress}: {ex.Message}");
                            return false;
                        }
                    });

                    // Wait for copy operation with 5-second timeout
                    if (!copyTask.Wait(5000))
                    {
                        LogError($"File copy to {ipAddress} timed out after 5 seconds");
                        return;
                    }

                    if (!copyTask.Result)
                    {
                        LogError($"File copy to {ipAddress} failed");
                        return;
                    }

                    // Execute the script with proper timeout handling
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = @"c:\SUMInstall\PsExec64.exe",
                        Arguments = $@"\\{ipAddress} -u cc3user -p sthi -h -i 1 -accepteula -nobanner cmd /c ""c:\Users\cc3user\Desktop\VM_Close_PopUP.bat""",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    LogInfo($"Executing VM close script on {ipAddress}");

                    using (Process process = Process.Start(startInfo))
                    {
                        // Set a more reasonable timeout for the process
                        bool finished = process.WaitForExit(10000); // 10 seconds

                        if (finished)
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            string errors = process.StandardError.ReadToEnd();

                            LogInfo($"VM script on {ipAddress} - Exit code: {process.ExitCode}");

                            if (!string.IsNullOrEmpty(output))
                                LogInfo($"Output: {output}");

                            if (!string.IsNullOrEmpty(errors))
                                LogError($"Errors: {errors}");
                        }
                        else
                        {
                            LogError($"VM script on {ipAddress} timed out, killing process");
                            try
                            {
                                process.Kill();
                            }
                            catch (Exception killEx)
                            {
                                LogError($"Failed to kill timed out process: {killEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error executing VM close script on {ipAddress}: {ex.Message}");
                }
            });
        }

        #region Utility Methods

        private static void HideConsoleWindow()
        {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
                ShowWindow(handle, 0);
            try { FreeConsole(); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private static void LoadConfiguration()
        {
            string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Intel", "SystemUtilizationMonitor", "SystemUtilizationConfig.json");

            if (!File.Exists(configPath))
                CreateDefaultConfiguration(configPath);

            string jsonContent = File.ReadAllText(configPath);
            appConfig = JsonConvert.DeserializeObject<ConfigurationModel>(jsonContent);
        }

        private static void CreateDefaultConfiguration(string configPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));

            var defaultConfig = new ConfigurationModel
            {
                SumPOR = new SumPORConfig { Debug = false, ShouldReadLogFiles = true },
                Monitoring = new MonitoringConfig { RecordIntervalMinutes = 5 },
                JsonOutputPath = ""
            };

            string jsonContent = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            File.WriteAllText(configPath, jsonContent);
        }

        private static void SetupOutputDirectory()
        {
            outputDirectory = !string.IsNullOrEmpty(appConfig.JsonOutputPath)
                ? Environment.ExpandEnvironmentVariables(appConfig.JsonOutputPath)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor");

            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
        }

        private static void InitializeForCurrentDay()
        {
            currentDay = DateTime.UtcNow.Date;
            currentOutputFile = Path.Combine(outputDirectory, $"SystemUtilizationTimeFrames{currentDay:yyyyMMdd}.json");
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
        }

        private static void InitializeInputHooks()
        {
            try
            {
                inputHook = new InputHookManager(appConfig);
                inputHook?.Start();
                LogInfo("Input monitoring initialized successfully");
            }
            catch (Exception ex)
            {
                LogError("Could not initialize input hooks: " + ex.Message);
            }
        }

        private static void StartFileCleanupTask()
        {
            Task.Factory.StartNew(() =>
            {
                while (!shouldStop)
                {
                    try
                    {
                        Thread.Sleep(TimeSpan.FromHours(1));
                        // Add file cleanup logic here if needed
                    }
                    catch (Exception ex)
                    {
                        LogError("File cleanup error: " + ex.Message);
                    }
                }
            });
        }

        private static UtilizationTimeFrame CollectUtilizationData(DateTime startTime, DateTime endTime)
        {
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = startTime,
                EndTime = endTime,
                MachineName = Environment.MachineName
            };

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
                    LogInfo($"INFO: VM check completed successfully. VMs in use: {vmInUse}");
                }
                else
                {
                    LogInfo("ERROR: VM check timed out after 5 seconds, continuing with file monitoring");
                    vmInUse = false;
                }
            }
            catch (Exception ex)
            {
                LogError($"ERROR: VM monitoring failed with error: {ex.Message}, continuing with file monitoring");
                vmInUse = false;
            }

            // Continue with the logic regardless of VM check results
            if (vmInUse)
            {
                timeFrame.FileChanges = string.Empty;
                timeFrame.FileChanges = "VMs In Use By VMC or hyperV";
                LogInfo("INFO: VMs detected in use, skipping file monitoring");
            }
            else
            {
                timeFrame = MonitoringSUM.MonitoringFiles(timeFrame, appConfig, logInfo);
                LogInfo("INFO: No VMs in use or VM check failed, proceeding with file monitoring");
            }

            return timeFrame;
        }

        private static void ResetCounters()
        {
            inputHook?.ResetCounters();
        }

        private static void WriteToFile(string fileName, UtilizationTimeFrame timeFrame)
        {
            try
            {
                var json = JsonConvert.SerializeObject(timeFrame);
                File.AppendAllText(fileName, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                LogError("Error writing to file: " + ex.Message);
            }
        }

        private static void LogInfo(string message)
        {
            if (appConfig?.SumPOR?.Debug == true)
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] INFO: {message}");

            try
            {
                logInfo += $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine}";
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor", "Monitoring_logs.txt");
                File.AppendAllText(logPath, logInfo);
            }
            catch { }
        }

        private static void LogError(string message)
        {
            if (appConfig?.SumPOR?.Debug == true)
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] ERROR: {message}");

            try
            {
                logInfo += $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR: {message}{Environment.NewLine}";
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor", "Monitoring_logs.txt");
                File.AppendAllText(logPath, logInfo);
            }
            catch { }
        }

        private static void Cleanup()
        {
            inputHook?.Dispose();
            LogInfo("Cleanup completed.");
        }

        #endregion
    }
}