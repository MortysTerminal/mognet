namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>
    /// What the API said about one video, and nothing derived from it. Strings are kept as read, so an unexpected
    /// value reaches the classifier instead of being mapped away.
    /// </summary>
    /// <param name="LiveBroadcastContent"><c>snippet.liveBroadcastContent</c> as read; <see langword="null"/> when missing.</param>
    /// <param name="Duration"><c>contentDetails.duration</c> as read; <see langword="null"/> when missing.</param>
    /// <param name="InShortsList">Membership in the channel's <c>UUSH</c> playlist.</param>
    /// <param name="InVideosList">Membership in the channel's <c>UULF</c> playlist.</param>
    /// <param name="InStreamsList">Membership in the channel's <c>UULV</c> playlist.</param>
    public sealed record ClassificationFacts(
        string? LiveBroadcastContent,
        bool HasLiveStreamingDetails,
        bool HasScheduledStartTime,
        bool HasActualStartTime,
        bool HasActualEndTime,
        string? Duration,
        ListMembership InShortsList = ListMembership.Unknown,
        ListMembership InVideosList = ListMembership.Unknown,
        ListMembership InStreamsList = ListMembership.Unknown);
}
