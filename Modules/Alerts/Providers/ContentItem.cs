namespace Mognet.Modules.Alerts.Providers
{
    /// <summary>One piece of content as a provider reports it, without any platform-specific fields.</summary>
    /// <param name="ScheduledStart">Planned start for a scheduled stream; <see langword="null"/> otherwise.</param>
    /// <param name="LiveState">Stage of a live stream; <see langword="null"/> for content that is not a stream.</param>
    /// <param name="Type">
    /// The type decided in this read. <see langword="null"/> means not determined this time (the stored type, if any,
    /// stays); it never means unclassified.
    /// </param>
    public sealed record ContentItem(string ContentId, DateTimeOffset? ScheduledStart, LiveState? LiveState = null, ContentType? Type = null);
}
