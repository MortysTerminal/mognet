using Mognet.Modules.Alerts.Detection;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.Tests
{
    public class ChangeDetectorTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        private static KnownContent Known(string id, DateTimeOffset? start = null, LiveState? state = null) =>
            new(1, id, start, T0, T0, state);

        [Fact]
        public void An_unknown_id_is_new()
        {
            var changes = ChangeDetector.Detect([new ContentItem("a", null)], [Known("b")]);

            Assert.Equal([new ContentChange("a", ChangeKind.New)], changes);
        }

        [Fact]
        public void An_equal_item_reports_nothing()
        {
            var start = T0.AddDays(1);

            var changes = ChangeDetector.Detect([new ContentItem("a", start, LiveState.Upcoming)], [Known("a", start, LiveState.Upcoming)]);

            Assert.Empty(changes);
        }

        [Fact]
        public void A_moved_start_is_reported()
        {
            var changes = ChangeDetector.Detect([new ContentItem("a", T0.AddDays(2), LiveState.Upcoming)], [Known("a", T0.AddDays(1), LiveState.Upcoming)]);

            Assert.Equal([new ContentChange("a", ChangeKind.ScheduledStartChanged)], changes);
        }

        [Fact]
        public void The_same_instant_in_another_offset_is_not_a_change()
        {
            var changes = ChangeDetector.Detect(
                [new ContentItem("a", new DateTimeOffset(2026, 3, 2, 14, 0, 0, TimeSpan.FromHours(2)), LiveState.Upcoming)],
                [Known("a", new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero), LiveState.Upcoming)]);

            Assert.Empty(changes);
        }

        [Fact]
        public void A_new_live_state_is_reported()
        {
            var start = T0.AddDays(1);

            var changes = ChangeDetector.Detect([new ContentItem("a", start, LiveState.Live)], [Known("a", start, LiveState.Upcoming)]);

            Assert.Equal([new ContentChange("a", ChangeKind.LiveStateChanged)], changes);
        }

        [Fact]
        public void Start_and_state_changing_together_report_both()
        {
            var changes = ChangeDetector.Detect([new ContentItem("a", T0.AddDays(2), LiveState.Live)], [Known("a", T0.AddDays(1), LiveState.Upcoming)]);

            Assert.Equal(
                [new ContentChange("a", ChangeKind.ScheduledStartChanged), new ContentChange("a", ChangeKind.LiveStateChanged)],
                changes);
        }

        [Fact]
        public void A_start_that_appears_or_disappears_is_a_change()
        {
            Assert.Single(ChangeDetector.Detect([new ContentItem("a", T0)], [Known("a", null)]));
            Assert.Single(ChangeDetector.Detect([new ContentItem("a", null)], [Known("a", T0)]));
        }
    }
}
