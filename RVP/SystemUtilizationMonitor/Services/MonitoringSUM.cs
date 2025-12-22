using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SystemUtilizationMonitor.Models;

namespace SystemUtilizationMonitor.Services
{
    public class MonitoringSUM
    {
        public static UtilizationTimeFrame MonitoringFiles(UtilizationTimeFrame timeFrame,ConfigurationModel appconf,string logInfo) {

            string logResult = string.Empty;
            string messagebad = string.Empty;

            var retryCount = 0;
            int maxRetries = 4;

            string logPath =  Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
              "Intel", "SystemUtilizationMonitor", "Monitoring_logs.txt");

            string pathToRead = string.Empty;

            string pathToStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
              "Intel", "SystemUtilizationMonitor", "SUM_DB_Local.json");

            string pathToReadCopy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
              "Intel", "SystemUtilizationMonitor", "Monitoring_strut_detail_log_copy.txt");

            string paths_checked = string.Empty;

            while (retryCount < maxRetries)
            {

                try
                {

                    List<Models.DataModelConfig> dataModelConfigListRead = new List<DataModelConfig>();

                    foreach(var i in appconf.Jose)
                    {
                        Models.DataModelConfig dataModelConfigRead = new DataModelConfig();
                        dataModelConfigRead.FilePath = i.Value.FilePath;
                        dataModelConfigRead.NoContent = i.Value.NoContent;
                        dataModelConfigRead.Skip = i.Value.Skip;
                        dataModelConfigRead.FormatDate = i.Value.FormatDate;
                        dataModelConfigRead.LastlineContent = i.Value.LastlineContent;
                        dataModelConfigListRead.Add(dataModelConfigRead);
                    }

                    bool wasUsed = false;

                    for (int j = 0; j < dataModelConfigListRead.Count() & !wasUsed; j++)
                    {
                        Models.DataModelConfig dataModelConfigToRead = dataModelConfigListRead[j];

                        pathToRead = string.IsNullOrEmpty(dataModelConfigToRead.FormatDate) ? dataModelConfigToRead.FilePath : dataModelConfigToRead.FilePath.Replace(dataModelConfigToRead.FormatDate, DateTime.Now.ToString(dataModelConfigToRead.FormatDate).ToString().Replace("/", ""));

                        if (File.Exists(pathToRead))
                        {

                            if (File.Exists(pathToReadCopy)) File.Delete(pathToReadCopy);
                            File.Copy(pathToRead, pathToReadCopy);
                            int LastLineWrote = File.ReadLines(pathToReadCopy).Count();

                            if (!File.Exists(pathToStorage))
                            {
                                File.Create(pathToStorage).Close();
                            }

                            if (string.IsNullOrEmpty(File.ReadAllText(pathToStorage)))
                            {
                                List<Models.DataModelStorage> newDataModelStorageList = new List<Models.DataModelStorage>();
                                Models.DataModelStorage newDataModelStorage = new Models.DataModelStorage
                                {
                                    FilePath = dataModelConfigToRead.FilePath,
                                    LastWriteTime = DateTime.Now.ToString(),
                                    NumlastLineWroteStorage = LastLineWrote <= 1250 ? 0 : LastLineWrote - 1250
                                };
                                newDataModelStorageList.Add(newDataModelStorage);
                                string DataModelStorageListJson = JsonConvert.SerializeObject(newDataModelStorageList, Formatting.Indented);
                                File.WriteAllText(pathToStorage, DataModelStorageListJson);
                            }

                            string pathToStorageText = File.ReadAllText(pathToStorage);
                            List<Models.DataModelStorage> DataModelStorageList = JsonConvert.DeserializeObject<List<Models.DataModelStorage>>(pathToStorageText);

                            List<Models.DataModelStorage> listToRemove = new List<Models.DataModelStorage>();
                            foreach (DataModelStorage Dtms in DataModelStorageList)
                            {
                                if (string.IsNullOrEmpty(Dtms.FilePath))
                                {
                                    listToRemove.Add(Dtms);
                                }
                            }
                            if (listToRemove != null & listToRemove.Count > 0)
                            {
                                foreach (var item in listToRemove)
                                {
                                    DataModelStorageList.Remove(item);
                                }
                            }

                            Models.DataModelStorage Modelstored = DataModelStorageList.Where(l => l.FilePath.Contains(dataModelConfigToRead.FilePath)).Select(l => l).FirstOrDefault();

                            if (Modelstored == null)
                            {
                                Modelstored = new Models.DataModelStorage
                                {
                                    FilePath = dataModelConfigToRead.FilePath,
                                    LastWriteTime = DateTime.Now.ToString(),
                                    NumlastLineWroteStorage = LastLineWrote <= 1250 ? 0 : LastLineWrote - 1250
                                };
                                DataModelStorageList.Add(Modelstored);
                                string DataModelStorageListJson = JsonConvert.SerializeObject(DataModelStorageList, Formatting.Indented);
                                File.WriteAllText(pathToStorage, DataModelStorageListJson);
                            }

                            int LastLineWriteToRead = Modelstored.NumlastLineWroteStorage;

                            Modelstored.NumlastLineWroteStorage = LastLineWrote;
                            Modelstored.LastWriteTime = DateTime.Now.ToString();

                            string DataModelStorageListJsonUpdate = JsonConvert.SerializeObject(DataModelStorageList, Formatting.Indented);

                            if (LastLineWriteToRead != LastLineWrote)
                            {

                                if (LastLineWriteToRead > LastLineWrote) 
                                {
                                    if (DateTime.Now.Date == DateTime.Now.AddMinutes(-10).Date) wasUsed = true; 
                                }

                                if (string.IsNullOrEmpty(dataModelConfigToRead.NoContent) & string.IsNullOrEmpty(dataModelConfigToRead.Skip)) 
                                {
                                    wasUsed = true;
                                }

                                List<Models.DataModelSkip> DataModelSkipList = new List<Models.DataModelSkip>();
                                if (!string.IsNullOrEmpty(dataModelConfigToRead.Skip) & !wasUsed)  
                                {
                                    var skipData = dataModelConfigToRead.Skip.Split(';');

                                    foreach (var s in skipData)
                                    {
                                        Models.DataModelSkip DataModelSkip = new Models.DataModelSkip();
                                        if (s.Contains('|'))
                                        {
                                            var fromTo = s.Split('|');

                                            DataModelSkip.From = fromTo[0];
                                            DataModelSkip.To = fromTo[1];
                                            DataModelSkipList.Add(DataModelSkip);
                                        }
                                        else
                                        {

                                            DataModelSkip.From = s;
                                            DataModelSkip.To = string.Empty;
                                            DataModelSkipList.Add(DataModelSkip);
                                        }
                                    }
                                }

                                for (int i = LastLineWriteToRead + 1; i <= LastLineWrote & !wasUsed; i++)
                                {
                                    string lineText = File.ReadLines(pathToReadCopy).Skip(i - 1).Take(1).FirstOrDefault();
                                    Console.WriteLine("NEW LINE DETECTED: " + lineText);
                                    if (!string.IsNullOrEmpty(lineText))
                                    {

                                        bool skip = false;
                                        if (!string.IsNullOrEmpty(dataModelConfigToRead.Skip))  
                                        {

                                            for (int skp = 0; skp < DataModelSkipList.Count(); skp++) 
                                            {
                                                var sDL = DataModelSkipList[skp];
                                                if (lineText.Contains(sDL.From))  
                                                {
                                                    skip = true;
                                                    if (!string.IsNullOrEmpty(sDL.To))
                                                    {
                                                        for (int f = i + 1; f <= LastLineWrote; f++)  
                                                        {
                                                            string textLineF = File.ReadLines(pathToReadCopy).Skip(f - 1).Take(1).FirstOrDefault();

                                                            if (textLineF.Contains(sDL.To))  
                                                            {
                                                                i = f - 1;
                                                                skp = DataModelSkipList.Count();
                                                                break;

                                                            }

                                                        }
                                                    }

                                                }
                                                else if (!string.IsNullOrEmpty(sDL.To) && lineText.Contains(sDL.To))
                                                {
                                                    skip = true;
                                                }

                                            }

                                        }
                                        if (!skip && !string.IsNullOrEmpty(dataModelConfigToRead.NoContent)) 
                                        {

                                            var noContent = dataModelConfigToRead.NoContent.Split(';');

                                            foreach (var word in noContent)
                                            {
                                                
                                                if (!noContent.Any(word => lineText.Contains(word)))
                                                {
                                                    wasUsed = true;
                                                }
                                            }


                               
                                        }

                                    }

                                }

                                if (wasUsed)
                                {
                                    paths_checked = paths_checked + "\n" + pathToRead + " had changes indicating  the tester had activity\n";
                                    timeFrame.FileChanges = string.Empty;
                                    timeFrame.FileChanges = pathToRead ;

                                }
                                else if (!string.IsNullOrEmpty(dataModelConfigToRead.LastlineContent))
                                {
                                    int LastlineContent = string.IsNullOrEmpty(Modelstored.LastlineContent) ? LastLineWriteToRead : int.Parse(Modelstored.LastlineContent);

                                    string lineText = File.ReadLines(pathToReadCopy).Skip(LastlineContent - 1).Take(1).FirstOrDefault();
                                    if (!string.IsNullOrEmpty(lineText))
                                    {
                                        foreach (string lastLineContentWord in dataModelConfigToRead.LastlineContent.Split(';'))
                                        {
                                            if (lineText.Contains(lastLineContentWord))  
                                            {

                                                wasUsed = true;
                                                timeFrame.FileChanges = string.Empty;
                                                timeFrame.FileChanges = pathToRead;
                                                paths_checked = paths_checked + "\n" + pathToRead + " indicated the tester had activity because the last line match with the parameter LastlineContent of the  monitoring configuration. \n";
                                                Modelstored.LastlineContent = LastlineContent.ToString();

                                            }
                                        }
                                    }
                                }
                                if (!wasUsed)
                                {
                                    paths_checked = paths_checked + "\n" + pathToRead + " indicated  that tester had NOT activity\n";
                                }

                            }
                            else
                            {

                                if (!string.IsNullOrEmpty(dataModelConfigToRead.LastlineContent))
                                {

                                    string lineText = File.ReadLines(pathToReadCopy).Skip(LastLineWriteToRead - 1).Take(1).FirstOrDefault();
                                    if (!string.IsNullOrEmpty(lineText))
                                    {
                                        foreach (string lastLineContentWord in dataModelConfigToRead.LastlineContent.Split(';'))
                                        {
                                            if (lineText.Contains(lastLineContentWord))  
                                            {
                                                wasUsed = true;
                                                paths_checked = paths_checked + "\n" + pathToRead + " indicated the tester had activity because the last line match with the parameter LastlineContent of the  monitoring configuration... \n";
                                                timeFrame.FileChanges = string.Empty;
                                                timeFrame.FileChanges = pathToRead;

                                            }
                                        }
                                    }

                                    if (!wasUsed)
                                    {
                                        paths_checked = paths_checked + "\n" + pathToRead + " indicated  that tester had NOT activity\n";
                                    }
                                }
                                else
                                {
                                    paths_checked = paths_checked + "\n" + pathToRead + " indicated  that tester had NOT activity\n";
                                }
                            }

                            File.WriteAllText(pathToStorage, DataModelStorageListJsonUpdate);
                            File.Delete(pathToReadCopy);

                        }
                        else
                        {
                            paths_checked = paths_checked + "\n" + pathToRead + "  this path dont exist, therefore  indicate  the Tester had NOT  activity \n";
                        }
                    }

                    retryCount = maxRetries + 1;

                }
                catch (Exception ex)
                {

                    messagebad = messagebad + "\nError to execute the code because: " + ex.Message + "\n";

                    if (File.Exists(pathToStorage) & retryCount == 0)
                    {
                        File.Delete(pathToStorage);
                    }

                    if (File.Exists(pathToReadCopy) & retryCount == 1)
                    {
                        File.Delete(pathToReadCopy);
                    }

                    ++retryCount;

                }

            }

            if (File.Exists(logPath))
            {

                if (DateTime.Now.Month - File.GetCreationTime(logPath).Month == 1)
                {

                    File.Delete(logPath);
                    File.Create(logPath).Close();
                }

            }
            else
            {
                File.Create(logPath).Close();
            }

            logResult = File.ReadAllText(logPath);

            if (!string.IsNullOrEmpty(messagebad))
            {
                messagebad  = messagebad + "\n" + logInfo;

                logResult = logResult + "\n--------------------------" + DateTime.Now.ToString() + "--------------------------\n" +
                                         "\n                                    ERROR \n\n" + messagebad + "\n\n";
            }
            else
            {
                paths_checked = paths_checked + "\n" + logInfo;

                logResult = logResult + "\n--------------------------" + DateTime.Now.ToString() + "--------------------------\n" +
                                        "\n                             Run succesfull                           \n\n" + paths_checked + "\n\n";

            }
            File.WriteAllText(logPath, logResult);

            return timeFrame;

        }

    }
}
