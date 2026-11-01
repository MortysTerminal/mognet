using Mognet.Modules.Alerts.Providers;

namespace Mognet.Modules.Alerts.Detection
{
    /// <param name="Type">The type of a new item, or the new type of a <see cref="ChangeKind.TypeChanged"/>; <see langword="null"/> otherwise.</param>
    public sealed record ContentChange(string ContentId, ChangeKind Kind, ContentType? Type = null);
}
