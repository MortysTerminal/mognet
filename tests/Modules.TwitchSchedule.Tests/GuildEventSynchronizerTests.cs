using Microsoft.Extensions.Logging;
using Mognet.Modules.TwitchSchedule.Models;
using Mognet.Modules.TwitchSchedule.Services;
using Mognet.Modules.TwitchSchedule.Storage;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class GuildEventSynchronizerTests
    {
        private sealed class FakeGuildEvents(params ExistingEvent[] existing) : IGuildEvents
        {
            public int Lists { get; private set; }
            public List<DesiredEvent> Created { get; } = [];
            public List<(ExistingEvent Existing, DesiredEvent Desired)> Updated { get; } = [];
            public List<ExistingEvent> Deleted { get; } = [];
            public Func<DesiredEvent, bool> FailCreate { get; set; } = _ => false;
            public int Writes => Created.Count + Updated.Count + Deleted.Count;

            public Task<IReadOnlyList<ExistingEvent>> ListAsync()
            {
                Lists++;
                return Task.FromResult<IReadOnlyList<ExistingEvent>>(existing);
            }

            public Task CreateAsync(DesiredEvent desired)
            {
                if (FailCreate(desired))
                    throw new InvalidOperationException("create failed");
                Created.Add(desired);
                return Task.CompletedTask;
            }

            public Task UpdateAsync(ExistingEvent e, DesiredEvent desired)
            {
                Updated.Add((e, desired));
                return Task.CompletedTask;
            }

            public Task DeleteAsync(ExistingEvent e)
            {
                Deleted.Add(e);
                return Task.CompletedTask;
            }
        }

        private static ScheduleConfig Config(string language = "en", bool dryRun = false) =>
            new(new SyncOptions(30, 14, 10), language, [new TwitchChannelConfig { Username = "somechannel", DisplayName = "Some Channel" }], dryRun);

        private static Task Sync(FakeGuildEvents events, ListLogger<object> log, ScheduleConfig config, params ChannelSchedule[] schedules) =>
            new GuildEventSynchronizer(log, new FixedTime(TestData.Now)).SyncAsync(events, schedules, config);

        private static StreamScheduleEntry Entry(string id, string channel = "somechannel", int day = 1) =>
            TestData.Entry(id, channel: channel, start: new DateTimeOffset(2026, 7, day, 18, 0, 0, TimeSpan.Zero));

        // New entry "new", changed event "changed", orphan event "orphan".
        private static (FakeGuildEvents Events, StreamScheduleEntry New, StreamScheduleEntry Changed, ExistingEvent Orphan) Mixed()
        {
            var created = Entry("new", day: 1);
            var changed = Entry("changed", day: 2);
            var orphan = TestData.ExistingFor(Entry("orphan", day: 3), 30);
            var stale = TestData.ExistingFor(changed, 20) with { Name = "old name" };
            return (new FakeGuildEvents(stale, orphan), created, changed, orphan);
        }

        [Fact]
        public async Task Real_mode_creates_updates_and_deletes_according_to_the_plan()
        {
            var (events, created, changed, orphan) = Mixed();
            var planner = new EventPlanner(TestData.English);

            await Sync(events, new ListLogger<object>(), Config(), TestData.Fetched("somechannel", created, changed));

            Assert.Equal(1, events.Lists);
            Assert.Equal([planner.ToDesiredEvent(created)], events.Created);
            var update = Assert.Single(events.Updated);
            Assert.Equal(20UL, update.Existing.Id);
            Assert.Equal(planner.ToDesiredEvent(changed), update.Desired);
            Assert.Equal([orphan], events.Deleted);
        }

        [Fact]
        public async Task Dry_run_lists_and_plans_but_writes_nothing_and_logs_each_change()
        {
            var (events, created, changed, orphan) = Mixed();
            var log = new ListLogger<object>();

            await Sync(events, log, Config(dryRun: true), TestData.Fetched("somechannel", created, changed));

            Assert.Equal(1, events.Lists);
            Assert.Equal(0, events.Writes);
            var planned = log.Messages.Where(m => m.Contains("[dry run]")).ToList();
            Assert.Equal(3, planned.Count);
            Assert.Contains(planned, m => m.Contains("create", StringComparison.OrdinalIgnoreCase) && m.Contains("Some Channel – Minecraft Monday") && m.Contains("2026-07-01"));
            Assert.Contains(planned, m => m.Contains("update", StringComparison.OrdinalIgnoreCase) && m.Contains("2026-07-02"));
            Assert.Contains(planned, m => m.Contains("delete", StringComparison.OrdinalIgnoreCase) && m.Contains(orphan.Name) && m.Contains("2026-07-03"));
            Assert.Single(log.Messages, m => m.Contains("Dry run is active"));
        }

        [Fact]
        public async Task Real_mode_does_not_log_dry_run()
        {
            var (events, created, changed, _) = Mixed();
            var log = new ListLogger<object>();

            await Sync(events, log, Config(), TestData.Fetched("somechannel", created, changed));

            Assert.DoesNotContain(log.Messages, m => m.Contains("dry run", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task A_tagged_event_of_a_channel_whose_fetch_failed_is_not_deleted()
        {
            var tagged = TestData.ExistingFor(Entry("gone", "failing"), 1);
            var other = TestData.ExistingFor(Entry("gone2", "fine"), 2);
            var events = new FakeGuildEvents(tagged, other);
            var log = new ListLogger<object>();

            await Sync(events, log, Config(), TestData.Failed("failing"), TestData.Fetched("fine"));

            Assert.Equal([other], events.Deleted);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(tagged.Name));
        }

        [Fact]
        public async Task An_untagged_channel_event_is_deleted_only_when_every_channel_succeeded()
        {
            var legacy = TestData.LegacyEventFor(Entry("old"), 5);

            var kept = new FakeGuildEvents(legacy);
            await Sync(kept, new ListLogger<object>(), Config(), TestData.Failed("a"), TestData.Fetched("b"));
            Assert.Empty(kept.Deleted);

            var deleted = new FakeGuildEvents(legacy);
            await Sync(deleted, new ListLogger<object>(), Config(), TestData.Fetched("a"), TestData.Fetched("b"));
            Assert.Equal([legacy], deleted.Deleted);
        }

        [Theory]
        [InlineData("en")]
        [InlineData("de")]
        public async Task Description_and_tags_equal_the_formatter_output_in_both_languages(string language)
        {
            var entry = Entry("segment-x");
            var events = new FakeGuildEvents();

            await Sync(events, new ListLogger<object>(), Config(language), TestData.Fetched("somechannel", entry));

            var formatter = EventFormatter.FromLanguage(language);
            var created = Assert.Single(events.Created);
            Assert.Equal(formatter.BuildDescription(entry), created.Description);
            Assert.Equal(formatter.BuildName(entry), created.Name);
            Assert.EndsWith("[twitch-segment-id:segment-x] [twitch-channel:somechannel]", created.Description);
            Assert.Equal("[twitch-segment-id:", EventFormatter.SegmentTagPrefix);
        }

        [Fact]
        public async Task A_create_that_throws_does_not_stop_the_following_calls()
        {
            var (events, created, changed, orphan) = Mixed();
            events.FailCreate = _ => true;
            var log = new ListLogger<object>();

            await Sync(events, log, Config(), TestData.Fetched("somechannel", created, changed));

            Assert.Empty(events.Created);
            Assert.Single(events.Updated);
            Assert.Equal([orphan], events.Deleted);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Failed to create"));
        }
    }
}
