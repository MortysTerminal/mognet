using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.Providers
{
    /// <summary>
    /// Reads the content of a source from one platform. The rest of the module only sees this interface, so a new
    /// platform is a new implementation registered next to the others.
    /// </summary>
    public interface IContentProvider
    {
        /// <summary>The platform this provider serves; matches <see cref="AlertSource.Platform"/>.</summary>
        string Platform { get; }

        /// <summary>The content the platform currently lists for <paramref name="source"/>.</summary>
        Task<IReadOnlyList<ContentItem>> FetchAsync(AlertSource source, CancellationToken cancellationToken);
    }
}
