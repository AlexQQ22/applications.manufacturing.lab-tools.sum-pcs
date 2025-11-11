using System;
using System.Threading;
using System.Threading.Tasks;

namespace SystemUtilizationMonitor.Services
{
    // Base background service for long-running tasks
    public abstract class BaseBackgroundService : IDisposable
    {
        private Task executingTask;
        private readonly CancellationTokenSource stoppingCts = new CancellationTokenSource();

        public virtual Task StartAsync(CancellationToken cancellationToken = default)
        {
            // Store the task we're executing
            executingTask = ExecuteAsync(stoppingCts.Token);

            // If the task is completed then return it, this will bubble cancellation and failure to the caller
            if (executingTask.IsCompleted)
            {
                return executingTask;
            }

            // Otherwise it's running
            return Task.CompletedTask;
        }

        public virtual async Task StopAsync(CancellationToken cancellationToken = default)
        {
            // Stop called without start
            if (executingTask == null)
            {
                return;
            }

            try
            {
                // Signal cancellation to the executing method
                stoppingCts.Cancel();
            }
            finally
            {
                // Wait until the task completes or the stop token triggers
                await Task.WhenAny(executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }

        public virtual void Dispose()
        {
            stoppingCts.Cancel();
            stoppingCts.Dispose();
        }

        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
    }
}