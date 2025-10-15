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
        //private static DateTime lastVmConnectKill = DateTime.UtcNow;

        private static bool shouldStop = false;
        private static string outputDirectory;
        private static string currentOutputFile;
        private static DateTime currentDay;
        private static ConfigurationModel appConfig;
        private static readonly Dictionary<string, string> tempCopyPaths = new Dictionary<string, string>();
        private static readonly object copyLockObj = new object();
        private static string logInfo;

        private static InputHookManager inputHook;
        private static DPCToolCellWatcher dpcWatcher;
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
                try
                {
                    dpcWatcher = new DPCToolCellWatcher();
                    LogInfo($"DPC Tool Cell Watcher initialized - PCName: {dpcWatcher.PCName}, Cell: {dpcWatcher.CellPosition}");
                }
                catch (Exception ex)
                {
                    LogError($"Failed to initialize DPC Tool Cell Watcher: {ex.Message}");
                    dpcWatcher = null;
                }

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
                Jose = new Dictionary<string, MonitorTxtConfig>
                {
                    ["montior_txt_priority"] = new MonitorTxtConfig
                    {
                        FilePath = "D:\\HDMT3\\logs\\commonhdmt\\hdmtOScommon.json",
                        NoContent = "Alarm;alarm",
                        Skip = "",
                        FormatDate = "yyyy/MM/dd",
                        LastlineContent = ""
                    },
                    ["montior_txt_priority_2"] = new MonitorTxtConfig
                    {
                        FilePath = "D:\\HDMT3\\logs\\commonhdmt\\hdmtOScommon.log",
                        NoContent = "Alarm;alarm",
                        Skip = "",
                        FormatDate = "yyyy/MM/dd",
                        LastlineContent = ""
                    },
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
                        LogInfo($"Created output directory: {outputDirectory}");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to create output directory '{outputDirectory}': {ex.Message}");

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
                    .Where(f => !f.Name.Equals("SystemUtilizationTimeFrames.json", StringComparison.OrdinalIgnoreCase))
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

        //
        //// Add this new method to the Program class:
        //private static void KillVmConnectProcesses()
        //{
        //    try
        //    {
        //        Process[] vmConnectProcesses = Process.GetProcessesByName("vmconnect");

        //        if (vmConnectProcesses.Length > 0)
        //        {
        //            LogInfo($"Found {vmConnectProcesses.Length} vmconnect process(es) to terminate");

        //            foreach (Process process in vmConnectProcesses)
        //            {
        //                try
        //                {
        //                    LogInfo($"Attempting to kill vmconnect process with PID: {process.Id}");
        //                    process.Kill();
        //                    process.WaitForExit(5000); // Wait up to 5 seconds for graceful exit
        //                    LogInfo($"Successfully killed vmconnect process with PID: {process.Id}");
        //                }
        //                catch (Exception ex)
        //                {
        //                    LogError($"Failed to kill vmconnect process with PID: {process.Id}. Error: {ex.Message}");
        //                }
        //                finally
        //                {
        //                    process.Dispose();
        //                }
        //            }
        //        }
        //        else
        //        {
        //            LogInfo("No vmconnect processes found to terminate");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        LogError($"Error while searching for vmconnect processes: {ex.Message}");
        //    }
        //}

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

                // Check if 60 minutes have passed since last vmconnect kill
                //if (DateTime.UtcNow.Subtract(lastVmConnectKill).TotalMinutes >= 60)
                //{
                //    KillVmConnectProcesses();
                //    lastVmConnectKill = DateTime.UtcNow;
                //}


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
            timeFrame.Product = GetProductPartNumber();
            // Add these lines
            if (dpcWatcher != null)
            {
                timeFrame.PCName = dpcWatcher.PCName;
                timeFrame.Cell = dpcWatcher.CellPosition;
            }
            else
            {
                timeFrame.PCName = Environment.MachineName;
                timeFrame.Cell = "UNKNOWN";
            }
            
            if (inputHook != null)
            {
                timeFrame.MouseEvents = inputHook.GetMouseEventCount();
                timeFrame.KeyboardEvents = inputHook.GetKeyboardEventCount();
            }

            bool vmInUse = false;

            try
            {
                string hostName = "localhost";
                int port = 1190;

                // Llamada sincrónica directa con timeout de 5 segundos
                vmInUse = MonitoringVMs.TestNetConnection(hostName, port);
                LogInfo($"VM check completed successfully. VMs in use: {vmInUse}");
            }
            catch (Exception ex)
            {
                LogError($"VM monitoring failed with error: {ex.Message}, continuing with file monitoring");
                vmInUse = false;
            }

            // Continue with the logic regardless of VM check results
            if (vmInUse)
            {
                timeFrame.FileChanges = string.Empty;
                timeFrame.FileChanges = "Tester In use by Redline";
                LogInfo("VMs detected in use, skipping file monitoring");
            }
            else
            {
                timeFrame = MonitoringSUM.MonitoringFiles(timeFrame, appConfig, logInfo);
                LogInfo("No VMs in use or VM check failed, proceeding with file monitoring");
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
                LogError("Error writing to file: " + ex.Message);
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

                string hstMethod1Product = GetProductFromHSTMethod1();
                if (!string.IsNullOrEmpty(hstMethod1Product))
                {
                    LogInfo($"Successfully retrieved product from HST Method 1: {hstMethod1Product}");
                    return hstMethod1Product;
                }

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
        private static string GetProductFromHDMX2(string hdmxPath)
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

                // Nuevo regex para buscar DUTSocketSerialNumber0
                var dutSocketSerialRegex = new System.Text.RegularExpressions.Regex(
                    @"<SupplementalData\s+Name=""DUTSocketSerialNumber0""\s+Value=""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
                );

                var match = dutSocketSerialRegex.Match(xmlContent);
                if (match.Success)
                {
                    string dutSerialNumber = match.Groups[1].Value.Trim();
                    LogInfo($"Found DUTSocketSerialNumber0 in HDMX config: {dutSerialNumber}");
                    return dutSerialNumber;
                }
                else
                {
                    LogInfo("DUTSocketSerialNumber0 pattern not found in TesterHwConfig.xml");
                    return "";
                }
            }
            catch (Exception ex)
            {
                LogError($"Error reading HDMX config: {ex.Message}");
                return "";
            }
        }
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

                var tiuSerialNumberRegex = new System.Text.RegularExpressions.Regex(
                    @"BoardName=""TIUEEPROM1""[^>]*SerialNumber=""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
                );

                var match = tiuSerialNumberRegex.Match(xmlContent);
                if (match.Success) // && match.in(json_config_product_dictionary)
                {
                    string serialNumber = match.Groups[1].Value.Trim();

                    LogInfo($"Found TIU SerialNumber in HDMX config: {serialNumber}");
                    return serialNumber;
                }
                else
                {
                    // try GetProductFromHDMX2, if also fails then:
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

                string[] firstLevelDirs = Directory.GetDirectories(hstCachePath);

                if (firstLevelDirs.Length == 0)
                {
                    LogInfo("No directories found in HST cache D7 folder");
                    return "";
                }

                string firstDir = firstLevelDirs[0];
                LogInfo($"Found first level directory: {Path.GetFileName(firstDir)}");

                string[] secondLevelDirs = Directory.GetDirectories(firstDir);

                if (secondLevelDirs.Length == 0)
                {
                    LogInfo("No second level directories found in HST cache");
                    return "";
                }

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

                string[] zipFiles = Directory.GetFiles(hstLoopsPath, "*.zip");

                if (zipFiles.Length == 0)
                {
                    LogInfo("No ZIP files found in HST loops directory");
                    return "";
                }

                LogInfo($"Found {zipFiles.Length} ZIP files in HST loops directory");

                Array.Sort(zipFiles, (x, y) => File.GetLastWriteTime(y).CompareTo(File.GetLastWriteTime(x)));

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