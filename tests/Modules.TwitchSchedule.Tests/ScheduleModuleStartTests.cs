using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mognet.Bot.Core.Gateway;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.TwitchSchedule.Storage;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class ScheduleModuleStartTests : IDisposable
    {
        private ScheduleDb _db = null!;

        private async Task<ScheduleDb> Db() => _db ??= await ScheduleDb.CreateAsync();

        public void Dispose() => _db?.Dispose();

        private TwitchScheduleModule Module(SqliteStore store, IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(configuration);
            services.AddSingleton(store);
            return new TwitchScheduleModule(services.BuildServiceProvider(), NullLogger<TwitchScheduleModule>.Instance, new NoShutdown());
        }

        [Fact]
        public void The_module_owns_a_migration_set_in_its_own_scope()
        {
            var set = new TwitchScheduleModule(new ServiceCollection().BuildServiceProvider(), NullLogger<TwitchScheduleModule>.Instance, new NoShutdown()).Migrations;

            Assert.NotNull(set);
            Assert.Equal("twitch_schedule", set.Scope);
            Assert.Equal([1], set.Migrations.Select(m => m.Version));
        }

        [Fact]
        public async Task An_empty_channel_list_after_a_finished_import_fails_the_start()
        {
            var db = await Db();
            await db.SetSettingAsync(ScheduleSettings.ImportDone, "true");
            // Env channels are ignored once the import is done, so the stored list stays empty.
            var module = Module(db.Store, Env.Config(Env.Channels(("alpha", "Alpha"))));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => module.StartAsync(CancellationToken.None));

            Assert.Contains("No Twitch channels", ex.Message);
            Assert.Empty(await db.ChannelsAsync());
        }

        [Fact]
        public async Task Without_any_channel_the_start_fails_and_imports_nothing()
        {
            var db = await Db();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Module(db.Store, Env.Config()).StartAsync(CancellationToken.None));

            Assert.Contains("No Twitch channels", ex.Message);
            Assert.Empty(await db.SettingsAsync());
        }

        [Fact]
        public async Task Start_without_twitch_credentials_fails_naming_the_missing_key_after_the_import()
        {
            var db = await Db();
            var module = Module(db.Store, Env.Config(Env.Channels(("alpha", "Alpha"))));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => module.StartAsync(CancellationToken.None));

            Assert.Contains("Twitch:ClientId", ex.Message);
            Assert.Single(await db.ChannelsAsync());
        }

        [Fact]
        public async Task An_invalid_stored_setting_fails_the_start_naming_the_key()
        {
            var db = await Db();
            await db.ExecuteAsync("INSERT INTO channels (login, display_name, position) VALUES ('alpha', 'Alpha', 0)");
            await db.SetSettingAsync(ScheduleSettings.ImportDone, "true");
            await db.SetSettingAsync(ScheduleSettings.IntervalMinutes, "1");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Module(db.Store, Env.Config()).StartAsync(CancellationToken.None));

            Assert.Contains(ScheduleSettings.IntervalMinutes, ex.Message);
        }

        private sealed class NoShutdown : IFatalShutdown
        {
            public void Shutdown() { }
        }
    }
}
