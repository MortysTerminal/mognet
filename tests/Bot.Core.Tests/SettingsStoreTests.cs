using Microsoft.Data.Sqlite;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core.Tests
{
    public class SettingsStoreTests : IAsyncLifetime, IDisposable
    {
        private readonly TempDatabase _db = new();

        public async ValueTask InitializeAsync() =>
            await new MigrationRunner(_db.Store()).ApplyAsync([CoreMigrations.Set]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Absent_key_returns_null()
        {
            Assert.Null(await new SettingsStore(_db.Store()).GetAsync("s", "k"));
        }

        [Fact]
        public async Task Set_get_overwrite_and_delete()
        {
            var settings = new SettingsStore(_db.Store());

            await settings.SetAsync("s", "k", "one");
            Assert.Equal("one", await settings.GetAsync("s", "k"));

            await settings.SetAsync("s", "k", "two");
            Assert.Equal("two", await settings.GetAsync("s", "k"));
            Assert.Single(await _db.QueryAsync("SELECT * FROM settings"));

            await settings.DeleteAsync("s", "k");
            Assert.Null(await settings.GetAsync("s", "k"));
            await settings.DeleteAsync("s", "k");
        }

        [Fact]
        public async Task Same_key_in_two_scopes_stays_separate()
        {
            var settings = new SettingsStore(_db.Store());

            await settings.SetAsync("a", "k", "from a");
            await settings.SetAsync("b", "k", "from b");
            await settings.DeleteAsync("a", "k");

            Assert.Null(await settings.GetAsync("a", "k"));
            Assert.Equal("from b", await settings.GetAsync("b", "k"));
        }

        [Fact]
        public async Task A_write_is_visible_at_once_through_another_instance()
        {
            var writer = new SettingsStore(_db.Store());
            var reader = new SettingsStore(_db.Store());

            Assert.Null(await reader.GetAsync("s", "k"));
            await writer.SetAsync("s", "k", "v1");
            Assert.Equal("v1", await reader.GetAsync("s", "k"));
            await writer.SetAsync("s", "k", "v2");
            Assert.Equal("v2", await reader.GetAsync("s", "k"));
            await writer.DeleteAsync("s", "k");
            Assert.Null(await reader.GetAsync("s", "k"));
        }

        [Theory]
        [InlineData("it's \"quoted\"")]
        [InlineData("'); DROP TABLE settings; --")]
        [InlineData("")]
        [InlineData("line1\nline2 äö \U0001F600")]
        public async Task Awkward_values_round_trip(string value)
        {
            var settings = new SettingsStore(_db.Store());

            await settings.SetAsync("s", "k'; --", value);

            Assert.Equal(value, await settings.GetAsync("s", "k'; --"));
            Assert.True(await _db.TableExistsAsync("settings"));
        }

        [Fact]
        public async Task Updated_at_is_iso_8601_utc_from_the_time_provider()
        {
            var time = new FixedTime(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)));
            await new SettingsStore(_db.Store(), time).SetAsync("s", "k", "v");

            var row = Assert.Single(await _db.QueryAsync("SELECT updated_at FROM settings"));
            Assert.Equal("2026-01-02T01:04:05.0000000+00:00", row[0]);
        }

        [Fact]
        public async Task Sent_alerts_key_is_unique_per_rule_stage_and_content()
        {
            await _db.ExecuteAsync("INSERT INTO sent_alerts VALUES ('r', 'announce', 'c1', 't')");

            await Assert.ThrowsAsync<SqliteException>(() =>
                _db.ExecuteAsync("INSERT INTO sent_alerts VALUES ('r', 'announce', 'c1', 'later')"));
            await _db.ExecuteAsync("INSERT INTO sent_alerts VALUES ('r', 'reminder', 'c1', 't')");

            Assert.Equal(2, (await _db.QueryAsync("SELECT * FROM sent_alerts")).Count);
        }

        [Fact]
        public async Task Data_survives_closing_and_reopening_the_file()
        {
            await _db.ExecuteAsync("INSERT INTO sent_alerts VALUES ('r', 'announce', 'c1', 't')");
            await new SettingsStore(_db.Store()).SetAsync("s", "k", "kept");
            SqliteConnection.ClearAllPools();

            await new MigrationRunner(_db.Store()).ApplyAsync([CoreMigrations.Set]);

            Assert.Single(await _db.QueryAsync("SELECT * FROM sent_alerts"));
            Assert.Equal("kept", await new SettingsStore(_db.Store()).GetAsync("s", "k"));
            await Assert.ThrowsAsync<SqliteException>(() =>
                _db.ExecuteAsync("INSERT INTO sent_alerts VALUES ('r', 'announce', 'c1', 'again')"));
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        }
    }
}
