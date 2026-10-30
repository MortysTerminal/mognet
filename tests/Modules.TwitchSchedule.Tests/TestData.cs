using Mognet.Modules.TwitchSchedule.Models;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    internal static class TestData
    {
        public static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        // "Now" for planner tests: one day before the default entry start.
        public static readonly DateTimeOffset Now = new(2026, 6, 30, 18, 0, 0, TimeSpan.Zero);

        public static EventFormatter English => new(germanEvents: false, displayTimeZone: Berlin);
        public static EventFormatter German => new(germanEvents: true, displayTimeZone: Berlin);

        public static StreamScheduleEntry Entry(
            string segmentId = "segment-1",
            string title = "Minecraft Monday",
            string category = "Minecraft",
            DateTimeOffset? start = null,
            DateTimeOffset? end = null,
            string channel = "somechannel") =>
            new()
            {
                TwitchSegmentId = segmentId,
                StreamerUsername = channel,
                StreamerDisplayName = "Some Channel",
                ChannelUrl = $"https://www.twitch.tv/{channel}",
                Title = title,
                CategoryName = category,
                StartTime = start ?? new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero),
                EndTime = end,
            };

        public static ChannelSchedule Fetched(string channel, params StreamScheduleEntry[] entries) =>
            ChannelSchedule.Success(channel, entries);

        public static ChannelSchedule Failed(string channel) => ChannelSchedule.Failure(channel);

        // A bot event as created by versions before the channel tag existed.
        public static ExistingEvent LegacyEventFor(StreamScheduleEntry entry, ulong id)
        {
            var current = ExistingFor(entry, id);
            var tagStart = current.Description!.IndexOf(" " + EventFormatter.ChannelTagPrefix, StringComparison.Ordinal);
            return current with { Description = current.Description[..tagStart] };
        }

        // An existing Discord event exactly as the bot would have created it for the entry.
        public static ExistingEvent ExistingFor(StreamScheduleEntry entry, ulong id = 1, EventFormatter? formatter = null)
        {
            var desired = new EventPlanner(formatter ?? English).ToDesiredEvent(entry);
            return new ExistingEvent(id, desired.Name, desired.Description, desired.StartTime, desired.EndTime, desired.Location);
        }
    }
}
