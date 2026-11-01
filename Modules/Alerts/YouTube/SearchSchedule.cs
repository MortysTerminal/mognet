using Microsoft.Extensions.Logging;

namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>
    /// Decides how often <c>search.list</c> may run. The search quota is per project, so an interval that would
    /// use more than <see cref="DailyLimit"/> calls a day for all sources is rejected.
    /// </summary>
    public sealed class SearchSchedule
    {
        public const int DailyLimit = 100;

        private readonly ILogger _logger;
        private readonly Dictionary<long, DateTimeOffset> _lastSearch = [];
        private int? _lastValidMinutes;

        public SearchSchedule(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>The interval searches run at, or <see langword="null"/> when no interval fits and searching is paused.</summary>
        public TimeSpan? Interval { get; private set; }

        public static bool Fits(int minutes, int sourceCount) =>
            minutes > 0 && (long)sourceCount * 24 * 60 <= (long)DailyLimit * minutes;

        /// <summary>
        /// Takes the requested interval. One that does not fit keeps the last valid value and is logged; with none
        /// valid (or none that still fits the current number of sources) searching is paused for the cycle.
        /// </summary>
        public void Apply(int requestedMinutes, int sourceCount)
        {
            if (Fits(requestedMinutes, sourceCount))
            {
                _lastValidMinutes = requestedMinutes;
                Interval = TimeSpan.FromMinutes(requestedMinutes);
                return;
            }

            if (_lastValidMinutes is { } kept && Fits(kept, sourceCount))
            {
                _logger.LogWarning(
                    "search_interval_minutes = {Requested} would use more than {Limit} search calls per day for {Sources} source(s); keeping {Kept} minute(s).",
                    requestedMinutes, DailyLimit, sourceCount, kept);
                Interval = TimeSpan.FromMinutes(kept);
                return;
            }

            _logger.LogWarning(
                "search_interval_minutes = {Requested} would use more than {Limit} search calls per day for {Sources} source(s) and no earlier value fits; searches are paused.",
                requestedMinutes, DailyLimit, sourceCount);
            Interval = null;
        }

        public bool IsDue(long sourceId, DateTimeOffset now) =>
            Interval is { } interval && (!_lastSearch.TryGetValue(sourceId, out var last) || now - last >= interval);

        /// <summary>Records a search attempt, whether or not it succeeded, so a failing call is not retried every cycle.</summary>
        public void MarkSearched(long sourceId, DateTimeOffset now) => _lastSearch[sourceId] = now;
    }
}
