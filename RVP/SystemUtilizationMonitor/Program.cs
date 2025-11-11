using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Services.Description;
using System.Windows.Forms;
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
                    Console.WriteLine($"Monitoring Configuration: RecordInterval={appConfig.Monitoring.RecordIntervalMinutes} minutes");
                    Console.WriteLine("Press Ctrl+C to stop...");
                    Console.WriteLine("==========================================");
                }

                SetupOutputDirectory();

                InitializeForCurrentDay();

                SetupMonitoringConfiguration();

                SetupCancellation();

                InitializeInputHooks();

                // StartFileCleanupTask();

                MonitoringLoop();
            }
            catch (Exception ex)
            {
                LogError("Main execution error: " + ex.Message + "\nStack trace: " + ex.StackTrace);
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

            if (!File.Exists(configPath))
            {

                CreateDefaultConfiguration(configPath);
            }

            string jsonContent = File.ReadAllText(configPath);
            appConfig = JsonConvert.DeserializeObject<ConfigurationModel>(jsonContent);
        }

        private static void CreateDefaultConfiguration(string configPath)
        {
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));

            var defaultConfig = new ConfigurationModel
            {
                Jose = new Dictionary<string, MonitorTxtConfig>(),
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
            // Try to use the specified network path if configured
            string networkPath = @"\\amr.corp.intel.com\ec\proj\mdl\cr\intel\hdmx_db\mae\SUM\RVP";
            bool useNetworkPath = false;

            try
            {
                // Test if we can write to the network path
                if (Directory.Exists(networkPath))
                {
                    // Try to create a test file to verify write permissions
                    string testFile = Path.Combine(networkPath, $"_test_{Guid.NewGuid()}.tmp");
                    try
                    {
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                        useNetworkPath = true;
                        LogInfo($"Network path is accessible with write permissions: {networkPath}");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        LogError($"No write permissions to network path: {networkPath}");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Cannot write to network path: {ex.Message}");
                    }
                }
                else
                {
                    LogError($"Network path does not exist: {networkPath}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error accessing network path: {ex.Message}");
            }

            if (useNetworkPath)
            {
                outputDirectory = networkPath;
            }
            else
            {
                // Fallback to local directory
                outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor");
                LogInfo($"Using local output directory: {outputDirectory}");

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }
            }

            LogInfo($"Final output directory set to: {outputDirectory}");
        }

        private static void InitializeForCurrentDay()
        {
            currentDay = DateTime.UtcNow.Date;
            string machineName = Environment.MachineName;
            currentOutputFile = Path.Combine(outputDirectory,
                $"SystemUtilizationTimeFrames{currentDay:yyyyMMdd}_{machineName}.json");
        }

        private static void SetupMonitoringConfiguration()
        {
            config = new MonitorConfiguration();

            // Use the configuration value instead of hardcoded value
            config.RecordInterval = TimeSpan.FromMinutes(appConfig.Monitoring.RecordIntervalMinutes);
            //config.RecordInterval = TimeSpan.FromSeconds(10);
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
                LogInfo("Input monitoring initialized successfully");
            }
            catch (Exception ex)
            {
                LogError("Could not initialize input hooks: " + ex.Message);
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
                        LogError("File cleanup error: " + ex.Message);
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
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                var filesToDelete = files.Skip(appConfig.SumPOR.Args.RetainedFileCountLimit).ToList();

                foreach (var file in filesToDelete)
                {
                    try
                    {
                        file.Delete();
                        LogInfo($"Deleted old file: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Could not delete file {file.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Error during file cleanup: " + ex.Message);
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

                LogInfo($"Saved JSON to: {currentOutputFile} \n" +
                        $"                                           with startTime: {startTime.ToString("yyyy-MM-ddTHH:mm:ssZ")}");

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
            timeFrame.Product = ""; // Empty by default

            // Set PCName to hostname and Cell to 'A101'
            timeFrame.PCName = Environment.MachineName;
            timeFrame.Cell = "A101";

            if (inputHook != null)
            {
                timeFrame.MouseEvents = inputHook.GetMouseEventCount();
                timeFrame.KeyboardEvents = inputHook.GetKeyboardEventCount();
            }

            // Only monitor files if Jose configuration has entries
            if (appConfig.Jose != null && appConfig.Jose.Count > 0)
            {
                timeFrame = MonitoringSUM.MonitoringFiles(timeFrame, appConfig, logInfo);
                LogInfo("File monitoring completed based on Jose configuration");
            }
            else
            {
                timeFrame.FileChanges = string.Empty;
                LogInfo("No Jose configuration found, skipping file monitoring");
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
            const int maxRetries = 3;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    var json = CustomJsonSerializer.Serialize(timeFrame);
                    File.AppendAllText(fileName, json + Environment.NewLine);

                    if (retryCount > 0)
                    {
                        LogInfo($"Successfully wrote to file after {retryCount} retry(ies)");
                    }
                    return; // Success, exit the method
                }
                catch (UnauthorizedAccessException ex)
                {
                    LogError($"Access denied writing to file (attempt {retryCount + 1}/{maxRetries}): {ex.Message}");

                    // If we've exhausted retries on the current path, try local fallback
                    if (retryCount == maxRetries - 1 && !fileName.Contains(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)))
                    {
                        string localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Intel", "SystemUtilizationMonitor");

                        if (!Directory.Exists(localDir))
                        {
                            Directory.CreateDirectory(localDir);
                        }

                        string localFileName = Path.Combine(localDir, Path.GetFileName(fileName));
                        LogInfo($"Attempting to write to local fallback location: {localFileName}");

                        try
                        {
                            var json = CustomJsonSerializer.Serialize(timeFrame);
                            File.AppendAllText(localFileName, json + Environment.NewLine);

                            // Update the current output file to the local path for future writes
                            currentOutputFile = localFileName;
                            outputDirectory = localDir;

                            LogInfo($"Successfully wrote to local fallback location");
                            return;
                        }
                        catch (Exception fallbackEx)
                        {
                            LogError($"Failed to write to local fallback: {fallbackEx.Message}");
                        }
                    }

                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        Thread.Sleep(1000 * retryCount); // Exponential backoff: 1s, 2s, 3s
                    }
                }
                catch (IOException ex)
                {
                    LogError($"IO error writing to file (attempt {retryCount + 1}/{maxRetries}): {ex.Message}");
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        Thread.Sleep(1000 * retryCount);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error writing to file (attempt {retryCount + 1}/{maxRetries}): {ex.Message}");
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        Thread.Sleep(1000 * retryCount);
                    }
                }
            }

            LogError($"Failed to write to file after {maxRetries} attempts. Data will be lost for this time frame.");
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
                        LogInfo($"Cleaned up temp directory: {tempPath}");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Could not clean up temp directory {tempPath}: {ex.Message}");
                }
            }

            LogInfo("Cleanup completed.");
        }
    }
}