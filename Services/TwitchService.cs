using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using mortys_twitch_discord_schedulebot.Models;
using mortys_twitch_discord_schedulebot.Sync;
using TwitchLib.Api;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Api.Helix.Models.Schedule;

namespace mortys_twitch_discord_schedulebot.Services
{
    public class TwitchService
    {
        private readonly TwitchAPI _twitchApi;
        private readonly List<TwitchChannelConfig> _channels;
        private readonly ILogger<TwitchService> _logger;
        private readonly int _lookaheadDays;
        private readonly int _maxEventsPerChannel;
        private readonly TimeProvider _timeProvider;

        public TwitchService(IConfiguration configuration, SyncOptions options, ILogger<TwitchService> logger, TimeProvider timeProvider)
        {
            _logger = logger;
            _timeProvider = timeProvider;

            var clientId = configuration["Twitch:ClientId"]
                ?? throw new InvalidOperationException("Twitch:ClientId is missing from configuration.");

            var clientSecret = configuration["Twitch:ClientSecret"]
                ?? throw new InvalidOperationException("Twitch:ClientSecret is missing from configuration.");

            // Load channels from configuration — populated at runtime via environment variables:
            //   Twitch__Channels__0__Username / Twitch__Channels__0__DisplayName (and so on)
            var channels = configuration
                .GetSection("Twitch:Channels")
                .Get<List<TwitchChannelConfig>>();

            // Filter out any slots that were declared in docker-compose.yml but left
            // unset in Portainer (Docker passes them as empty strings in that case).
            var validChannels = channels?
                .Where(c => !string.IsNullOrWhiteSpace(c.Username))
                .ToList();

            if (validChannels == null || validChannels.Count == 0)
            {
                _logger.LogError(
                    "No Twitch channels are configured. Set at least one channel pair via environment variables: " +
                    "Twitch__Channels__0__Username and Twitch__Channels__0__DisplayName"
                );
                throw new InvalidOperationException(
                    "No Twitch channels configured. " +
                    "Set Twitch__Channels__0__Username and Twitch__Channels__0__DisplayName."
                );
            }

            _channels = validChannels;
            _lookaheadDays = options.LookaheadDays;
            _maxEventsPerChannel = options.MaxEventsPerChannel;

            _twitchApi = new TwitchAPI();
            _twitchApi.Settings.ClientId = clientId;
            _twitchApi.Settings.Secret = clientSecret;

            _logger.LogInformation(
                "TwitchService initialized with {Count} channel(s): {Channels}",
                _channels.Count,
                string.Join(", ", _channels.Select(c => c.DisplayName))
            );
        }

        // Throws when authentication fails, so the caller skips the whole cycle.
        // Per-channel failures are returned as failed results instead of throwing.
        public async Task<List<ChannelSchedule>> GetAllSchedulesAsync()
        {
            var schedules = new List<ChannelSchedule>();

            await AuthenticateAsync();

            foreach (var channel in _channels)
            {
                _logger.LogInformation("Fetching schedule for channel: {Channel}", channel.Username);

                var schedule = await GetScheduleForChannelAsync(channel);
                schedules.Add(schedule);

                if (schedule.Succeeded)
                {
                    _logger.LogInformation(
                        "Channel {Channel}: {Count} entries found.",
                        channel.Username,
                        schedule.Entries.Count
                    );
                }
                else
                {
                    _logger.LogWarning(
                        "Channel {Channel}: schedule could not be fetched — its Discord events are kept this cycle.",
                        channel.Username
                    );
                }
            }

            return schedules;
        }

        private async Task AuthenticateAsync()
        {
            try
            {
                var token = await _twitchApi.Auth.GetAccessTokenAsync();
                _twitchApi.Settings.AccessToken = token;
                _logger.LogInformation("Twitch authentication successful.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Twitch authentication failed.");
                throw;
            }
        }

        private async Task<ChannelSchedule> GetScheduleForChannelAsync(TwitchChannelConfig channel)
        {
            var entries = new List<StreamScheduleEntry>();

            try
            {
                var userResponse = await _twitchApi.Helix.Users.GetUsersAsync(
                    logins: new List<string> { channel.Username }
                );

                // Unknown login (typo, renamed channel): treat as a failure so existing events stay.
                if (userResponse.Users.Length == 0)
                {
                    _logger.LogWarning("Channel '{Channel}' was not found on Twitch.", channel.Username);
                    return ChannelSchedule.Failure(channel.Username);
                }

                var userId = userResponse.Users[0].Id;

                var now = _timeProvider.GetUtcNow();
                var cutoff = now.AddDays(_lookaheadDays);
                var segments = new List<TwitchSegment>();
                VacationWindow? vacation = null;
                string? cursor = null;

                // Page through the schedule until the look-ahead window is covered.
                for (var page = 1; page <= SchedulePaging.MaxPages; page++)
                {
                    var scheduleResponse = await _twitchApi.Helix.Schedule.GetChannelStreamScheduleAsync(
                        broadcasterId: userId,
                        first: SchedulePaging.PageSize,
                        after: cursor
                    );

                    var schedule = scheduleResponse?.Schedule;
                    if (schedule?.Segments == null)
                        break;

                    if (schedule.Vacation != null)
                        vacation = new VacationWindow(ToUtc(schedule.Vacation.StartTime), ToUtc(schedule.Vacation.EndTime));

                    var pageSegments = schedule.Segments.Select(ToTwitchSegment).ToList();
                    segments.AddRange(pageSegments);

                    cursor = scheduleResponse!.Pagination?.Cursor;
                    if (!SchedulePaging.NeedsNextPage(pageSegments, cursor, cutoff))
                        break;

                    // Enough events already; later pages cannot add earlier segments.
                    if (SegmentSelector.Select(segments, vacation, now, _lookaheadDays, _maxEventsPerChannel).Count >= _maxEventsPerChannel)
                        break;
                }

                var canceled = segments.Count(s => s.IsCanceled);
                if (canceled > 0)
                    _logger.LogInformation("Channel '{Channel}': skipping {Count} canceled segment(s).", channel.Username, canceled);

                if (vacation != null)
                {
                    _logger.LogInformation(
                        "Channel '{Channel}' is on vacation from {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}; segments in that period are skipped.",
                        channel.Username,
                        vacation.StartTime,
                        vacation.EndTime
                    );
                }

                foreach (var segment in SegmentSelector.Select(segments, vacation, now, _lookaheadDays, _maxEventsPerChannel))
                {
                    entries.Add(new StreamScheduleEntry
                    {
                        TwitchSegmentId = segment.Id,
                        StreamerUsername = channel.Username,
                        StreamerDisplayName = channel.DisplayName,
                        ChannelUrl = channel.ChannelUrl,
                        // Empty title and category are resolved to language-specific fallbacks in EventFormatter
                        Title = segment.Title,
                        CategoryName = segment.CategoryName,
                        StartTime = segment.StartTime,
                        EndTime = segment.EndTime
                    });
                }
            }
            catch (BadResourceException)
            {
                // Twitch answers 404 when the broadcaster has not created a streaming schedule.
                _logger.LogInformation("Channel '{Channel}' has no streaming schedule.", channel.Username);
                return ChannelSchedule.Success(channel.Username, entries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch schedule for channel '{Channel}'.", channel.Username);
                return ChannelSchedule.Failure(channel.Username);
            }

            return ChannelSchedule.Success(channel.Username, entries);
        }

        private static TwitchSegment ToTwitchSegment(Segment segment) =>
            new(
                Id: segment.Id,
                Title: segment.Title ?? string.Empty,
                CategoryName: segment.Category?.Name ?? string.Empty,
                StartTime: ToUtc(segment.StartTime),
                // TwitchLib leaves EndTime at DateTime.MinValue when Twitch sends no end time.
                EndTime: segment.EndTime == default ? null : ToUtc(segment.EndTime),
                IsCanceled: segment.CanceledUntil.HasValue,
                IsRecurring: segment.IsRecurring
            );

        // Twitch sends UTC timestamps; treat an unspecified kind as UTC instead of local time.
        private static DateTimeOffset ToUtc(DateTime time) =>
            time.Kind == DateTimeKind.Local
                ? new DateTimeOffset(time.ToUniversalTime())
                : new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }
}
