using System;
using System.IO;
using SystemUtilizationMonitor.Models;

namespace SystemUtilizationMonitor.Utilities
{
    /// <summary>
    /// Testable file system operations wrapper
    /// This allows us to mock file operations in tests
    /// </summary>
    public interface IFileSystemOperations
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        void AppendAllText(string path, string contents);
        string[] ReadAllLines(string path);
        void CreateDirectory(string path);
        void DeleteFile(string path);
        DateTime GetLastWriteTime(string path);
    }

    /// <summary>
    /// Default implementation using real file system
    /// </summary>
    public class FileSystemOperations : IFileSystemOperations
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
        public void AppendAllText(string path, string contents) => File.AppendAllText(path, contents);
        public string[] ReadAllLines(string path) => File.ReadAllLines(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void DeleteFile(string path) => File.Delete(path);
        public DateTime GetLastWriteTime(string path) => File.GetLastWriteTime(path);
    }

    /// <summary>
    /// Testable configuration loader
    /// </summary>
    public class ConfigurationLoader
    {
        private readonly IFileSystemOperations fileSystem;

        public ConfigurationLoader() : this(new FileSystemOperations()) { }

        public ConfigurationLoader(IFileSystemOperations fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        public ConfigurationModel LoadConfiguration(string configPath)
        {
            if (!fileSystem.FileExists(configPath))
            {
                return CreateDefaultConfiguration();
            }

            try
            {
                var jsonContent = fileSystem.ReadAllText(configPath);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<ConfigurationModel>(jsonContent) 
                    ?? CreateDefaultConfiguration();
            }
            catch
            {
                return CreateDefaultConfiguration();
            }
        }

        public ConfigurationModel CreateDefaultConfiguration()
        {
            var config = new ConfigurationModel
            {
                JsonOutputPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor", "TimeFrames"
                ),
                SumPOR = new SumPORConfig
                {
                    Debug = false,
                    ShouldReadLogFiles = true,
                    ProductLogPath = @"C:\Logs"
                },
                Monitoring = new MonitoringConfig
                {
                    RecordIntervalMinutes = 5
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
                VM = new VMConfig()
            };

            return config;
        }

        public void SaveConfiguration(string configPath, ConfigurationModel config)
        {
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory) && !fileSystem.DirectoryExists(directory))
            {
                fileSystem.CreateDirectory(directory);
            }

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
            fileSystem.WriteAllText(configPath, json);
        }
    }

    /// <summary>
    /// Testable time frame recorder
    /// </summary>
    public class TimeFrameRecorder
    {
        private readonly IFileSystemOperations fileSystem;
        private readonly string outputDirectory;

        public TimeFrameRecorder(string outputDir) : this(outputDir, new FileSystemOperations()) { }

        public TimeFrameRecorder(string outputDir, IFileSystemOperations fileSystem)
        {
            this.outputDirectory = outputDir;
            this.fileSystem = fileSystem;
        }

        public void RecordTimeFrame(UtilizationTimeFrame timeFrame)
        {
            if (timeFrame == null)
                throw new ArgumentNullException(nameof(timeFrame));

            if (!fileSystem.DirectoryExists(outputDirectory))
            {
                fileSystem.CreateDirectory(outputDirectory);
            }

            var fileName = GetOutputFileName(timeFrame);
            var json = CustomJsonSerializer.Serialize(timeFrame);
            
            fileSystem.AppendAllText(fileName, json + Environment.NewLine);
        }

        public string GetOutputFileName(UtilizationTimeFrame timeFrame)
        {
            var date = timeFrame.StartTime.ToString("yyyyMMdd");
            var machine = SanitizeFileName(timeFrame.MachineName);
            var product = SanitizeFileName(timeFrame.Product);
            
            return Path.Combine(outputDirectory, 
                $"SystemUtilizationTimeFrames{date}_{machine}_{product}.json");
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Unknown";

            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        public UtilizationTimeFrame[] LoadTimeFrames(string filePath)
        {
            if (!fileSystem.FileExists(filePath))
                return Array.Empty<UtilizationTimeFrame>();

            try
            {
                var lines = fileSystem.ReadAllLines(filePath);
                var timeFrames = new System.Collections.Generic.List<UtilizationTimeFrame>();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var timeFrame = Newtonsoft.Json.JsonConvert.DeserializeObject<UtilizationTimeFrame>(line);
                        if (timeFrame != null)
                            timeFrames.Add(timeFrame);
                    }
                    catch
                    {
                        // Skip invalid lines
                    }
                }

                return timeFrames.ToArray();
            }
            catch
            {
                return Array.Empty<UtilizationTimeFrame>();
            }
        }
    }

    /// <summary>
    /// Product detection helper
    /// </summary>
    public class ProductDetector
    {
        private readonly IFileSystemOperations fileSystem;

        public ProductDetector() : this(new FileSystemOperations()) { }

        public ProductDetector(IFileSystemOperations fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        public string DetectProduct()
        {
            // Try HDMX path
            string hdmxPath = @"D:\HDMT3\HdmtOutputFiles";
            if (fileSystem.DirectoryExists(hdmxPath))
            {
                var product = GetProductFromHDMX(hdmxPath);
                if (!string.IsNullOrEmpty(product))
                    return product;
            }

            // Try HST methods
            var hstProduct = GetProductFromHST();
            if (!string.IsNullOrEmpty(hstProduct))
                return hstProduct;

            return "PRODUCT_UNKNOWN";
        }

        private string GetProductFromHDMX(string hdmxPath)
        {
            try
            {
                string configFile = Path.Combine(hdmxPath, "TesterHwConfig.xml");
                if (fileSystem.FileExists(configFile))
                {
                    var content = fileSystem.ReadAllText(configFile);
                    // Simple pattern matching - can be enhanced
                    if (content.Contains("DUTSocketSerialNumber0"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(
                            content, 
                            @"DUTSocketSerialNumber0""\s+Value=""([^""]+)"""
                        );
                        if (match.Success)
                            return match.Groups[1].Value.Trim();
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private string GetProductFromHST()
        {
            // Placeholder for HST detection logic
            // This would involve checking registry, config files, etc.
            return string.Empty;
        }
    }
}