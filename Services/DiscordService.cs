using Discord;
using Discord.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using mortys_twitch_discord_schedulebot.Sync;

namespace mortys_twitch_discord_schedulebot.Services
{
    public class DiscordService : IAsyncDisposable
    {
        private readonly DiscordRestClient _client;
        private readonly ulong _guildId;
        private readonly ILogger<DiscordService> _logger;
        private readonly string _configuration_token;
        // Decides what to create, update and delete; this service only executes the plan.
        // Event language: set Discord__EventLanguage=de for German, defaults to English.
        private readonly EventPlanner _planner;
        private readonly TimeProvider _timeProvider;

        public DiscordService(IConfiguration configuration, ILogger<DiscordService> logger, TimeProvider timeProvider)
        {
            _logger = logger;
            _timeProvider = timeProvider;

            var token = configuration["Discord:BotToken"]
                ?? throw new InvalidOperationException("Discord:BotToken is missing from configuration.");

            _configuration_token = token;

            var guildIdRaw = configuration["Discord:GuildId"]
                ?? throw new InvalidOperationException("Discord:GuildId is missing from configuration.");

            if (!ulong.TryParse(guildIdRaw, out _guildId))
                throw new InvalidOperationException("Discord:GuildId is invalid — must be a numeric value.");

            var formatter = EventFormatter.FromLanguage(configuration["Discord:EventLanguage"] ?? "en");
            _planner = new EventPlanner(formatter);

            _client = new DiscordRestClient();

            _logger.LogInformation(
                "DiscordService initialized (event language: {Language}).",
                formatter.IsGerman ? "de" : "en"
            );
        }

        public async Task LoginAsync()
        {
            await _client.LoginAsync(TokenType.Bot, _configuration_token);
            _logger.LogInformation("Discord login successful.");
        }

        public async Task SyncEventsAsync(IReadOnlyList<ChannelSchedule> schedules)
        {
            _logger.LogInformation("Starting Discord event synchronization...");

            var guild = await _client.GetGuildAsync(_guildId);

            if (guild == null)
            {
                _logger.LogError("Discord server (GuildId: {GuildId}) not found.", _guildId);
                return;
            }

            var guildEvents = (await guild.GetEventsAsync()).ToDictionary(e => e.Id);

            _logger.LogInformation(
                "Found {ScheduleCount} Twitch entries and {EventCount} existing Discord events.",
                schedules.Sum(s => s.Entries.Count),
                guildEvents.Count
            );

            var snapshots = guildEvents.Values
                .Select(e => new ExistingEvent(e.Id, e.Name, e.Description, e.StartTime, e.EndTime, e.Location))
                .ToList();

            var plan = _planner.Plan(schedules, snapshots, _timeProvider.GetUtcNow());

            foreach (var desired in plan.Creates)
                await CreateEventAsync(guild, desired);

            foreach (var update in plan.Updates)
                await UpdateEventAsync(guildEvents[update.Existing.Id], update.Desired);

            foreach (var unchanged in plan.Unchanged)
                _logger.LogInformation("Event '{Name}' is up to date — no changes needed.", unchanged.Name);

            foreach (var obsolete in plan.Deletes)
                await DeleteEventAsync(guildEvents[obsolete.Id]);

            foreach (var running in plan.Started)
                _logger.LogInformation("Event '{Name}' has already started — leaving it to Discord.", running.Name);

            foreach (var kept in plan.Protected)
            {
                _logger.LogWarning(
                    "Discord event '{Name}' is not in the fetched schedules, but not every channel could be fetched — keeping it.",
                    kept.Name
                );
            }

            _logger.LogInformation("Discord event synchronization complete.");
        }

        private async Task CreateEventAsync(RestGuild guild, DesiredEvent desired)
        {
            try
            {
                await guild.CreateEventAsync(
                    name: desired.Name,
                    startTime: desired.StartTime,
                    type: GuildScheduledEventType.External,
                    privacyLevel: GuildScheduledEventPrivacyLevel.Private,
                    description: desired.Description,
                    endTime: desired.EndTime,
                    location: desired.Location
                );

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

        private async Task UpdateEventAsync(RestGuildEvent existingEvent, DesiredEvent desired)
        {
            try
            {
                await existingEvent.ModifyAsync(props =>
                {
                    props.Name = desired.Name;
                    props.StartTime = desired.StartTime;
                    props.EndTime = desired.EndTime;
                    props.Description = desired.Description;
                    props.Location = desired.Location;
                });

                _logger.LogInformation("Discord event updated: '{Name}'", desired.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update event '{Name}'.", existingEvent.Name);
            }
        }

        private async Task DeleteEventAsync(RestGuildEvent obsoleteEvent)
        {
            try
            {
                _logger.LogInformation(
                    "Discord event '{Name}' is no longer in the schedule — deleting.",
                    obsoleteEvent.Name
                );
                await obsoleteEvent.DeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete event '{Name}'.", obsoleteEvent.Name);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _client.LogoutAsync();
            _client.Dispose();
        }
    }
}
