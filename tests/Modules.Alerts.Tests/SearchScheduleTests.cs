using Microsoft.Extensions.Logging;
using Mognet.Modules.Alerts.YouTube;

namespace Mognet.Modules.Alerts.Tests
{
    public class SearchScheduleTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        private readonly LogSink _log = new();

        private SearchSchedule Schedule() => new(_log.Logger<YouTubeProvider>());

        [Theory]
        [InlineData(30, 1, true)]
        [InlineData(30, 2, true)]
        [InlineData(30, 3, false)]
        [InlineData(15, 1, true)]
        [InlineData(14, 1, false)]
        [InlineData(15, 2, false)]
        [InlineData(60, 4, true)]
        [InlineData(60, 5, false)]
        public void An_interval_fits_when_all_sources_stay_within_100_calls_a_day(int minutes, int sources, bool fits)
        {
            Assert.Equal(fits, SearchSchedule.Fits(minutes, sources));
        }

        [Fact]
        public void A_fitting_interval_is_taken()
        {
            var schedule = Schedule();

            schedule.Apply(30, 1);

            Assert.Equal(TimeSpan.FromMinutes(30), schedule.Interval);
            Assert.Empty(_log.Entries);
        }

        [Fact]
        public void An_interval_that_would_exceed_the_limit_is_rejected_and_the_last_valid_one_is_kept_and_logged()
        {
            var schedule = Schedule();
            schedule.Apply(30, 1);

            schedule.Apply(5, 1);

            Assert.Equal(TimeSpan.FromMinutes(30), schedule.Interval);
            Assert.Contains(_log.At(LogLevel.Warning), m => m.Contains("search_interval_minutes = 5") && m.Contains("keeping 30"));
        }

        [Fact]
        public void With_no_valid_interval_yet_searching_is_paused()
        {
            var schedule = Schedule();

            schedule.Apply(5, 1);

            Assert.Null(schedule.Interval);
            Assert.False(schedule.IsDue(1, T0));
            Assert.Contains(_log.At(LogLevel.Warning), m => m.Contains("paused"));
        }

        [Fact]
        public void A_kept_value_that_no_longer_fits_after_a_source_is_added_pauses_searching()
        {
            var schedule = Schedule();
            schedule.Apply(30, 2);

            schedule.Apply(30, 3);

            Assert.Null(schedule.Interval);
        }

        [Fact]
        public void A_source_is_due_when_never_searched_and_again_after_the_interval()
        {
            var schedule = Schedule();
            schedule.Apply(30, 1);
            Assert.True(schedule.IsDue(1, T0));

            schedule.MarkSearched(1, T0);

            Assert.False(schedule.IsDue(1, T0.AddMinutes(29)));
            Assert.True(schedule.IsDue(1, T0.AddMinutes(30)));
            Assert.True(schedule.IsDue(2, T0));
        }
    }
}
