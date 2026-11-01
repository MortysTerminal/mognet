using Mognet.Bot.Core.Storage;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.Tests
{
    public class AlertSourceStoreTests : IDisposable
    {
        private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        private AlertsDb _db = null!;

        private async Task<AlertSourceStore> Store() => (_db ??= await AlertsDb.CreateAsync()).Sources;

        public void Dispose() => _db?.Dispose();

        [Fact]
        public async Task An_added_source_is_stored_with_platform_external_id_and_time_and_no_baseline()
        {
            var store = await Store();

            var added = await store.AddSourceAsync("youtube", "UC123", T0);
            var loaded = await store.GetSourceAsync(added.Id);

            Assert.Equal(added, loaded);
            Assert.Equal("youtube", loaded!.Platform);
            Assert.Equal("UC123", loaded.ExternalId);
            Assert.False(loaded.BaselineRecorded);
            Assert.Equal(T0, loaded.CreatedAt);
        }

        [Fact]
        public async Task An_unknown_source_is_null()
        {
            var store = await Store();

            Assert.Null(await store.GetSourceAsync(999));
        }

        [Fact]
        public async Task Several_sources_per_platform_are_allowed_and_listed_by_platform()
        {
            var store = await Store();
            await store.AddSourceAsync("youtube", "UC1", T0);
            await store.AddSourceAsync("youtube", "UC2", T0);
            await store.AddSourceAsync("twitch", "morty", T0);

            Assert.Equal(["UC1", "UC2"], (await store.ListSourcesAsync("youtube")).Select(s => s.ExternalId));
            Assert.Equal(["morty"], (await store.ListSourcesAsync("twitch")).Select(s => s.ExternalId));
            Assert.Equal(3, (await store.ListSourcesAsync()).Count);
        }

        [Fact]
        public async Task The_same_external_id_on_two_platforms_is_allowed()
        {
            var store = await Store();

            await store.AddSourceAsync("youtube", "same", T0);
            await store.AddSourceAsync("twitch", "same", T0);

            Assert.Equal(2, (await store.ListSourcesAsync()).Count);
        }

        [Fact]
        public async Task Adding_the_same_platform_and_external_id_twice_fails()
        {
            var store = await Store();
            await store.AddSourceAsync("youtube", "UC1", T0);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddSourceAsync("youtube", "UC1", T0));

            Assert.Single(await store.ListSourcesAsync());
        }

        [Fact]
        public async Task The_database_itself_rejects_a_duplicate_source()
        {
            var store = await Store();
            await store.AddSourceAsync("youtube", "UC1", T0);

            await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteAsync(
                "INSERT INTO sources (platform, external_id, created_at) VALUES ('youtube', 'UC1', '2026-01-01')"));
        }

        [Theory]
        [InlineData("", "UC1")]
        [InlineData("youtube", " ")]
        public async Task Blank_platform_or_external_id_is_rejected(string platform, string externalId)
        {
            var store = await Store();

            await Assert.ThrowsAsync<ArgumentException>(() => store.AddSourceAsync(platform, externalId, T0));
        }

        [Fact]
        public async Task Marking_the_baseline_sets_the_marker_and_reports_whether_the_source_exists()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            var other = await store.AddSourceAsync("youtube", "UC2", T0);

            Assert.True(await store.MarkBaselineRecordedAsync(source.Id, T0.AddMinutes(1)));

            Assert.True((await store.GetSourceAsync(source.Id))!.BaselineRecorded);
            Assert.False((await store.GetSourceAsync(other.Id))!.BaselineRecorded);
            Assert.False(await store.MarkBaselineRecordedAsync(999, T0));
        }

        [Fact]
        public async Task Removing_a_source_removes_its_known_content_and_keeps_other_sources()
        {
            var store = await Store();
            var gone = await store.AddSourceAsync("youtube", "UC1", T0);
            var kept = await store.AddSourceAsync("youtube", "UC2", T0);
            await store.UpsertKnownContentAsync(gone.Id, "v1", null, T0);
            await store.UpsertKnownContentAsync(gone.Id, "v2", null, T0);
            await store.UpsertKnownContentAsync(kept.Id, "v1", null, T0);

            Assert.True(await store.RemoveSourceAsync(gone.Id));

            Assert.Null(await store.GetSourceAsync(gone.Id));
            Assert.Empty(await store.ListKnownContentAsync(gone.Id));
            Assert.Single(await _db.QueryAsync("SELECT 1 FROM known_content"));
            Assert.Single(await store.ListKnownContentAsync(kept.Id));
        }

        [Fact]
        public async Task Removing_an_unknown_source_returns_false()
        {
            var store = await Store();

            Assert.False(await store.RemoveSourceAsync(999));
        }

        [Fact]
        public async Task Known_content_needs_an_existing_source()
        {
            var store = await Store();

            await Assert.ThrowsAnyAsync<Exception>(() => store.UpsertKnownContentAsync(999, "v1", null, T0));
        }

        [Fact]
        public async Task Upsert_inserts_with_first_seen_and_last_refreshed_set_to_now()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            var start = T0.AddDays(2);

            await store.UpsertKnownContentAsync(source.Id, "v1", start, T0.AddMinutes(5));

            var item = await store.GetKnownContentAsync(source.Id, "v1");
            Assert.Equal(new KnownContent(source.Id, "v1", start, T0.AddMinutes(5), T0.AddMinutes(5)), item);
        }

        [Fact]
        public async Task Upsert_of_an_existing_item_updates_the_start_and_refresh_time_and_keeps_first_seen()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", T0.AddDays(2), T0);

            await store.UpsertKnownContentAsync(source.Id, "v1", T0.AddDays(3), T0.AddHours(1));

            var item = await store.GetKnownContentAsync(source.Id, "v1");
            Assert.Equal(T0.AddDays(3), item!.ScheduledStart);
            Assert.Equal(T0, item.FirstSeenAt);
            Assert.Equal(T0.AddHours(1), item.LastRefreshedAt);
            Assert.Single(await store.ListKnownContentAsync(source.Id));
        }

        [Fact]
        public async Task Upsert_without_a_start_clears_a_stored_start()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", T0.AddDays(2), T0);

            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0.AddHours(1));

            Assert.Null((await store.GetKnownContentAsync(source.Id, "v1"))!.ScheduledStart);
        }

        [Fact]
        public async Task The_same_content_id_under_two_sources_is_two_items()
        {
            var store = await Store();
            var a = await store.AddSourceAsync("youtube", "UC1", T0);
            var b = await store.AddSourceAsync("youtube", "UC2", T0);

            await store.UpsertKnownContentAsync(a.Id, "v1", null, T0);
            await store.UpsertKnownContentAsync(b.Id, "v1", null, T0);

            Assert.Equal(2, (await _db.QueryAsync("SELECT 1 FROM known_content")).Count);
        }

        [Fact]
        public async Task The_database_itself_rejects_a_duplicate_known_content_item()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0);

            await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteAsync(
                $"INSERT INTO known_content (source_id, content_id, first_seen_at, last_refreshed_at) VALUES ({source.Id}, 'v1', '2026-01-01', '2026-01-01')"));
        }

        [Fact]
        public async Task An_unknown_item_is_null()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);

            Assert.Null(await store.GetKnownContentAsync(source.Id, "nope"));
        }

        [Fact]
        public async Task Items_not_refreshed_since_the_cutoff_are_listed_across_sources_and_the_cutoff_itself_is_fresh()
        {
            var store = await Store();
            var a = await store.AddSourceAsync("youtube", "UC1", T0);
            var b = await store.AddSourceAsync("youtube", "UC2", T0);
            var cutoff = T0.AddDays(10);
            await store.UpsertKnownContentAsync(a.Id, "old", null, cutoff.AddSeconds(-1));
            await store.UpsertKnownContentAsync(a.Id, "edge", null, cutoff);
            await store.UpsertKnownContentAsync(b.Id, "older", null, cutoff.AddDays(-5));
            await store.UpsertKnownContentAsync(b.Id, "new", null, cutoff.AddSeconds(1));

            var stale = await store.ListNotRefreshedSinceAsync(cutoff);

            Assert.Equal(["older", "old"], stale.Select(i => i.ContentId));
        }

        [Fact]
        public async Task Times_with_another_offset_compare_by_instant()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            var plusTwo = new DateTimeOffset(2026, 11, 1, 14, 0, 0, TimeSpan.FromHours(2)); // equals T0
            await store.UpsertKnownContentAsync(source.Id, "v1", null, plusTwo);

            Assert.Empty(await store.ListNotRefreshedSinceAsync(T0));
            Assert.Single(await store.ListNotRefreshedSinceAsync(T0.AddSeconds(1)));
            Assert.Equal(T0, (await store.GetKnownContentAsync(source.Id, "v1"))!.LastRefreshedAt);
        }

        [Fact]
        public async Task Marking_an_item_refreshed_takes_it_out_of_the_stale_list_and_leaves_the_rest()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", T0.AddDays(1), T0);
            await store.UpsertKnownContentAsync(source.Id, "v2", null, T0);
            var cutoff = T0.AddDays(1);

            Assert.True(await store.MarkRefreshedAsync(source.Id, "v1", T0.AddDays(2)));

            Assert.Equal(["v2"], (await store.ListNotRefreshedSinceAsync(cutoff)).Select(i => i.ContentId));
            var item = await store.GetKnownContentAsync(source.Id, "v1");
            Assert.Equal(T0.AddDays(2), item!.LastRefreshedAt);
            Assert.Equal(T0, item.FirstSeenAt);
            Assert.Equal(T0.AddDays(1), item.ScheduledStart);
        }

        [Fact]
        public async Task Marking_an_unknown_item_refreshed_returns_false()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);

            Assert.False(await store.MarkRefreshedAsync(source.Id, "nope", T0));
        }

        [Fact]
        public async Task Deleting_an_item_removes_only_that_item()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0);
            await store.UpsertKnownContentAsync(source.Id, "v2", null, T0);

            Assert.True(await store.DeleteKnownContentAsync(source.Id, "v1"));

            Assert.Null(await store.GetKnownContentAsync(source.Id, "v1"));
            Assert.NotNull(await store.GetKnownContentAsync(source.Id, "v2"));
            Assert.False(await store.DeleteKnownContentAsync(source.Id, "v1"));
        }

        [Fact]
        public async Task Only_ids_times_the_start_and_the_live_state_are_stored_for_known_content()
        {
            await Store();

            var columns = (await _db.QueryAsync("SELECT name FROM pragma_table_info('known_content') ORDER BY cid"))
                .Select(r => (string)r[0]!);

            Assert.Equal(["source_id", "content_id", "scheduled_start", "first_seen_at", "last_refreshed_at", "live_state", "content_type"], columns);
        }

        [Theory]
        [InlineData(LiveState.Upcoming, "upcoming")]
        [InlineData(LiveState.Live, "live")]
        [InlineData(LiveState.Ended, "ended")]
        public async Task The_live_state_is_stored_as_text_and_read_back(LiveState state, string text)
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);

            await store.UpsertKnownContentAsync(source.Id, "v1", T0.AddDays(1), T0, state);

            Assert.Equal(state, (await store.GetKnownContentAsync(source.Id, "v1"))!.LiveState);
            Assert.Equal(text, (await _db.QueryAsync("SELECT live_state FROM known_content"))[0][0]);
        }

        [Fact]
        public async Task Ordinary_content_has_no_live_state_and_an_upsert_replaces_a_stored_one()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0);
            Assert.Null((await store.GetKnownContentAsync(source.Id, "v1"))!.LiveState);

            await store.UpsertKnownContentAsync(source.Id, "v1", T0.AddDays(1), T0, LiveState.Upcoming);
            await store.UpsertKnownContentAsync(source.Id, "v1", T0.AddDays(1), T0.AddHours(1), LiveState.Live);

            Assert.Equal(LiveState.Live, (await store.GetKnownContentAsync(source.Id, "v1"))!.LiveState);
        }

        [Fact]
        public async Task The_database_rejects_an_unknown_live_state()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0);

            await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteAsync("UPDATE known_content SET live_state = 'paused'"));
        }

        [Fact]
        public async Task Migration_two_keeps_rows_written_under_migration_one()
        {
            _db = AlertsDb.CreateUnmigrated();
            var runner = new MigrationRunner(_db.Store);
            await runner.ApplyAsync([CoreMigrations.Set, new MigrationSet(AlertsModule.ModuleId, [AlertsMigrations.Set.Migrations[0]])]);
            await _db.ExecuteAsync("INSERT INTO sources (platform, external_id, created_at) VALUES ('youtube', 'UC1', '2026-01-01T00:00:00.0000000+00:00')");
            await _db.ExecuteAsync("INSERT INTO known_content VALUES (1, 'v1', NULL, '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00')");

            await runner.ApplyAsync([CoreMigrations.Set, AlertsMigrations.Set]);

            var item = await new AlertSourceStore(_db.Store).GetKnownContentAsync(1, "v1");
            Assert.NotNull(item);
            Assert.Null(item.LiveState);
        }

        [Theory]
        [InlineData(ContentType.Video, "video")]
        [InlineData(ContentType.Short, "short")]
        [InlineData(ContentType.StreamScheduled, "stream_scheduled")]
        [InlineData(ContentType.StreamLive, "stream_live")]
        [InlineData(ContentType.StreamEnded, "stream_ended")]
        [InlineData(ContentType.Unclassified, "unclassified")]
        public async Task The_type_is_stored_as_text_and_read_back(ContentType type, string text)
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);

            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0, contentType: type);

            Assert.Equal(type, (await store.GetKnownContentAsync(source.Id, "v1"))!.Type);
            Assert.Equal(text, (await _db.QueryAsync("SELECT content_type FROM known_content"))[0][0]);
        }

        [Fact]
        public async Task An_item_stored_without_a_type_has_none()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);

            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0);

            Assert.Null((await store.GetKnownContentAsync(source.Id, "v1"))!.Type);
        }

        [Fact]
        public async Task An_upsert_without_a_type_keeps_the_stored_one_and_a_new_type_replaces_it()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0, contentType: ContentType.Unclassified);

            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0.AddHours(1));
            Assert.Equal(ContentType.Unclassified, (await store.GetKnownContentAsync(source.Id, "v1"))!.Type);

            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0.AddHours(2), contentType: ContentType.Video);
            Assert.Equal(ContentType.Video, (await store.GetKnownContentAsync(source.Id, "v1"))!.Type);
        }

        [Fact]
        public async Task The_database_rejects_an_unknown_type()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0);

            await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteAsync("UPDATE known_content SET content_type = 'podcast'"));
        }

        [Fact]
        public async Task Migration_three_keeps_rows_written_under_migration_two_with_no_type()
        {
            _db = AlertsDb.CreateUnmigrated();
            var runner = new MigrationRunner(_db.Store);
            await runner.ApplyAsync([CoreMigrations.Set, new MigrationSet(AlertsModule.ModuleId, AlertsMigrations.Set.Migrations.Take(2).ToList())]);
            await _db.ExecuteAsync("INSERT INTO sources (platform, external_id, created_at) VALUES ('youtube', 'UC1', '2026-01-01T00:00:00.0000000+00:00')");
            await _db.ExecuteAsync("INSERT INTO known_content VALUES (1, 'v1', NULL, '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00', 'live')");

            await runner.ApplyAsync([CoreMigrations.Set, AlertsMigrations.Set]);

            var item = (await new AlertSourceStore(_db.Store).GetKnownContentAsync(1, "v1"))!;
            Assert.Equal(LiveState.Live, item.LiveState);
            Assert.Null(item.Type);
        }

        [Fact]
        public async Task State_survives_a_new_store_instance_on_the_same_file()
        {
            var store = await Store();
            var source = await store.AddSourceAsync("youtube", "UC1", T0);
            await store.MarkBaselineRecordedAsync(source.Id, T0);
            await store.UpsertKnownContentAsync(source.Id, "v1", null, T0);

            var reopened = new AlertSourceStore(_db.Store);

            Assert.True((await reopened.GetSourceAsync(source.Id))!.BaselineRecorded);
            Assert.NotNull(await reopened.GetKnownContentAsync(source.Id, "v1"));
        }
    }
}
