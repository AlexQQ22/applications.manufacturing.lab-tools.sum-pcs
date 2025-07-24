using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Services;
using SystemUtilizationMonitor.Utilities;

namespace SystemUtilizationMonitor
{
    // Main Program class with integrated monitoring
    public class Program
    {
        private static readonly PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        private static readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private static readonly Dictionary<string, uint> basicFileChanges = new Dictionary<string, uint>();
        private static readonly object lockObj = new object();
        private static MonitorConfiguration config;
        private static bool shouldStop = false;
        private static string outputDirectory;
        private static string currentOutputFile;
        private static DateTime currentDay;
        private static ConfigurationModel appConfig;

        // CPU usage tracking
        private static readonly Dictionary<string, int> cpuUsageDistribution = new Dictionary<string, int>();
        private static readonly object cpuLockObj = new object();

        // Input monitoring
        private static InputHookManager inputHook;

        // Activity monitoring service
        private static ActivityMonitoringService activityMonitor;

        // Track if any file has changed in current interval
        private static volatile bool fileChangeDetected = false;
        private static string firstChangedFile = string.Empty;

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

                // Start CPU monitoring background task
                StartCpuMonitoring();

                // Start file cleanup task
                StartFileCleanupTask();

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
                "Intel", "SystemUtilizationMonitor", "SystemUtilizationTimeFrames.json");

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
            currentDay = DateTime.Now.AddHours(6).Date;
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

        private static void StartCpuMonitoring()
        {
            Task.Factory.StartNew(delegate ()
            {
                while (!shouldStop)
                {
                    try
                    {
                        float currentCpu = cpuCounter.NextValue();
                        int cpuBucket = ((int)Math.Round(currentCpu / 5.0)) * 5;
                        string bucketKey = cpuBucket.ToString();

                        lock (cpuLockObj)
                        {
                            if (!cpuUsageDistribution.ContainsKey(bucketKey))
                                cpuUsageDistribution[bucketKey] = 0;
                            cpuUsageDistribution[bucketKey]++;
                        }

                        Thread.Sleep(1000); // Sample every second
                    }
                    catch (Exception ex)
                    {
                        LogError("CPU monitoring error: " + ex.Message);
                        Thread.Sleep(5000); // Wait longer on error
                    }
                }
            });
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

        private static void MonitoringLoop()
        {
            while (!shouldStop)
            {
                var startTime = DateTime.Now.AddHours(6);

                // Check if we need to switch to a new day's file
                if (DateTime.Now.AddHours(6).Date != currentDay)
                {
                    InitializeForCurrentDay();
                    LogInfo($"Switched to new daily file: {currentOutputFile}");
                }

                // Reset counters
                ResetCounters();

                try
                {
                    // Wait for the monitoring interval
                    Thread.Sleep(config.RecordInterval);
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }

                if (shouldStop) break;

                var endTime = DateTime.Now.AddHours(6);
                var actualDuration = endTime - startTime;

                // Collect utilization data
                var timeFrame = CollectUtilizationData(startTime, endTime, actualDuration);

                // Write to file
                LogInfo($"Saved JSON to: {currentOutputFile}");
                WriteToFile(currentOutputFile, timeFrame);

                var avgCpu = GetAverageCpuUsage(timeFrame.CpuUsage);
                var topProcess = timeFrame.TopProcesses.Count > 0 ? timeFrame.TopProcesses.First().Key : "None";

                LogInfo($"[{endTime:HH:mm:ss}] Data collected - CPU: {avgCpu:F1}%, Top Process: {topProcess}, " +
                       $"Mouse: {timeFrame.MouseEvents}, Keyboard: {timeFrame.KeyboardEvents}, " +
                       $"File Changes: {timeFrame.FileChanges.Count}");
            }
        }

        private static UtilizationTimeFrame CollectUtilizationData(DateTime startTime, DateTime endTime, TimeSpan actualDuration)
        {
            var timeFrame = new UtilizationTimeFrame();
            timeFrame.StartTime = startTime;
            timeFrame.EndTime = endTime;
            timeFrame.Duration = actualDuration;
            timeFrame.ExpectedDuration = config.RecordInterval;
            timeFrame.MachineName = Environment.MachineName;
            timeFrame.ProcessorCount = Environment.ProcessorCount;
            timeFrame.TickCount64 = Environment.TickCount;
            timeFrame.UserDomainName = Environment.UserDomainName;
            timeFrame.UserName = Environment.UserName;

            // Get input event counts
            if (inputHook != null)
            {
                timeFrame.MouseEvents = inputHook.GetMouseEventCount();
                timeFrame.KeyboardEvents = inputHook.GetKeyboardEventCount();
            }

            // Collect CPU usage data
            CollectCpuUsage(timeFrame);

            // Collect process data
            CollectProcessData(timeFrame);

            // Collect file changes using both methods
            CollectFileChanges(timeFrame);

            return timeFrame;
        }

        private static void CollectCpuUsage(UtilizationTimeFrame timeFrame)
        {
            lock (cpuLockObj)
            {
                foreach (var kvp in cpuUsageDistribution)
                {
                    timeFrame.CpuUsage[kvp.Key] = kvp.Value;
                }
            }

            // If no data collected, provide default
            if (timeFrame.CpuUsage.Count == 0)
            {
                timeFrame.CpuUsage["0"] = 300; // Default 5 minutes worth of samples at 0% CPU
            }
        }

        private static void CollectProcessData(UtilizationTimeFrame timeFrame)
        {
            try
            {
                var processes = Process.GetProcesses();
                var processData = new List<ProcessInfo>();

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            process.Dispose();
                            continue;
                        }

                        var cpuTime = process.TotalProcessorTime;
                        var processName = GetProcessDisplayName(process);
                        processData.Add(new ProcessInfo { Name = processName, CpuTime = cpuTime });
                    }
                    catch
                    {
                        // Skip processes that can't be accessed
                    }
                    finally
                    {
                        try
                        {
                            process.Dispose();
                        }
                        catch { }
                    }
                }

                // Get top 10 processes by CPU time
                var groupedProcesses = processData
                    .Where(p => p.CpuTime.TotalMilliseconds > 0)
                    .GroupBy(p => p.Name)
                    .Select(g => new ProcessInfo
                    {
                        Name = g.Key,
                        CpuTime = TimeSpan.FromTicks(g.Sum(p => p.CpuTime.Ticks))
                    })
                    .OrderByDescending(p => p.CpuTime)
                    .Take(10)
                    .ToList();

                foreach (var process in groupedProcesses)
                {
                    timeFrame.TopProcesses[process.Name] = FormatTimeSpan(process.CpuTime);
                }
            }
            catch (Exception ex)
            {
                LogError("Could not collect process data: " + ex.Message);
            }
        }

        private class ProcessInfo
        {
            public string Name { get; set; }
            public TimeSpan CpuTime { get; set; }
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

            // Use activity monitoring service to get intelligent file analysis
            if (activityMonitor != null)
            {
                try
                {
                    var activityFileChanges = activityMonitor.AnalyzeSystemActivity();

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

        private static void InitializeFileWatchers()
        {
            foreach (var directoryWatch in config.DirectoriesToWatch)
            {
                if (Directory.Exists(directoryWatch.Path))
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(directoryWatch.Path, directoryWatch.Filter);
                        watcher.IncludeSubdirectories = false;
                        watcher.InternalBufferSize = 524288;
                        watcher.EnableRaisingEvents = true;

                        watcher.Created += OnFileSystemEvent;
                        watcher.Changed += OnFileSystemEvent;
                        watcher.Deleted += OnFileSystemEvent;
                        watcher.Renamed += OnFileSystemEvent;
                        watcher.Error += OnFileSystemError;

                        watchers.Add(watcher);
                        LogInfo("Watching directory: " + directoryWatch.Path);
                    }
                    catch (Exception ex)
                    {
                        LogError("Could not watch directory " + directoryWatch.Path + ": " + ex.Message);
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
            lock (cpuLockObj)
            {
                cpuUsageDistribution.Clear();
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

        private static double GetAverageCpuUsage(Dictionary<string, int> cpuUsage)
        {
            if (cpuUsage.Count == 0) return 0;

            double totalWeightedUsage = 0;
            int totalSamples = 0;

            foreach (var kvp in cpuUsage)
            {
                int cpuPercent;
                if (int.TryParse(kvp.Key, out cpuPercent))
                {
                    totalWeightedUsage += cpuPercent * kvp.Value;
                    totalSamples += kvp.Value;
                }
            }

            return totalSamples > 0 ? totalWeightedUsage / totalSamples : 0;
        }

        private static void LogInfo(string message)
        {
            // Log to console if debug mode is enabled
            if (appConfig?.SumPOR?.Debug == true)
            {
                Console.WriteLine($"[{DateTime.Now.AddHours(6):HH:mm:ss}] INFO: {message}");
            }

            // Always log to file
            try
            {
                string logFile = Path.Combine(outputDirectory, "SystemUtilizationMonitor.log");
                File.AppendAllText(logFile, $"[{DateTime.Now.AddHours(6):yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine}");
            }
            catch { }
        }

        private static void LogError(string message)
        {
            // Log to console if debug mode is enabled
            if (appConfig?.SumPOR?.Debug == true)
            {
                Console.WriteLine($"[{DateTime.Now.AddHours(6):HH:mm:ss}] ERROR: {message}");
            }

            // Always log to file
            try
            {
                string logFile = Path.Combine(outputDirectory, "SystemUtilizationMonitor.log");
                File.AppendAllText(logFile, $"[{DateTime.Now.AddHours(6):yyyy-MM-dd HH:mm:ss}] ERROR: {message}{Environment.NewLine}");
            }
            catch { }
        }

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

            try
            {
                if (cpuCounter != null)
                    cpuCounter.Dispose();
            }
            catch { }

            LogInfo("Cleanup completed.");
        }
    }
}