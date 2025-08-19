using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.IO.Compression;
using Newtonsoft.Json;
using SystemUtilizationMonitor.Models;
using System.Text.RegularExpressions;

namespace SystemUtilizationMonitor.Services
{
    public class ActivityMonitoringService
    {
        private readonly string pathToStorage;
        private readonly string pathToReadCopy;
        private readonly string pathOfMonitoring;
        private readonly ConfigurationModel appConfig;

        // Current machine's cell name
        private readonly string currentCellName;

        // Regex patterns
        public readonly Regex COLLATERAL_ID_REGEX_HST = new Regex(@"TIU assignment \d+ board name and CMMS id \(SAC, ([A-Z0-9]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public readonly Regex CELL_COLLATERAL_REGEX_HST = new Regex(@"([ABCD]\d{3}):\s*TIU assignment \d+ board name and CMMS id \(SAC, ([A-Z0-9]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public readonly Regex COLLATERAL_ID_REGEX = new Regex(@"Updated TIU collateral id to ([A-Z0-9 ]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public readonly Regex CELL_COLLATERAL_REGEX = new Regex(@"(A\d{2}[0-9X]):\s*Updated TIU collateral id to ([A-Z0-9 ]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Retry configuration
        private const int MAX_RETRY_ATTEMPTS = 5;
        private const int RETRY_DELAY_MS = 500;
        private const int MAX_DAYS_BACK = 5;

        // Last known product code cache
        private string lastKnownProductCode = "";

        public ActivityMonitoringService(ConfigurationModel config)
        {
            appConfig = config ?? throw new ArgumentNullException(nameof(config));

            // Detect if this is HST and get appropriate cell name
            bool isHST = !string.IsNullOrEmpty(appConfig.SumPOR.ProductLogPath) &&
                         appConfig.SumPOR.ProductLogPath.ToLower().Contains("hst");

            if (isHST)
            {
                currentCellName = GetCurrentCellNameHST();
                LogMonitoringResults($"HST mode detected. Using HST cell mapping.", null);
            }
            else
            {
                currentCellName = GetCurrentCellName();
                LogMonitoringResults($"Standard mode detected. Using standard cell mapping.", null);
            }

            // Setup paths in LocalAppData
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Intel", "SystemUtilizationMonitor");

            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            pathToStorage = Path.Combine(baseDir, "SUM_DB_Local.json");
            pathToReadCopy = Path.Combine(baseDir, "Monitoring_copy.txt");
            pathOfMonitoring = Path.Combine(baseDir, "SystemUtilizationMonitor.log");

            LogMonitoringResults($"Initialized ActivityMonitoringService for cell: {currentCellName}", null);
        }

        #region IP to Cell Mapping

        /// <summary>
        /// Gets the cell name for the current machine based on its IP using HST mapping
        /// </summary>
        private string GetCurrentCellNameHST()
        {
            string localIP = GetLocalIPv4();

            LogMonitoringResults($"HST Local IP detected: {localIP}", null);

            if (localIP.StartsWith("10.250.0."))
            {
                string lastOctet = localIP.Substring(9);
                if (int.TryParse(lastOctet, out int ipNumber))
                {
                    // Find the HST cell that matches this IP number
                    var cellEntry = HSTCellToIPMap.FirstOrDefault(x => x.Value == ipNumber);
                    if (!string.IsNullOrEmpty(cellEntry.Key))
                    {
                        LogMonitoringResults($"Mapped HST IP {localIP} to cell {cellEntry.Key}", null);
                        return cellEntry.Key;
                    }
                }
            }

            LogMonitoringResults($"Could not map HST IP {localIP} to a known cell. Using fallback.", null);
            return "UNKNOWN";
        }

        private static readonly Dictionary<string, int> HSTCellToIPMap = new Dictionary<string, int>
        {
            // A series: 10.250.0.0 to 10.250.0.5
            {"A101", 0}, {"A201", 1}, {"A301", 2}, {"A401", 3}, {"A501", 4}, {"A601", 5},
            // B series: 10.250.0.6 to 10.250.0.10  
            {"B101", 6}, {"B201", 7}, {"B301", 8}, {"B401", 9}, {"B501", 10}, {"B601", 11},
            // C series: 10.250.0.12 to 10.250.0.17
            {"C101", 12}, {"C201", 13}, {"C301", 14}, {"C401", 15}, {"C501", 16}, {"C601", 17},
            // D series: 10.250.0.18 to 10.250.0.23
            {"D101", 18}, {"D201", 19}, {"D301", 20}, {"D401", 21}, {"D501", 22}, {"D601", 23}
        };

        /// <summary>
        /// Maps IP addresses to cell names: A101 (10.250.0.1) to A502 (10.250.0.10)
        /// </summary>
        private static readonly Dictionary<string, int> CellToIPMap = new Dictionary<string, int>
        {
            {"A101", 1}, {"A102", 2}, {"A201", 3}, {"A202", 4}, {"A301", 5},
            {"A302", 6}, {"A401", 7}, {"A402", 8}, {"A501", 9}, {"A502", 10}
        };

        /// <summary>
        /// Gets the local IPv4 address
        /// </summary>
        private string GetLocalIPv4()
        {
            try
            {
                // Method 1: Try to get the IP that can reach external networks
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    if (endPoint != null)
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch
            {
                // Method 2: Fallback to network interfaces
                try
                {
                    var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                   ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                        .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                                     !IPAddress.IsLoopback(addr.Address))
                        .Select(addr => addr.Address.ToString())
                        .FirstOrDefault(ip => ip.StartsWith("10.250.0."));

                    if (!string.IsNullOrEmpty(networkInterfaces))
                        return networkInterfaces;

                    // If no 10.250.0.x found, return any valid local IP
                    return NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                   ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                        .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                                     !IPAddress.IsLoopback(addr.Address))
                        .Select(addr => addr.Address.ToString())
                        .FirstOrDefault();
                }
                catch (Exception ex)
                {
                    LogMonitoringResults(null, $"Error getting local IP: {ex.Message}");
                }
            }

            return "Unknown";
        }

        /// <summary>
        /// Gets the cell name for the current machine based on its IP
        /// </summary>
        private string GetCurrentCellName()
        {
            string localIP = GetLocalIPv4();

            LogMonitoringResults($"Local IP detected: {localIP}", null);

            if (localIP.StartsWith("10.250.0."))
            {
                string lastOctet = localIP.Substring(9);
                if (int.TryParse(lastOctet, out int ipNumber))
                {
                    // Find the cell that matches this IP number
                    var cellEntry = CellToIPMap.FirstOrDefault(x => x.Value == ipNumber);
                    if (!string.IsNullOrEmpty(cellEntry.Key))
                    {
                        LogMonitoringResults($"Mapped IP {localIP} to cell {cellEntry.Key}", null);
                        return cellEntry.Key;
                    }
                }
            }

            LogMonitoringResults($"Could not map IP {localIP} to a known cell. Using fallback.", null);
            return "UNKNOWN";
        }

        #endregion

        #region File Reading Methods

        private string ReadFileWithRetry(string filePath)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fileStream))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (IOException ex) when (IsFileLockedException(ex))
                {
                    if (attempt == MAX_RETRY_ATTEMPTS)
                    {
                        throw new IOException($"Failed to read file after {MAX_RETRY_ATTEMPTS} attempts: {ex.Message}", ex);
                    }
                    Thread.Sleep(RETRY_DELAY_MS * attempt);
                }
            }
            return null;
        }

        private async Task<string> ReadFileWithRetryAsync(string filePath)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fileStream))
                    {
                        return await reader.ReadToEndAsync();
                    }
                }
                catch (IOException ex) when (IsFileLockedException(ex))
                {
                    if (attempt == MAX_RETRY_ATTEMPTS)
                    {
                        throw new IOException($"Failed to read file after {MAX_RETRY_ATTEMPTS} attempts: {ex.Message}", ex);
                    }
                    await Task.Delay(RETRY_DELAY_MS * attempt);
                }
            }
            return null;
        }

        private void CopyFileWithRetry(string sourceFile, string destFile)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    using (var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var dest = new FileStream(destFile, FileMode.Create, FileAccess.Write))
                    {
                        source.CopyTo(dest);
                        return;
                    }
                }
                catch (IOException ex) when (IsFileLockedException(ex))
                {
                    if (attempt == MAX_RETRY_ATTEMPTS)
                        throw;

                    Thread.Sleep(RETRY_DELAY_MS * attempt);
                }
            }
        }

        private bool IsFileLockedException(IOException ex)
        {
            return ex.HResult == -2147024864 ||
                   ex.Message.Contains("being used by another process") ||
                   ex.Message.Contains("cannot access the file");
        }

        #endregion

        #region Enhanced HST Product Code Search

        /// <summary>
        /// Enhanced HST product code search with comprehensive historical search
        /// </summary>
        public string GetProductCodeFromDiagnosticLogHST()
        {
            try
            {
                string product_code_fetching_log = $"Enhanced HST search: Searching for product codes for cell: {currentCellName}\n";
                DateTime now = DateTime.Now;

                string diagnosticLogPath = appConfig.SumPOR.ProductLogPath;

                if (string.IsNullOrEmpty(diagnosticLogPath))
                {
                    product_code_fetching_log += "Diagnostic log path not configured in ProductLogPath\n";
                    LogMonitoringResults(product_code_fetching_log, null);
                    return GetLastKnownProductCode();
                }

                if (!Directory.Exists(diagnosticLogPath))
                {
                    product_code_fetching_log += $"Diagnostic log directory does not exist: {diagnosticLogPath}\n";
                    LogMonitoringResults(product_code_fetching_log, null);
                    return GetLastKnownProductCode();
                }

                // Step 1: Search current day hours (current hour backwards)
                string foundProductCode = SearchCurrentDayHours(diagnosticLogPath, now, ref product_code_fetching_log);
                if (!string.IsNullOrEmpty(foundProductCode))
                {
                    lastKnownProductCode = foundProductCode;
                    product_code_fetching_log += $"Found HST ProductCode in current day: {foundProductCode}";
                    LogMonitoringResults(product_code_fetching_log, null);
                    return foundProductCode;
                }

                // Step 2: Search previous days (up to MAX_DAYS_BACK)
                foundProductCode = SearchPreviousDays(diagnosticLogPath, now, ref product_code_fetching_log);
                if (!string.IsNullOrEmpty(foundProductCode))
                {
                    lastKnownProductCode = foundProductCode;
                    product_code_fetching_log += $"Found HST ProductCode in previous days: {foundProductCode}";
                    LogMonitoringResults(product_code_fetching_log, null);
                    return foundProductCode;
                }

                // Step 3: Search zipped files
                foundProductCode = SearchZippedFiles(diagnosticLogPath, now, ref product_code_fetching_log);
                if (!string.IsNullOrEmpty(foundProductCode))
                {
                    lastKnownProductCode = foundProductCode;
                    product_code_fetching_log += $"Found HST ProductCode in zipped files: {foundProductCode}";
                    LogMonitoringResults(product_code_fetching_log, null);
                    return foundProductCode;
                }

                product_code_fetching_log += $"Comprehensive HST search completed. No CollateralId found for cell {currentCellName}. Using last known: {lastKnownProductCode}";
                LogMonitoringResults(product_code_fetching_log, null);
                return GetLastKnownProductCode();
            }
            catch (Exception ex)
            {
                LogMonitoringResults(null, $"Error in enhanced HST diagnostic log search: {ex.Message}");
                return GetLastKnownProductCode();
            }
        }

        /// <summary>
        /// Search current day hours backwards from current time
        /// </summary>
        private string SearchCurrentDayHours(string logDirectory, DateTime currentTime, ref string searchLog)
        {
            searchLog += "=== Searching Current Day Hours ===\n";

            // Search backwards from current hour to 00:00
            for (int hour = currentTime.Hour; hour >= 0; hour--)
            {
                DateTime searchTime = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, hour, 0, 0);

                // Search for both .log and .txt files
                string[] logPatterns = {
                    $"{searchTime:yyyy-MM-ddTHH}-*-*.log",
                    $"{searchTime:yyyy-MM-ddTHH}-*-*.txt"
                };

                searchLog += $"Searching for patterns: {searchTime:yyyy-MM-ddTHH}-*-*.log and {searchTime:yyyy-MM-ddTHH}-*-*.txt\n";

                try
                {
                    List<string> matchingFiles = new List<string>();

                    foreach (string pattern in logPatterns)
                    {
                        string[] files = Directory.GetFiles(logDirectory, pattern);
                        matchingFiles.AddRange(files);
                    }

                    if (matchingFiles.Count > 0)
                    {
                        searchLog += $"Found {matchingFiles.Count} files for hour {hour:00}:00\n";

                        // Sort files by name (newest first)
                        matchingFiles.Sort(StringComparer.OrdinalIgnoreCase);
                        matchingFiles.Reverse();

                        foreach (string logFile in matchingFiles)
                        {
                            string productCode = SearchLogFileForProductCode(logFile, ref searchLog);
                            if (!string.IsNullOrEmpty(productCode))
                            {
                                searchLog += $"SUCCESS: Found product code in {Path.GetFileName(logFile)}\n";
                                return productCode;
                            }
                        }
                    }
                    else
                    {
                        searchLog += $"No files found for hour {hour:00}:00\n";
                    }
                }
                catch (Exception ex)
                {
                    searchLog += $"Error searching hour {hour:00}:00: {ex.Message}\n";
                }
            }

            searchLog += "=== Current Day Search Complete - No Results ===\n";
            return "";
        }

        /// <summary>
        /// Search previous days (all hours) up to MAX_DAYS_BACK
        /// </summary>
        private string SearchPreviousDays(string logDirectory, DateTime currentTime, ref string searchLog)
        {
            searchLog += "=== Searching Previous Days ===\n";

            for (int daysBack = 1; daysBack <= MAX_DAYS_BACK; daysBack++)
            {
                DateTime searchDate = currentTime.AddDays(-daysBack);
                searchLog += $"Searching day {daysBack} back: {searchDate:yyyy-MM-dd}\n";

                // Search all hours of the day (23 down to 0)
                for (int hour = 23; hour >= 0; hour--)
                {
                    DateTime searchTime = new DateTime(searchDate.Year, searchDate.Month, searchDate.Day, hour, 0, 0);

                    // Search for both .log and .txt files
                    string[] logPatterns = {
                        $"{searchTime:yyyy-MM-ddTHH}-*-*.log",
                        $"{searchTime:yyyy-MM-ddTHH}-*-*.txt"
                    };

                    try
                    {
                        List<string> matchingFiles = new List<string>();

                        foreach (string pattern in logPatterns)
                        {
                            string[] files = Directory.GetFiles(logDirectory, pattern);
                            matchingFiles.AddRange(files);
                        }

                        if (matchingFiles.Count > 0)
                        {
                            // Sort files by name (newest first)
                            matchingFiles.Sort(StringComparer.OrdinalIgnoreCase);
                            matchingFiles.Reverse();

                            foreach (string logFile in matchingFiles)
                            {
                                string productCode = SearchLogFileForProductCode(logFile, ref searchLog);
                                if (!string.IsNullOrEmpty(productCode))
                                {
                                    searchLog += $"SUCCESS: Found product code in previous day file {Path.GetFileName(logFile)}\n";
                                    return productCode;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Don't log every hour's errors to keep log manageable
                        if (hour == 12) // Log error once per day at noon check
                        {
                            searchLog += $"Error searching {searchDate:yyyy-MM-dd}: {ex.Message}\n";
                        }
                    }
                }

                searchLog += $"Completed search for {searchDate:yyyy-MM-dd} - No results\n";
            }

            searchLog += "=== Previous Days Search Complete - No Results ===\n";
            return "";
        }

        /// <summary>
        /// Search for zipped log files in the directory
        /// </summary>
        private string SearchZippedFiles(string logDirectory, DateTime currentTime, ref string searchLog)
        {
            searchLog += "=== Searching Zipped Files ===\n";

            try
            {
                // Look for common zip file patterns
                string[] zipPatterns = { "*.zip", "*.7z", "*.rar", "*.gz" };

                foreach (string pattern in zipPatterns)
                {
                    string[] zipFiles = Directory.GetFiles(logDirectory, pattern);

                    if (zipFiles.Length > 0)
                    {
                        searchLog += $"Found {zipFiles.Length} {pattern} files\n";

                        // Sort by modification date (newest first)
                        Array.Sort(zipFiles, (x, y) => File.GetLastWriteTime(y).CompareTo(File.GetLastWriteTime(x)));

                        foreach (string zipFile in zipFiles)
                        {
                            searchLog += $"Examining zip file: {Path.GetFileName(zipFile)}\n";

                            string productCode = SearchZipFileForProductCode(zipFile, ref searchLog);
                            if (!string.IsNullOrEmpty(productCode))
                            {
                                searchLog += $"SUCCESS: Found product code in zip file {Path.GetFileName(zipFile)}\n";
                                return productCode;
                            }
                        }
                    }
                }

                searchLog += "No zip files found or no product codes in zip files\n";
            }
            catch (Exception ex)
            {
                searchLog += $"Error searching zip files: {ex.Message}\n";
            }

            searchLog += "=== Zipped Files Search Complete - No Results ===\n";
            return "";
        }

        /// <summary>
        /// Search a single log file for the product code
        /// </summary>
        private string SearchLogFileForProductCode(string logFile, ref string searchLog)
        {
            try
            {
                string content = ReadFileWithRetry(logFile);
                if (!string.IsNullOrEmpty(content))
                {
                    return ExtractProductCodeForCurrentCellHST(content);
                }
            }
            catch (Exception ex)
            {
                searchLog += $"Error reading {Path.GetFileName(logFile)}: {ex.Message}\n";
            }

            return "";
        }

        /// <summary>
        /// Search inside a zip file for log files containing product codes
        /// Enhanced to handle multiple log files and prioritize by date/time
        /// </summary>
        private string SearchZipFileForProductCode(string zipFile, ref string searchLog)
        {
            string tempDir = null;

            try
            {
                searchLog += $"Processing ZIP file: {Path.GetFileName(zipFile)}\n";

                // Create temporary directory for extraction
                tempDir = Path.Combine(Path.GetTempPath(), $"HST_LogSearch_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                // Extract zip file
                if (zipFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(zipFile, tempDir);
                }
                else
                {
                    searchLog += $"Unsupported archive format: {Path.GetFileName(zipFile)}\n";
                    return "";
                }

                // Search all extracted .log and .txt files
                string[] extractedLogFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
                    .Where(file => file.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                                   file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                searchLog += $"Extracted {extractedLogFiles.Length} log/txt files from {Path.GetFileName(zipFile)}\n";

                if (extractedLogFiles.Length == 0)
                {
                    searchLog += "No .log or .txt files found in ZIP archive\n";
                    return "";
                }

                // Filter and sort HST log files by date/time (most recent first)
                var hstLogFiles = extractedLogFiles
                    .Where(file =>
                    {
                        string fileName = Path.GetFileName(file);
                        return Regex.IsMatch(fileName, @"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-.*\.(log|txt)", RegexOptions.IgnoreCase);
                    })
                    .Select(file => new
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        ParsedDateTime = ParseLogFileDateTime(Path.GetFileName(file))
                    })
                    .Where(x => x.ParsedDateTime.HasValue)
                    .OrderByDescending(x => x.ParsedDateTime.Value) // Most recent first
                    .ToArray();

                searchLog += $"Found {hstLogFiles.Length} HST log/txt files in ZIP (sorted by date, newest first):\n";
                foreach (var logInfo in hstLogFiles.Take(10)) // Show first 10 for logging
                {
                    searchLog += $"  - {logInfo.FileName} ({logInfo.ParsedDateTime.Value:yyyy-MM-dd HH:mm})\n";
                }

                if (hstLogFiles.Length > 10)
                {
                    searchLog += $"  ... and {hstLogFiles.Length - 10} more files\n";
                }

                // Search through each HST log file (starting with most recent)
                foreach (var logInfo in hstLogFiles)
                {
                    try
                    {
                        searchLog += $"Searching {logInfo.FileName}... ";

                        string productCode = SearchLogFileForProductCode(logInfo.FilePath, ref searchLog);
                        if (!string.IsNullOrEmpty(productCode))
                        {
                            searchLog += $"SUCCESS! Found product code: {productCode}\n";
                            return productCode;
                        }
                        else
                        {
                            searchLog += "no match\n";
                        }
                    }
                    catch (Exception ex)
                    {
                        searchLog += $"error: {ex.Message}\n";
                    }
                }

                // Also check for any other .log/.txt files that might not match the strict pattern
                var otherLogFiles = extractedLogFiles
                    .Where(file =>
                    {
                        string fileName = Path.GetFileName(file);
                        return !Regex.IsMatch(fileName, @"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-.*\.(log|txt)", RegexOptions.IgnoreCase);
                    })
                    .OrderByDescending(file => new FileInfo(file).LastWriteTime)
                    .ToArray();

                if (otherLogFiles.Length > 0)
                {
                    searchLog += $"Also checking {otherLogFiles.Length} other .log/.txt files that don't match HST pattern:\n";

                    foreach (string logFile in otherLogFiles.Take(5)) // Limit to first 5 other files
                    {
                        try
                        {
                            string fileName = Path.GetFileName(logFile);
                            searchLog += $"Searching {fileName}... ";

                            string productCode = SearchLogFileForProductCode(logFile, ref searchLog);
                            if (!string.IsNullOrEmpty(productCode))
                            {
                                searchLog += $"SUCCESS! Found product code: {productCode}\n";
                                return productCode;
                            }
                            else
                            {
                                searchLog += "no match\n";
                            }
                        }
                        catch (Exception ex)
                        {
                            searchLog += $"error: {ex.Message}\n";
                        }
                    }
                }

                searchLog += $"Completed search of ZIP file {Path.GetFileName(zipFile)} - no product codes found\n";
            }
            catch (Exception ex)
            {
                searchLog += $"Error processing zip file {Path.GetFileName(zipFile)}: {ex.Message}\n";
            }
            finally
            {
                // Cleanup temporary directory
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch (Exception cleanupEx)
                    {
                        searchLog += $"Warning: Could not cleanup temp directory: {cleanupEx.Message}\n";
                    }
                }
            }

            return "";
        }

        /// <summary>
        /// Parse date/time from HST log file name
        /// </summary>
        private DateTime? ParseLogFileDateTime(string fileName)
        {
            try
            {
                // Match pattern like: 2025-08-06T15-00-001.log
                var match = Regex.Match(fileName, @"(\d{4})-(\d{2})-(\d{2})T(\d{2})-(\d{2})-", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    int year = int.Parse(match.Groups[1].Value);
                    int month = int.Parse(match.Groups[2].Value);
                    int day = int.Parse(match.Groups[3].Value);
                    int hour = int.Parse(match.Groups[4].Value);
                    int minute = int.Parse(match.Groups[5].Value);

                    return new DateTime(year, month, day, hour, minute, 0);
                }
            }
            catch (Exception)
            {
                // Return null if parsing fails
            }

            return null;
        }

        /// <summary>
        /// Enhanced copy method with comprehensive search
        /// </summary>
        public string GetProductCodeFromDiagnosticLogWithCopyHST()
        {
            // Use the enhanced search method which is more comprehensive
            // than the copy approach, but keep this method for compatibility
            return GetProductCodeFromDiagnosticLogHST();
        }

        #endregion

        #region Enhanced Product Code Extraction

        /// <summary>
        /// Extracts product code specifically for the current cell from HST logs
        /// </summary>
        private string ExtractProductCodeForCurrentCellHST(string content)
        {
            if (currentCellName == "UNKNOWN")
            {
                // Fallback to original method if cell is unknown
                return ExtractProductCodeFromContentHST(content);
            }

            try
            {
                // No need to convert cell names in HST - use currentCellName directly since it's already in the correct format
                LogMonitoringResults($"Looking for HST product code for cell: {currentCellName}", null);

                // Find all cell-collateral pairs using the HST regex
                MatchCollection cellCollateralMatches = CELL_COLLATERAL_REGEX_HST.Matches(content);

                string lastProductCodeForCurrentCell = "";

                foreach (Match match in cellCollateralMatches)
                {
                    string cellName = match.Groups[1].Value.Trim().ToUpper();
                    string collateralId = match.Groups[2].Value.Trim();

                    // Check if this matches our current cell
                    if (cellName.Equals(currentCellName, StringComparison.OrdinalIgnoreCase))
                    {
                        lastProductCodeForCurrentCell = collateralId;
                        LogMonitoringResults($"Found HST product code {collateralId} for cell {cellName}", null);
                    }
                }

                if (!string.IsNullOrEmpty(lastProductCodeForCurrentCell))
                {
                    return lastProductCodeForCurrentCell;
                }

                // If no match found for current cell, try fallback method
                LogMonitoringResults($"No HST product code found for specific cell {currentCellName}, trying fallback method", null);
                return ExtractProductCodeFromContentHST(content);
            }
            catch (Exception ex)
            {
                LogMonitoringResults(null, $"Error in ExtractProductCodeForCurrentCellHST: {ex.Message}");
                return ExtractProductCodeFromContentHST(content);
            }
        }

        /// <summary>
        /// Original method as fallback for HST logs
        /// </summary>
        private string ExtractProductCodeFromContentHST(string content)
        {
            // First, try the HST-specific regex
            MatchCollection matches = COLLATERAL_ID_REGEX_HST.Matches(content);
            if (matches.Count > 0)
            {
                Match lastMatch = matches[matches.Count - 1];
                string productCode = lastMatch.Groups[1].Value.Trim();

                if (!string.IsNullOrEmpty(productCode))
                {
                    lastKnownProductCode = productCode; // Cache it
                }

                return productCode;
            }

            // If HST regex didn't find anything, try a more flexible approach
            try
            {
                // Look for the HST pattern but be more flexible about cell matching
                Regex flexibleHSTRegex = new Regex(@"([ABCD]\d{3}):\s*TIU assignment \d+ board name and CMMS id \([^,]+,\s*([A-Z0-9]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                MatchCollection flexibleMatches = flexibleHSTRegex.Matches(content);

                if (flexibleMatches.Count > 0)
                {
                    string lastMatchingProductCode = "";

                    foreach (Match match in flexibleMatches)
                    {
                        string cellName = match.Groups[1].Value.Trim().ToUpper();
                        string productCode = match.Groups[2].Value.Trim();

                        // Try to match with current cell name directly (no conversion needed)
                        if (cellName.Equals(currentCellName, StringComparison.OrdinalIgnoreCase))
                        {
                            lastMatchingProductCode = productCode;
                            LogMonitoringResults($"Found HST product code using flexible method: {productCode} for cell {cellName}", null);
                        }
                    }

                    if (!string.IsNullOrEmpty(lastMatchingProductCode))
                    {
                        lastKnownProductCode = lastMatchingProductCode;
                        return lastMatchingProductCode;
                    }
                    else
                    {
                        // If no cell match, return the last product code found
                        Match lastMatch = flexibleMatches[flexibleMatches.Count - 1];
                        string fallbackProductCode = lastMatch.Groups[2].Value.Trim();

                        if (!string.IsNullOrEmpty(fallbackProductCode))
                        {
                            lastKnownProductCode = fallbackProductCode;
                            LogMonitoringResults($"Found HST product code using flexible fallback: {fallbackProductCode}", null);
                        }

                        return fallbackProductCode;
                    }
                }
                else
                {
                    LogMonitoringResults("HST flexible method found no matching patterns", null);
                }
            }
            catch (Exception ex)
            {
                LogMonitoringResults(null, $"Error in HST flexible method: {ex.Message}");
            }

            return "";
        }

        /// <summary>
        /// Gets product code from diagnostic log, filtering by current cell name
        /// Automatically detects HST logs and uses appropriate extraction method
        /// </summary>
        public string GetProductCodeFromDiagnosticLog()
        {
            try
            {
                // Check if this is an HST log path
                bool isHSTLog = !string.IsNullOrEmpty(appConfig.SumPOR.ProductLogPath) &&
                               appConfig.SumPOR.ProductLogPath.ToLower().Contains("hst");

                if (isHSTLog)
                {
                    LogMonitoringResults("HST log path detected, using enhanced HST-specific extraction method", null);
                    return GetProductCodeFromDiagnosticLogHST();
                }

                // Original method for non-HST logs
                string product_code_fetching_log = $"Searching for product codes associated with cell: {currentCellName}\n";
                DateTime now = DateTime.Now;

                string diagnosticLogPath = appConfig.SumPOR.ProductLogPath;

                if (string.IsNullOrEmpty(diagnosticLogPath))
                {
                    product_code_fetching_log += "Diagnostic log path not configured in ProductLogPath\n";
                    LogMonitoringResults(product_code_fetching_log, null);
                    return GetLastKnownProductCode();
                }

                List<string> logFilesToTry = new List<string>();

                // Try current hour first, then previous hours
                for (int hoursBack = 0; hoursBack <= 2; hoursBack++)
                {
                    DateTime timeToCheck = now.AddHours(-hoursBack);
                    string logFilePattern = $"{timeToCheck:yyyy-MM-ddTHH}-00-*.log";
                    string fullLogPattern = Path.Combine(diagnosticLogPath, logFilePattern);

                    // Get all matching files for this hour
                    string[] matchingFiles = Directory.GetFiles(diagnosticLogPath, logFilePattern);
                    logFilesToTry.AddRange(matchingFiles);
                }

                foreach (string logPath in logFilesToTry)
                {
                    product_code_fetching_log += $"Attempting to read diagnostic log: {logPath}\n";

                    if (!File.Exists(logPath))
                    {
                        product_code_fetching_log += $"File not found: {logPath}\n";
                        continue;
                    }

                    try
                    {
                        string content = ReadFileWithRetry(logPath);

                        if (!string.IsNullOrEmpty(content))
                        {
                            string productCode = ExtractProductCodeForCurrentCell(content);
                            if (!string.IsNullOrEmpty(productCode))
                            {
                                lastKnownProductCode = productCode; // Cache the result
                                product_code_fetching_log += $"Successfully found ProductCode for cell {currentCellName}: {productCode}";
                                LogMonitoringResults(product_code_fetching_log, null);
                                return productCode;
                            }
                        }
                    }
                    catch (IOException ex)
                    {
                        product_code_fetching_log += $"Failed to read {logPath}: {ex.Message}\n";
                    }
                }

                product_code_fetching_log += $"No CollateralId found for cell {currentCellName} in any accessible diagnostic logs. Using last known product code: {lastKnownProductCode}";
                LogMonitoringResults(product_code_fetching_log, null);
                return GetLastKnownProductCode();
            }
            catch (Exception ex)
            {
                LogMonitoringResults(null, $"Error reading diagnostic log: {ex.Message}");
                return GetLastKnownProductCode();
            }
        }

        /// <summary>
        /// Alternative method with copy - automatically detects HST logs
        /// </summary>
        public string GetProductCodeFromDiagnosticLogWithCopy()
        {
            // Check if this is an HST log path
            bool isHSTLog = !string.IsNullOrEmpty(appConfig.SumPOR.ProductLogPath) &&
                           appConfig.SumPOR.ProductLogPath.ToLower().Contains("hst");

            if (isHSTLog)
            {
                LogMonitoringResults("HST log path detected, using enhanced HST-specific copy extraction method", null);
                return GetProductCodeFromDiagnosticLogWithCopyHST();
            }

            // Original copy method for non-HST logs
            try
            {
                string product_code_fetching_log = $"Searching for product codes (with copy method) for cell: {currentCellName}\n";
                DateTime now = DateTime.Now;
                string diagnosticLogPath = appConfig.SumPOR.ProductLogPath;

                if (string.IsNullOrEmpty(diagnosticLogPath))
                {
                    product_code_fetching_log += "Diagnostic log path not configured in ProductLogPath\n";
                    LogMonitoringResults(product_code_fetching_log, null);
                    return GetLastKnownProductCode();
                }

                List<string> logFilesToTry = new List<string>();

                // Try current hour first, then previous hours
                for (int hoursBack = 0; hoursBack <= 2; hoursBack++)
                {
                    DateTime timeToCheck = now.AddHours(-hoursBack);
                    string logFilePattern = $"{timeToCheck:yyyy-MM-ddTHH}-00-*.log";

                    // Get all matching files for this hour and sort them (newest first)
                    string[] matchingFiles = Directory.GetFiles(diagnosticLogPath, logFilePattern)
                                                   .OrderByDescending(f => f)
                                                   .ToArray();

                    logFilesToTry.AddRange(matchingFiles);
                }

                if (logFilesToTry.Count == 0)
                {
                    product_code_fetching_log += $"No diagnostic log files found in: {diagnosticLogPath}";
                    LogMonitoringResults(product_code_fetching_log, null);
                    return GetLastKnownProductCode();
                }

                string tempCopyPath = Path.Combine(Path.GetTempPath(), $"diagnostic_copy_{Guid.NewGuid()}.log");

                // Try each log file until we find a product code
                foreach (string fullLogPath in logFilesToTry)
                {
                    product_code_fetching_log += $"Attempting to read diagnostic log: {fullLogPath}\n";

                    if (!File.Exists(fullLogPath))
                    {
                        product_code_fetching_log += $"Diagnostic log file not found: {fullLogPath}\n";
                        continue; // Try next file
                    }

                    try
                    {
                        CopyFileWithRetry(fullLogPath, tempCopyPath);
                        string content = File.ReadAllText(tempCopyPath);
                        string productCode = ExtractProductCodeForCurrentCell(content);

                        if (!string.IsNullOrEmpty(productCode))
                        {
                            lastKnownProductCode = productCode;
                            product_code_fetching_log += $"Found ProductCode for cell {currentCellName}: {productCode} in {Path.GetFileName(fullLogPath)}";
                            LogMonitoringResults(product_code_fetching_log, null);
                            return productCode;
                        }
                        else
                        {
                            product_code_fetching_log += $"No CollateralId found for cell {currentCellName} in {Path.GetFileName(fullLogPath)}\n";
                        }
                    }
                    catch (Exception fileEx)
                    {
                        product_code_fetching_log += $"Error reading {Path.GetFileName(fullLogPath)}: {fileEx.Message}\n";
                        continue; // Try next file
                    }
                    finally
                    {
                        if (File.Exists(tempCopyPath))
                        {
                            try { File.Delete(tempCopyPath); } catch { }
                        }
                    }
                }

                // If we get here, no product code was found in any file
                product_code_fetching_log += $"No CollateralId found for cell {currentCellName} in any diagnostic log. Using last known: {lastKnownProductCode}";
                LogMonitoringResults(product_code_fetching_log, null);
                return GetLastKnownProductCode();
            }
            catch (Exception ex)
            {
                LogMonitoringResults(null, $"Error reading diagnostic log: {ex.Message}");
                return GetLastKnownProductCode();
            }
        }

        /// <summary>
        /// Extracts product code specifically for the current cell
        /// </summary>
        private string ExtractProductCodeForCurrentCell(string content)
        {
            if (currentCellName == "UNKNOWN")
            {
                // Fallback to original method if cell is unknown
                return ExtractProductCodeFromContent(content);
            }

            try
            {
                // Find all cell-collateral pairs using the new regex
                MatchCollection cellCollateralMatches = CELL_COLLATERAL_REGEX.Matches(content);

                string lastProductCodeForCurrentCell = "";

                foreach (Match match in cellCollateralMatches)
                {
                    string cellName = match.Groups[1].Value.Trim().ToUpper();
                    string collateralId = match.Groups[2].Value.Trim();

                    // Check if this matches our current cell
                    if (cellName.Equals(currentCellName, StringComparison.OrdinalIgnoreCase))
                    {
                        lastProductCodeForCurrentCell = collateralId;
                        LogMonitoringResults($"Found product code {collateralId} for cell {cellName}", null);
                    }
                }

                if (!string.IsNullOrEmpty(lastProductCodeForCurrentCell))
                {
                    return lastProductCodeForCurrentCell;
                }

                // If no match found for current cell, try fallback method
                LogMonitoringResults($"No product code found for specific cell {currentCellName}, trying fallback method", null);
                return ExtractProductCodeFromContent(content);
            }
            catch (Exception ex)
            {
                LogMonitoringResults(null, $"Error in ExtractProductCodeForCurrentCell: {ex.Message}");
                return ExtractProductCodeFromContent(content);
            }
        }

        /// <summary>
        /// Original method as fallback
        /// </summary>
        private string ExtractProductCodeFromContent(string content)
        {
            // First, try the original regex
            MatchCollection matches = COLLATERAL_ID_REGEX.Matches(content);
            if (matches.Count > 0)
            {
                Match lastMatch = matches[matches.Count - 1];
                string productCode = lastMatch.Groups[1].Value.Trim();

                if (!string.IsNullOrEmpty(productCode))
                {
                    lastKnownProductCode = productCode; // Cache it
                }

                return productCode;
            }

            // If original regex didn't find anything, try the XML-based approach
            try
            {
                // Regex to find CollateralId XML tags with their content
                Regex xmlCollateralRegex = new Regex(@"<CollateralId>([^<]+)</CollateralId>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                // Regex to find Site XML tags with their content
                Regex xmlSiteRegex = new Regex(@"<Site>([^<]+)</Site>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                MatchCollection collateralMatches = xmlCollateralRegex.Matches(content);
                MatchCollection siteMatches = xmlSiteRegex.Matches(content);

                if (collateralMatches.Count > 0 && siteMatches.Count > 0)
                {
                    string targetSite = currentCellName; // Use current cell name for matching
                    string lastMatchingProductCode = "";

                    // Create a list to store CollateralId and Site pairs with their positions
                    var collateralSitePairs = new List<(string CollateralId, string Site, int Position)>();

                    // Find all CollateralId matches and their positions
                    foreach (Match collateralMatch in collateralMatches)
                    {
                        string collateralId = collateralMatch.Groups[1].Value.Trim();
                        int collateralPosition = collateralMatch.Index;

                        // Look for the nearest Site tag after this CollateralId
                        // We'll search in a reasonable range (e.g., within 2000 characters)
                        string nearestSite = FindNearestSiteTag(content, collateralPosition, xmlSiteRegex);

                        if (!string.IsNullOrEmpty(nearestSite))
                        {
                            collateralSitePairs.Add((collateralId, nearestSite, collateralPosition));
                        }
                    }

                    // Filter pairs that match our current cell and get the last occurrence
                    var matchingPairs = collateralSitePairs
                        .Where(pair => pair.Site.Equals(targetSite, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(pair => pair.Position)
                        .ToList();

                    if (matchingPairs.Any())
                    {
                        var lastMatchingPair = matchingPairs.Last();
                        lastMatchingProductCode = lastMatchingPair.CollateralId;

                        LogMonitoringResults($"Found product code using XML fallback method: {lastMatchingProductCode} for site {lastMatchingPair.Site}", null);

                        if (!string.IsNullOrEmpty(lastMatchingProductCode))
                        {
                            lastKnownProductCode = lastMatchingProductCode; // Cache it
                        }

                        return lastMatchingProductCode;
                    }
                    else
                    {
                        LogMonitoringResults($"XML fallback method found CollateralId entries, but none matched current site: {targetSite}", null);
                    }
                }
                else
                {
                    LogMonitoringResults("XML fallback method found no CollateralId or Site tags", null);
                }
            }
            catch (Exception ex)
            {
                LogMonitoringResults(null, $"Error in XML fallback method: {ex.Message}");
            }

            return "";
        }

        /// <summary>
        /// Helper method to find the nearest Site tag after a given position
        /// </summary>
        private string FindNearestSiteTag(string content, int startPosition, Regex siteRegex)
        {
            try
            {
                // Look within a reasonable range after the CollateralId (e.g., next 2000 characters)
                int searchRange = Math.Min(2000, content.Length - startPosition);
                string searchContent = content.Substring(startPosition, searchRange);

                Match siteMatch = siteRegex.Match(searchContent);
                if (siteMatch.Success)
                {
                    return siteMatch.Groups[1].Value.Trim();
                }
            }
            catch (Exception ex)
            {
                LogMonitoringResults(null, $"Error finding nearest site tag: {ex.Message}");
            }

            return "";
        }

        /// <summary>
        /// Returns the last known non-null product code
        /// </summary>
        private string GetLastKnownProductCode()
        {
            return string.IsNullOrEmpty(lastKnownProductCode) ? "" : lastKnownProductCode;
        }

        #endregion

        #region Existing Methods (unchanged)

        public Dictionary<string, uint> AnalyzeSystemActivity()
        {
            var fileChanges = new Dictionary<string, uint>();

            try
            {
                bool wasUsed = false;
                string paths_checked = string.Empty;
                string firstChangedFile = string.Empty;

                foreach (var configEntry in appConfig.Jose)
                {
                    if (wasUsed) break;

                    var dataModelConfigToRead = ConvertToDataModelConfig(configEntry.Value);

                    string pathToRead = string.IsNullOrEmpty(dataModelConfigToRead.FormatDate)
                        ? dataModelConfigToRead.FilePath
                        : dataModelConfigToRead.FilePath.Replace(dataModelConfigToRead.FormatDate,
                            DateTime.Now.ToString(dataModelConfigToRead.FormatDate).ToString().Replace("/", ""));

                    if (File.Exists(pathToRead))
                    {
                        var activityResult = AnalyzeFileActivity(pathToRead, dataModelConfigToRead);
                        wasUsed = activityResult.WasUsed;

                        if (wasUsed)
                        {
                            firstChangedFile = pathToRead;
                            fileChanges[pathToRead] = (uint)activityResult.ChangesDetected;
                            paths_checked += $"\n{pathToRead} had changes indicating the tester had activity\n";
                            break;
                        }
                        else
                        {
                            paths_checked += $"\n{pathToRead} indicated that tester had NOT activity\n";
                        }
                    }
                    else
                    {
                        paths_checked += $"\n{pathToRead} this path doesn't exist, therefore indicate the Tester had NOT activity\n";
                    }
                }

                LogMonitoringResults(paths_checked, null);
                return fileChanges;
            }
            catch (Exception ex)
            {
                LogMonitoringResults(null, ex.Message);
                return fileChanges;
            }
        }

        private DataModelConfig ConvertToDataModelConfig(MonitorTxtConfig config)
        {
            return new DataModelConfig
            {
                FilePath = config.FilePath,
                NoContent = config.NoContent,
                Skip = config.Skip,
                FormatDate = config.FormatDate,
                LastlineContent = config.LastlineContent
            };
        }

        private void InitializeStorage()
        {
            if (!File.Exists(pathToStorage))
            {
                File.Create(pathToStorage).Close();
            }

            if (string.IsNullOrEmpty(File.ReadAllText(pathToStorage)))
            {
                List<DataModelStorage> newDataModelStorageList = new List<DataModelStorage>();
                string dataModelStorageListJson = JsonConvert.SerializeObject(newDataModelStorageList, Formatting.Indented);
                File.WriteAllText(pathToStorage, dataModelStorageListJson);
            }
        }

        private (bool WasUsed, int ChangesDetected) AnalyzeFileActivity(string pathToRead, DataModelConfig dataModelConfigToRead)
        {
            try
            {
                // Create copy of file for analysis
                if (File.Exists(pathToReadCopy)) File.Delete(pathToReadCopy);
                File.Copy(pathToRead, pathToReadCopy);

                int lastLineWrote = File.ReadLines(pathToReadCopy).Count();

                // Initialize or load storage
                InitializeStorage();

                string pathToStorageText = File.ReadAllText(pathToStorage);
                List<DataModelStorage> dataModelStorageList = JsonConvert.DeserializeObject<List<DataModelStorage>>(pathToStorageText);

                // Clean invalid data
                dataModelStorageList.RemoveAll(dtms => string.IsNullOrEmpty(dtms.FilePath));

                DataModelStorage dataModelStored = dataModelStorageList
                    .FirstOrDefault(l => l.FilePath.Contains(dataModelConfigToRead.FilePath));

                if (dataModelStored == null)
                {
                    dataModelStored = new DataModelStorage
                    {
                        FilePath = dataModelConfigToRead.FilePath,
                        LastWriteTime = DateTime.Now.ToString(),
                        NumlastLineWroteStorage = lastLineWrote <= 1250 ? 0 : lastLineWrote - 1250
                    };
                    dataModelStorageList.Add(dataModelStored);
                }

                int lastLineWriteToRead = dataModelStored.NumlastLineWroteStorage;
                int changesDetected = Math.Max(0, lastLineWrote - lastLineWriteToRead);

                // Update storage
                dataModelStored.NumlastLineWroteStorage = lastLineWrote;
                dataModelStored.LastWriteTime = DateTime.Now.ToString();

                bool wasUsed = false;

                if (lastLineWriteToRead != lastLineWrote)
                {
                    if (lastLineWriteToRead > lastLineWrote)
                    {
                        if (DateTime.Now.Date == DateTime.Now.AddMinutes(-10).Date)
                            wasUsed = true;
                    }

                    if (string.IsNullOrEmpty(dataModelConfigToRead.NoContent) &&
                        string.IsNullOrEmpty(dataModelConfigToRead.Skip))
                    {
                        wasUsed = true;
                    }

                    // Analyze new lines for activity
                    wasUsed = wasUsed || AnalyzeNewLines(pathToReadCopy, dataModelConfigToRead,
                        lastLineWriteToRead, lastLineWrote);
                }
                else if (!string.IsNullOrEmpty(dataModelConfigToRead.LastlineContent))
                {
                    wasUsed = CheckLastLineContent(pathToReadCopy, dataModelConfigToRead, lastLineWriteToRead);
                }

                // Save updated storage
                string dataModelStorageListJsonUpdate = JsonConvert.SerializeObject(dataModelStorageList, Formatting.Indented);
                File.WriteAllText(pathToStorage, dataModelStorageListJsonUpdate);

                // Clean up
                if (File.Exists(pathToReadCopy)) File.Delete(pathToReadCopy);

                return (wasUsed, changesDetected);
            }
            catch (Exception)
            {
                return (false, 0);
            }
        }

        private bool CheckLastLineContent(string pathToReadCopy, DataModelConfig dataModelConfigToRead, int lastLineWriteToRead)
        {
            try
            {
                string lineText = File.ReadLines(pathToReadCopy).Skip(lastLineWriteToRead - 1).Take(1).FirstOrDefault();
                if (!string.IsNullOrEmpty(lineText))
                {
                    foreach (string lastLineContentWord in dataModelConfigToRead.LastlineContent.Split(';'))
                    {
                        if (lineText.Contains(lastLineContentWord))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore errors in this check
            }
            return false;
        }

        private bool AnalyzeNewLines(string pathToReadCopy, DataModelConfig dataModelConfigToRead,
           int lastLineWriteToRead, int lastLineWrote)
        {
            bool wasUsed = false;

            // Parse skip rules
            List<DataModelSkip> dataModelSkipList = new List<DataModelSkip>();
            if (!string.IsNullOrEmpty(dataModelConfigToRead.Skip))
            {
                var skipData = dataModelConfigToRead.Skip.Split(';');
                foreach (var s in skipData)
                {
                    DataModelSkip dataModelSkip = new DataModelSkip();
                    if (s.Contains('|'))
                    {
                        var fromTo = s.Split('|');
                        dataModelSkip.From = fromTo[0];
                        dataModelSkip.To = fromTo[1];
                    }
                    else
                    {
                        dataModelSkip.From = s;
                        dataModelSkip.To = string.Empty;
                    }
                    dataModelSkipList.Add(dataModelSkip);
                }
            }


            // Analyze each new line
            for (int i = lastLineWriteToRead + 1; i <= lastLineWrote && !wasUsed; i++)
            {
                string lineText = File.ReadLines(pathToReadCopy).Skip(i - 1).Take(1).FirstOrDefault();

                if (!string.IsNullOrEmpty(lineText))
                {
                    bool skip = CheckIfLineSkipped(lineText, dataModelSkipList, pathToReadCopy, ref i, lastLineWrote);

                    if (!skip && !string.IsNullOrEmpty(dataModelConfigToRead.NoContent))
                    {
                        var noContent = dataModelConfigToRead.NoContent.Split(';');
                        foreach (var word in noContent)
                        {
                            if (!lineText.Contains(word))
                            {
                                wasUsed = true;
                                break;
                            }
                        }
                    }
                }
            }


            return wasUsed;
        }

        private bool CheckIfLineSkipped(string lineText, List<DataModelSkip> dataModelSkipList,
           string pathToReadCopy, ref int currentLine, int lastLineWrote)
        {
            foreach (var skipRule in dataModelSkipList)
            {
                if (lineText.Contains(skipRule.From))
                {
                    if (!string.IsNullOrEmpty(skipRule.To))
                    {
                        // Find the end of the skip section
                        for (int f = currentLine + 1; f <= lastLineWrote; f++)
                        {
                            string textLineF = File.ReadLines(pathToReadCopy).Skip(f - 1).Take(1).FirstOrDefault();
                            if (textLineF.Contains(skipRule.To))
                            {
                                currentLine = f - 1;
                                return true;
                            }
                        }
                    }
                    return true;
                }
                else if (!string.IsNullOrEmpty(skipRule.To) && lineText.Contains(skipRule.To))
                {
                    return true;
                }
            }
            return false;
        }
        
        private void LogMonitoringResults(string pathsChecked, string errorMessage)
        {
            try
            {
                if (File.Exists(pathOfMonitoring))
                {
                    if (DateTime.Now.Month - File.GetCreationTime(pathOfMonitoring).Month == 1)
                    {
                        File.Delete(pathOfMonitoring);
                        File.Create(pathOfMonitoring).Close();
                    }
                }
                else
                {
                    File.Create(pathOfMonitoring).Close();
                }

                string logResult = File.ReadAllText(pathOfMonitoring);

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    logResult += $"\n--------------------------{DateTime.Now}--------------------------\n" +
                                $"\n                                    ERROR \n\n{errorMessage}\n\n";
                }
                else if (!string.IsNullOrEmpty(pathsChecked))
                {
                    logResult += $"\n--------------------------{DateTime.Now}--------------------------\n" +
                                $"\n                             Run successful                           \n\n{pathsChecked}\n\n";
                }

                File.WriteAllText(pathOfMonitoring, logResult);
            }
            catch (Exception)
            {
                // Ignore logging errors
            }
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the current cell name for this machine
        /// </summary>
        public string CurrentCellName => currentCellName;

        /// <summary>
        /// Gets the current machine's IP address
        /// </summary>
        public string CurrentIP => GetLocalIPv4();

        #endregion
    }
}