namespace Mognet.Modules.Alerts.Providers
{
    /// <summary>
    /// What one read of a source returned. <paramref name="Items"/> hold current data from the platform's detail call,
    /// never from a listing alone. <paramref name="Gone"/> are known ids the platform confirmed it no longer returns.
    /// </summary>
    /// <param name="Complete">
    /// False when the first read of a source did not finish; nothing may be stored then and the source stays without
    /// a baseline. Always true for later reads, which may be partial because the next cycle repeats what failed.
    /// </param>
    public sealed record SourcePoll(IReadOnlyList<ContentItem> Items, IReadOnlyList<string> Gone, bool Complete)
    {
        public static SourcePoll Incomplete { get; } = new([], [], Complete: false);
    }
}
