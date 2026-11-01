using Mognet.Modules.Alerts.Providers;

namespace Mognet.Modules.Alerts.Storage
{
    /// <summary>
    /// Content the module has already seen. Only ids, times, the live state and the type are kept; titles, descriptions and other platform text are not.
    /// </summary>
    /// <param name="ScheduledStart">Planned start of a scheduled stream; <see langword="null"/> for other content.</param>
    /// <param name="LastRefreshedAt">When the platform last confirmed the item.</param>
    /// <param name="LiveState">Stage of a live stream; <see langword="null"/> for other content.</param>
    /// <param name="Type">
    /// What the item was classified as. <see langword="null"/> for content that existed before tracking started and
    /// was never alerted, or whose type was never determined.
    /// </param>
    public sealed record KnownContent(
        long SourceId,
        string ContentId,
        DateTimeOffset? ScheduledStart,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastRefreshedAt,
        LiveState? LiveState = null,
        ContentType? Type = null);
}
