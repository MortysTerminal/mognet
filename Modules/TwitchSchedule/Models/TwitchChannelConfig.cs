namespace Mognet.Modules.TwitchSchedule.Models
{
    /// <summary>
    /// A configured Twitch channel, bound from <c>Twitch:Channels</c>.
    /// </summary>
    public class TwitchChannelConfig
    {
        /// <summary>
        /// The channel's Twitch login (lowercase, as in the URL). Example: "shroud"
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The name shown in the Discord event. Example: "Shroud"
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Full link to the Twitch channel.
        /// </summary>
        public string ChannelUrl => $"https://www.twitch.tv/{Username}";
    }
}
