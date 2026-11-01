using Microsoft.Extensions.Logging.Abstractions;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.Alerts.Providers;

namespace Mognet.Modules.Alerts.Tests
{
    public class AlertsModuleTests : IDisposable
    {
        private AlertsDb _db = null!;

        public void Dispose() => _db?.Dispose();

        private async Task<ModuleManager> ManagerAsync()
        {
            _db = await AlertsDb.CreateAsync();
            return new ModuleManager([new AlertsModule()], new SettingsStore(_db.Store), NullLogger<ModuleManager>.Instance);
        }

        [Fact]
        public void The_module_has_its_own_id_and_migration_scope()
        {
            var module = new AlertsModule();

            Assert.Equal("alerts", module.Id);
            Assert.Equal("alerts", module.Migrations!.Scope);
            Assert.NotEqual(CoreMigrations.Scope, module.Migrations.Scope);
        }

        [Fact]
        public async Task The_module_is_off_until_switched_on()
        {
            var manager = await ManagerAsync();

            await manager.StartEnabledAsync();

            Assert.False(await manager.IsEnabledAsync("alerts"));
            Assert.False(manager.IsRunning("alerts"));
        }

        [Fact]
        public async Task Switching_on_starts_the_module_and_switching_off_stops_it()
        {
            var manager = await ManagerAsync();

            Assert.True((await manager.SetEnabledAsync("alerts", true)).Succeeded);
            Assert.True(manager.IsRunning("alerts"));

            Assert.True((await manager.SetEnabledAsync("alerts", false)).Succeeded);
            Assert.False(manager.IsRunning("alerts"));
        }

        [Fact]
        public async Task A_module_switched_on_earlier_starts_with_the_host()
        {
            var manager = await ManagerAsync();
            await new SettingsStore(_db.Store).SetAsync(CoreMigrations.Scope, ModuleManager.SettingKey("alerts"), "true");

            await manager.StartEnabledAsync();

            Assert.True(manager.IsRunning("alerts"));
        }

        [Fact]
        public async Task Migrations_create_the_tables_while_the_module_is_off()
        {
            await ManagerAsync();

            Assert.Equal(2, (await _db.QueryAsync(
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name IN ('sources', 'known_content')")).Count);
        }

        [Fact]
        public async Task A_provider_is_looked_up_by_platform_only_through_the_interface()
        {
            IContentProvider provider = new FakeProvider("youtube", new ContentItem("v1", null));
            var source = new Storage.AlertSource(1, "youtube", "UC1", false, DateTimeOffset.UnixEpoch);

            var items = await provider.FetchAsync(source, CancellationToken.None);

            Assert.Equal("youtube", provider.Platform);
            Assert.Equal([new ContentItem("v1", null)], items);
        }

        private sealed class FakeProvider(string platform, params ContentItem[] items) : IContentProvider
        {
            public string Platform => platform;

            public Task<IReadOnlyList<ContentItem>> FetchAsync(Storage.AlertSource source, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ContentItem>>(items);
        }
    }
}
