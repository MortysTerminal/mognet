using mortys_twitch_discord_schedulebot.Models;

namespace mortys_twitch_discord_schedulebot.Sync
{
    /// <summary>
    /// Builds the Discord event content (name, description, end time) for a Twitch schedule entry.
    /// Pure: no I/O, no clock.
    /// </summary>
    public class EventFormatter
    {
        // Marks bot-managed events; the segment ID follows the colon.
        public const string SegmentTagPrefix = "[twitch-segment-id:";

        // Names the Twitch channel (login) an event belongs to, so deletions can be scoped per channel.
        // Events created before this tag existed don't have it.
        public const string ChannelTagPrefix = "[twitch-channel:";

        // Discord limits event names to 100 characters.
        private const int MaxNameLength = 100;

        // Used when Twitch has no end time (or an end time not after the start).
        private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(3);

        private readonly bool _germanEvents;
        private readonly TimeZoneInfo _displayTimeZone;

        public EventFormatter(bool germanEvents, TimeZoneInfo displayTimeZone)
        {
            _germanEvents = germanEvents;
            _displayTimeZone = displayTimeZone;
        }

        /// <summary>
        /// Creates a formatter from the configured language ("de" for German, anything else English),
        /// showing start times in Europe/Berlin.
        /// </summary>
        public static EventFormatter FromLanguage(string? language) =>
            new(
                germanEvents: string.Equals(language, "de", StringComparison.OrdinalIgnoreCase),
                displayTimeZone: TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")
            );

        public bool IsGerman => _germanEvents;

        // Builds the Discord event title, applying a language-specific fallback when Twitch has no title set.
        public string BuildName(StreamScheduleEntry entry)
        {
            var title = string.IsNullOrWhiteSpace(entry.Title)
                ? (_germanEvents ? $"Stream von {entry.StreamerDisplayName}" : $"Stream by {entry.StreamerDisplayName}")
                : entry.Title;

            var eventName = $"{entry.StreamerDisplayName} – {title}";

            return eventName.Length > MaxNameLength ? eventName[..(MaxNameLength - 3)] + "..." : eventName;
        }

        // Builds the Discord event description with language-specific labels.
        // The tags on the last line identify bot-managed events and the channel they belong to.
        public string BuildDescription(StreamScheduleEntry entry)
        {
            var localStart = TimeZoneInfo.ConvertTime(entry.StartTime, _displayTimeZone);

            var category = string.IsNullOrEmpty(entry.CategoryName)
                ? (_germanEvents ? "Keine Kategorie" : "No Category")
                : entry.CategoryName;

            var tags = $"{SegmentTagPrefix}{entry.TwitchSegmentId}] {ChannelTagPrefix}{NormalizeLogin(entry.StreamerUsername)}]";

            if (_germanEvents)
            {
                return $"""
                        🎬 Inhalt: {category}
                        📺 Kanal: {entry.ChannelUrl}
                        🕐 Start: {localStart:dd.MM.yyyy HH:mm} Uhr

                        {tags}
                        """;
            }

            return $"""
                    🎬 Content: {category}
                    📺 Channel: {entry.ChannelUrl}
                    🕐 Start: {localStart:dd.MM.yyyy HH:mm}

                    {tags}
                    """;
        }

        /// <summary>
        /// Reads the channel tag from an event description; null when the event has none (created before the tag existed).
        /// </summary>
        public static string? ReadChannelTag(string? description)
        {
            if (description == null) return null;

            var start = description.IndexOf(ChannelTagPrefix, StringComparison.Ordinal);
            if (start < 0) return null;

            start += ChannelTagPrefix.Length;
            var end = description.IndexOf(']', start);
            if (end <= start) return null;

            return NormalizeLogin(description[start..end]);
        }

        // Twitch logins are case-insensitive; tags and comparisons use lower case.
        public static string NormalizeLogin(string login) => login.Trim().ToLowerInvariant();

        public static DateTimeOffset BuildEndTime(StreamScheduleEntry entry) =>
            entry.EndTime.HasValue && entry.EndTime.Value > entry.StartTime
                ? entry.EndTime.Value
                : entry.StartTime.Add(DefaultDuration);
    }
}
