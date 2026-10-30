namespace Mognet.Modules.TwitchSchedule.Models
{
    /// <summary>
    /// One entry of a Twitch streaming schedule that is mirrored to a Discord event.
    /// </summary>
    public class StreamScheduleEntry
    {
        /// <summary>
        /// The Twitch segment ID; identifies the Discord event that mirrors this entry.
        /// </summary>
        public string TwitchSegmentId { get; set; } = string.Empty;

        /// <summary>
        /// The streamer's Twitch login.
        /// </summary>
        public string StreamerUsername { get; set; } = string.Empty;

        /// <summary>
        /// The streamer's display name as shown in Discord.
        /// </summary>
        public string StreamerDisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Link to the Twitch channel.
        /// </summary>
        public string ChannelUrl { get; set; } = string.Empty;

        /// <summary>
        /// Title of the scheduled stream (e.g. "Minecraft Monday"); may be empty.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Category or game of the stream; may be empty.
        /// </summary>
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// Start of the stream (UTC).
        /// </summary>
        public DateTimeOffset StartTime { get; set; }

        /// <summary>
        /// End of the stream (UTC); null when Twitch has no end time.
        /// </summary>
        public DateTimeOffset? EndTime { get; set; }
    }
}
