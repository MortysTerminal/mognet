using Microsoft.Extensions.Configuration;
using Mognet.Modules.TwitchSchedule.Storage;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class ScheduleImporterTests : IDisposable
    {
        private ScheduleDb _db = null!;

        private async Task<ScheduleDb> Db() => _db ??= await ScheduleDb.CreateAsync();

        public void Dispose() => _db?.Dispose();

        private static IConfiguration Env3(params (string, string?)[] more) =>
            Env.Config([.. Env.Channels(("alpha", "Alpha"), ("beta", "Beta"), ("gamma", "Gamma")), .. more]);

        [Fact]
        public async Task First_start_imports_channels_in_order_the_settings_that_are_set_and_the_marker()
        {
            var db = await Db();
            var imported = await db.Importer(Env3(
                ("Sync:IntervalMinutes", "15"),
                ("Sync:LookaheadDays", "7"),
                ("Discord:EventLanguage", "de"))).ImportAsync();

            Assert.True(imported);
            Assert.Equal(
                [("alpha", "Alpha", 0L), ("beta", "Beta", 1L), ("gamma", "Gamma", 2L)],
                await db.ChannelsAsync());
            var settings = await db.SettingsAsync();
            Assert.Equal("15", settings[ScheduleSettings.IntervalMinutes]);
            Assert.Equal("7", settings[ScheduleSettings.LookaheadDays]);
            Assert.Equal("de", settings[ScheduleSettings.EventLanguage]);
            Assert.False(settings.ContainsKey(ScheduleSettings.MaxEventsPerChannel));
            Assert.False(settings.ContainsKey(ScheduleSettings.DryRun));
            Assert.True(settings.ContainsKey(ScheduleSettings.ImportDone));
            Assert.Equal(4, settings.Count);
        }

        [Fact]
        public async Task A_second_start_with_a_changed_environment_changes_nothing()
        {
            var db = await Db();
            await db.Importer(Env3(("Sync:IntervalMinutes", "15"))).ImportAsync();
            var before = await db.DumpAsync();

            var imported = await db.Importer(Env.Config(
                [.. Env.Channels(("other", "Other")), ("Sync:IntervalMinutes", "99"), ("Sync:LookaheadDays", "3")])).ImportAsync();

            Assert.False(imported);
            Assert.Equal(before, await db.DumpAsync());
        }

        [Fact]
        public async Task A_removed_channel_stays_and_an_added_channel_is_not_imported()
        {
            var db = await Db();
            await db.Importer(Env3()).ImportAsync();

            await db.Importer(Env.Config(Env.Channels(("alpha", "Alpha"), ("delta", "Delta")))).ImportAsync();

            Assert.Equal(["alpha", "beta", "gamma"], (await db.ChannelsAsync()).Select(c => c.Login));
        }

        [Fact]
        public async Task Blank_slots_and_blank_settings_are_skipped_and_the_first_of_a_duplicate_login_wins()
        {
            var db = await Db();
            var config = Env.Config(
                ("Twitch:Channels:0:Username", "alpha"), ("Twitch:Channels:0:DisplayName", "Alpha"),
                ("Twitch:Channels:1:Username", ""), ("Twitch:Channels:1:DisplayName", ""),
                ("Twitch:Channels:2:Username", "alpha"), ("Twitch:Channels:2:DisplayName", "Other"),
                ("Twitch:Channels:3:Username", "  "), ("Twitch:Channels:3:DisplayName", "Blank"),
                ("Twitch:Channels:4:Username", "beta"), ("Twitch:Channels:4:DisplayName", "Beta"),
                ("Sync:IntervalMinutes", ""), ("Sync:LookaheadDays", "  "), ("Sync:MaxEventsPerChannel", null),
                ("Discord:EventLanguage", " "));

            await db.Importer(config).ImportAsync();

            Assert.Equal([("alpha", "Alpha", 0L), ("beta", "Beta", 1L)], await db.ChannelsAsync());
            Assert.Equal([ScheduleSettings.ImportDone], (await db.SettingsAsync()).Keys);
        }

        [Fact]
        public async Task Without_env_channels_nothing_is_written_and_a_later_start_can_still_import()
        {
            var db = await Db();
            var before = await db.DumpAsync();

            var imported = await db.Importer(Env.Config(
                ("Twitch:Channels:0:Username", ""), ("Sync:IntervalMinutes", "15"))).ImportAsync();

            Assert.False(imported);
            Assert.Equal(before, await db.DumpAsync());
            Assert.Empty(await db.SettingsAsync());

            Assert.True(await db.Importer(Env3()).ImportAsync());
            Assert.Equal(3, (await db.ChannelsAsync()).Count);
        }

        [Theory]
        [InlineData("Sync:IntervalMinutes", "4")]
        [InlineData("Sync:LookaheadDays", "61")]
        [InlineData("Sync:MaxEventsPerChannel", "many")]
        public async Task An_invalid_env_number_throws_and_nothing_is_written(string key, string value)
        {
            var db = await Db();
            var before = await db.DumpAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Importer(Env3((key, value))).ImportAsync());

            Assert.Contains(value, ex.Message);
            Assert.Equal(before, await db.DumpAsync());
        }

        [Fact]
        public async Task A_failure_during_the_import_leaves_no_partial_rows_and_no_marker()
        {
            var db = await Db();
            // A leftover row for the second channel makes the second insert fail after the first succeeded.
            await db.ExecuteAsync("INSERT INTO channels (login, display_name, position) VALUES ('beta', 'Old', 9)");
            var before = await db.DumpAsync();

            await Assert.ThrowsAnyAsync<Exception>(() => db.Importer(Env3(("Sync:IntervalMinutes", "15"))).ImportAsync());

            Assert.Equal(before, await db.DumpAsync());
            Assert.Empty(await db.SettingsAsync());
        }

        [Fact]
        public async Task Token_and_secret_values_appear_in_no_table()
        {
            var db = await Db();
            var config = Env3(
                ("Discord:BotToken", "bot-token-value-12345"),
                ("Discord:GuildId", "123456789"),
                ("Twitch:ClientId", "client-id-value-12345"),
                ("Twitch:ClientSecret", "client-secret-value-12345"));

            await db.Importer(config).ImportAsync();

            var dump = await db.DumpAsync();
            Assert.True(dump.Count > 5);
            foreach (var secret in new[] { "bot-token-value-12345", "client-id-value-12345", "client-secret-value-12345" })
                Assert.DoesNotContain(dump, line => line.Contains(secret));
        }
    }
}
