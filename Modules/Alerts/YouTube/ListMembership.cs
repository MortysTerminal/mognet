namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>Whether a video is in one of the channel's generated playlists, as far as the bot could read it.</summary>
    public enum ListMembership
    {
        /// <summary>The list was not read, or the read failed.</summary>
        Unknown,
        No,
        Yes,
    }

    internal static class ListMembershipExtensions
    {
        /// <summary>Membership of an id in a list that may be unknown (<see langword="null"/>).</summary>
        public static ListMembership Of(this IReadOnlySet<string>? list, string id) =>
            list is null ? ListMembership.Unknown : list.Contains(id) ? ListMembership.Yes : ListMembership.No;
    }
}
