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

        /// <summary>Called before the sources of a cycle are read, so the provider can reset its per-cycle counters.</summary>
        void BeginCycle(CycleContext context);

        /// <summary>
        /// Reads one source. <paramref name="known"/> is what is stored for it. A failed call is handled inside: it
        /// changes nothing in the result and is repeated by the next cycle.
        /// </summary>
        Task<SourcePoll> PollAsync(AlertSource source, IReadOnlyList<KnownContent> known, CancellationToken cancellationToken);

        /// <summary>Called after the sources of a cycle were read, so the provider can report what the cycle used.</summary>
        void EndCycle();
    }
}
