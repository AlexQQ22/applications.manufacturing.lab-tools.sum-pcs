using System;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Utilities;
using Newtonsoft.Json;

namespace SystemUtilizationMonitor.Tests
{
    /// <summary>
    /// Comprehensive test suite targeting 65%+ code coverage
    /// Tests all Model classes and CustomJsonSerializer
    /// </summary>
    public class ComprehensiveModelTests
    {
        #region UtilizationTimeFrame Tests

        [Fact]
        public void UtilizationTimeFrame_Constructor_ShouldInitializeWithDefaults()
        {
            // Act
            var timeFrame = new UtilizationTimeFrame();

            // Assert
            timeFrame.FileChanges.Should().Be("");
            timeFrame.Product.Should().Be("");
            timeFrame.MachineName.Should().Be("");
            timeFrame.MouseEvents.Should().Be(0);
            timeFrame.KeyboardEvents.Should().Be(0);
        }

        [Fact]
        public void UtilizationTimeFrame_SetProperties_ShouldStoreValues()
        {
            // Arrange
            var startTime = new DateTime(2025, 10, 1, 12, 0, 0);
            var endTime = new DateTime(2025, 10, 1, 12, 5, 0);

            // Act
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = startTime,
                EndTime = endTime,
                MouseEvents = 100,
                KeyboardEvents = 50,
                FileChanges = "file1.txt,file2.txt",
                Product = "A201",
                MachineName = "TESTMACHINE"
            };

            // Assert
            timeFrame.StartTime.Should().Be(startTime);
            timeFrame.EndTime.Should().Be(endTime);
            timeFrame.MouseEvents.Should().Be(100);
            timeFrame.KeyboardEvents.Should().Be(50);
            timeFrame.FileChanges.Should().Be("file1.txt,file2.txt");
            timeFrame.Product.Should().Be("A201");
            timeFrame.MachineName.Should().Be("TESTMACHINE");
        }

        [Fact]
        public void UtilizationTimeFrame_WithZeroEvents_ShouldAcceptZeroValues()
        {
            // Act
            var timeFrame = new UtilizationTimeFrame
            {
                MouseEvents = 0,
                KeyboardEvents = 0
            };

            // Assert
            timeFrame.MouseEvents.Should().Be(0);
            timeFrame.KeyboardEvents.Should().Be(0);
        }

        [Fact]
        public void UtilizationTimeFrame_WithLargeEventCounts_ShouldAcceptLargeValues()
        {
            // Act
            var timeFrame = new UtilizationTimeFrame
            {
                MouseEvents = 999999,
                KeyboardEvents = 888888
            };

            // Assert
            timeFrame.MouseEvents.Should().Be(999999);
            timeFrame.KeyboardEvents.Should().Be(888888);
        }

        #endregion

        #region ConfigurationModel Tests

        [Fact]
        public void ConfigurationModel_Constructor_ShouldInitializeAllProperties()
        {
            // Act
            var config = new ConfigurationModel();

            // Assert
            config.Jose.Should().NotBeNull().And.BeEmpty();
            config.SumPOR.Should().NotBeNull();
            config.Mouse.Should().NotBeNull();
            config.Keyboard.Should().NotBeNull();
            config.Hook.Should().NotBeNull();
            config.VM.Should().NotBeNull();
            config.Monitoring.Should().NotBeNull();
            config.JsonOutputPath.Should().Be("");
        }

        [Fact]
        public void ConfigurationModel_SetJsonOutputPath_ShouldStoreValue()
        {
            // Arrange
            var config = new ConfigurationModel();
            var path = @"C:\Temp\output.json";

            // Act
            config.JsonOutputPath = path;

            // Assert
            config.JsonOutputPath.Should().Be(path);
        }

        [Fact]
        public void ConfigurationModel_AddMonitorTxtConfig_ShouldStoreInDictionary()
        {
            // Arrange
            var config = new ConfigurationModel();
            var monitorConfig = new MonitorTxtConfig
            {
                FilePath = @"C:\Logs\app.log",
                NoContent = "No data",
                Skip = "skip_section",
                FormatDate = "yyyyMMdd",
                LastlineContent = "Last line"
            };

            // Act
            config.Jose.Add("monitor1", monitorConfig);

            // Assert
            config.Jose.Should().ContainKey("monitor1");
            config.Jose["monitor1"].FilePath.Should().Be(@"C:\Logs\app.log");
        }

        #endregion

        #region VMConfig Tests

        [Fact]
        public void VMConfig_Constructor_ShouldInitializeWithEmptyValues()
        {
            // Act
            var vmConfig = new VMConfig();

            // Assert
            vmConfig.Username.Should().NotBeNull();
            vmConfig.Password.Should().NotBeNull();
        }

        [Fact]
        public void VMConfig_SetCredentials_ShouldStoreValues()
        {
            // Arrange
            var vmConfig = new VMConfig();

            // Act
            vmConfig.Username = "testuser";
            vmConfig.Password = "testpass";

            // Assert
            vmConfig.Username.Should().Be("testuser");
            vmConfig.Password.Should().Be("testpass");
        }

        #endregion

        #region MonitoringConfig Tests

        [Fact]
        public void MonitoringConfig_Constructor_ShouldSetDefaultInterval()
        {
            // Act
            var config = new MonitoringConfig();

            // Assert
            config.RecordIntervalMinutes.Should().Be(5);
        }

        [Fact]
        public void MonitoringConfig_SetCustomInterval_ShouldStoreValue()
        {
            // Arrange
            var config = new MonitoringConfig();

            // Act
            config.RecordIntervalMinutes = 10;

            // Assert
            config.RecordIntervalMinutes.Should().Be(10);
        }

        #endregion

        #region MouseConfig Tests

        [Fact]
        public void MouseConfig_SetAllProperties_ShouldStoreValues()
        {
            // Act
            var mouseConfig = new MouseConfig
            {
                WM_LBUTTONDOWN = 0x0201,
                WM_RBUTTONDOWN = 0x0204,
                WM_MBUTTONDOWN = 0x0207,
                WM_MOUSEMOVE = 0x0200,
                WM_MOUSEWHEEL = 0x020A,
                MouseMoveThrottleMs = 100
            };

            // Assert
            mouseConfig.WM_LBUTTONDOWN.Should().Be(0x0201);
            mouseConfig.WM_RBUTTONDOWN.Should().Be(0x0204);
            mouseConfig.WM_MBUTTONDOWN.Should().Be(0x0207);
            mouseConfig.WM_MOUSEMOVE.Should().Be(0x0200);
            mouseConfig.WM_MOUSEWHEEL.Should().Be(0x020A);
            mouseConfig.MouseMoveThrottleMs.Should().Be(100);
        }

        #endregion

        #region KeyboardConfig Tests

        [Fact]
        public void KeyboardConfig_SetProperties_ShouldStoreValues()
        {
            // Act
            var keyboardConfig = new KeyboardConfig
            {
                WM_KEYDOWN = 0x0100,
                WM_SYSKEYDOWN = 0x0104
            };

            // Assert
            keyboardConfig.WM_KEYDOWN.Should().Be(0x0100);
            keyboardConfig.WM_SYSKEYDOWN.Should().Be(0x0104);
        }

        #endregion

        #region HookConfig Tests

        [Fact]
        public void HookConfig_SetProperties_ShouldStoreValues()
        {
            // Act
            var hookConfig = new HookConfig
            {
                WH_KEYBOARD_LL = 13,
                WH_MOUSE_LL = 14
            };

            // Assert
            hookConfig.WH_KEYBOARD_LL.Should().Be(13);
            hookConfig.WH_MOUSE_LL.Should().Be(14);
        }

        #endregion

        #region MonitorTxtConfig Tests

        [Fact]
        public void MonitorTxtConfig_SetAllProperties_ShouldStoreValues()
        {
            // Act
            var config = new MonitorTxtConfig
            {
                FilePath = @"C:\Logs\test.log",
                NoContent = "Empty",
                Skip = "skip_data",
                FormatDate = "yyyy-MM-dd",
                LastlineContent = "Final line"
            };

            // Assert
            config.FilePath.Should().Be(@"C:\Logs\test.log");
            config.NoContent.Should().Be("Empty");
            config.Skip.Should().Be("skip_data");
            config.FormatDate.Should().Be("yyyy-MM-dd");
            config.LastlineContent.Should().Be("Final line");
        }

        #endregion

        #region SumPORConfig Tests

        [Fact]
        public void SumPORConfig_Constructor_ShouldInitializeDefaults()
        {
            // Act
            var config = new SumPORConfig();

            // Assert
            config.Args.Should().NotBeNull();
            config.Debug.Should().BeFalse();
        }

        [Fact]
        public void SumPORConfig_SetProperties_ShouldStoreValues()
        {
            // Act
            var config = new SumPORConfig
            {
                ShouldReadLogFiles = true,
                Debug = true,
                ProductLogPath = @"C:\Products\logs"
            };

            // Assert
            config.ShouldReadLogFiles.Should().BeTrue();
            config.Debug.Should().BeTrue();
            config.ProductLogPath.Should().Be(@"C:\Products\logs");
        }

        #endregion

        #region ArgsConfig Tests

        [Fact]
        public void ArgsConfig_Constructor_ShouldSetDefaults()
        {
            // Act
            var config = new ArgsConfig();

            // Assert
            config.RollingInterval.Should().Be("Day");
            config.RetainedFileCountLimit.Should().Be(15);
            config.OutputTemplate.Should().Be("{Message:l}{NewLine}");
        }

        [Fact]
        public void ArgsConfig_SetCustomValues_ShouldStoreValues()
        {
            // Act
            var config = new ArgsConfig
            {
                RollingInterval = "Hour",
                RetainedFileCountLimit = 30,
                OutputTemplate = "{Timestamp} {Message}"
            };

            // Assert
            config.RollingInterval.Should().Be("Hour");
            config.RetainedFileCountLimit.Should().Be(30);
            config.OutputTemplate.Should().Be("{Timestamp} {Message}");
        }

        #endregion

        #region DataModelConfig Tests

        [Fact]
        public void DataModelConfig_SetAllProperties_ShouldStoreValues()
        {
            // Act
            var config = new DataModelConfig
            {
                FilePath = @"C:\Data\file.txt",
                NoContent = "N/A",
                Skip = "header",
                FormatDate = "yyyyMMdd",
                LastlineContent = "EOF"
            };

            // Assert
            config.FilePath.Should().Be(@"C:\Data\file.txt");
            config.NoContent.Should().Be("N/A");
            config.Skip.Should().Be("header");
            config.FormatDate.Should().Be("yyyyMMdd");
            config.LastlineContent.Should().Be("EOF");
        }

        #endregion

        #region DataModelStorage Tests

        [Fact]
        public void DataModelStorage_SetProperties_ShouldStoreValues()
        {
            // Act
            var storage = new DataModelStorage
            {
                FilePath = @"C:\Storage\data.db",
                LastWriteTime = "2025-10-01 12:00:00",
                NumlastLineWroteStorage = 42,
                LastlineContent = "Last stored line"
            };

            // Assert
            storage.FilePath.Should().Be(@"C:\Storage\data.db");
            storage.LastWriteTime.Should().Be("2025-10-01 12:00:00");
            storage.NumlastLineWroteStorage.Should().Be(42);
            storage.LastlineContent.Should().Be("Last stored line");
        }

        #endregion

        #region DataModelSkip Tests

        [Fact]
        public void DataModelSkip_SetProperties_ShouldStoreValues()
        {
            // Act
            var skip = new DataModelSkip
            {
                From = "2025-10-01",
                To = "2025-10-31"
            };

            // Assert
            skip.From.Should().Be("2025-10-01");
            skip.To.Should().Be("2025-10-31");
        }

        #endregion

        #region MonitorConfiguration Tests

        [Fact]
        public void MonitorConfiguration_Constructor_ShouldSetDefaults()
        {
            // Act
            var config = new MonitorConfiguration();

            // Assert
            config.RecordInterval.Should().Be(TimeSpan.FromMinutes(5));
            config.DirectoriesToWatch.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void MonitorConfiguration_AddDirectories_ShouldStoreInList()
        {
            // Arrange
            var config = new MonitorConfiguration();
            var dir1 = new DirectoryWatch { Path = @"C:\Watch1", Filter = "*.log" };
            var dir2 = new DirectoryWatch { Path = @"C:\Watch2", Filter = "*.txt" };

            // Act
            config.DirectoriesToWatch.Add(dir1);
            config.DirectoriesToWatch.Add(dir2);

            // Assert
            config.DirectoriesToWatch.Should().HaveCount(2);
            config.DirectoriesToWatch[0].Path.Should().Be(@"C:\Watch1");
            config.DirectoriesToWatch[1].Filter.Should().Be("*.txt");
        }

        [Fact]
        public void MonitorConfiguration_SetCustomInterval_ShouldStoreValue()
        {
            // Arrange
            var config = new MonitorConfiguration();

            // Act
            config.RecordInterval = TimeSpan.FromMinutes(10);

            // Assert
            config.RecordInterval.Should().Be(TimeSpan.FromMinutes(10));
        }

        #endregion

        #region DirectoryWatch Tests

        [Fact]
        public void DirectoryWatch_SetProperties_ShouldStoreValues()
        {
            // Act
            var watch = new DirectoryWatch
            {
                Path = @"C:\Monitored",
                Filter = "*.xml"
            };

            // Assert
            watch.Path.Should().Be(@"C:\Monitored");
            watch.Filter.Should().Be("*.xml");
        }

        #endregion

        #region CustomJsonSerializer Tests

        [Fact]
        public void CustomJsonSerializer_Serialize_ShouldProduceValidJson()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2025, 10, 1, 12, 5, 0, DateTimeKind.Utc),
                MouseEvents = 100,
                KeyboardEvents = 50,
                FileChanges = "file1.txt",
                Product = "A201",
                MachineName = "TEST"
            };

            // Act
            var json = CustomJsonSerializer.Serialize(timeFrame);

            // Assert
            json.Should().NotBeNullOrEmpty();
            json.Should().Contain("StartTime");
            json.Should().Contain("EndTime");
            json.Should().Contain("MouseEvents");
            json.Should().Contain("KeyboardEvents");
        }

        [Fact]
        public void CustomJsonSerializer_Serialize_ShouldBeDeserializable()
        {
            // Arrange
            var original = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2025, 10, 1, 12, 5, 0, DateTimeKind.Utc),
                MouseEvents = 75,
                KeyboardEvents = 25,
                FileChanges = "test.log",
                Product = "A101",
                MachineName = "MACHINE1"
            };

            // Act
            var json = CustomJsonSerializer.Serialize(original);
            var deserialized = JsonConvert.DeserializeObject<UtilizationTimeFrame>(json);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.StartTime.Should().Be(original.StartTime);
            deserialized.EndTime.Should().Be(original.EndTime);
            deserialized.MouseEvents.Should().Be(original.MouseEvents);
            deserialized.KeyboardEvents.Should().Be(original.KeyboardEvents);
            deserialized.Product.Should().Be(original.Product);
            deserialized.MachineName.Should().Be(original.MachineName);
        }

        [Fact]
        public void CustomJsonSerializer_WithEmptyObject_ShouldSerialize()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame();

            // Act
            var json = CustomJsonSerializer.Serialize(timeFrame);

            // Assert
            json.Should().NotBeNullOrEmpty();
            var deserialized = JsonConvert.DeserializeObject<UtilizationTimeFrame>(json);
            deserialized.Should().NotBeNull();
        }

        [Fact]
        public void CustomJsonSerializer_WithSpecialCharacters_ShouldHandleCorrectly()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                FileChanges = "file with spaces.txt, file\"quotes\".log",
                Product = "A-201",
                MachineName = "TEST_MACHINE_123"
            };

            // Act
            var json = CustomJsonSerializer.Serialize(timeFrame);
            var deserialized = JsonConvert.DeserializeObject<UtilizationTimeFrame>(json);

            // Assert
            deserialized.FileChanges.Should().Contain("file with spaces.txt");
            deserialized.Product.Should().Be("A-201");
            deserialized.MachineName.Should().Be("TEST_MACHINE_123");
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void ConfigurationModel_CompleteConfiguration_ShouldWorkTogether()
        {
            // Arrange & Act
            var config = new ConfigurationModel
            {
                JsonOutputPath = @"C:\Output\data.json",
                Jose = new Dictionary<string, MonitorTxtConfig>
                {
                    ["monitor1"] = new MonitorTxtConfig
                    {
                        FilePath = @"C:\Logs\app.log",
                        NoContent = "Empty",
                        Skip = "header",
                        FormatDate = "yyyyMMdd",
                        LastlineContent = "EOF"
                    }
                },
                SumPOR = new SumPORConfig
                {
                    ShouldReadLogFiles = true,
                    Debug = false,
                    ProductLogPath = @"C:\Products"
                },
                Mouse = new MouseConfig
                {
                    WM_LBUTTONDOWN = 0x0201,
                    WM_RBUTTONDOWN = 0x0204,
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
                VM = new VMConfig
                {
                    Username = "testuser",
                    Password = "testpass"
                },
                Monitoring = new MonitoringConfig
                {
                    RecordIntervalMinutes = 10
                }
            };

            // Assert
            config.Jose.Should().ContainKey("monitor1");
            config.SumPOR.ShouldReadLogFiles.Should().BeTrue();
            config.Mouse.MouseMoveThrottleMs.Should().Be(100);
            config.Keyboard.WM_KEYDOWN.Should().Be(0x0100);
            config.Hook.WH_KEYBOARD_LL.Should().Be(13);
            config.VM.Username.Should().Be("testuser");
            config.Monitoring.RecordIntervalMinutes.Should().Be(10);
        }

        [Fact]
        public void MonitorConfiguration_WithMultipleDirectories_ShouldManageAll()
        {
            // Arrange
            var config = new MonitorConfiguration
            {
                RecordInterval = TimeSpan.FromMinutes(15)
            };

            // Act
            config.DirectoriesToWatch.Add(new DirectoryWatch { Path = @"C:\Dir1", Filter = "*.log" });
            config.DirectoriesToWatch.Add(new DirectoryWatch { Path = @"C:\Dir2", Filter = "*.txt" });
            config.DirectoriesToWatch.Add(new DirectoryWatch { Path = @"C:\Dir3", Filter = "*.xml" });

            // Assert
            config.DirectoriesToWatch.Should().HaveCount(3);
            config.RecordInterval.TotalMinutes.Should().Be(15);
        }

        #endregion
    }
}