using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class SegmentSelectorTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        private static TwitchSegment Segment(string id, DateTimeOffset start, bool canceled = false) =>
            new(id, "Title", "Category", start, start.AddHours(2), canceled);

        // An occurrence of a recurring series, with an ID in the format Twitch uses (base64 JSON).
        private static TwitchSegment Weekly(string series, DateTimeOffset start, bool canceled = false)
        {
            var week = System.Globalization.ISOWeek.GetWeekOfYear(start.UtcDateTime);
            var json = $"{{\"segmentID\":\"{series}\",\"isoYear\":{start.Year},\"isoWeek\":{week}}}";
            var id = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            return new TwitchSegment(id, "Weekly", "Category", start, start.AddHours(2), canceled, IsRecurring: true);
        }

        private static IEnumerable<string> SelectIds(
            IEnumerable<TwitchSegment> segments, VacationWindow? vacation = null, int maxEvents = 10) =>
            SegmentSelector.Select(segments, vacation, Now, lookaheadDays: 14, maxEvents).Select(s => s.Id);

        [Fact]
        public void Recurring_series_shows_only_its_next_occurrence()
        {
            var thisWeek = Weekly("series-a", Now.AddDays(1));
            var nextWeek = Weekly("series-a", Now.AddDays(8));

            Assert.Equal([thisWeek.Id], SelectIds([nextWeek, thisWeek]));
        }

        [Fact]
        public void Different_recurring_series_are_shown_side_by_side()
        {
            var monday = Weekly("series-monday", Now.AddDays(1));
            var friday = Weekly("series-friday", Now.AddDays(4));
            var nextMonday = Weekly("series-monday", Now.AddDays(8));

            Assert.Equal([monday.Id, friday.Id], SelectIds([monday, friday, nextMonday]));
        }

        [Fact]
        public void Canceled_next_occurrence_shows_the_one_after()
        {
            var canceled = Weekly("series-a", Now.AddDays(1), canceled: true);
            var following = Weekly("series-a", Now.AddDays(8));

            Assert.Equal([following.Id], SelectIds([canceled, following]));
        }

        [Fact]
        public void Occurrence_in_vacation_is_replaced_by_the_first_one_after()
        {
            var vacation = new VacationWindow(Now, Now.AddDays(5));
            var during = Weekly("series-a", Now.AddDays(1));
            var after = Weekly("series-a", Now.AddDays(8));

            Assert.Equal([after.Id], SelectIds([during, after], vacation));
        }

        [Fact]
        public void One_time_segments_are_never_merged()
        {
            Assert.Equal(["a", "b"], SelectIds([Segment("a", Now.AddDays(1)), Segment("b", Now.AddDays(8))]));
        }

        // Recurring segment whose ID does not decode (e.g. if Twitch changes the ID format).
        private static TwitchSegment Opaque(string id, string title, DateTimeOffset start) =>
            new(id, title, "c", start, null, false, IsRecurring: true);

        [Fact]
        public void Undecodable_ids_fall_back_to_title_weekday_and_time()
        {
            var thisWeek = Opaque("not-base64!", "Minecraft Monday", Now.AddDays(1));
            var nextWeek = Opaque("also-opaque", "Minecraft Monday", Now.AddDays(8));

            Assert.Equal(["not-base64!"], SelectIds([thisWeek, nextWeek]));
        }

        [Fact]
        public void Fallback_keeps_different_titles_apart()
        {
            var first = Opaque("x1", "Minecraft Monday", Now.AddDays(1));
            var second = Opaque("x2", "Minecraft Monday – Finale", Now.AddDays(8));

            Assert.Equal(["x1", "x2"], SelectIds([first, second]));
        }

        [Fact]
        public void Fallback_keeps_different_times_apart()
        {
            var first = Opaque("x1", "Weekly", Now.AddDays(1));
            var second = Opaque("x2", "Weekly", Now.AddDays(8).AddHours(1));

            Assert.Equal(["x1", "x2"], SelectIds([first, second]));
        }

        [Fact]
        public void Fallback_keeps_different_weekdays_apart()
        {
            var first = Opaque("x1", "Weekly", Now.AddDays(1));
            var second = Opaque("x2", "Weekly", Now.AddDays(2));

            Assert.Equal(["x1", "x2"], SelectIds([first, second]));
        }

        [Fact]
        public void Fallback_is_only_used_for_recurring_segments()
        {
            var first = new TwitchSegment("x1", "Weekly", "c", Now.AddDays(1), null, false);
            var second = new TwitchSegment("x2", "Weekly", "c", Now.AddDays(8), null, false);

            Assert.Equal(["x1", "x2"], SelectIds([first, second]));
        }

        [Fact]
        public void Twitch_documentation_example_id_decodes_to_its_series()
        {
            // Example segment from https://dev.twitch.tv/docs/api/reference/#get-channel-stream-schedule
            var segment = new TwitchSegment(
                "eyJzZWdtZW50SUQiOiJlNGFjYzcyNC0zNzFmLTQwMmMtODFjYS0yM2FkYTc5NzU5ZDQiLCJpc29ZZWFyIjoyMDIxLCJpc29XZWVrIjoyNn0=",
                "t", "c", Now.AddDays(1), null, false, IsRecurring: true);

            Assert.Equal("series:e4acc724-371f-402c-81ca-23ada79759d4", segment.SeriesKey);
        }

        [Fact]
        public void At_most_max_events_are_selected_earliest_first()
        {
            var segments = Enumerable.Range(1, 13).Select(i => Segment($"s{i:00}", Now.AddHours(i * 20))).Reverse();

            Assert.Equal(["s01", "s02", "s03"], SelectIds(segments, maxEvents: 3));
            Assert.Equal(10, SelectIds(segments).Count());
        }

        [Fact]
        public void Cap_applies_after_merging_recurring_series()
        {
            var segments = new[]
            {
                Weekly("series-a", Now.AddDays(1)),
                Weekly("series-a", Now.AddDays(8)),
                Segment("one-time", Now.AddDays(2)),
            };

            Assert.Equal(2, SelectIds(segments, maxEvents: 2).Count());
            Assert.Contains("one-time", SelectIds(segments, maxEvents: 2));
        }

        [Fact]
        public void Upcoming_segment_inside_the_window_is_selected()
        {
            Assert.Equal(["a"], SelectIds([Segment("a", Now.AddDays(1))]));
        }

        [Fact]
        public void Canceled_segment_is_skipped()
        {
            Assert.Equal(["a"], SelectIds([Segment("a", Now.AddDays(1)), Segment("b", Now.AddDays(2), canceled: true)]));
        }

        [Fact]
        public void Segments_outside_the_window_are_skipped()
        {
            var segments = new[]
            {
                Segment("past", Now.AddHours(-1)),
                Segment("now", Now),
                Segment("cutoff", Now.AddDays(14)),
                Segment("after", Now.AddDays(14).AddSeconds(1)),
            };

            Assert.Equal(["cutoff"], SelectIds(segments));
        }

        [Fact]
        public void Segments_during_vacation_are_skipped()
        {
            var vacation = new VacationWindow(Now.AddDays(2), Now.AddDays(5));
            var segments = new[]
            {
                Segment("before", Now.AddDays(1)),
                Segment("vacation-start", Now.AddDays(2)),
                Segment("during", Now.AddDays(3)),
                Segment("vacation-end", Now.AddDays(5)),
                Segment("after", Now.AddDays(6)),
            };

            Assert.Equal(["before", "vacation-end", "after"], SelectIds(segments, vacation));
        }

        [Fact]
        public void Vacation_that_ends_mid_window_only_hides_its_own_period()
        {
            var vacation = new VacationWindow(Now.AddDays(-3), Now.AddDays(1));
            var segments = new[] { Segment("in-vacation", Now.AddHours(12)), Segment("back", Now.AddDays(2)) };

            Assert.Equal(["back"], SelectIds(segments, vacation));
        }

        [Fact]
        public void Vacation_after_the_window_changes_nothing()
        {
            var vacation = new VacationWindow(Now.AddDays(20), Now.AddDays(30));

            Assert.Equal(["a"], SelectIds([Segment("a", Now.AddDays(1))], vacation));
        }
    }

    public class SchedulePagingTests
    {
        private static readonly DateTimeOffset Cutoff = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

        private static TwitchSegment Segment(DateTimeOffset start) => new("id", "", "", start, null, false);

        [Fact]
        public void Next_page_is_needed_when_the_page_ends_before_the_cutoff_and_a_cursor_exists()
        {
            Assert.True(SchedulePaging.NeedsNextPage([Segment(Cutoff.AddDays(-5)), Segment(Cutoff)], "cursor", Cutoff));
        }

        [Fact]
        public void No_next_page_when_the_page_reaches_past_the_cutoff()
        {
            Assert.False(SchedulePaging.NeedsNextPage([Segment(Cutoff.AddDays(-5)), Segment(Cutoff.AddSeconds(1))], "cursor", Cutoff));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void No_next_page_without_a_cursor(string? cursor)
        {
            Assert.False(SchedulePaging.NeedsNextPage([Segment(Cutoff.AddDays(-5))], cursor, Cutoff));
        }

        [Fact]
        public void No_next_page_after_an_empty_page()
        {
            Assert.False(SchedulePaging.NeedsNextPage([], "cursor", Cutoff));
        }
    }
}
