using Mognet.Modules.TwitchSchedule.Storage;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class ScheduleConfigStoreTests : IDisposable
    {
        private ScheduleDb _db = null!;

        private async Task<ScheduleDb> Db() => _db ??= await ScheduleDb.CreateAsync();

        public void Dispose() => _db?.Dispose();

        [Fact]
        public async Task Absent_settings_give_the_defaults()
        {
            var db = await Db();

            var config = await new ScheduleConfigStore(db.Store).LoadAsync();

            Assert.Equal(new SyncOptions(30, 14, 10), config.Options);
            Assert.Equal("en", config.Language);
            Assert.False(config.DryRun);
            Assert.Empty(config.Channels);
        }

        [Fact]
        public async Task Stored_values_and_channels_by_position_are_read()
        {
            var db = await Db();
            await db.ExecuteAsync("INSERT INTO channels (login, display_name, position) VALUES ('b', 'B', 1), ('a', 'A', 0)");
            await db.SetSettingAsync(ScheduleSettings.IntervalMinutes, "45");
            await db.SetSettingAsync(ScheduleSettings.LookaheadDays, "21");
            await db.SetSettingAsync(ScheduleSettings.MaxEventsPerChannel, "5");
            await db.SetSettingAsync(ScheduleSettings.EventLanguage, "de");
            await db.SetSettingAsync(ScheduleSettings.DryRun, "true");

            var config = await new ScheduleConfigStore(db.Store).LoadAsync();

            Assert.Equal(new SyncOptions(45, 21, 5), config.Options);
            Assert.Equal("de", config.Language);
            Assert.True(config.DryRun);
            Assert.Equal(["a", "b"], config.Channels.Select(c => c.Username));
            Assert.Equal(["A", "B"], config.Channels.Select(c => c.DisplayName));
        }

        [Theory]
        [InlineData(ScheduleSettings.IntervalMinutes, "4", "5 to 1440")]
        [InlineData(ScheduleSettings.LookaheadDays, "abc", "1 to 60")]
        [InlineData(ScheduleSettings.MaxEventsPerChannel, "51", "1 to 50")]
        public async Task An_invalid_stored_value_fails_naming_the_key_and_the_range(string key, string value, string range)
        {
            var db = await Db();
            await db.SetSettingAsync(key, value);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new ScheduleConfigStore(db.Store).LoadAsync());

            Assert.Contains(key, ex.Message);
            Assert.Contains(range, ex.Message);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("TRUE", false)]
        [InlineData("1", false)]
        [InlineData("yes", false)]
        [InlineData("false", false)]
        [InlineData("", false)]
        public async Task Dry_run_is_on_only_for_exactly_true(string stored, bool expected)
        {
            var db = await Db();
            await db.SetSettingAsync(ScheduleSettings.DryRun, stored);

            Assert.Equal(expected, (await new ScheduleConfigStore(db.Store).LoadAsync()).DryRun);
        }
    }
}
