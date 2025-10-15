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

            // FIXED: Properly convert to UTC and format
            parts.Add("\"StartTime\":\"" + timeFrame.StartTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") + "\"");
            parts.Add("\"EndTime\":\"" + timeFrame.EndTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") + "\"");
            parts.Add("\"MachineName\":\"" + EscapeJson(timeFrame.MachineName) + "\"");
            parts.Add("\"PCName\":\"" + EscapeJson(timeFrame.PCName) + "\"");           // Add this line
            parts.Add("\"Cell\":\"" + EscapeJson(timeFrame.Cell) + "\"");               // Add this line
            parts.Add("\"Product\":\"" + timeFrame.Product + "\"");
            parts.Add("\"MouseEvents\":" + timeFrame.MouseEvents);
            parts.Add("\"KeyboardEvents\":" + timeFrame.KeyboardEvents);

            //var fileParts = new List<string>();
            //foreach (var kvp in timeFrame.FileChanges)
            //{
            //    fileParts.Add("\"" + EscapeJson(kvp.Key) + "\":" + kvp.Value);
            //}
            //parts.Add("\"FileChanges\":{" + string.Join(",", fileParts.ToArray()) + "}");

            if (string.IsNullOrEmpty(timeFrame.FileChanges))
            {
                parts.Add("\"FileChanges\":\"\"");
            }
            else
            {
                parts.Add("\"FileChanges\":\"" + EscapeJson(timeFrame.FileChanges) + "\"");
            }

            return "{" + string.Join(",", parts.ToArray()) + "}";
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