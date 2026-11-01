namespace Mognet.Modules.Alerts.Storage
{
    /// <summary>One place content is read from, for example one YouTube channel.</summary>
    /// <param name="Platform">Platform name, as reported by the provider.</param>
    /// <param name="ExternalId">The source's id on that platform.</param>
    /// <param name="BaselineRecorded">The content present at the first read has been stored without alerting.</param>
    public sealed record AlertSource(long Id, string Platform, string ExternalId, bool BaselineRecorded, DateTimeOffset CreatedAt);
}
