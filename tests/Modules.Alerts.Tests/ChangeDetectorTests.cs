using Mognet.Modules.Alerts.Detection;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.Tests
{
    public class ChangeDetectorTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        private static KnownContent Known(string id, DateTimeOffset? start = null, LiveState? state = null, ContentType? type = null) =>
            new(1, id, start, T0, T0, state, type);

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

        [Fact]
        public void A_new_item_reports_its_type()
        {
            var changes = ChangeDetector.Detect([new ContentItem("a", null, null, ContentType.Short)], []);

            Assert.Equal([new ContentChange("a", ChangeKind.New, ContentType.Short)], changes);
        }

        [Fact]
        public void A_new_type_is_reported_with_that_type()
        {
            var changes = ChangeDetector.Detect([new ContentItem("a", null, null, ContentType.Video)], [Known("a", type: ContentType.Unclassified)]);

            Assert.Equal([new ContentChange("a", ChangeKind.TypeChanged, ContentType.Video)], changes);
        }

        [Fact]
        public void The_same_type_reports_nothing()
        {
            Assert.Empty(ChangeDetector.Detect([new ContentItem("a", null, null, ContentType.Video)], [Known("a", type: ContentType.Video)]));
        }

        [Fact]
        public void An_item_with_no_type_this_time_is_not_a_type_change()
        {
            Assert.Empty(ChangeDetector.Detect([new ContentItem("a", null)], [Known("a", type: ContentType.Video)]));
        }

        [Fact]
        public void A_stream_going_from_scheduled_to_live_reports_the_state_and_the_type()
        {
            var start = T0.AddDays(1);

            var changes = ChangeDetector.Detect(
                [new ContentItem("a", start, LiveState.Live, ContentType.StreamLive)],
                [Known("a", start, LiveState.Upcoming, ContentType.StreamScheduled)]);

            Assert.Equal(
                [new ContentChange("a", ChangeKind.LiveStateChanged), new ContentChange("a", ChangeKind.TypeChanged, ContentType.StreamLive)],
                changes);
        }

        [Fact]
        public void A_type_for_an_item_stored_without_one_is_a_change()
        {
            var changes = ChangeDetector.Detect([new ContentItem("a", null, null, ContentType.Video)], [Known("a")]);

            Assert.Equal([new ContentChange("a", ChangeKind.TypeChanged, ContentType.Video)], changes);
        }
    }
}
