using mortys_twitch_discord_schedulebot.Models;

namespace mortys_twitch_discord_schedulebot.Sync
{
    /// <summary>
    /// Result of fetching one channel's schedule in a sync cycle. A failed fetch carries no entries and
    /// protects that channel's Discord events from deletion.
    /// </summary>
    public record ChannelSchedule(string ChannelLogin, bool Succeeded, IReadOnlyList<StreamScheduleEntry> Entries)
    {
        public static ChannelSchedule Success(string channelLogin, IReadOnlyList<StreamScheduleEntry> entries) =>
            new(EventFormatter.NormalizeLogin(channelLogin), true, entries);

        public static ChannelSchedule Failure(string channelLogin) =>
            new(EventFormatter.NormalizeLogin(channelLogin), false, []);
    }
}
