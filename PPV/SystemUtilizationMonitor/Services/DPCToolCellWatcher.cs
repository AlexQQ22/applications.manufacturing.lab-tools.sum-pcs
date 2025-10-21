using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace SystemUtilizationMonitor.Services
{
    public class DPCToolCellWatcher
    {
        // Dictionary mappings for different module types
        private static readonly Dictionary<string, string> HDMx10 = new Dictionary<string, string>
        {
            {"1","A101"}, {"2","A102"}, {"3","A201"}, {"4","A202"}, {"5","A301"},
            {"6","A302"}, {"7","A401"}, {"8","A402"}, {"9","A501"}, {"10","A502"}
        };

        private static readonly Dictionary<string, string> HDMx30 = new Dictionary<string, string>
        {
            {"1","A101"}, {"2","A102"}, {"3","A201"}, {"4","A202"}, {"5","A301"},
            {"6","A302"}, {"7","A401"}, {"8","A402"}, {"9","A501"}, {"10","A502"},
            {"11","B101"}, {"12","B102"}, {"13","B201"}, {"14","B202"}, {"15","B301"},
            {"16","B302"}, {"17","B401"}, {"18","B402"}, {"19","B501"}, {"20","B502"},
            {"21","C101"}, {"22","C102"}, {"23","C201"}, {"24","C202"}, {"25","C301"},
            {"26","C302"}, {"27","C401"}, {"28","C402"}, {"29","C501"}, {"30","C502"}
        };

        private static readonly Dictionary<string, string> HSTx24 = new Dictionary<string, string>
        {
            {"1","A101"}, {"2","A201"}, {"3","A301"}, {"4","A401"}, {"5","A501"}, {"6","A601"},
            {"7","B101"}, {"8","B201"}, {"9","B301"}, {"10","B401"}, {"11","B501"}, {"12","B601"},
            {"13","C101"}, {"14","C201"}, {"15","C301"}, {"16","C401"}, {"17","C501"}, {"18","C601"},
            {"19","D101"}, {"20","D201"}, {"21","D301"}, {"22","D401"}, {"23","D501"}, {"24","D601"}
        };

        private static readonly Dictionary<string, string> SSTx20 = new Dictionary<string, string>
        {
            {"1","A101"}, {"2","A201"}, {"3","A301"}, {"4","A401"},
            {"5","B101"}, {"6","B201"}, {"7","B301"}, {"8","B401"},
            {"9","C101"}, {"10","C201"}, {"11","C301"}, {"12","C401"},
            {"13","D101"}, {"14","D201"}, {"15","D301"}, {"16","D401"},
            {"17","E101"}, {"18","E201"}, {"19","E301"}, {"20","E401"}
        };

        private static readonly Dictionary<string, string> PTCx5 = new Dictionary<string, string>
        {
            {"1","A101"}, {"2","A201"}, {"3","A301"}, {"4","A401"}, {"5","A501"}
        };

        private static readonly Dictionary<string, string> SECS = new Dictionary<string, string>
        {
            {"1","A101"}, {"2","A201"}, {"3","A301"}, {"4","A401"},
            {"5","B101"}, {"6","B201"}, {"7","B301"}, {"8","B401"},
            {"9","C101"}, {"10","C201"}, {"11","C301"}, {"12","C401"}
        };

        private static readonly Dictionary<string, string> SAP = new Dictionary<string, string>
        {
            {"1","A101"}, {"2","A201"}, {"3","A301"}, {"4","A401"}, {"5","A501"}, {"6","A601"},
            {"7","B101"}, {"8","B201"}, {"9","B301"}, {"10","B401"}, {"11","B501"}, {"12","B601"}
        };

        private string pcName = "";
        private string cellPosition = "";
        private string moduleType = "";

        public string PCName => pcName;
        public string CellPosition => cellPosition;
        public string ModuleType => moduleType;

        // public DPCToolCellWatcher()
        // {
        //     Initialize();
        // }

        public void Initialize()
        {
            try
            {
                // Get PC name from network location
                pcName = GetPCNameFromNetwork().GetAwaiter().GetResult();

                // If network retrieval fails, use local machine name
                if (string.IsNullOrEmpty(pcName))
                {
                    pcName = Environment.MachineName;
                    LogInfo($"Using local machine name: {pcName}");
                }
                else
                {
                    LogInfo($"Retrieved PC name from network: {pcName}");
                }

                // Get last octet of IP address
                int lastOctet = GetLastOctetIPv4();
                if (lastOctet == -1)
                {
                    LogInfo("Failed to get last octet of IPv4 address");
                    cellPosition = "";
                    moduleType = "";
                    return;
                }

                string lastOctetStr = lastOctet.ToString();
                LogInfo($"Last octet of IP: {lastOctetStr}");

                // Determine cell position based on PC name pattern
                DetermineCellPosition(lastOctetStr);
            }
            catch (Exception ex)
            {
                LogInfo($"Error initializing DPCToolCellWatcher: {ex.Message}");
                pcName = Environment.MachineName;
                cellPosition = "";
                moduleType = "";
            }
        }

        private async Task<string> GetPCNameFromNetwork()
        {
            string[] ipsToTry = { "10.250.0.100", "10.250.0.99" };

            foreach (string ip in ipsToTry)
            {
                try
                {
                    string networkPath = $@"\\{ip}\c$\SUMInstall\PCinfo.txt";
                    LogInfo($"Attempting to read PC info from: {networkPath}");

                    if (File.Exists(networkPath))
                    {
                        string content = File.ReadAllText(networkPath).Trim();
                        if (!string.IsNullOrEmpty(content))
                        {
                            LogInfo($"Successfully read PC name from {ip}: {content}");
                            return content;
                        }
                    }
                    else
                    {
                        LogInfo($"PCinfo.txt not found at {ip}, attempting to run PCinfo.bat...");

                        const string PSEXEC_PATH = @"c:\SUMInstall\PsExec64.exe";
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = PSEXEC_PATH,
                            Arguments = $@"\\{ip} -accepteula -nobanner cmd /c ""\\{ip}\c$\SUMInstall\PCinfo.bat""",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        using (var process = new Process())
                        {
                            process.StartInfo = processInfo;
                            process.Start();

                            string output = await process.StandardOutput.ReadToEndAsync();
                            string error = await process.StandardError.ReadToEndAsync();
                            await process.WaitForExitAsync();

                            if (!string.IsNullOrEmpty(error))
                            {
                                LogInfo($"Error executing on {ip}: {error}");
                            }

                            string cleanOutput = output.Trim();

                            if (!string.IsNullOrEmpty(cleanOutput))
                            {
                                LogInfo($"Output from {ip}: {cleanOutput}");
                                return cleanOutput;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogInfo($"Failed to read from {ip}: {ex.Message}");
                }
            }

            LogInfo("Failed to retrieve PC name from all network locations");
            return "";
        }

        private void DetermineCellPosition(string lastOctetStr)
        {
            // Check each pattern and assign appropriate position
            if (Regex.IsMatch(pcName, @"\w\w\d\dDHHX\d\d\d\d", RegexOptions.IgnoreCase))
            {
                if (HDMx10.ContainsKey(lastOctetStr))
                {
                    cellPosition = HDMx10[lastOctetStr];
                    moduleType = "HDMX_X10";
                    LogInfo("Detected HDMx X10 module");
                }
            }
            else if (Regex.IsMatch(pcName, @"\w\w\d\dDHTX\d\d\d\d", RegexOptions.IgnoreCase))
            {
                if (HDMx30.ContainsKey(lastOctetStr))
                {
                    cellPosition = HDMx30[lastOctetStr];
                    moduleType = "HDMX_X30";
                    LogInfo("Detected HDMx X30 module");
                }
            }
            else if (Regex.IsMatch(pcName, @"\w\w\d\dDHST\d\d\d\d", RegexOptions.IgnoreCase))
            {
                if (HSTx24.ContainsKey(lastOctetStr))
                {
                    cellPosition = HSTx24[lastOctetStr];
                    moduleType = "HST_X24";
                    LogInfo("Detected HST X24 module");
                }
            }
            else if (Regex.IsMatch(pcName, @"\w\w\d\d(H|D)PTC\d\d\d\d", RegexOptions.IgnoreCase))
            {
                if (PTCx5.ContainsKey(lastOctetStr))
                {
                    cellPosition = PTCx5[lastOctetStr];
                    moduleType = "PTC_X5";
                    LogInfo("Detected PTC X5 module");
                }
            }
            else if (Regex.IsMatch(pcName, @"\w\w\d\dTSST\d\d\d\d", RegexOptions.IgnoreCase))
            {
                if (SSTx20.ContainsKey(lastOctetStr))
                {
                    cellPosition = SSTx20[lastOctetStr];
                    moduleType = "SST_X20";
                    LogInfo("Detected SST X20 module");
                }
            }
            else if (Regex.IsMatch(pcName, @"\w\w\d\d(TPBT|TPPV)\d\d\d\dE*N*", RegexOptions.IgnoreCase))
            {
                if (SECS.ContainsKey(lastOctetStr))
                {
                    cellPosition = SECS[lastOctetStr];
                    moduleType = "SECS";
                    LogInfo("Detected SECS module");
                }
            }
            else if (Regex.IsMatch(pcName, @"\w\w\d\dTASP\d\d\d\d", RegexOptions.IgnoreCase))
            {
                if (SAP.ContainsKey(lastOctetStr))
                {
                    cellPosition = SAP[lastOctetStr];
                    moduleType = "SAP_X12";
                    LogInfo("Detected SAP X12 module");
                }
            }
            else
            {
                cellPosition = "";
                moduleType = "";
                LogInfo($"PC type not supported: {pcName}");
            }

            // If position not found in dictionary
            if (string.IsNullOrEmpty(cellPosition) && moduleType != "")
            {
                cellPosition = "";
                LogInfo($"Position not found for octet {lastOctetStr} in {moduleType}");
            }
        }

        private int GetLastOctetIPv4()
        {
            try
            {
                string hostName = Dns.GetHostName();
                IPAddress[] addresses = Dns.GetHostAddresses(hostName);

                var ipv4Address = addresses
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork &&
                                        ip.ToString().StartsWith("10.250.0."));

                if (ipv4Address != null)
                {
                    string[] octets = ipv4Address.ToString().Split('.');
                    int lastOctet = int.Parse(octets[3]);
                    LogInfo($"Found IP address: {ipv4Address}");
                    return lastOctet;
                }

                LogInfo("No suitable IPv4 address found with prefix 10.250.0.");
                return -1;
            }
            catch (Exception ex)
            {
                LogInfo($"Error getting last octet: {ex.Message}");
                return -1;
            }
        }

        // Static method for easy access
        public static (string pcName, string cellPosition) GetPCNameAndCell()
        {
            var watcher = new DPCToolCellWatcher();
            return (watcher.PCName, watcher.CellPosition);
        }

        private static void LogInfo(string message)
        {
            try
            {
                string logInfo = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine}";
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor", "Monitoring_logs.txt");
                File.AppendAllText(logPath, logInfo);
            }
            catch { }
        }
    }


}