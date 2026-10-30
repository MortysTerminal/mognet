using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mognet.Bot.Core.Gateway;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.TwitchSchedule;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class TwitchScheduleModuleTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "mognet-module-tests-" + Guid.NewGuid().ToString("N"));
        private readonly FakeShutdown _shutdown = new();

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private TwitchScheduleModule Module(IConfiguration? configuration = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());
            return new TwitchScheduleModule(services.BuildServiceProvider(), NullLogger<TwitchScheduleModule>.Instance, _shutdown);
        }

        [Fact]
        public void Construction_needs_no_configuration()
        {
            var module = Module();

            Assert.Equal("twitch_schedule", module.Id);
            Assert.Null(module.Migrations);
        }

        [Fact]
        public async Task Start_without_twitch_configuration_throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Module().StartAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Stop_without_a_start_does_nothing()
        {
            await Module().StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task The_manager_isolates_a_failing_start_and_the_host_keeps_running()
        {
            var store = new SqliteStore(Path.Combine(_directory, "bot.db"));
            await new MigrationRunner(store).ApplyAsync([CoreMigrations.Set]);
            var manager = new ModuleManager([Module()], new SettingsStore(store), NullLogger<ModuleManager>.Instance);

            var result = await manager.SetEnabledAsync("twitch_schedule", true);

            Assert.False(result.Succeeded);
            Assert.False(manager.IsRunning("twitch_schedule"));
            Assert.Equal(0, _shutdown.Calls);
        }

        [Fact]
        public async Task A_faulted_sync_task_stops_the_host()
        {
            await Module().ObserveAsync(Task.FromException(new InvalidOperationException("login failed")), CancellationToken.None);

            Assert.Equal(1, _shutdown.Calls);
        }

        [Fact]
        public async Task A_sync_task_that_ends_normally_does_not_stop_the_host()
        {
            await Module().ObserveAsync(Task.CompletedTask, CancellationToken.None);

            Assert.Equal(0, _shutdown.Calls);
        }

        [Fact]
        public async Task A_fault_after_the_module_was_stopped_does_not_stop_the_host()
        {
            using var stopped = new CancellationTokenSource();
            await stopped.CancelAsync();

            await Module().ObserveAsync(Task.FromException(new OperationCanceledException()), stopped.Token);

            Assert.Equal(0, _shutdown.Calls);
        }

        private sealed class FakeShutdown : IFatalShutdown
        {
            public int Calls { get; private set; }

            public void Shutdown() => Calls++;
        }
    }
}
