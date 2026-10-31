using System.Collections.Concurrent;

namespace Mognet.Bot.Core.Health
{
    /// <summary>Result of a health evaluation: the modules whose deadline has passed.</summary>
    public sealed record HealthStatus(IReadOnlyList<string> Stalled)
    {
        public bool IsOk => Stalled.Count == 0;
    }

    /// <summary>
    /// Holds one deadline per running module. A module that keeps working moves its deadline forward; a module whose
    /// deadline has passed is reported as stalled. No registered module means healthy.
    /// </summary>
    public sealed class HealthRegistry
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _deadlines = new();

        public void Report(string moduleId, DateTimeOffset deadline) => _deadlines[moduleId] = deadline;

        public void Clear(string moduleId) => _deadlines.TryRemove(moduleId, out _);

        public HealthStatus Evaluate(DateTimeOffset now) =>
            new(_deadlines.Where(d => d.Value < now).Select(d => d.Key).Order(StringComparer.Ordinal).ToList());
    }
}
