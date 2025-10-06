using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Services;
using SystemUtilizationMonitor.Utilities;

namespace SystemUtilizationMonitor
{
    [ExcludeFromCodeCoverage]
    public class Program
    {
        #region Private Variables
        private static MonitorConfiguration config;
        private static bool shouldStop = false;
        private static string outputDirectory;
        private static string currentOutputFile;
        private static DateTime currentDay;
        private static ConfigurationModel appConfig;
        private static InputHookManager inputHook;
        private static DateTime lastEndTime = DateTime.UtcNow;
        #endregion

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
                    Console.WriteLine("=== Input Monitor Debug Mode ===");
                    Console.WriteLine("Press Ctrl+C to stop...");
                }

                SetupOutputDirectory();
                InitializeForCurrentDay();
                SetupMonitoringConfiguration();
                SetupCancellation();
                InitializeInputHooks();

                MonitoringLoop();
            }
            catch (Exception ex)
            {
                LogError("Error in main execution: " + ex.Message);
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

                if (DateTime.UtcNow.Date != currentDay)
                {
                    InitializeForCurrentDay();
                    LogInfo($"Switched to new daily file: {currentOutputFile}");
                }

                var endTime = startTime.Add(config.RecordInterval);
                
                try
                {
                    Thread.Sleep(config.RecordInterval);
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }

                if (shouldStop) break;

                var timeFrame = CollectInputData(startTime, endTime);
                lastEndTime = endTime;
                
                ResetCounters();
                WriteToFile(currentOutputFile, timeFrame);
            }
        }

        private static UtilizationTimeFrame CollectInputData(DateTime startTime, DateTime endTime)
        {
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = startTime,
                EndTime = endTime,
                MachineName = Environment.MachineName,
                Product = "INPUT_MONITOR"
            };

            if (inputHook != null)
            {
                timeFrame.MouseEvents = inputHook.GetMouseEventCount();
                timeFrame.KeyboardEvents = inputHook.GetKeyboardEventCount();
            }

            return timeFrame;
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
                SumPOR = new SumPORConfig
                {
                    Debug = false
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
            currentOutputFile = Path.Combine(outputDirectory, $"InputEvents_{currentDay:yyyyMMdd}.json");
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
                LogError("Failed to initialize input hooks: " + ex.Message);
            }
        }

        private static void ResetCounters()
        {
            inputHook?.ResetCounters();
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
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] INFO: {message}");

            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor", "InputMonitor_logs.txt");
                string logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine}";
                File.AppendAllText(logPath, logEntry);
            }
            catch { }
        }

        private static void LogError(string message)
        {
            if (appConfig?.SumPOR?.Debug == true)
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] ERROR: {message}");

            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor", "InputMonitor_logs.txt");
                string logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR: {message}{Environment.NewLine}";
                File.AppendAllText(logPath, logEntry);
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