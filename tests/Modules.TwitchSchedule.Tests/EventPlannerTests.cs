using Mognet.Modules.TwitchSchedule.Models;
using Mognet.Modules.TwitchSchedule.Sync;
using static Mognet.Modules.TwitchSchedule.Tests.TestData;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class EventPlannerTests
    {
        private readonly EventPlanner _planner = new(English);

        [Fact]
        public void New_entry_is_created()
        {
            var entry = Entry();

            var plan = _planner.Plan([Fetched("somechannel", entry)], [], Now);

            var created = Assert.Single(plan.Creates);
            Assert.Same(entry, created.Entry);
            Assert.Equal("Some Channel – Minecraft Monday", created.Name);
            Assert.Equal(entry.StartTime, created.StartTime);
            Assert.Equal(entry.StartTime.AddHours(3), created.EndTime);
            Assert.Equal("https://www.twitch.tv/somechannel", created.Location);
            Assert.Empty(plan.Updates);
            Assert.Empty(plan.Deletes);
        }

        [Fact]
        public void Matching_event_without_changes_is_left_alone()
        {
            var entry = Entry();
            var existing = ExistingFor(entry);

            var plan = _planner.Plan([Fetched("somechannel", entry)], [existing], Now);

            Assert.Equal([existing], plan.Unchanged);
            Assert.Empty(plan.Creates);
            Assert.Empty(plan.Updates);
            Assert.Empty(plan.Deletes);
        }

        [Fact]
        public void Changed_title_updates_the_event()
        {
            var existing = ExistingFor(Entry(title: "Old title"));
            var entry = Entry(title: "New title");

            var plan = _planner.Plan([Fetched("somechannel", entry)], [existing], Now);

            var update = Assert.Single(plan.Updates);
            Assert.Same(existing, update.Existing);
            Assert.Equal("Some Channel – New title", update.Desired.Name);
            Assert.Empty(plan.Creates);
            Assert.Empty(plan.Deletes);
        }

        [Fact]
        public void Changed_start_updates_the_event()
        {
            var start = new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero);
            var existing = ExistingFor(Entry(start: start));
            var entry = Entry(start: start.AddHours(1));

            var plan = _planner.Plan([Fetched("somechannel", entry)], [existing], Now);

            var update = Assert.Single(plan.Updates);
            Assert.Equal(start.AddHours(1), update.Desired.StartTime);
        }

        [Fact]
        public void Changed_category_updates_the_event()
        {
            var existing = ExistingFor(Entry(category: "Minecraft"));
            var entry = Entry(category: "Just Chatting");

            var plan = _planner.Plan([Fetched("somechannel", entry)], [existing], Now);

            Assert.Single(plan.Updates);
        }

        [Fact]
        public void Bot_event_no_longer_in_schedule_is_deleted()
        {
            var obsolete = ExistingFor(Entry(segmentId: "gone"), id: 7);

            var plan = _planner.Plan([Fetched("somechannel")], [obsolete], Now);

            Assert.Equal([obsolete], plan.Deletes);
        }

        [Fact]
        public void Manually_created_events_are_never_touched()
        {
            var manual = new ExistingEvent(9, "Community game night", "Bring snacks", DateTimeOffset.UnixEpoch, null, "Voice");
            var withoutDescription = new ExistingEvent(10, "Quiz", null, DateTimeOffset.UnixEpoch, null, null);

            var plan = _planner.Plan([Fetched("somechannel", Entry())], [manual, withoutDescription], Now);

            Assert.Empty(plan.Deletes);
            Assert.Empty(plan.Updates);
            Assert.Single(plan.Creates);
        }

        [Fact]
        public void Mixed_cycle_creates_updates_keeps_and_deletes()
        {
            var keep = Entry(segmentId: "keep");
            var change = Entry(segmentId: "change", title: "New title");
            var add = Entry(segmentId: "add");
            var existingKeep = ExistingFor(keep, id: 1);
            var existingChange = ExistingFor(Entry(segmentId: "change", title: "Old title"), id: 2);
            var existingGone = ExistingFor(Entry(segmentId: "gone"), id: 3);

            var plan = _planner.Plan([Fetched("somechannel", keep, change, add)], [existingKeep, existingChange, existingGone], Now);

            Assert.Equal(["add"], plan.Creates.Select(c => c.Entry.TwitchSegmentId));
            Assert.Equal([2UL], plan.Updates.Select(u => u.Existing.Id));
            Assert.Equal([1UL], plan.Unchanged.Select(e => e.Id));
            Assert.Equal([3UL], plan.Deletes.Select(e => e.Id));
        }

        [Fact]
        public void German_planner_creates_German_content()
        {
            var plan = new EventPlanner(German).Plan([Fetched("somechannel", Entry(title: ""))], [], Now);

            var created = Assert.Single(plan.Creates);
            Assert.Equal("Some Channel – Stream von Some Channel", created.Name);
            Assert.Contains("Uhr", created.Description);
        }

        // ── Deletion safety ─────────────────────────────────────────────────────

        [Fact]
        public void Failed_fetch_keeps_that_channels_events()
        {
            var existing = ExistingFor(Entry(segmentId: "s1"), id: 1);

            var plan = _planner.Plan([Failed("somechannel")], [existing], Now);

            Assert.Empty(plan.Deletes);
            Assert.Equal([existing], plan.Protected);
        }

        [Fact]
        public void Failed_fetch_of_one_channel_does_not_protect_another_channels_events()
        {
            var goneA = ExistingFor(Entry(segmentId: "a-gone", channel: "channel_a"), id: 1);
            var keptB = ExistingFor(Entry(segmentId: "b-1", channel: "channel_b"), id: 2);

            var plan = _planner.Plan([Fetched("channel_a"), Failed("channel_b")], [goneA, keptB], Now);

            Assert.Equal([goneA], plan.Deletes);
            Assert.Equal([keptB], plan.Protected);
        }

        [Fact]
        public void Failed_fetch_creates_nothing_for_that_channel()
        {
            var plan = _planner.Plan([Failed("somechannel")], [], Now);

            Assert.Empty(plan.Creates);
            Assert.Empty(plan.Deletes);
        }

        [Fact]
        public void Successful_empty_schedule_deletes_that_channels_events()
        {
            // A 404 "no schedule" from Twitch is reported as a successful, empty fetch.
            var existing = ExistingFor(Entry(segmentId: "s1"), id: 1);

            var plan = _planner.Plan([Fetched("somechannel")], [existing], Now);

            Assert.Equal([existing], plan.Deletes);
            Assert.Empty(plan.Protected);
        }

        [Fact]
        public void Channel_tag_comparison_ignores_case()
        {
            var existing = ExistingFor(Entry(segmentId: "s1", channel: "somechannel"), id: 1);

            var plan = _planner.Plan([Failed("SomeChannel")], [existing], Now);

            Assert.Equal([existing], plan.Protected);
        }

        [Fact]
        public void Legacy_event_without_channel_tag_is_deleted_when_all_channels_succeeded()
        {
            var legacy = LegacyEventFor(Entry(segmentId: "old"), id: 1);

            var plan = _planner.Plan([Fetched("channel_a"), Fetched("channel_b")], [legacy], Now);

            Assert.Equal([legacy], plan.Deletes);
        }

        [Fact]
        public void Legacy_event_without_channel_tag_is_kept_when_any_channel_failed()
        {
            var legacy = LegacyEventFor(Entry(segmentId: "old"), id: 1);

            var plan = _planner.Plan([Fetched("channel_a"), Failed("channel_b")], [legacy], Now);

            Assert.Empty(plan.Deletes);
            Assert.Equal([legacy], plan.Protected);
        }

        [Fact]
        public void Legacy_event_still_matches_and_gets_the_channel_tag()
        {
            var entry = Entry(segmentId: "s1");
            var legacy = LegacyEventFor(entry, id: 1);

            var plan = _planner.Plan([Fetched("somechannel", entry)], [legacy], Now);

            var update = Assert.Single(plan.Updates);
            Assert.Same(legacy, update.Existing);
            Assert.EndsWith("[twitch-channel:somechannel]", update.Desired.Description);
            Assert.Empty(plan.Creates);
            Assert.Empty(plan.Deletes);
        }

        [Fact]
        public void Event_of_a_removed_channel_is_deleted_when_all_configured_channels_succeeded()
        {
            var removed = ExistingFor(Entry(segmentId: "r1", channel: "removed_channel"), id: 1);

            var plan = _planner.Plan([Fetched("somechannel")], [removed], Now);

            Assert.Equal([removed], plan.Deletes);
        }

        [Fact]
        public void Event_of_a_removed_channel_is_kept_when_any_configured_channel_failed()
        {
            // Protects against a typo in a channel name: the misspelled channel fails, and the events of the
            // correctly spelled (now "unconfigured") channel must survive.
            var events = ExistingFor(Entry(segmentId: "r1", channel: "mortys_channel"), id: 1);

            var plan = _planner.Plan([Failed("mortys_chanel")], [events], Now);

            Assert.Empty(plan.Deletes);
            Assert.Equal([events], plan.Protected);
        }

        // ── Started events ──────────────────────────────────────────────────────

        [Fact]
        public void Started_event_no_longer_in_schedule_is_kept()
        {
            // Twitch drops a segment from the window once it starts; Discord must keep the event until it completes.
            var running = ExistingFor(Entry(segmentId: "live", start: Now.AddMinutes(-30)), id: 1);

            var plan = _planner.Plan([Fetched("somechannel")], [running], Now);

            Assert.Empty(plan.Deletes);
            Assert.Empty(plan.Protected);
            Assert.Equal([running], plan.Started);
        }

        [Fact]
        public void Event_starting_exactly_now_counts_as_started()
        {
            var starting = ExistingFor(Entry(segmentId: "s1", start: Now), id: 1);

            var plan = _planner.Plan([Fetched("somechannel")], [starting], Now);

            Assert.Equal([starting], plan.Started);
            Assert.Empty(plan.Deletes);
        }

        [Fact]
        public void Started_event_is_not_modified_even_if_the_schedule_differs()
        {
            var start = Now.AddMinutes(-10);
            var running = ExistingFor(Entry(segmentId: "s1", title: "Old title", start: start), id: 1);
            var entry = Entry(segmentId: "s1", title: "New title", start: start);

            var plan = _planner.Plan([Fetched("somechannel", entry)], [running], Now);

            Assert.Empty(plan.Updates);
            Assert.Empty(plan.Creates);
            Assert.Equal([running], plan.Started);
        }

        [Fact]
        public void Started_legacy_event_is_neither_deleted_nor_protected_when_a_channel_failed()
        {
            var running = LegacyEventFor(Entry(segmentId: "old", start: Now.AddHours(-1)), id: 1);

            var plan = _planner.Plan([Failed("somechannel")], [running], Now);

            Assert.Equal([running], plan.Started);
            Assert.Empty(plan.Protected);
            Assert.Empty(plan.Deletes);
        }

        [Fact]
        public void Started_manual_events_are_ignored()
        {
            var manual = new ExistingEvent(9, "Game night", "Bring snacks", Now.AddHours(-1), null, "Voice");

            var plan = _planner.Plan([Fetched("somechannel")], [manual], Now);

            Assert.Empty(plan.Started);
            Assert.Empty(plan.Deletes);
        }

        // ── Change detection ────────────────────────────────────────────────────

        [Fact]
        public void Changed_end_time_updates_the_event()
        {
            var start = Now.AddDays(1);
            var existing = ExistingFor(Entry(start: start, end: start.AddHours(2)));
            var entry = Entry(start: start, end: start.AddHours(4));

            var plan = _planner.Plan([Fetched("somechannel", entry)], [existing], Now);

            var update = Assert.Single(plan.Updates);
            Assert.Equal(start.AddHours(4), update.Desired.EndTime);
        }

        [Fact]
        public void Changed_location_updates_the_event()
        {
            var entry = Entry();
            var existing = ExistingFor(entry) with { Location = "https://example.org/somewhere-else" };

            var plan = _planner.Plan([Fetched("somechannel", entry)], [existing], Now);

            var update = Assert.Single(plan.Updates);
            Assert.Equal("https://www.twitch.tv/somechannel", update.Desired.Location);
        }

        [Fact]
        public void Changed_end_time_from_missing_to_set_updates_the_event()
        {
            var start = Now.AddDays(1);
            var existing = ExistingFor(Entry(start: start, end: null));
            var entry = Entry(start: start, end: start.AddHours(5));

            var plan = _planner.Plan([Fetched("somechannel", entry)], [existing], Now);

            Assert.Single(plan.Updates);
        }

        [Fact]
        public void Manual_events_are_neither_deleted_nor_protected()
        {
            var manual = new ExistingEvent(9, "Community game night", "Bring snacks", DateTimeOffset.UnixEpoch, null, "Voice");

            var plan = _planner.Plan([Failed("somechannel")], [manual], Now);

            Assert.Empty(plan.Deletes);
            Assert.Empty(plan.Protected);
        }
    }
}
