using System;
using System.Collections.Generic;
using System.Linq;
using SystemUtilizationMonitor.Models;

namespace SystemUtilizationMonitor.Utilities
{
    // Custom JSON serializer
    public class CustomJsonSerializer
    {
        public static string Serialize(UtilizationTimeFrame timeFrame)
        {
            var parts = new List<string>();

            parts.Add("\"StartTime\":\"" + timeFrame.StartTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ") + "\"");
            parts.Add("\"EndTime\":\"" + timeFrame.EndTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ") + "\"");
            parts.Add("\"Duration\":\"" + FormatTimeSpan(timeFrame.Duration) + "\"");
            parts.Add("\"ExpectedDuration\":\"" + FormatTimeSpan(timeFrame.ExpectedDuration) + "\"");
            parts.Add("\"MachineName\":\"" + EscapeJson(timeFrame.MachineName) + "\"");
            parts.Add("\"ProcessorCount\":" + timeFrame.ProcessorCount);
            parts.Add("\"TickCount64\":" + timeFrame.TickCount64);
            parts.Add("\"UserDomainName\":\"" + EscapeJson(timeFrame.UserDomainName) + "\"");
            parts.Add("\"UserName\":\"" + EscapeJson(timeFrame.UserName) + "\"");
            parts.Add("\"MouseEvents\":" + timeFrame.MouseEvents);
            parts.Add("\"KeyboardEvents\":" + timeFrame.KeyboardEvents);

            var cpuParts = new List<string>();
            foreach (var kvp in timeFrame.CpuUsage)
            {
                cpuParts.Add("\"" + kvp.Key + "\":" + kvp.Value);
            }
            parts.Add("\"CpuUsage\":{" + string.Join(",", cpuParts.ToArray()) + "}");

            var processParts = new List<string>();
            foreach (var kvp in timeFrame.TopProcesses)
            {
                processParts.Add("\"" + EscapeJson(kvp.Key) + "\":\"" + EscapeJson(kvp.Value) + "\"");
            }
            parts.Add("\"TopProcesses\":{" + string.Join(",", processParts.ToArray()) + "}");

            var fileParts = new List<string>();
            foreach (var kvp in timeFrame.FileChanges)
            {
                fileParts.Add("\"" + EscapeJson(kvp.Key) + "\":" + kvp.Value);
            }
            parts.Add("\"FileChanges\":{" + string.Join(",", fileParts.ToArray()) + "}");

            return "{" + string.Join(",", parts.ToArray()) + "}";
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            return ts.ToString(@"hh\:mm\:ss\.fffffff");
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("\\", "\\\\")
                       .Replace("\"", "\\\"")
                       .Replace("\r", "\\r")
                       .Replace("\n", "\\n")
                       .Replace("\t", "\\t");
        }
    }
}