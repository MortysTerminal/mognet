using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mognet.Bot.Core.Health;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.Alerts.Services;
using Mognet.Modules.Alerts.YouTube;

namespace Mognet.Modules.Alerts.Tests
{
    public class AlertsModuleTests : IAsyncDisposable
    {
        private AlertsDb _db = null!;
        private readonly HealthRegistry _health = new();
        private readonly LogSink _log = new();
        private readonly TestClock _clock = new(PollHarness.Start);

        private readonly List<AlertsModule> _modules = [];

        // A module a test left running must stop before its database file goes away.
        public async ValueTask DisposeAsync()
        {
            foreach (var module in _modules)
                await module.StopAsync(CancellationToken.None);
            _db?.Dispose();
        }

        private async Task<AlertsModule> ModuleAsync(string? apiKey = "test-key-not-a-real-secret-9f3a1c")
        {
            _db ??= await AlertsDb.CreateAsync();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(apiKey is null ? [] : [new KeyValuePair<string, string?>(AlertsModule.YouTubeApiKeyVariable, apiKey)])
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton(_health);
            services.AddSingleton(_db.Store);
            services.AddSingleton(new SettingsStore(_db.Store, _clock));
            services.AddSingleton(_log.Logger<AlertsModule>());
            services.AddSingleton(_log.Logger<PollService>());
            services.AddSingleton(_log.Logger<YouTubeProvider>());
            var module = new AlertsModule(services.BuildServiceProvider(), _log.Logger<AlertsModule>());
            _modules.Add(module);
            return module;
        }

        private async Task<ModuleManager> ManagerAsync(string? apiKey = "test-key-not-a-real-secret-9f3a1c")
        {
            var module = await ModuleAsync(apiKey);
            return new ModuleManager([module], new SettingsStore(_db.Store), NullLogger<ModuleManager>.Instance);
        }

        [Fact]
        public async Task The_module_has_its_own_id_and_migration_scope()
        {
            var module = await ModuleAsync();

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
        public async Task A_missing_key_fails_the_start_naming_the_variable_and_the_module_stays_stopped()
        {
            var manager = await ManagerAsync(apiKey: null);

            var result = await manager.SetEnabledAsync("alerts", true);

            Assert.False(result.Succeeded);
            Assert.Contains("YOUTUBE_API_KEY", result.Error);
            Assert.False(manager.IsRunning("alerts"));
        }

        [Fact]
        public async Task A_blank_key_counts_as_missing()
        {
            var module = await ModuleAsync(apiKey: "  ");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => module.StartAsync(CancellationToken.None));

            Assert.Contains("YOUTUBE_API_KEY", ex.Message);
        }

        [Fact]
        public async Task A_running_module_reports_health_and_stopping_it_clears_the_deadline()
        {
            var module = await ModuleAsync();

            await module.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => !_health.Evaluate(_clock.Now.AddYears(1)).IsOk);
            Assert.Equal([AlertsModule.ModuleId], _health.Evaluate(_clock.Now.AddYears(1)).Stalled);

            await module.StopAsync(CancellationToken.None);
            Assert.True(_health.Evaluate(_clock.Now.AddYears(1)).IsOk);
        }

        [Fact]
        public async Task A_stopped_module_can_be_started_again()
        {
            var module = await ModuleAsync();
            await module.StartAsync(CancellationToken.None);
            await module.StopAsync(CancellationToken.None);

            await module.StartAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => module.StartAsync(CancellationToken.None));
            await module.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task The_poll_the_module_builds_reports_to_the_registered_registry()
        {
            var module = await ModuleAsync();
            using var poll = module.CreatePoll([]);

            await poll.RunCycleAsync();

            Assert.Equal([AlertsModule.ModuleId], _health.Evaluate(_clock.Now.AddYears(1)).Stalled);
        }

        [Fact]
        public async Task A_missing_health_registry_fails_loudly()
        {
            _db = await AlertsDb.CreateAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton(_db.Store);
            services.AddSingleton(new SettingsStore(_db.Store, _clock));
            var module = new AlertsModule(services.BuildServiceProvider(), _log.Logger<AlertsModule>());

            Assert.Throws<InvalidOperationException>(() => module.CreatePoll([]));
        }

        [Fact]
        public async Task A_fault_of_the_poll_is_logged_and_not_thrown()
        {
            var module = await ModuleAsync();

            await module.ObserveAsync(Task.FromException(new InvalidOperationException("boom")), CancellationToken.None);

            Assert.Contains(_log.At(LogLevel.Error), m => m.Contains("ended with an error"));
        }

        [Fact]
        public async Task A_fault_after_stopping_is_not_logged()
        {
            var module = await ModuleAsync();
            using var stopped = new CancellationTokenSource();
            await stopped.CancelAsync();

            await module.ObserveAsync(Task.FromException(new OperationCanceledException()), stopped.Token);

            Assert.Empty(_log.At(LogLevel.Error));
        }

        [Fact]
        public async Task Migrations_create_the_tables_while_the_module_is_off()
        {
            using var db = AlertsDb.CreateUnmigrated();
            var module = await ModuleAsync();

            await new MigrationRunner(db.Store).ApplyAsync([CoreMigrations.Set, module.Migrations!]);

            Assert.Equal(2, (await db.QueryAsync(
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name IN ('sources', 'known_content')")).Count);
        }
    }
}
