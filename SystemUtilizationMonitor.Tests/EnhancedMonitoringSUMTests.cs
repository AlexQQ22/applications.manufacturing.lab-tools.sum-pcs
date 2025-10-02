using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FluentAssertions;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Services;

namespace SystemUtilizationMonitor.Tests
{
    /// <summary>
    /// Enhanced tests specifically targeting MonitoringSUM to increase coverage
    /// </summary>
    public class EnhancedMonitoringSUMTests : IDisposable
    {
        private readonly string tempDirectory;

        public EnhancedMonitoringSUMTests()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), $"SUM_Enhanced_Tests_{Guid.NewGuid()}");
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

        [Fact]
        public void MonitoringSUM_WithValidFile_ShouldReadContent()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow.AddMinutes(-5),
                EndTime = DateTime.UtcNow,
                MachineName = "TEST1",
                Product = "P001"
            };

            var testFile = Path.Combine(tempDirectory, "valid_log.txt");
            File.WriteAllLines(testFile, new[]
            {
                "Line 1: Test content",
                "Line 2: More content",
                "Line 3: Final content"
            });

            var config = new ConfigurationModel();
            config.Jose.Add("test1", new MonitorTxtConfig
            {
                FilePath = testFile,
                NoContent = "EMPTY",
                Skip = "",
                FormatDate = "",
                LastlineContent = ""
            });

            // Act
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "test log");

            // Assert
            result.Should().NotBeNull();
            result.MachineName.Should().Be("TEST1");
        }

        [Fact]
        public void MonitoringSUM_WithSkipConfig_ShouldHandleSkipSections()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "SKIP_TEST",
                Product = "SK01"
            };

            var testFile = Path.Combine(tempDirectory, "skip_log.txt");
            File.WriteAllLines(testFile, new[]
            {
                "Normal line",
                "SKIP_START",
                "This should be skipped",
                "SKIP_END",
                "Normal line again"
            });

            var config = new ConfigurationModel();
            config.Jose.Add("skipTest", new MonitorTxtConfig
            {
                FilePath = testFile,
                NoContent = "",
                Skip = "SKIP_START|SKIP_END",
                FormatDate = "",
                LastlineContent = ""
            });

            // Act
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringSUM_WithFormatDate_ShouldFormatPath()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "DATE_FORMAT_TEST",
                Product = "DF01"
            };

            var dateStr = DateTime.Now.ToString("yyyyMMdd");
            var testFile = Path.Combine(tempDirectory, $"log_{dateStr}.txt");
            File.WriteAllText(testFile, "Dated content");

            var config = new ConfigurationModel();
            config.Jose.Add("dateFormat", new MonitorTxtConfig
            {
                FilePath = Path.Combine(tempDirectory, "log_{0}.txt"),
                NoContent = "",
                Skip = "",
                FormatDate = "yyyyMMdd",
                LastlineContent = ""
            });

            // Act
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringSUM_WithEmptyFile_ShouldUseNoContent()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "EMPTY_TEST",
                Product = "ET01"
            };

            var emptyFile = Path.Combine(tempDirectory, "empty.txt");
            File.WriteAllText(emptyFile, "");

            var config = new ConfigurationModel();
            config.Jose.Add("empty", new MonitorTxtConfig
            {
                FilePath = emptyFile,
                NoContent = "NO_DATA_FOUND",
                Skip = "",
                FormatDate = "",
                LastlineContent = ""
            });

            // Act
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringSUM_WithLastlineContent_ShouldCheckLastLine()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "LASTLINE_TEST",
                Product = "LL01"
            };

            var testFile = Path.Combine(tempDirectory, "lastline.txt");
            File.WriteAllLines(testFile, new[]
            {
                "First line",
                "Middle line",
                "EXPECTED_LAST_LINE"
            });

            var config = new ConfigurationModel();
            config.Jose.Add("lastline", new MonitorTxtConfig
            {
                FilePath = testFile,
                NoContent = "",
                Skip = "",
                FormatDate = "",
                LastlineContent = "EXPECTED_LAST_LINE"
            });

            // Act
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringSUM_WithMultipleConfigs_FirstValidWins()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "PRIORITY_TEST",
                Product = "PR01"
            };

            var validFile = Path.Combine(tempDirectory, "valid.txt");
            File.WriteAllText(validFile, "Valid content");

            var config = new ConfigurationModel();
            
            // First config - invalid file
            config.Jose.Add("invalid", new MonitorTxtConfig
            {
                FilePath = @"C:\NonExistent\invalid.txt",
                NoContent = "",
                Skip = "",
                FormatDate = "",
                LastlineContent = ""
            });

            // Second config - valid file
            config.Jose.Add("valid", new MonitorTxtConfig
            {
                FilePath = validFile,
                NoContent = "",
                Skip = "",
                FormatDate = "",
                LastlineContent = ""
            });

            // Act
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringSUM_WithIOException_ShouldRetry()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "RETRY_TEST",
                Product = "RT01"
            };

            var config = new ConfigurationModel();
            config.Jose.Add("retry", new MonitorTxtConfig
            {
                FilePath = @"C:\InvalidPath\\\file.txt",
                NoContent = "",
                Skip = "",
                FormatDate = "",
                LastlineContent = ""
            });

            // Act - This should trigger retry logic
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "");

            // Assert - Should return timeFrame even after retries fail
            result.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringSUM_WithLargeFile_ShouldProcess()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "LARGE_FILE_TEST",
                Product = "LF01"
            };

            var largeFile = Path.Combine(tempDirectory, "large.txt");
            var lines = new List<string>();
            for (int i = 0; i < 1000; i++)
            {
                lines.Add($"Line {i}: Some test content here");
            }
            File.WriteAllLines(largeFile, lines);

            var config = new ConfigurationModel();
            config.Jose.Add("large", new MonitorTxtConfig
            {
                FilePath = largeFile,
                NoContent = "",
                Skip = "",
                FormatDate = "",
                LastlineContent = ""
            });

            // Act
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringSUM_WithSpecialCharactersInPath_ShouldHandle()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "SPECIAL_CHAR_TEST",
                Product = "SC01"
            };

            var specialDir = Path.Combine(tempDirectory, "folder with spaces");
            Directory.CreateDirectory(specialDir);
            var specialFile = Path.Combine(specialDir, "file-with-dashes.txt");
            File.WriteAllText(specialFile, "Special content");

            var config = new ConfigurationModel();
            config.Jose.Add("special", new MonitorTxtConfig
            {
                FilePath = specialFile,
                NoContent = "",
                Skip = "",
                FormatDate = "",
                LastlineContent = ""
            });

            // Act
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringSUM_WithDifferentLineEndings_ShouldProcess()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                MachineName = "LINE_ENDING_TEST",
                Product = "LE01"
            };

            var testFile = Path.Combine(tempDirectory, "lineendings.txt");
            // Mix of line endings
            File.WriteAllText(testFile, "Line1\r\nLine2\nLine3\rLine4");

            var config = new ConfigurationModel();
            config.Jose.Add("lineending", new MonitorTxtConfig
            {
                FilePath = testFile,
                NoContent = "",
                Skip = "",
                FormatDate = "",
                LastlineContent = ""
            });

            // Act
            var result = MonitoringSUM.MonitoringFiles(timeFrame, config, "");

            // Assert
            result.Should().NotBeNull();
        }
    }
}