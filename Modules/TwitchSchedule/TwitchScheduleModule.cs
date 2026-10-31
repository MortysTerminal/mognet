using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mognet.Bot.Core.Gateway;
using Mognet.Bot.Core.Health;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.TwitchSchedule.Services;
using Mognet.Modules.TwitchSchedule.Storage;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule
{
    /// <summary>
    /// Mirrors Twitch schedules as Discord events. The services are built on every start, so the configuration is
    /// read only when the module is switched on, and a stopped module can be started again. Channels and sync
    /// settings come from the database; the environment is imported once at the first start.
    /// </summary>
    public sealed class TwitchScheduleModule : IBotModule
    {
        public const string ModuleId = "twitch_schedule";

        private readonly IServiceProvider _services;
        private readonly ILogger<TwitchScheduleModule> _logger;
        private readonly IFatalShutdown _shutdown;
        private Run? _run;

        public TwitchScheduleModule(IServiceProvider services, ILogger<TwitchScheduleModule> logger, IFatalShutdown shutdown)
        {
            _services = services;
            _logger = logger;
            _shutdown = shutdown;
        }

        public string Id => ModuleId;

        public MigrationSet? Migrations => ScheduleMigrations.Set;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_run is not null)
                throw new InvalidOperationException("The module is already running.");

            await ActivatorUtilities.CreateInstance<ScheduleImporter>(_services).ImportAsync(cancellationToken);

            var configStore = ActivatorUtilities.CreateInstance<ScheduleConfigStore>(_services);
            var config = await configStore.LoadAsync(cancellationToken);
            if (config.Channels.Count == 0)
            {
                throw new InvalidOperationException(
                    "No Twitch channels configured. Set Twitch__Channels__0__Username and Twitch__Channels__0__DisplayName " +
                    "before the first start of the module; later changes to them have no effect."
                );
            }

            var twitch = ActivatorUtilities.CreateInstance<TwitchService>(_services);
            DiscordService? discord = null;
            SyncService? sync = null;
            try
            {
                discord = ActivatorUtilities.CreateInstance<DiscordService>(_services);
                sync = CreateSync(twitch, discord, configStore, config.Options);
                await sync.StartAsync(cancellationToken);
            }
            catch
            {
                sync?.Dispose();
                if (discord is not null)
                    await discord.DisposeAsync();
                throw;
            }

            Attach(sync, discord);
        }

        // The health registry is required, so a missing registration fails loudly instead of disabling the signal.
        internal SyncService CreateSync(ITwitchSchedules twitch, IDiscordSync discord, IScheduleConfigSource configSource, SyncOptions options) =>
            new(
                twitch,
                discord,
                configSource,
                options,
                _services.GetRequiredService<ILogger<SyncService>>(),
                _services.GetRequiredService<HealthRegistry>(),
                _services.GetRequiredService<TimeProvider>());

        internal void Attach(SyncService sync, IAsyncDisposable discord)
        {
            var run = new Run(sync, discord);
            _run = run;
            if (sync.ExecuteTask is { } executeTask)
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
                await run.Sync.StopAsync(cancellationToken);
            }
            finally
            {
                _services.GetService<HealthRegistry>()?.Clear(ModuleId);
                run.Sync.Dispose();
                await run.Discord.DisposeAsync();
                run.Stopped.Dispose();
            }
        }

        // A hosted service started by hand no longer stops the host when it faults, so the fault is observed here:
        // a failed Discord login must still end the process so the container restarts.
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

                _logger.LogCritical(ex, "The Twitch schedule sync ended with an error. The bot stops and the container will be restarted.");
                _shutdown.Shutdown();
            }
        }

        private sealed record Run(SyncService Sync, IAsyncDisposable Discord)
        {
            public CancellationTokenSource Stopped { get; } = new();
        }
    }
}
