using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mognet.Modules.TwitchSchedule.Storage;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Services
{
    public class SyncService : BackgroundService
    {
        private readonly ITwitchSchedules _twitchService;
        private readonly IDiscordSync _discordService;
        private readonly IScheduleConfigSource _configSource;
        private readonly ILogger<SyncService> _logger;
        private int _intervalMinutes;

        internal SyncService(
            ITwitchSchedules twitchService,
            IDiscordSync discordService,
            IScheduleConfigSource configSource,
            SyncOptions options,
            ILogger<SyncService> logger)
        {
            _twitchService = twitchService;
            _discordService = discordService;
            _configSource = configSource;
            _logger = logger;
            _intervalMinutes = options.IntervalMinutes;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "SyncService started — syncing every {Interval} minute(s).",
                _intervalMinutes
            );

            try
            {
                await _discordService.LoginAsync();
            }
            catch (Exception ex)
            {
                // Without a Discord session nothing can be synced; stop the host so the container restarts.
                _logger.LogCritical(
                    ex,
                    "Discord login failed — check Discord__BotToken. The bot stops and the container will be restarted."
                );
                Environment.ExitCode = 1;
                throw;
            }

            await RunSyncAsync();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation(
                        "Next sync in {Interval} minute(s).",
                        _intervalMinutes
                    );

                    await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);

                    await RunSyncAsync();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // The next loop iteration waits the regular interval before retrying.
                    _logger.LogError(ex, "Unexpected error in SyncService. Retrying in {Interval} minute(s).", _intervalMinutes);
                }
            }

            _logger.LogInformation("SyncService is stopping.");
        }

        internal int IntervalMinutes => _intervalMinutes;

        // Loads the config first, so a changed setting applies from this cycle on. When the config cannot be
        // loaded or holds no channel, the cycle is skipped before any Twitch or Discord call.
        internal async Task RunSyncAsync()
        {
            _logger.LogInformation("═══════════════════════════════════════");
            _logger.LogInformation("Starting sync...");

            try
            {
                var config = await LoadConfigAsync();
                if (config is not null)
                {
                    _intervalMinutes = config.Options.IntervalMinutes;

                    var schedules = await _twitchService.GetAllSchedulesAsync(config);

                    _logger.LogInformation("{Count} Twitch entries found.", schedules.Sum(s => s.Entries.Count));

                    await _discordService.SyncEventsAsync(schedules, config);

                    _logger.LogInformation("Sync completed successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sync.");
            }

            _logger.LogInformation("═══════════════════════════════════════");
        }

        private async Task<ScheduleConfig?> LoadConfigAsync()
        {
            ScheduleConfig config;
            try
            {
                config = await _configSource.LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The schedule settings could not be read — skipping this sync cycle.");
                return null;
            }

            if (config.Channels.Count == 0)
            {
                _logger.LogError("No Twitch channels are configured — skipping this sync cycle.");
                return null;
            }

            return config;
        }
    }
}
