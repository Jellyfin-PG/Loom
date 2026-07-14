using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Loom.Middleware
{
    public class StartupBarrier
    {
        private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ILogger<StartupBarrier> _logger;

        public StartupBarrier(IHostApplicationLifetime lifetime, ILogger<StartupBarrier> logger)
        {
            _logger = logger;
            
            lifetime.ApplicationStarted.Register(() => 
            {
                _logger.LogInformation("Application has started. Loom startup barrier cleared.");
                _tcs.TrySetResult(true);
            });
            
            var config = Plugin.Instance?.Configuration;
            int timeoutSeconds = config?.StartupTimeoutSeconds ?? 10;
            
            Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)).ContinueWith(_ => 
            {
                if (!_tcs.Task.IsCompleted)
                {
                    _logger.LogWarning("Loom startup barrier timed out after {Timeout}s. Clearing barrier anyway.", timeoutSeconds);
                    _tcs.TrySetResult(false);
                }
            });
        }
        
        public Task WaitForStartupAsync() => _tcs.Task;
        public bool IsComplete => _tcs.Task.IsCompleted;
    }
}
