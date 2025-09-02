using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Services;
using SystemUtilizationMonitor.Utilities;

namespace SystemUtilizationMonitor
{
    // Main Program class with integrated monitoring
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

        // Input monitoring
        private static InputHookManager inputHook;

        // Activity monitoring service
        private static ActivityMonitoringService activityMonitor;

        // Track if any file has changed in current interval
        private static volatile bool fileChangeDetected = false;
        private static string firstChangedFile = string.Empty;

        // PsExec execution tracking
        private static DateTime lastPsExecRun = DateTime.MinValue;

        // Time continuity tracking - NEW
        private static DateTime lastEndTime = DateTime.UtcNow;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                // Load configuration from JSON first to check debug mode
                LoadConfiguration();

                // Hide console window only if debug mode is disabled
                if (!appConfig.SumPOR.Debug)
                {
                    HideConsoleWindow();
                }
                else
                {
                    // Show console and display debug information
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
                    Console.WriteLine("Press Ctrl+C to stop...");
                    Console.WriteLine("==========================================");
                }

                // Setup output directory
                SetupOutputDirectory();

                // Initialize for current day
                InitializeForCurrentDay();

                // Setup configuration for monitoring
                SetupMonitoringConfiguration();

                // Setup cancellation for graceful shutdown
                SetupCancellation();

                // Initialize services if file monitoring is enabled
                if (appConfig.SumPOR.ShouldReadLogFiles)
                {
                    // Initialize activity monitoring service
                    activityMonitor = new ActivityMonitoringService(appConfig);

                    // Initialize file watchers
                    InitializeFileWatchers();
                }

                // Initialize input hooks
                InitializeInputHooks();

                // Start file cleanup task
                StartFileCleanupTask();

                // Start PsExec execution task
                // StartPsExecTask();

                // Main monitoring loop
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

        // NEW METHOD: Load the last end time from existing file for continuity
        private static void StartPsExecTask()
        {
            Task.Factory.StartNew(async delegate ()
            {
                // Run immediately on startup
                await ExecutePsExecCommand();
                lastPsExecRun = DateTime.Now;

                while (!shouldStop)
                {
                    try
                    {
                        // Wait for 5 minutes (same interval as monitoring)
                        await Task.Delay(TimeSpan.FromMinutes(5));

                        if (!shouldStop)
                        {
                            await ExecutePsExecCommand();
                            lastPsExecRun = DateTime.Now;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError("PsExec task error: " + ex.Message);
                        // Wait a bit before retrying
                        await Task.Delay(TimeSpan.FromMinutes(1));
                    }
                }
            });
        }

        private static async Task ExecutePsExecCommand()
        {
            try
            {
                LogInfo("Starting PsExec command execution...");

                // Path to PsExec64 executable
                string exePath = @"c:\SUMInstall\PsExec64.exe";

                // Check if PsExec64 exists
                if (!File.Exists(exePath))
                {
                    LogError($"PsExec64.exe not found at: {exePath}");
                    return;
                }

                // Arguments to pass to PsExec
                string arguments = @"-u sysc -p tr@nsf3r cmd /c ""start /wait C:\SUMInstall\VM_Monitoring_Tester.bat""";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process proc = new Process())
                {
                    proc.StartInfo = psi;

                    // Capture output and error streams
                    proc.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            LogInfo($"PsExec Output: {e.Data}");
                        }
                    };

                    proc.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            LogError($"PsExec Error: {e.Data}");
                        }
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    // Wait for the process to complete with a timeout
                    bool completed = proc.WaitForExit(TimeSpan.FromMinutes(2).Milliseconds);

                    if (!completed)
                    {
                        LogError("PsExec command timed out after 2 minutes");
                        try
                        {
                            proc.Kill();
                        }
                        catch (Exception killEx)
                        {
                            LogError($"Failed to kill PsExec process: {killEx.Message}");
                        }
                    }
                    else
                    {
                        LogInfo($"PsExec command completed with exit code: {proc.ExitCode}");

                        if (proc.ExitCode == 0)
                        {
                            LogInfo("PsExec command executed successfully");
                        }
                        else
                        {
                            LogError($"PsExec command failed with exit code: {proc.ExitCode}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Exception during PsExec execution: {ex.Message}");
            }
        }

        private static void HideConsoleWindow()
        {
            // First try to hide the window
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SW_HIDE);
            }

            // Also try to free the console completely
            try
            {
                FreeConsole();
            }
            catch
            {
                // Ignore if console can't be freed
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
                // Create default configuration if it doesn't exist
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
                    ProductLogPath = @"", // NEW: Add the default path here
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
                JsonOutputPath = ""
            };

            string jsonContent = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            File.WriteAllText(configPath, jsonContent);
        }
        private static void SetupOutputDirectory()
        {
            // Use JsonOutputPath from config if specified, otherwise use default
            if (!string.IsNullOrEmpty(appConfig.JsonOutputPath))
            {
                outputDirectory = Environment.ExpandEnvironmentVariables(appConfig.JsonOutputPath);

                // Create the directory if it doesn't exist
                if (!Directory.Exists(outputDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDirectory);
                        LogInfo($"Created output directory: {outputDirectory}");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to create output directory '{outputDirectory}': {ex.Message}");
                        // Fall back to default directory
                        outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Intel", "SystemUtilizationMonitor");
                        LogInfo($"Falling back to default output directory: {outputDirectory}");
                    }
                }
            }
            else
            {
                outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor");
            }

            // Ensure the final output directory exists
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            LogInfo($"Output directory set to: {outputDirectory}");
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
            config.RecordInterval = TimeSpan.FromMinutes(5); // Default 5-minute intervals

            // Add directories to watch if file monitoring is enabled
            if (appConfig.SumPOR.ShouldReadLogFiles)
            {
                foreach (var monitorConfig in appConfig.Jose.Values)
                {
                    if (!string.IsNullOrEmpty(monitorConfig.FilePath))
                    {
                        string directoryPath = Path.GetDirectoryName(monitorConfig.FilePath);
                        if (!string.IsNullOrEmpty(directoryPath) &&
                            !config.DirectoriesToWatch.Any(d => d.Path.Equals(directoryPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            config.DirectoriesToWatch.Add(new DirectoryWatch
                            {
                                Path = directoryPath,
                                Filter = "*.*"
                            });

                            // Create unique temp copy path for this directory
                            string tempCopyPath = Path.Combine(Path.GetTempPath(),
                                $"SUM_Copy_{Path.GetFileName(directoryPath)}_{Guid.NewGuid().ToString("N")[..8]}");

                            if (!Directory.Exists(tempCopyPath))
                            {
                                Directory.CreateDirectory(tempCopyPath);
                            }

                            tempCopyPaths[directoryPath] = tempCopyPath;
                            LogInfo($"Created temp copy directory for {directoryPath}: {tempCopyPath}");
                        }
                    }
                }
            }
        }
        private static void SetupCancellation()
        {
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                shouldStop = true;
            };

            // Handle application exit
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
                        Thread.Sleep(TimeSpan.FromHours(1)); // Check every hour
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
                    .Where(f => !f.Name.Equals("SystemUtilizationTimeFrames.json", StringComparison.OrdinalIgnoreCase)) // Exclude the specific file
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                // Keep only the specified number of files
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



        // NEW METHOD: Non-blocking directory copy
        private static void CopyDirectoryFilesNonBlocking(string sourceDir, string destDir)
        {
            lock (copyLockObj)
            {
                try
                {
                    // Ensure destination directory exists
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Get all files from source directory
                    string[] sourceFiles = Directory.GetFiles(sourceDir);

                    foreach (string sourceFile in sourceFiles)
                    {
                        try
                        {
                            string fileName = Path.GetFileName(sourceFile);
                            string destFile = Path.Combine(destDir, fileName);

                            // Use non-blocking copy with FileShare.ReadWrite to avoid file locking
                            using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                            {
                                sourceStream.CopyTo(destStream);
                            }
                        }
                        catch (IOException ioEx)
                        {
                            // Log but don't fail the entire operation for one file
                            LogError($"Could not copy file {Path.GetFileName(sourceFile)}: {ioEx.Message}");
                        }
                        catch (UnauthorizedAccessException uaEx)
                        {
                            // Log but don't fail the entire operation for one file
                            LogError($"Access denied copying file {Path.GetFileName(sourceFile)}: {uaEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error copying directory {sourceDir} to {destDir}: {ex.Message}");
                }
            }
        }


        // Modified MonitoringLoop method
        private static void MonitoringLoop()
        {
            while (!shouldStop)
            {
                // MODIFIED: Copy files from each watched directory to their respective temp locations
                foreach (var directoryWatch in config.DirectoriesToWatch)
                {
                    if (Directory.Exists(directoryWatch.Path) && tempCopyPaths.ContainsKey(directoryWatch.Path))
                    {
                        try
                        {
                            string sourcePath = directoryWatch.Path;
                            string tempPath = tempCopyPaths[directoryWatch.Path];

                            // Copy all files from source to temp directory without blocking
                            CopyDirectoryFilesNonBlocking(sourcePath, tempPath);
                        }
                        catch (Exception ex)
                        {
                            LogError($"Could not copy files from directory {directoryWatch.Path}: {ex.Message}");
                        }
                    }
                }

                // Use lastEndTime for continuity, or current time if no previous end time
                var startTime = lastEndTime;

                // Check if we need to switch to a new day's file
                if (DateTime.UtcNow.Date != currentDay)
                {
                    InitializeForCurrentDay();
                    LogInfo($"Switched to new daily file: {currentOutputFile}");
                }

                // Calculate endTime based on startTime + interval for precise timing
                var endTime = startTime.Add(config.RecordInterval);

                // Reset counters
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

                var timeFrame = CollectUtilizationData(startTime, endTime);

                // Update lastEndTime to maintain continuity
                lastEndTime = endTime;

                // Write to file
                LogInfo($"Saved JSON to: {currentOutputFile}");
                WriteToFile(currentOutputFile, timeFrame);

                LogInfo($"[{endTime:HH:mm:ss}] Data collected " +
                       $"Mouse: {timeFrame.MouseEvents}, Keyboard: {timeFrame.KeyboardEvents}, " +
                       $"File Changes: {timeFrame.FileChanges.Count}, Product: {timeFrame.Product}, " +
                       $"Last PsExec: {(lastPsExecRun == DateTime.MinValue ? "Never" : lastPsExecRun.ToString("HH:mm:ss"))}, " +
                       $"Timeframe: {startTime:HH:mm:ss.fff} -> {endTime:HH:mm:ss.fff}");
            }
        }
        private static UtilizationTimeFrame CollectUtilizationData(DateTime startTime, DateTime endTime)
        {
            var timeFrame = new UtilizationTimeFrame();
            timeFrame.StartTime = startTime;
            timeFrame.EndTime = endTime;
            timeFrame.MachineName = Environment.MachineName;
            timeFrame.Product = GetProductPartNumber();

            // Get input event counts
            if (inputHook != null)
            {
                timeFrame.MouseEvents = inputHook.GetMouseEventCount();
                timeFrame.KeyboardEvents = inputHook.GetKeyboardEventCount();
            }

            // Collect file changes using both methods
            CollectFileChanges(timeFrame);

            return timeFrame;
        }

        private class ProcessInfo
        {
            public string Name { get; set; }
        }

        private static string GetProcessDisplayName(Process process)
        {
            try
            {
                if (process.MainModule != null &&
                    process.MainModule.FileVersionInfo != null &&
                    !string.IsNullOrEmpty(process.MainModule.FileVersionInfo.ProductName))
                {
                    return process.MainModule.FileVersionInfo.ProductName;
                }
            }
            catch { }

            return process.ProcessName;
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            return timeSpan.ToString(@"hh\:mm\:ss\.fffffff");
        }


        // Modified ActivityMonitoringService usage to work with temp paths
        private static void CollectFileChanges(UtilizationTimeFrame timeFrame)
        {
            // If file monitoring is disabled, skip
            if (!appConfig.SumPOR.ShouldReadLogFiles)
            {
                return;
            }

            // If a file change was detected, only report the first changed file
            if (fileChangeDetected && !string.IsNullOrEmpty(firstChangedFile))
            {
                timeFrame.FileChanges[firstChangedFile] = 1;
                return;
            }

            // Collect basic file changes from watchers
            lock (lockObj)
            {
                foreach (var change in basicFileChanges)
                {
                    timeFrame.FileChanges[change.Key] = change.Value;
                }
            }

            // Use activity monitoring service with temp paths
            if (activityMonitor != null)
            {
                try
                {
                    // Pass the temp copy paths to the activity monitor
                    var activityFileChanges = activityMonitor.AnalyzeSystemActivityFromTempPaths(tempCopyPaths);

                    // If any activity detected, only report the first one
                    if (activityFileChanges.Count > 0)
                    {
                        var firstChange = activityFileChanges.First();
                        timeFrame.FileChanges.Clear();
                        timeFrame.FileChanges[firstChange.Key] = firstChange.Value;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LogError("Activity monitoring failed: " + ex.Message);
                }
            }
        }


        // Modified InitializeFileWatchers to use temp paths
        private static void InitializeFileWatchers()
        {
            foreach (var directoryWatch in config.DirectoriesToWatch)
            {
                if (tempCopyPaths.ContainsKey(directoryWatch.Path))
                {
                    try
                    {
                        string tempPath = tempCopyPaths[directoryWatch.Path];

                        var watcher = new FileSystemWatcher(tempPath, directoryWatch.Filter);
                        watcher.IncludeSubdirectories = false;
                        watcher.InternalBufferSize = 524288;
                        watcher.EnableRaisingEvents = true;

                        watcher.Created += OnFileSystemEvent;
                        watcher.Changed += OnFileSystemEvent;
                        watcher.Deleted += OnFileSystemEvent;
                        watcher.Renamed += OnFileSystemEvent;
                        watcher.Error += OnFileSystemError;

                        watchers.Add(watcher);
                        LogInfo($"Watching temp directory: {tempPath} (source: {directoryWatch.Path})");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Could not watch temp directory for {directoryWatch.Path}: {ex.Message}");
                    }
                }
            }
        }

        private static void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            RecordFileChange(e.FullPath);
        }

        private static void OnFileSystemError(object sender, ErrorEventArgs e)
        {
            LogError("File watcher error: " + e.GetException().Message);
        }

        private static void RecordFileChange(string path)
        {
            lock (lockObj)
            {
                if (!fileChangeDetected)
                {
                    fileChangeDetected = true;
                    firstChangedFile = path;
                }

                if (!basicFileChanges.ContainsKey(path))
                    basicFileChanges[path] = 0;
                basicFileChanges[path]++;
            }
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

                // Append to file (create if doesn't exist)
                File.AppendAllText(fileName, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                LogError("Error writing to file: " + ex.Message);
            }
        }
        private static void LogInfo(string message)
        {
            // Log to console if debug mode is enabled
            if (appConfig?.SumPOR?.Debug == true)
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] INFO: {message}");
            }

            // Always log to file
            try
            {
                string logFile = Path.Combine(outputDirectory, "SystemUtilizationMonitor.log");
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine}");
            }
            catch { }
        }

        private static void LogError(string message)
        {
            // Log to console if debug mode is enabled
            if (appConfig?.SumPOR?.Debug == true)
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] ERROR: {message}");
            }

            // Always log to file
            try
            {
                string logFile = Path.Combine(outputDirectory, "SystemUtilizationMonitor.log");
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR: {message}{Environment.NewLine}");
            }
            catch { }
        }

        // Modified Cleanup method to clean up temp directories
        private static void Cleanup()
        {
            // Stop input hooks
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

            // Clean up temp copy directories
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

        private static string GetProductPartNumber()
        {
            try
            {
                LogInfo("Starting enhanced product detection logic...");

                // Method 1: Check for HDMX machine - D:\HDMT3\HdmtOutputFiles
                string hdmxPath = @"D:\HDMT3\HdmtOutputFiles";
                if (Directory.Exists(hdmxPath))
                {
                    LogInfo("HDMX machine detected - checking TesterHwConfig.xml");
                    string product = GetProductFromHDMX(hdmxPath);
                    if (!string.IsNullOrEmpty(product))
                    {
                        LogInfo($"Successfully retrieved product from HDMX: {product}");
                        return product;
                    }
                    LogInfo("HDMX path exists but no product found, falling back to HST methods");
                }
                else
                {
                    LogInfo("HDMX path not found, assuming HST machine");
                }

                // Method 2: HST method 1 - c:\hst\tpcache\o\D7\{folder1}\{folder2}
                string hstMethod1Product = GetProductFromHSTMethod1();
                if (!string.IsNullOrEmpty(hstMethod1Product))
                {
                    LogInfo($"Successfully retrieved product from HST Method 1: {hstMethod1Product}");
                    return hstMethod1Product;
                }

                // Method 3: HST method 2 - D:\HST\TP_ENG_Loops\TP (most recent ZIP)
                string hstMethod2Product = GetProductFromHSTMethod2();
                if (!string.IsNullOrEmpty(hstMethod2Product))
                {
                    LogInfo($"Successfully retrieved product from HST Method 2: {hstMethod2Product}");
                    return hstMethod2Product;
                }

                LogError("All product detection methods failed");
                return "PRODUCT_NOT_FOUND";
            }
            catch (Exception ex)
            {
                LogError($"Error in GetProductPartNumber: {ex.Message}");
                return "ERROR_GETTING_PRODUCT";
            }
        }
        /// <summary>
        /// Method 1: HDMX - Extract product from TesterHwConfig.xml using SerialNumber regex
        /// </summary>
        private static string GetProductFromHDMX(string hdmxPath)
        {
            try
            {
                string configFilePath = Path.Combine(hdmxPath, "TesterHwConfig.xml");

                if (!File.Exists(configFilePath))
                {
                    LogInfo($"TesterHwConfig.xml not found at: {configFilePath}");
                    return "";
                }

                LogInfo($"Reading TesterHwConfig.xml from: {configFilePath}");

                string xmlContent = File.ReadAllText(configFilePath);

                // Regex to match SerialNumber in lines that contain BoardName="TIU"
                var tiuSerialNumberRegex = new System.Text.RegularExpressions.Regex(
                    @"BoardName=""TIU""[^>]*SerialNumber=""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
                );

                var match = tiuSerialNumberRegex.Match(xmlContent);
                if (match.Success)
                {
                    string serialNumber = match.Groups[1].Value.Trim();

                    LogInfo($"Found TIU SerialNumber in HDMX config: {serialNumber}");
                    return serialNumber;
                }
                else
                {
                    LogInfo("TIU SerialNumber pattern not found in TesterHwConfig.xml");
                    return "";
                }
            }
            catch (Exception ex)
            {
                LogError($"Error reading HDMX config: {ex.Message}");
                return "";
            }
        }


        /// <summary>
        /// Method 2: HST Method 1 - c:\hst\tpcache\o\D7\{folder1}\{folder2}
        /// Returns the name of the second folder
        /// </summary>
        private static string GetProductFromHSTMethod1()
        {
            try
            {
                string hstCachePath = @"c:\hst\tpcache\o\D7";

                if (!Directory.Exists(hstCachePath))
                {
                    LogInfo($"HST cache path not found: {hstCachePath}");
                    return "";
                }

                LogInfo($"Checking HST cache path: {hstCachePath}");

                // Get all directories in D7
                string[] firstLevelDirs = Directory.GetDirectories(hstCachePath);

                if (firstLevelDirs.Length == 0)
                {
                    LogInfo("No directories found in HST cache D7 folder");
                    return "";
                }

                // Take the first (and supposedly only) directory
                string firstDir = firstLevelDirs[0];
                LogInfo($"Found first level directory: {Path.GetFileName(firstDir)}");

                // Look for second level directories
                string[] secondLevelDirs = Directory.GetDirectories(firstDir);

                if (secondLevelDirs.Length == 0)
                {
                    LogInfo("No second level directories found in HST cache");
                    return "";
                }

                // Take the first second-level directory name as the product
                string secondDir = secondLevelDirs[0];
                string productName = Path.GetFileName(secondDir);

                LogInfo($"Found second level directory (product): {productName}");
                return productName;
            }
            catch (Exception ex)
            {
                LogError($"Error in HST Method 1: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Method 3: HST Method 2 - D:\HST\TP_ENG_Loops\TP
        /// Returns the name of the most recently modified ZIP file (without extension)
        /// </summary>
        private static string GetProductFromHSTMethod2()
        {
            try
            {
                string hstLoopsPath = @"D:\HST\TP_ENG_Loops\TP";

                if (!Directory.Exists(hstLoopsPath))
                {
                    LogInfo($"HST loops path not found: {hstLoopsPath}");
                    return "";
                }

                LogInfo($"Checking HST loops path: {hstLoopsPath}");

                // Get all ZIP files in the directory
                string[] zipFiles = Directory.GetFiles(hstLoopsPath, "*.zip");

                if (zipFiles.Length == 0)
                {
                    LogInfo("No ZIP files found in HST loops directory");
                    return "";
                }

                LogInfo($"Found {zipFiles.Length} ZIP files in HST loops directory");

                // Sort by last write time (most recent first)
                Array.Sort(zipFiles, (x, y) => File.GetLastWriteTime(y).CompareTo(File.GetLastWriteTime(x)));

                // Get the most recent ZIP file
                string mostRecentZip = zipFiles[0];
                string productName = Path.GetFileNameWithoutExtension(mostRecentZip);

                LogInfo($"Most recent ZIP file: {Path.GetFileName(mostRecentZip)}");
                LogInfo($"Product name from ZIP: {productName}");

                return productName;
            }
            catch (Exception ex)
            {
                LogError($"Error in HST Method 2: {ex.Message}");
                return "";
            }
        }
    }
}