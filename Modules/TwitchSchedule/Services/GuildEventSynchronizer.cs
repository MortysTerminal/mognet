using Microsoft.Extensions.Logging;
using Mognet.Modules.TwitchSchedule.Storage;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Services
{
    /// <summary>
    /// Plans one sync cycle against the guild's events and executes the plan. With dry run on, the plan is
    /// logged and no write is made. Every write is isolated, so one failure does not stop the others.
    /// </summary>
    internal sealed class GuildEventSynchronizer
    {
        private const string DryRunMark = "[dry run]";

        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;

        public GuildEventSynchronizer(ILogger logger, TimeProvider time)
        {
            _logger = logger;
            _timeProvider = time;
        }

        public async Task SyncAsync(IGuildEvents events, IReadOnlyList<ChannelSchedule> schedules, ScheduleConfig config)
        {
            if (config.DryRun)
                _logger.LogInformation("Dry run is active — the plan is logged and nothing is written to Discord.");

            var snapshots = await events.ListAsync();

            _logger.LogInformation(
                "Found {ScheduleCount} Twitch entries and {EventCount} existing Discord events.",
                schedules.Sum(s => s.Entries.Count),
                snapshots.Count
            );

            var planner = new EventPlanner(EventFormatter.FromLanguage(config.Language));
            var plan = planner.Plan(schedules, snapshots, _timeProvider.GetUtcNow());

            foreach (var desired in plan.Creates)
            {
                if (config.DryRun)
                {
                    _logger.LogInformation(
                        "{Mark} Would create Discord event '{Name}' on {Date}",
                        DryRunMark, desired.Name, desired.StartTime.ToString("yyyy-MM-dd HH:mm"));
                }
                else
                {
                    await CreateEventAsync(events, desired);
                }
            }

            foreach (var update in plan.Updates)
            {
                if (config.DryRun)
                {
                    _logger.LogInformation(
                        "{Mark} Would update Discord event '{Name}' on {Date}",
                        DryRunMark, update.Desired.Name, update.Desired.StartTime.ToString("yyyy-MM-dd HH:mm"));
                }
                else
                {
                    await UpdateEventAsync(events, update.Existing, update.Desired);
                }
            }

            foreach (var unchanged in plan.Unchanged)
                _logger.LogInformation("Event '{Name}' is up to date — no changes needed.", unchanged.Name);

            foreach (var obsolete in plan.Deletes)
            {
                if (config.DryRun)
                {
                    _logger.LogInformation(
                        "{Mark} Would delete Discord event '{Name}' on {Date}",
                        DryRunMark, obsolete.Name, obsolete.StartTime.ToString("yyyy-MM-dd HH:mm"));
                }
                else
                {
                    await DeleteEventAsync(events, obsolete);
                }
            }

            foreach (var running in plan.Started)
                _logger.LogInformation("Event '{Name}' has already started — leaving it to Discord.", running.Name);

            foreach (var kept in plan.Protected)
            {
                _logger.LogWarning(
                    "Discord event '{Name}' is not in the fetched schedules, but not every channel could be fetched — keeping it.",
                    kept.Name
                );
            }
        }

        private async Task CreateEventAsync(IGuildEvents events, DesiredEvent desired)
        {
            try
            {
                await events.CreateAsync(desired);

                _logger.LogInformation(
                    "Discord event created: '{Name}' on {Date}",
                    desired.Name,
                    desired.StartTime.ToString("yyyy-MM-dd HH:mm")
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Discord event for '{Title}'.", desired.Entry.Title);
            }
        }

        private async Task UpdateEventAsync(IGuildEvents events, ExistingEvent existing, DesiredEvent desired)
        {
            try
            {
                await events.UpdateAsync(existing, desired);

                _logger.LogInformation("Discord event updated: '{Name}'", desired.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update event '{Name}'.", existing.Name);
            }
        }

        private async Task DeleteEventAsync(IGuildEvents events, ExistingEvent obsolete)
        {
            try
            {
                _logger.LogInformation(
                    "Discord event '{Name}' is no longer in the schedule — deleting.",
                    obsolete.Name
                );
                await events.DeleteAsync(obsolete);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete event '{Name}'.", obsolete.Name);
            }
        }
    }
}
