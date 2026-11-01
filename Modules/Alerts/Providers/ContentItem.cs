namespace Mognet.Modules.Alerts.Providers
{
    /// <summary>One piece of content as a provider reports it, without any platform-specific fields.</summary>
    /// <param name="ScheduledStart">Planned start for a scheduled stream; <see langword="null"/> otherwise.</param>
    public sealed record ContentItem(string ContentId, DateTimeOffset? ScheduledStart);
}
