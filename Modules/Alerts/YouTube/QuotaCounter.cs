namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>
    /// Counts what the bot sends to YouTube, per cycle and per UTC day. Units are the shared daily quota
    /// (playlist, video and channel calls cost 1 each); search calls have their own, smaller daily limit.
    /// </summary>
    /// <remarks>
    /// YouTube resets its quota at midnight Pacific Time, not at midnight UTC, so the daily figures are an
    /// approximation. The counters live in memory and start at zero after a restart.
    /// </remarks>
    public sealed class QuotaCounter
    {
        private readonly TimeProvider _time;
        private DateOnly _day;

        public QuotaCounter(TimeProvider time)
        {
            _time = time;
            _day = Today();
        }

        public int CycleUnits { get; private set; }
        public int CycleSearchCalls { get; private set; }
        public int DayUnits { get; private set; }
        public int DaySearchCalls { get; private set; }

        public void StartCycle()
        {
            RollDay();
            CycleUnits = 0;
            CycleSearchCalls = 0;
        }

        public void AddUnits(int units)
        {
            RollDay();
            CycleUnits += units;
            DayUnits += units;
        }

        public void AddSearchCall()
        {
            RollDay();
            CycleSearchCalls++;
            DaySearchCalls++;
        }

        private void RollDay()
        {
            var today = Today();
            if (today == _day)
                return;

            _day = today;
            DayUnits = 0;
            DaySearchCalls = 0;
        }

        private DateOnly Today() => DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
    }
}
