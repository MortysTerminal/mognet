using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mognet.Bot.Core.Health;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Services;
using Mognet.Modules.Alerts.Storage;
using Mognet.Modules.Alerts.YouTube;

namespace Mognet.Modules.Alerts
{
    /// <summary>
    /// Alerts for new and upcoming content on other platforms. It owns the tables for sources and known content,
    /// which are created at start-up whether or not the module is switched on. Switched on, it polls the sources
    /// and logs what it finds; there is no rule and no message yet. The services are built on every start, so a
    /// stopped module can be started again.
    /// </summary>
    public sealed class AlertsModule : IBotModule
    {
        public const string ModuleId = "alerts";

        /// <summary>Name of the environment variable that holds the YouTube Data API key.</summary>
        public const string YouTubeApiKeyVariable = "YOUTUBE_API_KEY";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        private readonly IServiceProvider _services;
        private readonly ILogger<AlertsModule> _logger;
        private Run? _run;

        public AlertsModule(IServiceProvider services, ILogger<AlertsModule> logger)
        {
            _services = services;
            _logger = logger;
        }

        public string Id => ModuleId;

        public MigrationSet? Migrations => AlertsMigrations.Set;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_run is not null)
                throw new InvalidOperationException("The module is already running.");

            var key = _services.GetRequiredService<IConfiguration>()[YouTubeApiKeyVariable];
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    $"{YouTubeApiKeyVariable} is missing. Set it as a stack variable before switching the alerts module on."
                );
            }

            var time = _services.GetRequiredService<TimeProvider>();
            var quota = new QuotaCounter(time);
            var http = new HttpClient { Timeout = RequestTimeout };
            PollService? poll = null;
            try
            {
                var provider = new YouTubeProvider(
                    new YouTubeApi(http, key.Trim(), quota),
                    quota,
                    _services.GetRequiredService<ILogger<YouTubeProvider>>(),
                    time);
                poll = CreatePoll([provider]);
                await poll.StartAsync(cancellationToken);
            }
            catch
            {
                poll?.Dispose();
                http.Dispose();
                throw;
            }

            Attach(poll, http);
        }

        // The health registry is required, so a missing registration fails loudly instead of disabling the signal.
        internal PollService CreatePoll(IReadOnlyList<IContentProvider> providers) =>
            new(
                providers,
                new AlertSourceStore(_services.GetRequiredService<SqliteStore>()),
                _services.GetRequiredService<SettingsStore>(),
                _services.GetRequiredService<ILogger<PollService>>(),
                _services.GetRequiredService<HealthRegistry>(),
                _services.GetRequiredService<TimeProvider>());

        internal void Attach(PollService poll, IDisposable resources)
        {
            var run = new Run(poll, resources);
            _run = run;
            if (poll.ExecuteTask is { } executeTask)
                _ = ObserveAsync(executeTask, run.Stopped.Token);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            var run = _run;
            if (run is null)
                return;

            _run = null;
            await run.Stopped.CancelAsync();
            try
            {
                await run.Poll.StopAsync(cancellationToken);
            }
            finally
            {
                _services.GetService<HealthRegistry>()?.Clear(ModuleId);
                run.Poll.Dispose();
                run.Resources.Dispose();
                run.Stopped.Dispose();
            }
        }

        // A hosted service started by hand no longer reports its fault anywhere, so it is observed here. The
        // module stops reporting health, which the container health check turns into a restart.
        internal async Task ObserveAsync(Task executeTask, CancellationToken stopped)
        {
            try
            {
                await executeTask;
            }
            catch (Exception ex)
            {
                if (stopped.IsCancellationRequested)
                    return;

                _logger.LogError(ex, "The alerts poll ended with an error and no longer reports health.");
            }
        }

        private sealed record Run(PollService Poll, IDisposable Resources)
        {
            public CancellationTokenSource Stopped { get; } = new();
        }
    }
}
