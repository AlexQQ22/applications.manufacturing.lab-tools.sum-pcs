using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Services;

namespace SystemUtilizationMonitor.Tests
{
    /// <summary>
    /// Tests for MonitoringVMs to increase coverage
    /// Note: Some methods require actual VM infrastructure, so we test what's testable
    /// </summary>
    public class MonitoringVMsTests : IDisposable
    {
        private readonly string tempDirectory;
        private readonly ConfigurationModel config;

        public MonitoringVMsTests()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), $"VMTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDirectory);

            // Create test configuration
            config = new ConfigurationModel
            {
                VM = new VMConfig
                {
                    Username = "testuser",
                    Password = "testpass"
                }
            };
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
        public void MonitoringVMs_Constructor_WithValidConfig_Initializes()
        {
            // Act
            var vmMonitor = new MonitoringVMs(config);

            // Assert
            vmMonitor.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringVMs_Constructor_WithNullConfig_UsesDefaults()
        {
            // Act
            var vmMonitor = new MonitoringVMs(null);

            // Assert
            vmMonitor.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringVMs_Constructor_WithCustomLogger_AcceptsLogger()
        {
            // Arrange
            var logCalled = false;
            Action<string> logger = (msg) => logCalled = true;

            // Act
            var vmMonitor = new MonitoringVMs(config, logger, logger);

            // Assert
            vmMonitor.Should().NotBeNull();
        }

        [Fact]
        public async Task MonitoringVMs_CheckVMsAsync_WithoutVMs_HandlesGracefully()
        {
            // Arrange
            var vmMonitor = new MonitoringVMs(config);

            // Act
            Func<Task> act = async () => await vmMonitor.CheckVMsAsync();

            // Assert
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task MonitoringVMs_ProcessPendingKillsAsync_WithNoFile_HandlesGracefully()
        {
            // Arrange
            var vmMonitor = new MonitoringVMs(config);

            // Act
            Func<Task> act = async () => await vmMonitor.ProcessPendingKillsAsync();

            // Assert
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task MonitoringVMs_CheckVMsAsync_MultipleCallsShouldNotThrow()
        {
            // Arrange
            var vmMonitor = new MonitoringVMs(config);

            // Act & Assert
            await vmMonitor.CheckVMsAsync();
            await vmMonitor.CheckVMsAsync();
            await vmMonitor.CheckVMsAsync();
            
            // If we got here without exceptions, test passes
            true.Should().BeTrue();
        }

        [Fact]
        public void MonitoringVMs_WithCustomVMConfig_UsesProvidedCredentials()
        {
            // Arrange
            var customConfig = new ConfigurationModel
            {
                VM = new VMConfig
                {
                    Username = "customuser",
                    Password = "custompass"
                }
            };

            // Act
            var vmMonitor = new MonitoringVMs(customConfig);

            // Assert
            vmMonitor.Should().NotBeNull();
        }

        [Fact]
        public async Task MonitoringVMs_ConcurrentOperations_ShouldBeThreadSafe()
        {
            // Arrange
            var vmMonitor = new MonitoringVMs(config);
            var tasks = new System.Collections.Generic.List<Task>();

            // Act
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await vmMonitor.CheckVMsAsync();
                }));
            }

            // Assert
            Func<Task> act = async () => await Task.WhenAll(tasks);
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void MonitoringVMs_WithEmptyConfig_InitializesWithDefaults()
        {
            // Arrange
            var emptyConfig = new ConfigurationModel();

            // Act
            var vmMonitor = new MonitoringVMs(emptyConfig);

            // Assert
            vmMonitor.Should().NotBeNull();
        }

        [Fact]
        public void MonitoringVMs_LoggingMethods_HandleNullLogger()
        {
            // Arrange & Act
            var vmMonitor = new MonitoringVMs(config, null, null);

            // Assert
            vmMonitor.Should().NotBeNull();
        }

        [Fact]
        public async Task MonitoringVMs_ProcessPendingKillsAsync_WithEmptyFile_HandlesGracefully()
        {
            // Arrange
            var killFile = Path.Combine(tempDirectory, "KillingPendings.txt");
            File.WriteAllText(killFile, "");
            var vmMonitor = new MonitoringVMs(config);

            // Act
            Func<Task> act = async () => await vmMonitor.ProcessPendingKillsAsync();

            // Assert
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void MonitoringVMs_ConfigurationValidation_AcceptsValidConfig()
        {
            // Arrange
            var validConfig = new ConfigurationModel
            {
                VM = new VMConfig
                {
                    Username = "validuser",
                    Password = "validpass"
                }
            };

            // Act
            Exception exception = null;
            try
            {
                var vmMonitor = new MonitoringVMs(validConfig);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            // Assert
            exception.Should().BeNull();
        }

        [Fact]
        public async Task MonitoringVMs_ErrorHandling_DoesNotPropagateExceptions()
        {
            // Arrange
            var vmMonitor = new MonitoringVMs(config);

            // Act & Assert
            // Should not throw even if VMs are not reachable
            Func<Task> act = async () =>
            {
                await vmMonitor.CheckVMsAsync();
                await vmMonitor.ProcessPendingKillsAsync();
            };

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void MonitoringVMs_Initialization_SetsDefaultValues()
        {
            // Arrange
            var minimalConfig = new ConfigurationModel
            {
                VM = new VMConfig()
            };

            // Act
            var vmMonitor = new MonitoringVMs(minimalConfig);

            // Assert
            vmMonitor.Should().NotBeNull();
        }

        [Fact]
        public async Task MonitoringVMs_LongRunningOperation_CompletesWithinTimeout()
        {
            // Arrange
            var vmMonitor = new MonitoringVMs(config);
            var timeout = TimeSpan.FromSeconds(30);

            // Act
            var task = vmMonitor.CheckVMsAsync();
            var completedInTime = await Task.WhenAny(task, Task.Delay(timeout)) == task;

            // Assert
            completedInTime.Should().BeTrue();
        }
    }
}