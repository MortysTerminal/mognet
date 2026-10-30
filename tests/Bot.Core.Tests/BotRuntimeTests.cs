using Microsoft.Extensions.Logging.Abstractions;
using Mognet.Bot.Core.Gateway;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core.Tests
{
    public class BotRuntimeTests : IDisposable
    {
        private readonly TempDatabase _db = new();
        private readonly List<string> _events = [];

        public void Dispose() => _db.Dispose();

        private BotRuntime Runtime(RecordingGateway gateway, FakeShutdown shutdown, params IBotModule[] modules) =>
            new(
                gateway,
                new MigrationRunner(_db.Store()),
                new ModuleManager(modules, new SettingsStore(_db.Store()), NullLogger<ModuleManager>.Instance),
                modules,
                shutdown,
                NullLogger<BotRuntime>.Instance);

        [Fact]
        public async Task Migrations_run_before_the_gateway_and_the_gateway_before_the_modules()
        {
            var module = new RecordingModule("alpha", _events, "CREATE TABLE alpha_things (id INTEGER)");
            await new MigrationRunner(_db.Store()).ApplyAsync([CoreMigrations.Set]);
            await new SettingsStore(_db.Store()).SetAsync("core", "module.alpha.enabled", "true");
            var gateway = new RecordingGateway(_events, async () =>
            {
                _events.Add($"core tables: {await _db.TableExistsAsync("settings")}, module tables: {await _db.TableExistsAsync("alpha_things")}");
            });

            await Runtime(gateway, new FakeShutdown(), module).StartAsync(CancellationToken.None);

            Assert.Equal(
                ["gateway start", "core tables: True, module tables: True", "start alpha"],
                _events.Where(e => e != "gateway started").ToList());
        }

        [Fact]
        public async Task Modules_are_off_by_default_so_nothing_starts()
        {
            var module = new RecordingModule("alpha", _events);

            await Runtime(new RecordingGateway(_events), new FakeShutdown(), module).StartAsync(CancellationToken.None);

            Assert.DoesNotContain("start alpha", _events);
        }

        [Fact]
        public async Task A_failed_migration_stops_the_host_and_starts_neither_gateway_nor_modules()
        {
            var module = new RecordingModule("alpha", _events, "THIS IS NOT SQL");
            await new MigrationRunner(_db.Store()).ApplyAsync([CoreMigrations.Set]);
            await new SettingsStore(_db.Store()).SetAsync("core", "module.alpha.enabled", "true");
            var shutdown = new FakeShutdown();

            await Runtime(new RecordingGateway(_events), shutdown, module).StartAsync(CancellationToken.None);

            Assert.Equal(1, shutdown.Calls);
            Assert.Empty(_events);
        }

        [Fact]
        public async Task Stopping_stops_the_modules_before_the_gateway()
        {
            var module = new RecordingModule("alpha", _events);
            var runtime = Runtime(new RecordingGateway(_events), new FakeShutdown(), module);
            await new MigrationRunner(_db.Store()).ApplyAsync([CoreMigrations.Set]);
            await new SettingsStore(_db.Store()).SetAsync("core", "module.alpha.enabled", "true");
            await runtime.StartAsync(CancellationToken.None);
            _events.Clear();

            await runtime.StopAsync(CancellationToken.None);

            Assert.Equal(["stop alpha", "gateway stop"], _events);
        }

        private sealed class FakeShutdown : IFatalShutdown
        {
            public int Calls { get; private set; }

            public void Shutdown() => Calls++;
        }

        private sealed class RecordingGateway(List<string> events, Func<Task>? onStart = null) : IGateway
        {
            public async Task StartAsync(CancellationToken cancellationToken)
            {
                events.Add("gateway start");
                if (onStart is not null)
                    await onStart();
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                events.Add("gateway stop");
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingModule : IBotModule
        {
            private readonly List<string> _events;

            public RecordingModule(string id, List<string> events, string? migrationSql = null)
            {
                Id = id;
                _events = events;
                Migrations = migrationSql is null ? null : new MigrationSet(id, [new Migration(1, "create", migrationSql)]);
            }

            public string Id { get; }
            public MigrationSet? Migrations { get; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _events.Add($"start {Id}");
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _events.Add($"stop {Id}");
                return Task.CompletedTask;
            }
        }
    }
}
