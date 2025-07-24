using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SystemUtilizationMonitor.Models;

namespace SystemUtilizationMonitor.Services
{
    // Activity monitoring service
    public class ActivityMonitoringService
    {
        private readonly string pathToStorage;
        private readonly string pathToReadCopy;
        private readonly string pathOfMonitoring;
        private readonly ConfigurationModel appConfig;

        public ActivityMonitoringService(ConfigurationModel config)
        {
            appConfig = config;

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
        }

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
                    if (wasUsed) break; // Stop checking once we find a change

                    var dataModelConfigToRead = ConvertToDataModelConfig(configEntry.Value);

                    string pathToRead = string.IsNullOrEmpty(dataModelConfigToRead.FormatDate)
                        ? dataModelConfigToRead.FilePath
                        : dataModelConfigToRead.FilePath.Replace(dataModelConfigToRead.FormatDate,
                            DateTime.Now.ToString(dataModelConfigToRead.FormatDate).ToString().Replace("/", ""));

                    if (File.Exists(pathToRead))
                    {
                        var activityResult = AnalyzeFileActivity(pathToRead, dataModelConfigToRead);
                        wasUsed = activityResult.WasUsed;

                        // If activity detected, record this as the first changed file and stop
                        if (wasUsed)
                        {
                            firstChangedFile = pathToRead;
                            fileChanges[pathToRead] = (uint)activityResult.ChangesDetected;
                            paths_checked += $"\n{pathToRead} had changes indicating the tester had activity\n";
                            break; // Stop checking other files
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

                // Log the results
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

        private void LogMonitoringResults(string pathsChecked, string errorMessage)
        {
            try
            {
                // Check if log file exists and manage size
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
    }
}