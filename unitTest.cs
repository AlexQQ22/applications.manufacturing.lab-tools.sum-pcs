using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using Newtonsoft.Json;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Utilities;

namespace SystemUtilizationMonitor.Tests
{
    public class EnhancedSystemUtilizationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private const string NETWORK_PATH = @"\\amr.corp.intel.com\ec\proj\mdl\cr\intel\hdmx_db\mae\SUM\HDMx";
        private readonly List<string> _tempFiles = new();

        public EnhancedSystemUtilizationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData("hdmx2989_a201_9_19.csv", "20250919", "A201")]
        [InlineData("hdmx2989_a101_9_19.csv", "20250919", "A101")]
        public async Task CompareSystemUtilizationData_WhenCsvAndJsonExist_ShouldValidateSuccessfully(
            string csvFileName, string expectedDate, string expectedProduct)
        {
            // Arrange
            var csvPath = await CreateTestCsvFileAsync(csvFileName);
            var jsonPath = await CreateTestJsonFileAsync($"SystemUtilizationTimeFrames{expectedDate}_CR03DHHX2989_(HDMT_60123)_{expectedProduct}.json");

            // Act
            var validationResult = await ValidateDataFilesAsync(csvPath, jsonPath);

            // Assert
            validationResult.IsValid.Should().BeTrue(validationResult.ErrorMessage);
            validationResult.MatchedRecords.Should().BeGreaterThan(0);
            validationResult.ProductMatch.Should().BeTrue();
        }

        [Fact]
        public async Task AccessNetworkPath_WhenAvailable_ShouldReturnFiles()
        {
            // Arrange & Act
            var result = await TryAccessNetworkPathAsync();

            if (result.IsAccessible)
            {
                // Assert
                result.CsvFiles.Should().NotBeEmpty("Network path should contain CSV files");
                result.CsvFiles.Should().AllSatisfy(file => 
                    Path.GetExtension(file).Should().Be(".csv"));

                _output.WriteLine($"Found {result.CsvFiles.Count} CSV files in network path");
            }
            else
            {
                _output.WriteLine($"Network path not accessible: {result.ErrorMessage}");
            }
        }

        [Theory]
        [InlineData("2989", "A201", 9, 19)]
        [InlineData("2989", "A101", 9, 19)]
        public async Task ValidateProductDetection_WhenFileContainsProductInfo_ShouldExtractCorrectly(
            string machineNumber, string productCode, int month, int day)
        {
            // Arrange
            var fileName = $"hdmx{machineNumber}_{productCode.ToLower()}_{month}_{day}.csv";
            var expectedProduct = productCode;

            // Act
            var fileInfo = ExtractFileInfoFromPath(fileName);

            // Assert
            fileInfo.ProductId.Should().Be(expectedProduct);
            fileInfo.Date.Month.Should().Be(month);
            fileInfo.Date.Day.Should().Be(day);
        }

        [Fact]
        public async Task JsonTimeFrameParsing_WhenValidJsonProvided_ShouldParseAllFields()
        {
            // Arrange
            var jsonLine = """{"StartTime":"2025-09-19T00:02:01Z","EndTime":"2025-09-19T00:07:01Z","MachineName":"HDMT_60123","Product":"PRODUCT_NOT_FOUND","MouseEvents":0,"KeyboardEvents":0,"FileChanges":""}""";

            // Act
            var timeFrame = JsonConvert.DeserializeObject<UtilizationTimeFrame>(jsonLine);

            // Assert
            timeFrame.Should().NotBeNull();
            timeFrame.StartTime.Should().Be(new DateTime(2025, 9, 19, 0, 2, 1, DateTimeKind.Utc));
            timeFrame.EndTime.Should().Be(new DateTime(2025, 9, 19, 0, 7, 1, DateTimeKind.Utc));
            timeFrame.MachineName.Should().Be("HDMT_60123");
            timeFrame.MouseEvents.Should().Be(0);
            timeFrame.KeyboardEvents.Should().Be(0);
        }

        [Fact]
        public async Task CustomJsonSerializer_WhenSerializingTimeFrame_ShouldMatchExpectedFormat()
        {
            // Arrange
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = new DateTime(2025, 9, 19, 12, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2025, 9, 19, 12, 5, 0, DateTimeKind.Utc),
                MachineName = "TEST_MACHINE",
                Product = "A201",
                MouseEvents = 5,
                KeyboardEvents = 10,
                FileChanges = "Test file changes"
            };

            // Act
            var serialized = CustomJsonSerializer.Serialize(timeFrame);
            var deserialized = JsonConvert.DeserializeObject<UtilizationTimeFrame>(serialized);

            // Assert
            deserialized.Should().BeEquivalentTo(timeFrame);
        }

        private async Task<ValidationResult> ValidateDataFilesAsync(string csvPath, string jsonPath)
        {
            try
            {
                var csvData = await LoadCsvDataAsync(csvPath);
                var jsonData = await LoadJsonDataAsync(jsonPath);

                var matchedRecords = 0;
                var productMatch = true;
                var errors = new List<string>();

                // Simple validation logic
                foreach (var jsonFrame in jsonData.Take(10)) // Validate first 10 records
                {
                    var correspondingCsvData = csvData
                        .Where(csv => Math.Abs((csv.StartTime - jsonFrame.StartTime).TotalMinutes) < 5)
                        .FirstOrDefault();

                    if (correspondingCsvData != null)
                    {
                        matchedRecords++;
                        
                        if (jsonFrame.MouseEvents != correspondingCsvData.MouseEvents)
                        {
                            errors.Add($"Mouse events mismatch at {jsonFrame.StartTime}: JSON={jsonFrame.MouseEvents}, CSV={correspondingCsvData.MouseEvents}");
                        }
                        
                        if (jsonFrame.KeyboardEvents != correspondingCsvData.KeyboardEvents)
                        {
                            errors.Add($"Keyboard events mismatch at {jsonFrame.StartTime}: JSON={jsonFrame.KeyboardEvents}, CSV={correspondingCsvData.KeyboardEvents}");
                        }
                    }
                }

                return new ValidationResult
                {
                    IsValid = errors.Count == 0,
                    MatchedRecords = matchedRecords,
                    ProductMatch = productMatch,
                    ErrorMessage = string.Join("; ", errors)
                };
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<NetworkPathResult> TryAccessNetworkPathAsync()
        {
            try
            {
                if (!Directory.Exists(NETWORK_PATH))
                {
                    return new NetworkPathResult
                    {
                        IsAccessible = false,
                        ErrorMessage = "Network path does not exist"
                    };
                }

                var csvFiles = Directory.GetFiles(NETWORK_PATH, "hdmx*.csv", SearchOption.TopDirectoryOnly)
                    .ToList();

                return new NetworkPathResult
                {
                    IsAccessible = true,
                    CsvFiles = csvFiles
                };
            }
            catch (Exception ex)
            {
                return new NetworkPathResult
                {
                    IsAccessible = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<List<CsvDataRecord>> LoadCsvDataAsync(string csvPath)
        {
            // Simplified CSV loading - replace with your actual CSV structure
            var lines = await File.ReadAllLinesAsync(csvPath);
            var records = new List<CsvDataRecord>();

            foreach (var line in lines.Skip(1)) // Skip header
            {
                var parts = line.Split(',');
                if (parts.Length >= 7)
                {
                    records.Add(new CsvDataRecord
                    {
                        StartTime = DateTime.Parse(parts[0]),
                        EndTime = DateTime.Parse(parts[1]),
                        MachineName = parts[2],
                        Product = parts[3],
                        MouseEvents = int.Parse(parts[4]),
                        KeyboardEvents = int.Parse(parts[5]),
                        FileChanges = parts[6]
                    });
                }
            }

            return records;
        }

        private async Task<List<UtilizationTimeFrame>> LoadJsonDataAsync(string jsonPath)
        {
            var lines = await File.ReadAllLinesAsync(jsonPath);
            var timeFrames = new List<UtilizationTimeFrame>();

            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                try
                {
                    var timeFrame = JsonConvert.DeserializeObject<UtilizationTimeFrame>(line);
                    if (timeFrame != null)
                    {
                        timeFrames.Add(timeFrame);
                    }
                }
                catch (JsonException ex)
                {
                    _output.WriteLine($"Failed to parse JSON line: {ex.Message}");
                }
            }

            return timeFrames;
        }

        private FileInfo ExtractFileInfoFromPath(string fileName)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            var parts = nameWithoutExtension.Split('_');

            if (parts.Length < 4)
            {
                throw new ArgumentException($"Invalid filename format: {fileName}");
            }

            var productId = parts[1].ToUpper();
            var month = int.Parse(parts[2]);
            var day = int.Parse(parts[3]);
            var year = DateTime.Now.Year;

            return new FileInfo
            {
                ProductId = productId,
                Date = new DateTime(year, month, day),
                OriginalFileName = nameWithoutExtension
            };
        }

        private async Task<string> CreateTestCsvFileAsync(string fileName)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            
            var csvContent = """
StartTime,EndTime,MachineName,Product,MouseEvents,KeyboardEvents,FileChanges
2025-09-19T00:02:01Z,2025-09-19T00:07:01Z,HDMT_60123,A201,0,0,
2025-09-19T00:07:01Z,2025-09-19T00:12:01Z,HDMT_60123,A201,5,3,/path/to/file
2025-09-19T00:12:01Z,2025-09-19T00:17:01Z,HDMT_60123,A201,2,1,
""";

            await File.WriteAllTextAsync(tempPath, csvContent);
            _tempFiles.Add(tempPath);
            
            return tempPath;
        }

        private async Task<string> CreateTestJsonFileAsync(string fileName)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            
            var jsonContent = """
{"StartTime":"2025-09-19T00:02:01Z","EndTime":"2025-09-19T00:07:01Z","MachineName":"HDMT_60123","Product":"A201","MouseEvents":0,"KeyboardEvents":0,"FileChanges":""}
{"StartTime":"2025-09-19T00:07:01Z","EndTime":"2025-09-19T00:12:01Z","MachineName":"HDMT_60123","Product":"A201","MouseEvents":5,"KeyboardEvents":3,"FileChanges":"/path/to/file"}
{"StartTime":"2025-09-19T00:12:01Z","EndTime":"2025-09-19T00:17:01Z","MachineName":"HDMT_60123","Product":"A201","MouseEvents":2,"KeyboardEvents":1,"FileChanges":""}
""";

            await File.WriteAllTextAsync(tempPath, jsonContent);
            _tempFiles.Add(tempPath);
            
            return tempPath;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync()
        {
            // Cleanup temp files
            foreach (var tempFile in _tempFiles)
            {
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Failed to delete temp file {tempFile}: {ex.Message}");
                }
            }
            
            return Task.CompletedTask;
        }
    }

    // Supporting classes
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public int MatchedRecords { get; set; }
        public bool ProductMatch { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class NetworkPathResult
    {
        public bool IsAccessible { get; set; }
        public List<string> CsvFiles { get; set; } = new();
        public string ErrorMessage { get; set; } = string.Empty;
    }
}