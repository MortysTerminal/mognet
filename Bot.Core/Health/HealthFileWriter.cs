using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mognet.Bot.Core.Health
{
    /// <summary>Writes the health file every 30 seconds so a container health check can read it.</summary>
    public sealed class HealthFileWriter : BackgroundService
    {
        private readonly HealthRegistry _registry;
        private readonly TimeProvider _time;
        private readonly ILogger<HealthFileWriter> _logger;
        private readonly string _path;
        private readonly TimeSpan _interval;

        public HealthFileWriter(HealthRegistry registry, TimeProvider time, ILogger<HealthFileWriter> logger)
            : this(registry, time, logger, HealthFile.DefaultPath, TimeSpan.FromSeconds(30))
        {
        }

        internal HealthFileWriter(HealthRegistry registry, TimeProvider time, ILogger<HealthFileWriter> logger, string path, TimeSpan interval)
        {
            _registry = registry;
            _time = time;
            _logger = logger;
            _path = path;
            _interval = interval;
        }

        // Writes to a temp file and renames it, so a reader never sees a half-written file. A failure is logged
        // and the next tick tries again; the host keeps running.
        internal void WriteOnce()
        {
            var temp = _path + ".tmp";
            try
            {
                var now = _time.GetUtcNow();
                File.WriteAllText(temp, HealthFile.Format(now, _registry.Evaluate(now)));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The health file could not be written.");
                try
                {
                    File.Delete(temp);
                }
                catch (Exception)
                {
                    // Nothing more to do; the next tick overwrites the temp file anyway.
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            WriteOnce();

            using var timer = new PeriodicTimer(_interval, _time);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                    WriteOnce();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
