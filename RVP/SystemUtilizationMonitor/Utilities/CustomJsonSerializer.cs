using System;
using System.Collections.Generic;
using SystemUtilizationMonitor.Models;

namespace SystemUtilizationMonitor.Utilities
{
    public class CustomJsonSerializer
    {
        public static string Serialize(UtilizationTimeFrame timeFrame)
        {
            var parts = new List<string>();

            parts.Add("\"StartTime\":\"" + timeFrame.StartTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") + "\"");
            parts.Add("\"EndTime\":\"" + timeFrame.EndTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") + "\"");
            parts.Add("\"MachineName\":\"" + EscapeJson(timeFrame.MachineName) + "\"");
            parts.Add("\"Product\":\"" + timeFrame.Product + "\"");
            parts.Add("\"MouseEvents\":" + timeFrame.MouseEvents);
            parts.Add("\"KeyboardEvents\":" + timeFrame.KeyboardEvents);

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