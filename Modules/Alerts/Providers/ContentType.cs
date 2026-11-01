namespace Mognet.Modules.Alerts.Providers
{
    /// <summary>
    /// What a piece of content is. <see cref="Unclassified"/> is the answer whenever the facts are missing or
    /// contradict each other; it never stands for a guess.
    /// </summary>
    public enum ContentType
    {
        Video,
        Short,
        StreamScheduled,
        StreamLive,
        StreamEnded,
        Unclassified,
    }
}
