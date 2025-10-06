using System;

namespace SystemUtilizationMonitor.Models
{
    public class ConfigurationModel
    {
        public SumPORConfig SumPOR { get; set; }
        public MouseConfig Mouse { get; set; }
        public KeyboardConfig Keyboard { get; set; }
        public HookConfig Hook { get; set; }
        public MonitoringConfig Monitoring { get; set; }
        public string JsonOutputPath { get; set; }

        public ConfigurationModel()
        {
            SumPOR = new SumPORConfig();
            Mouse = new MouseConfig();
            Keyboard = new KeyboardConfig();
            Hook = new HookConfig();
            Monitoring = new MonitoringConfig();
            JsonOutputPath = "";
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
    }

    public class KeyboardConfig
    {
        public int WM_KEYDOWN { get; set; }
        public int WM_SYSKEYDOWN { get; set; }
    }

    public class HookConfig
    {
        public int WH_KEYBOARD_LL { get; set; }
        public int WH_MOUSE_LL { get; set; }
    }

    public class SumPORConfig
    {
        public bool Debug { get; set; }

        public SumPORConfig()
        {
            Debug = false;
        }
    }

    public class UtilizationTimeFrame
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int MouseEvents { get; set; }
        public int KeyboardEvents { get; set; }
        public string Product { get; set; }
        public string MachineName { get; set; }

        public UtilizationTimeFrame()
        {
            Product = "";
            MachineName = "";
        }
    }

    public class MonitorConfiguration
    {
        public TimeSpan RecordInterval { get; set; }

        public MonitorConfiguration()
        {
            RecordInterval = TimeSpan.FromMinutes(5);
        }
    }
}