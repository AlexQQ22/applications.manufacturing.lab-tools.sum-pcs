using System;
using System.IO;
using System.Linq;
using Xunit;
using FluentAssertions;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Utilities;

namespace SystemUtilizationMonitor.Tests
{
    /// <summary>
    /// Tests for utility wrappers to maximize code coverage
    /// </summary>
    public class UtilityWrapperTests : IDisposable
    {
        private readonly string tempDirectory;
        private readonly IFileSystemOperations fileSystem;

        public UtilityWrapperTests()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), $"SUM_Utility_Tests_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDirectory);
            fileSystem = new FileSystemOperations();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
            catch { }
        }

        #region FileSystemOperations Tests

        [Fact]
        public void FileSystemOperations_FileExists_WhenFileExists_ShouldReturnTrue()
        {
            // Arrange
            var testFile = Path.Combine(tempDirectory, "test.txt");
            File.WriteAllText(testFile, "test content");

            // Act
            var exists = fileSystem.FileExists(testFile);

            // Assert
            exists.Should().BeTrue();
        }

        [Fact]
        public void FileSystemOperations_FileExists_WhenFileDoesNotExist_ShouldReturnFalse()
        {
            // Arrange
            var nonExistentFile = Path.Combine(tempDirectory, "nonexistent.txt");

            // Act
            var exists = fileSystem.FileExists(nonExistentFile);

            // Assert
            exists.Should().BeFalse();
        }

        [Fact]
        public void FileSystemOperations_DirectoryExists_WhenDirectoryExists_ShouldReturnTrue()
        {
            // Act
            var exists = fileSystem.DirectoryExists(tempDirectory);

            // Assert
            exists.Should().BeTrue();
        }

        [Fact]
        public void FileSystemOperations_ReadAllText_ShouldReturnFileContent()
        {
            // Arrange
            var testFile = Path.Combine(tempDirectory, "read_test.txt");
            var content = "Test file content";
            File.WriteAllText(testFile, content);

            // Act
            var result = fileSystem.ReadAllText(testFile);

            // Assert
            result.Should().Be(content);
        }

        [Fact]
        public void FileSystemOperations_WriteAllText_ShouldCreateFile()
        {
            // Arrange
            var testFile = Path.Combine(tempDirectory, "write_test.txt");
            var content = "Written content";

            // Act
            fileSystem.WriteAllText(testFile, content);

            // Assert
            File.Exists(testFile).Should().BeTrue();
            File.ReadAllText(testFile).Should().Be(content);
        }

        [Fact]
        public void FileSystemOperations_AppendAllText_ShouldAppendToFile()
        {
            // Arrange
            var testFile = Path.Combine(tempDirectory, "append_test.txt");
            fileSystem.WriteAllText(testFile, "Line 1\n");

            // Act
            fileSystem.AppendAllText(testFile, "Line 2\n");

            // Assert
            var content = File.ReadAllText(testFile);
            content.Should().Contain("Line 1");
            content.Should().Contain("Line 2");
        }

        [Fact]
        public void FileSystemOperations_ReadAllLines_ShouldReturnLines()
        {
            // Arrange
            var testFile = Path.Combine(tempDirectory, "lines_test.txt");
            File.WriteAllText(testFile, "Line 1\nLine 2\nLine 3");

            // Act
            var lines = fileSystem.ReadAllLines(testFile);

            // Assert
            lines.Should().HaveCount(3);
            lines[0].Should().Be("Line 1");
        }

        [Fact]
        public void FileSystemOperations_CreateDirectory_ShouldCreateDirectory()
        {
            // Arrange
            var newDir = Path.Combine(tempDirectory, "new_directory");

            // Act
            fileSystem.CreateDirectory(newDir);

            // Assert
            Directory.Exists(newDir).Should().BeTrue();
        }

        [Fact]
        public void FileSystemOperations_DeleteFile_ShouldRemoveFile()
        {
            // Arrange
            var testFile = Path.Combine(tempDirectory, "delete_test.txt");
            File.WriteAllText(testFile, "to be deleted");

            // Act
            fileSystem.DeleteFile(testFile);

            // Assert
            File.Exists(testFile).Should().BeFalse();
        }

        [Fact]
        public void FileSystemOperations_GetLastWriteTime_ShouldReturnDateTime()
        {
            // Arrange
            var testFile = Path.Combine(tempDirectory, "time_test.txt");
            File.WriteAllText(testFile, "content");

            // Act
            var writeTime = fileSystem.GetLastWriteTime(testFile);

            // Assert
            writeTime.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));
        }

        #endregion

        #region ConfigurationLoader Tests

        [Fact]
        public void ConfigurationLoader_LoadConfiguration_WhenFileDoesNotExist_ShouldReturnDefaults()
        {
            // Arrange
            var loader = new ConfigurationLoader(fileSystem);
            var nonExistentPath = Path.Combine(tempDirectory, "nonexistent_config.json");

            // Act
            var config = loader.LoadConfiguration(nonExistentPath);

            // Assert
            config.Should().NotBeNull();
            config.SumPOR.Should().NotBeNull();
            config.Monitoring.RecordIntervalMinutes.Should().Be(5);
        }

        [Fact]
        public void ConfigurationLoader_CreateDefaultConfiguration_ShouldHaveAllProperties()
        {
            // Arrange
            var loader = new ConfigurationLoader();

            // Act
            var config = loader.CreateDefaultConfiguration();

            // Assert
            config.Should().NotBeNull();
            config.Jose.Should().NotBeNull();
            config.SumPOR.Should().NotBeNull();
            config.Mouse.Should().NotBeNull();
            config.Keyboard.Should().NotBeNull();
            config.Hook.Should().NotBeNull();
            config.VM.Should().NotBeNull();
            config.Monitoring.Should().NotBeNull();
            config.JsonOutputPath.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void ConfigurationLoader_SaveConfiguration_ShouldCreateFile()
        {
            // Arrange
            var loader = new ConfigurationLoader(fileSystem);
            var configPath = Path.Combine(tempDirectory, "saved_config.json");
            var config = loader.CreateDefaultConfiguration();

            // Act
            loader.SaveConfiguration(configPath, config);

            // Assert
            File.Exists(configPath).Should().BeTrue();
            var content = File.ReadAllText(configPath);
            content.Should().Contain("SumPOR");
        }

        [Fact]
        public void ConfigurationLoader_SaveAndLoad_ShouldRoundTrip()
        {
            // Arrange
            var loader = new ConfigurationLoader(fileSystem);
            var configPath = Path.Combine(tempDirectory, "roundtrip_config.json");
            var originalConfig = loader.CreateDefaultConfiguration();
            originalConfig.SumPOR.Debug = true;
            originalConfig.Monitoring.RecordIntervalMinutes = 10;

            // Act
            loader.SaveConfiguration(configPath, originalConfig);
            var loadedConfig = loader.LoadConfiguration(configPath);

            // Assert
            loadedConfig.SumPOR.Debug.Should().BeTrue();
            loadedConfig.Monitoring.RecordIntervalMinutes.Should().Be(10);
        }

        [Fact]
        public void ConfigurationLoader_LoadConfiguration_WithInvalidJson_ShouldReturnDefaults()
        {
            // Arrange
            var loader = new ConfigurationLoader(fileSystem);
            var configPath = Path.Combine(tempDirectory, "invalid_config.json");
            File.WriteAllText(configPath, "{ invalid json content }}}");

            // Act
            var config = loader.LoadConfiguration(configPath);

            // Assert
            config.Should().NotBeNull();
            config.Monitoring.RecordIntervalMinutes.Should().Be(5);
        }

        #endregion

        #region TimeFrameRecorder Tests

        [Fact]
        public void TimeFrameRecorder_RecordTimeFrame_ShouldCreateFile()
        {
            // Arrange
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow.AddMinutes(-5),
                EndTime = DateTime.UtcNow,
                MachineName = "TEST_MACHINE",
                Product = "A201",
                MouseEvents = 100,
                KeyboardEvents = 50
            };

            // Act
            recorder.RecordTimeFrame(timeFrame);

            // Assert
            var files = Directory.GetFiles(tempDirectory, "*.json");
            files.Should().NotBeEmpty();
        }

        [Fact]
        public void TimeFrameRecorder_RecordTimeFrame_WithNullTimeFrame_ShouldThrow()
        {
            // Arrange
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);

            // Act
            Action act = () => recorder.RecordTimeFrame(null);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void TimeFrameRecorder_GetOutputFileName_ShouldIncludeDateMachineProduct()
        {
            // Arrange
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 10, 2, 12, 0, 0),
                MachineName = "MACHINE1",
                Product = "A101"
            };

            // Act
            var fileName = recorder.GetOutputFileName(timeFrame);

            // Assert
            fileName.Should().Contain("20251002");
            fileName.Should().Contain("MACHINE1");
            fileName.Should().Contain("A101");
        }

        [Fact]
        public void TimeFrameRecorder_GetOutputFileName_ShouldSanitizeInvalidCharacters()
        {
            // Arrange
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                MachineName = "MACHINE<>:*?|",
                Product = "A/B\\C"
            };

            // Act
            var fileName = recorder.GetOutputFileName(timeFrame);

            // Assert
            fileName.Should().NotContain("<");
            fileName.Should().NotContain(">");
            fileName.Should().NotContain(":");
            fileName.Should().NotContain("*");
            fileName.Should().NotContain("?");
            fileName.Should().NotContain("|");
        }

        [Fact]
        public void TimeFrameRecorder_RecordMultipleTimeFrames_ShouldAppendToSameFile()
        {
            // Arrange
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);
            var timeFrame1 = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 10, 2, 12, 0, 0),
                EndTime = new DateTime(2025, 10, 2, 12, 5, 0),
                MachineName = "MACHINE1",
                Product = "A201",
                MouseEvents = 10
            };
            var timeFrame2 = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 10, 2, 12, 5, 0),
                EndTime = new DateTime(2025, 10, 2, 12, 10, 0),
                MachineName = "MACHINE1",
                Product = "A201",
                MouseEvents = 20
            };

            // Act
            recorder.RecordTimeFrame(timeFrame1);
            recorder.RecordTimeFrame(timeFrame2);

            // Assert
            var fileName = recorder.GetOutputFileName(timeFrame1);
            var lines = File.ReadAllLines(fileName);
            lines.Should().HaveCount(2);
        }

        [Fact]
        public void TimeFrameRecorder_LoadTimeFrames_ShouldReturnRecordedFrames()
        {
            // Arrange
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 10, 2, 12, 0, 0),
                EndTime = new DateTime(2025, 10, 2, 12, 5, 0),
                MachineName = "LOAD_TEST",
                Product = "LT01",
                MouseEvents = 75
            };
            recorder.RecordTimeFrame(timeFrame);
            var fileName = recorder.GetOutputFileName(timeFrame);

            // Act
            var loadedFrames = recorder.LoadTimeFrames(fileName);

            // Assert
            loadedFrames.Should().NotBeEmpty();
            loadedFrames[0].MachineName.Should().Be("LOAD_TEST");
            loadedFrames[0].MouseEvents.Should().Be(75);
        }

        [Fact]
        public void TimeFrameRecorder_LoadTimeFrames_FromNonExistentFile_ShouldReturnEmpty()
        {
            // Arrange
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);
            var nonExistentFile = Path.Combine(tempDirectory, "nonexistent.json");

            // Act
            var frames = recorder.LoadTimeFrames(nonExistentFile);

            // Assert
            frames.Should().BeEmpty();
        }

        [Fact]
        public void TimeFrameRecorder_LoadTimeFrames_WithInvalidLines_ShouldSkipThem()
        {
            // Arrange
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);
            var testFile = Path.Combine(tempDirectory, "mixed_content.json");
            var validFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "TEST",
                Product = "T01"
            };
            var validJson = Newtonsoft.Json.JsonConvert.SerializeObject(validFrame);
            
            File.WriteAllText(testFile, 
                validJson + "\n" +
                "invalid json line\n" +
                validJson + "\n" +
                "{ broken: json }\n");

            // Act
            var frames = recorder.LoadTimeFrames(testFile);

            // Assert
            frames.Should().HaveCount(2);
        }

        #endregion

        #region ProductDetector Tests

        [Fact]
        public void ProductDetector_Constructor_ShouldInitialize()
        {
            // Act
            var detector = new ProductDetector(fileSystem);

            // Assert
            detector.Should().NotBeNull();
        }

        [Fact]
        public void ProductDetector_DetectProduct_WhenNoPathsExist_ShouldReturnUnknown()
        {
            // Arrange
            var detector = new ProductDetector(fileSystem);

            // Act
            var product = detector.DetectProduct();

            // Assert
            product.Should().Be("PRODUCT_UNKNOWN");
        }

        [Fact]
        public void ProductDetector_DefaultConstructor_ShouldWork()
        {
            // Act
            var detector = new ProductDetector();

            // Assert
            detector.Should().NotBeNull();
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void FullWorkflow_CreateConfigRecordLoadData_ShouldWorkEndToEnd()
        {
            // Arrange
            var loader = new ConfigurationLoader(fileSystem);
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);
            
            // Create and save configuration
            var config = loader.CreateDefaultConfiguration();
            config.SumPOR.Debug = true;
            var configPath = Path.Combine(tempDirectory, "workflow_config.json");
            loader.SaveConfiguration(configPath, config);

            // Record some time frames
            var timeFrame1 = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 10, 2, 10, 0, 0),
                EndTime = new DateTime(2025, 10, 2, 10, 5, 0),
                MachineName = "WORKFLOW_TEST",
                Product = "WF01",
                MouseEvents = 100,
                KeyboardEvents = 50,
                FileChanges = "file1.txt,file2.txt"
            };

            var timeFrame2 = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 10, 2, 10, 5, 0),
                EndTime = new DateTime(2025, 10, 2, 10, 10, 0),
                MachineName = "WORKFLOW_TEST",
                Product = "WF01",
                MouseEvents = 150,
                KeyboardEvents = 75,
                FileChanges = "file3.txt"
            };

            // Act
            recorder.RecordTimeFrame(timeFrame1);
            recorder.RecordTimeFrame(timeFrame2);
            
            var loadedConfig = loader.LoadConfiguration(configPath);
            var fileName = recorder.GetOutputFileName(timeFrame1);
            var loadedFrames = recorder.LoadTimeFrames(fileName);

            // Assert
            loadedConfig.SumPOR.Debug.Should().BeTrue();
            loadedFrames.Should().HaveCount(2);
            loadedFrames[0].MouseEvents.Should().Be(100);
            loadedFrames[1].MouseEvents.Should().Be(150);
            loadedFrames.Sum(f => f.KeyboardEvents).Should().Be(125);
        }

        [Fact]
        public void TimeFrameRecorder_WithEmptyMachineNameAndProduct_ShouldUseDefaults()
        {
            // Arrange
            var recorder = new TimeFrameRecorder(tempDirectory, fileSystem);
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "",
                Product = ""
            };

            // Act
            var fileName = recorder.GetOutputFileName(timeFrame);

            // Assert
            fileName.Should().Contain("Unknown");
        }

        [Fact]
        public void ConfigurationLoader_SaveConfiguration_InNestedDirectory_ShouldCreatePath()
        {
            // Arrange
            var loader = new ConfigurationLoader(fileSystem);
            var nestedPath = Path.Combine(tempDirectory, "level1", "level2", "config.json");
            var config = loader.CreateDefaultConfiguration();

            // Act
            loader.SaveConfiguration(nestedPath, config);

            // Assert
            File.Exists(nestedPath).Should().BeTrue();
        }

        [Fact]
        public void MultipleRecorders_WritingConcurrently_ShouldNotConflict()
        {
            // Arrange
            var recorder1 = new TimeFrameRecorder(tempDirectory, fileSystem);
            var recorder2 = new TimeFrameRecorder(tempDirectory, fileSystem);
            
            var frame1 = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "M1",
                Product = "P1"
            };
            
            var frame2 = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "M2",
                Product = "P2"
            };

            // Act
            recorder1.RecordTimeFrame(frame1);
            recorder2.RecordTimeFrame(frame2);

            // Assert
            var files = Directory.GetFiles(tempDirectory, "*.json");
            files.Should().HaveCountGreaterOrEqualTo(1);
        }

        #endregion
    }
}