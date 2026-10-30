using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class ScheduleWindowTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Start_in_the_past_is_excluded()
        {
            Assert.False(ScheduleWindow.Includes(Now.AddMinutes(-1), Now, lookaheadDays: 14));
        }

        [Fact]
        public void Start_exactly_now_is_excluded()
        {
            Assert.False(ScheduleWindow.Includes(Now, Now, lookaheadDays: 14));
        }

        [Fact]
        public void Start_inside_the_window_is_included()
        {
            Assert.True(ScheduleWindow.Includes(Now.AddDays(3), Now, lookaheadDays: 14));
        }

        [Fact]
        public void Start_exactly_at_the_cutoff_is_included()
        {
            Assert.True(ScheduleWindow.Includes(Now.AddDays(14), Now, lookaheadDays: 14));
        }

        [Fact]
        public void Start_after_the_cutoff_is_excluded()
        {
            Assert.False(ScheduleWindow.Includes(Now.AddDays(14).AddSeconds(1), Now, lookaheadDays: 14));
        }
    }
}
