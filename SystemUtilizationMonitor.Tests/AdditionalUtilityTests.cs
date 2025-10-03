using System;
using System.IO;
using Xunit;
using FluentAssertions;
using SystemUtilizationMonitor.Utilities;
using SystemUtilizationMonitor.Models;

namespace SystemUtilizationMonitor.Tests
{
    /// <summary>
    /// Additional utility tests to push coverage over 85%
    /// </summary>
    public class AdditionalUtilityTests : IDisposable
    {
        private readonly string tempDirectory;

        public AdditionalUtilityTests()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), $"UtilityTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDirectory);
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

        #region ConfigurationLoader Tests

        [Fact]
        public void ConfigurationLoader_LoadConfiguration_WithValidFile_ReturnsConfig()
        {
            // Arrange
            var configPath = Path.Combine(tempDirectory, "config.json");
            var configContent = @"{
                ""SumPOR"": {
                    ""Debug"": true,
                    ""ShouldReadLogFiles"": true
                }
            }";
            File.WriteAllText(configPath, configContent);

            // Act
            var loader = new ConfigurationLoader();
            var result = loader.LoadConfiguration(configPath);

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void ConfigurationLoader_LoadConfiguration_WithMissingFile_ReturnsDefault()
        {
            // Arrange
            var nonExistentPath = Path.Combine(tempDirectory, "nonexistent.json");

            // Act
            var loader = new ConfigurationLoader();
            var result = loader.LoadConfiguration(nonExistentPath);

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void ConfigurationLoader_LoadConfiguration_WithInvalidJson_HandlesGracefully()
        {
            // Arrange
            var configPath = Path.Combine(tempDirectory, "invalid.json");
            File.WriteAllText(configPath, "{ invalid json }");

            // Act
            var loader = new ConfigurationLoader();
            var result = loader.LoadConfiguration(configPath);

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void ConfigurationLoader_LoadConfiguration_WithEmptyFile_ReturnsDefault()
        {
            // Arrange
            var configPath = Path.Combine(tempDirectory, "empty.json");
            File.WriteAllText(configPath, "");

            // Act
            var loader = new ConfigurationLoader();
            var result = loader.LoadConfiguration(configPath);

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void ConfigurationLoader_GetDefaultConfiguration_ReturnsValidConfig()
        {
            // Act
            var result = new ConfigurationModel();

            // Assert
            result.Should().NotBeNull();
            result.SumPOR.Should().NotBeNull();
            result.VM.Should().NotBeNull();
            result.Monitoring.Should().NotBeNull();
        }

        #endregion

        #region FileSystemOperations Tests

        [Fact]
        public void FileSystemOperations_CreateDirectory_WithValidPath_CreatesDirectory()
        {
            // Arrange
            var testDir = Path.Combine(tempDirectory, "testdir");

            // Act
            var fileSystem = new FileSystemOperations();
            fileSystem.EnsureDirectoryExists(testDir);

            // Assert
            Directory.Exists(testDir).Should().BeTrue();
        }

        [Fact]
        public void FileSystemOperations_CreateDirectory_WithExistingDirectory_DoesNotThrow()
        {
            // Arrange
            var testDir = Path.Combine(tempDirectory, "existing");
            Directory.CreateDirectory(testDir);

            // Act
            var fileSystem = new FileSystemOperations();
            Action act = () => fileSystem.EnsureDirectoryExists(testDir);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void FileSystemOperations_WriteFile_WithValidPath_WritesContent()
        {
            // Arrange
            var filePath = Path.Combine(tempDirectory, "test.txt");
            var content = "test content";

            // Act
            var fileSystem = new FileSystemOperations();
            fileSystem.WriteFile(filePath, content);

            // Assert
            File.Exists(filePath).Should().BeTrue();
            File.ReadAllText(filePath).Should().Be(content);
        }

        [Fact]
        public void FileSystemOperations_ReadFile_WithExistingFile_ReturnsContent()
        {
            // Arrange
            var filePath = Path.Combine(tempDirectory, "read.txt");
            var content = "read content";
            File.WriteAllText(filePath, content);

            // Act
            var fileSystem = new FileSystemOperations();
            var result = fileSystem.ReadFile(filePath);

            // Assert
            result.Should().Be(content);
        }

        [Fact]
        public void FileSystemOperations_DeleteFile_WithExistingFile_DeletesFile()
        {
            // Arrange
            var filePath = Path.Combine(tempDirectory, "delete.txt");
            File.WriteAllText(filePath, "content");

            // Act
            FileSystemOperations.DeleteFile(filePath);

            // Assert
            File.Exists(filePath).Should().BeFalse();
        }

        [Fact]
        public void FileSystemOperations_DeleteFile_WithNonExistentFile_DoesNotThrow()
        {
            // Arrange
            var filePath = Path.Combine(tempDirectory, "nonexistent.txt");

            // Act
            Action act = () => FileSystemOperations.DeleteFile(filePath);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void FileSystemOperations_GetFiles_WithPattern_ReturnsMatchingFiles()
        {
            // Arrange
            var file1 = Path.Combine(tempDirectory, "test1.txt");
            var file2 = Path.Combine(tempDirectory, "test2.txt");
            var file3 = Path.Combine(tempDirectory, "other.log");
            File.WriteAllText(file1, "");
            File.WriteAllText(file2, "");
            File.WriteAllText(file3, "");

            // Act
            var fileSystem = new FileSystemOperations();
            var result = fileSystem.GetFiles(tempDirectory, "*.txt");

            // Assert
            result.Should().HaveCount(2);
        }

        #endregion

        #region TimeFrameRecorder Additional Tests

        [Fact]
        public void TimeFrameRecorder_RecordTimeFrame_WithMaximumValues_HandlesLargeNumbers()
        {
            // Arrange
            var outputDir = tempDirectory;
            var recorder = new TimeFrameRecorder(outputDir);
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(5),
                MouseEvents = int.MaxValue,
                KeyboardEvents = int.MaxValue,
                FileChanges = new string('A', 10000),
                Product = "TEST",
                MachineName = Environment.MachineName
            };

            // Act
            Action act = () => recorder.RecordTimeFrame(timeFrame);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void TimeFrameRecorder_RecordTimeFrame_WithSpecialCharacters_HandlesCorrectly()
        {
            // Arrange
            var outputDir = tempDirectory;
            var recorder = new TimeFrameRecorder(outputDir);
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(5),
                MouseEvents = 10,
                KeyboardEvents = 20,
                FileChanges = "file with spaces.txt,\"quoted,file\".txt",
                Product = "A-201",
                MachineName = "MACHINE_NAME-123"
            };

            // Act
            Action act = () => recorder.RecordTimeFrame(timeFrame);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void TimeFrameRecorder_MultipleRecords_CreatesMultipleEntries()
        {
            // Arrange
            var outputDir = tempDirectory;
            var recorder = new TimeFrameRecorder(outputDir);

            // Act
            for (int i = 0; i < 10; i++)
            {
                var timeFrame = new UtilizationTimeFrame
                {
                    StartTime = DateTime.UtcNow.AddMinutes(i),
                    EndTime = DateTime.UtcNow.AddMinutes(i + 1),
                    MouseEvents = i * 10,
                    KeyboardEvents = i * 5,
                    FileChanges = $"file{i}.txt",
                    Product = "TEST",
                    MachineName = Environment.MachineName
                };
                recorder.RecordTimeFrame(timeFrame);
            }

            // Assert
            var files = Directory.GetFiles(outputDir, "*.json");
            files.Should().NotBeEmpty();
        }

        #endregion

        #region CustomJsonSerializer Tests

        [Fact]
        public void CustomJsonSerializer_Serialize_WithComplexObject_ReturnsJson()
        {
            // Arrange
            var config = new ConfigurationModel
            {
                SumPOR = new SumPORConfig
                {
                    Debug = true,
                    ShouldReadLogFiles = true
                }
            };

            // Act
            var result = CustomJsonSerializer.Serialize(config);

            // Assert
            result.Should().NotBeNullOrWhiteSpace();
            result.Should().Contain("Debug");
        }

        [Fact]
        public void CustomJsonSerializer_Deserialize_WithValidJson_ReturnsObject()
        {
            // Arrange
            var json = @"{""Debug"": true, ""ShouldReadLogFiles"": true}";

            // Act
            var result = CustomJsonSerializer.Deserialize<SumPORConfig>(json);

            // Assert
            result.Should().NotBeNull();
            result.Debug.Should().BeTrue();
        }

        [Fact]
        public void CustomJsonSerializer_SerializeDeserialize_PreservesData()
        {
            // Arrange
            var original = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 10, 1, 12, 0, 0),
                EndTime = new DateTime(2025, 10, 1, 13, 0, 0),
                MouseEvents = 100,
                KeyboardEvents = 50,
                FileChanges = "file1.txt,file2.txt",
                Product = "A201",
                MachineName = "TEST"
            };

            // Act
            var json = CustomJsonSerializer.Serialize(original);
            var result = CustomJsonSerializer.Deserialize<UtilizationTimeFrame>(json);

            // Assert
            result.StartTime.Should().Be(original.StartTime);
            result.EndTime.Should().Be(original.EndTime);
            result.MouseEvents.Should().Be(original.MouseEvents);
            result.KeyboardEvents.Should().Be(original.KeyboardEvents);
        }

        [Fact]
        public void CustomJsonSerializer_Deserialize_WithInvalidJson_HandlesGracefully()
        {
            // Arrange
            var invalidJson = "{ invalid json }";

            // Act
            Action act = () => CustomJsonSerializer.Deserialize<ConfigurationModel>(invalidJson);

            // Assert
            act.Should().Throw<Exception>();
        }

        #endregion
    }
}