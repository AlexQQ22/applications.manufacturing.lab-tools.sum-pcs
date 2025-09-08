// Updated DataModel.cs
using System;
using System.Collections.Generic;

namespace SystemUtilizationMonitor.Models
{
    // Configuration models for JSON config file
    public class ConfigurationModel
    {
        public Dictionary<string, MonitorTxtConfig> Jose { get; set; }
        public SumPORConfig SumPOR { get; set; }
        public MouseConfig Mouse { get; set; }
        public KeyboardConfig Keyboard { get; set; }
        public HookConfig Hook { get; set; }
        public VMConfig VM { get; set; }
        public MonitoringConfig Monitoring { get; set; }
        public string JsonOutputPath { get; set; }

        public ConfigurationModel()
        {
            Jose = new Dictionary<string, MonitorTxtConfig>();
            SumPOR = new SumPORConfig();
            Mouse = new MouseConfig();
            Keyboard = new KeyboardConfig();
            Hook = new HookConfig();
            VM = new VMConfig();
            Monitoring = new MonitoringConfig();
            JsonOutputPath = "";
        }
    }

    public class VMConfig
    {
        public string Username { get; set; }
        public string Password { get; set; }

        public VMConfig()
        {
            Username = "cc3user";
            Password = "sthi";
        }
    }

    public class MonitoringConfig
    {
        public int RecordIntervalMinutes { get; set; }

        public MonitoringConfig()
        {
            RecordIntervalMinutes = 5;
        }
    }

    public class MouseConfig
    {
        public int WM_LBUTTONDOWN { get; set; }
        public int WM_RBUTTONDOWN { get; set; }
        public int WM_MBUTTONDOWN { get; set; }
        public int WM_MOUSEMOVE { get; set; }
        public int WM_MOUSEWHEEL { get; set; }
        public int MouseMoveThrottleMs { get; set; }

        // No constructor - values will be set from JSON configuration
    }

    public class KeyboardConfig
    {
        public int WM_KEYDOWN { get; set; }
        public int WM_SYSKEYDOWN { get; set; }

        // No constructor - values will be set from JSON configuration
    }

    public class HookConfig
    {
        public int WH_KEYBOARD_LL { get; set; }
        public int WH_MOUSE_LL { get; set; }

        // No constructor - values will be set from JSON configuration
    }

    public class MonitorTxtConfig
    {
        public string FilePath { get; set; }
        public string NoContent { get; set; }
        public string Skip { get; set; }
        public string FormatDate { get; set; }
        public string LastlineContent { get; set; }
    }

    public class SumPORConfig
    {
        public bool ShouldReadLogFiles { get; set; }
        public bool Debug { get; set; }
        public string ProductLogPath { get; set; } // NEW: Add this property
        public ArgsConfig Args { get; set; }

        public SumPORConfig()
        {
            Args = new ArgsConfig();
            Debug = false;
            
        }
    }

    public class ArgsConfig
    {
        public string RollingInterval { get; set; }
        public int RetainedFileCountLimit { get; set; }
        public string OutputTemplate { get; set; }

        public ArgsConfig()
        {
            RollingInterval = "Day";
            RetainedFileCountLimit = 15;
            OutputTemplate = "{Message:l}{NewLine}";
        }
    }

    // Data models from MonitoringSUM
    public class DataModelConfig
    {
        public string FilePath { get; set; }
        public string NoContent { get; set; }
        public string Skip { get; set; }
        public string FormatDate { get; set; }
        public string LastlineContent { get; set; }
    }

    public class DataModelStorage
    {
        public string FilePath { get; set; }
        public string LastWriteTime { get; set; }
        public int NumlastLineWroteStorage { get; set; }
        public string LastlineContent { get; set; }
    }

    public class DataModelSkip
    {
        public string From { get; set; }
        public string To { get; set; }
    }

    public class UtilizationTimeFrame
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int MouseEvents { get; set; }
        public int KeyboardEvents { get; set; }
        //public Dictionary<string, uint> FileChanges { get; set; }
        public string FileChanges { get; set; }
        public string Product { get; set; }
        public string MachineName { get; set; }

        public UtilizationTimeFrame()
        {
            FileChanges =  "";
            Product = ""; // Initialize with empty string
            MachineName = "";
        }
    }

    public class MonitorConfiguration
    {
        public TimeSpan RecordInterval { get; set; }
        public List<DirectoryWatch> DirectoriesToWatch { get; set; }

        public MonitorConfiguration()
        {
            RecordInterval = TimeSpan.FromMinutes(5);
            DirectoriesToWatch = new List<DirectoryWatch>();
        }
    }

    public class DirectoryWatch
    {
        public string Path { get; set; }
        public string Filter { get; set; }
    }
}