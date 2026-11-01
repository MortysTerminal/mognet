namespace Mognet.Modules.Alerts.Providers
{
    /// <summary>What a provider needs to know about one poll cycle before it reads any source.</summary>
    /// <param name="SourceCount">Sources of this provider's platform that the cycle reads.</param>
    /// <param name="DiscoveryIntervalMinutes">
    /// Requested minutes between two runs of the platform's costlier call that finds content a listing misses.
    /// </param>
    public sealed record CycleContext(int SourceCount, int DiscoveryIntervalMinutes);
}
