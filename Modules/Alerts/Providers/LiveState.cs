namespace Mognet.Modules.Alerts.Providers
{
    /// <summary>Where a live stream is in its life. Content that is not a stream has no live state.</summary>
    public enum LiveState
    {
        Upcoming,
        Live,
        Ended,
    }
}
