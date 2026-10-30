namespace Mognet.Modules.TwitchSchedule.Sync
{
    /// <summary>
    /// Selects the Twitch segments that are mirrored to Discord. Pure: no I/O, no clock.
    /// </summary>
    /// <remarks>
    /// Keeps segments inside the look-ahead window that are not canceled and not during a vacation; of each recurring
    /// series only the next occurrence; at most <c>maxEvents</c> segments, earliest first.
    /// </remarks>
    public static class SegmentSelector
    {
        public static IReadOnlyList<TwitchSegment> Select(
            IEnumerable<TwitchSegment> segments,
            VacationWindow? vacation,
            DateTimeOffset now,
            int lookaheadDays,
            int maxEvents) =>
            segments
                .Where(s =>
                    ScheduleWindow.Includes(s.StartTime, now, lookaheadDays) &&
                    !s.IsCanceled &&
                    (vacation == null || !vacation.Contains(s.StartTime)))
                .OrderBy(s => s.StartTime)
                .DistinctBy(s => s.SeriesKey)
                .Take(Math.Max(0, maxEvents))
                .ToList();
    }

    /// <summary>
    /// Decides whether another page of the Twitch schedule is needed to cover the look-ahead window.
    /// </summary>
    public static class SchedulePaging
    {
        // Twitch allows at most 25 segments per page.
        public const int PageSize = 25;

        // Upper bound so a misbehaving cursor can never loop forever (25 × 10 = 250 segments).
        public const int MaxPages = 10;

        public static bool NeedsNextPage(IReadOnlyCollection<TwitchSegment> page, string? cursor, DateTimeOffset cutoff) =>
            !string.IsNullOrEmpty(cursor) &&
            page.Count > 0 &&
            page.Max(s => s.StartTime) <= cutoff;
    }
}
