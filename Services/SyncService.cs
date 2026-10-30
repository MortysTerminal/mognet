using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using mortys_twitch_discord_schedulebot.Sync;

namespace mortys_twitch_discord_schedulebot.Services
{
    public class SyncService : BackgroundService
    {
        private readonly TwitchService _twitchService;
        private readonly DiscordService _discordService;
        private readonly ILogger<SyncService> _logger;
        private readonly int _intervalMinutes;

        public SyncService(
            TwitchService twitchService,
            DiscordService discordService,
            SyncOptions options,
            ILogger<SyncService> logger)
        {
            _twitchService = twitchService;
            _discordService = discordService;
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

        private async Task RunSyncAsync()
        {
            _logger.LogInformation("═══════════════════════════════════════");
            _logger.LogInformation("Starting sync...");

            try
            {
                var schedules = await _twitchService.GetAllSchedulesAsync();

                _logger.LogInformation("{Count} Twitch entries found.", schedules.Sum(s => s.Entries.Count));

                await _discordService.SyncEventsAsync(schedules);

                _logger.LogInformation("Sync completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sync.");
            }

            _logger.LogInformation("═══════════════════════════════════════");
        }
    }
}
