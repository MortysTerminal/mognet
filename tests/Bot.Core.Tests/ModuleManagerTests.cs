using Microsoft.Extensions.Logging.Abstractions;
using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core.Tests
{
    public class ModuleManagerTests : IAsyncLifetime, IDisposable
    {
        private readonly TempDatabase _db = new();

        public async ValueTask InitializeAsync() =>
            await new MigrationRunner(_db.Store()).ApplyAsync([CoreMigrations.Set]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose() => _db.Dispose();

        private ModuleManager Manager(params IBotModule[] modules) =>
            new(modules, new SettingsStore(_db.Store()), NullLogger<ModuleManager>.Instance);

        [Fact]
        public async Task Absent_setting_means_off_and_start_enabled_starts_nothing()
        {
            var module = new FakeModule("alpha");
            var manager = Manager(module);

            Assert.False(await manager.IsEnabledAsync("alpha"));
            await manager.StartEnabledAsync();

            Assert.Equal(0, module.StartCount);
            Assert.False(manager.IsRunning("alpha"));
        }

        [Fact]
        public async Task Switching_on_starts_at_once_and_persists()
        {
            var module = new FakeModule("alpha");
            var manager = Manager(module);

            var result = await manager.SetEnabledAsync("alpha", true);

            Assert.True(result.Succeeded);
            Assert.Null(result.Error);
            Assert.Equal(1, module.StartCount);
            Assert.True(manager.IsRunning("alpha"));
            Assert.Equal("true", await new SettingsStore(_db.Store()).GetAsync("core", "module.alpha.enabled"));
            Assert.True(await manager.IsEnabledAsync("alpha"));
        }

        [Fact]
        public async Task Switching_off_stops_at_once_and_persists()
        {
            var module = new FakeModule("alpha");
            var manager = Manager(module);
            await manager.SetEnabledAsync("alpha", true);

            var result = await manager.SetEnabledAsync("alpha", false);

            Assert.True(result.Succeeded);
            Assert.Equal(1, module.StopCount);
            Assert.False(manager.IsRunning("alpha"));
            Assert.Equal("false", await new SettingsStore(_db.Store()).GetAsync("core", "module.alpha.enabled"));
            Assert.False(await manager.IsEnabledAsync("alpha"));
        }

        [Fact]
        public async Task A_new_manager_on_the_same_file_starts_what_was_on()
        {
            await Manager(new FakeModule("alpha"), new FakeModule("beta")).SetEnabledAsync("alpha", true);

            var alpha = new FakeModule("alpha");
            var beta = new FakeModule("beta");
            await Manager(alpha, beta).StartEnabledAsync();

            Assert.Equal(1, alpha.StartCount);
            Assert.Equal(0, beta.StartCount);
        }

        [Fact]
        public async Task A_module_switched_off_stays_off_after_restart()
        {
            var first = Manager(new FakeModule("alpha"));
            await first.SetEnabledAsync("alpha", true);
            await first.SetEnabledAsync("alpha", false);

            var alpha = new FakeModule("alpha");
            await Manager(alpha).StartEnabledAsync();

            Assert.Equal(0, alpha.StartCount);
        }

        [Fact]
        public async Task A_throwing_start_is_isolated_and_other_modules_still_start()
        {
            var bad = new FakeModule("bad") { StartThrows = true };
            var good = new FakeModule("good");
            var setup = Manager(bad, good);
            await setup.SetEnabledAsync("good", true);
            await new SettingsStore(_db.Store()).SetAsync("core", "module.bad.enabled", "true");

            var manager = Manager(bad, good);
            await manager.StartEnabledAsync();

            Assert.Equal(1, bad.StartCount);
            Assert.False(manager.IsRunning("bad"));
            Assert.True(manager.IsRunning("good"));
            Assert.Equal(2, good.StartCount);
        }

        [Fact]
        public async Task A_failed_start_keeps_the_setting_counts_as_stopped_and_reports_failure()
        {
            var bad = new FakeModule("bad") { StartThrows = true };
            var manager = Manager(bad);

            var result = await manager.SetEnabledAsync("bad", true);

            Assert.False(result.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
            Assert.False(manager.IsRunning("bad"));
            Assert.True(await manager.IsEnabledAsync("bad"));
            Assert.Equal("true", await new SettingsStore(_db.Store()).GetAsync("core", "module.bad.enabled"));
        }

        [Fact]
        public async Task A_throwing_stop_is_isolated_and_the_module_counts_as_stopped()
        {
            var bad = new FakeModule("bad") { StopThrows = true };
            var good = new FakeModule("good");
            var manager = Manager(bad, good);
            await manager.SetEnabledAsync("bad", true);
            await manager.SetEnabledAsync("good", true);

            var result = await manager.SetEnabledAsync("bad", false);
            Assert.False(result.Succeeded);
            Assert.False(manager.IsRunning("bad"));
            Assert.Equal("false", await new SettingsStore(_db.Store()).GetAsync("core", "module.bad.enabled"));

            await manager.SetEnabledAsync("bad", true);
            await manager.StopAllAsync();
            Assert.Equal(2, bad.StopCount);
            Assert.Equal(1, good.StopCount);
            Assert.False(manager.IsRunning("good"));
        }

        [Fact]
        public async Task Switching_on_a_running_module_or_off_a_stopped_one_does_nothing()
        {
            var module = new FakeModule("alpha");
            var manager = Manager(module);

            var offAgain = await manager.SetEnabledAsync("alpha", false);
            Assert.True(offAgain.Succeeded);
            Assert.Equal(0, module.StopCount);

            await manager.SetEnabledAsync("alpha", true);
            var onAgain = await manager.SetEnabledAsync("alpha", true);

            Assert.True(onAgain.Succeeded);
            Assert.Equal(1, module.StartCount);
        }

        [Fact]
        public async Task Stop_all_stops_in_reverse_start_order()
        {
            var order = new List<string>();
            var a = new FakeModule("a") { Log = order };
            var b = new FakeModule("b") { Log = order };
            var c = new FakeModule("c") { Log = order };
            var manager = Manager(a, b, c);
            await manager.SetEnabledAsync("b", true);
            await manager.SetEnabledAsync("c", true);
            await manager.SetEnabledAsync("a", true);
            order.Clear();

            await manager.StopAllAsync();

            Assert.Equal(["stop a", "stop c", "stop b"], order);
        }

        [Fact]
        public async Task Stop_all_keeps_the_settings()
        {
            var manager = Manager(new FakeModule("alpha"));
            await manager.SetEnabledAsync("alpha", true);

            await manager.StopAllAsync();

            Assert.True(await manager.IsEnabledAsync("alpha"));
        }

        [Fact]
        public void Duplicate_ids_are_rejected()
        {
            Assert.Throws<ArgumentException>(() => Manager(new FakeModule("alpha"), new FakeModule("alpha")));
        }

        [Theory]
        [InlineData("")]
        [InlineData("Alpha")]
        [InlineData("1alpha")]
        [InlineData("al-pha")]
        [InlineData("al pha")]
        public void Ids_outside_the_scope_pattern_are_rejected(string id)
        {
            Assert.Throws<ArgumentException>(() => Manager(new FakeModule(id)));
        }

        [Fact]
        public void The_id_core_is_reserved()
        {
            Assert.Throws<ArgumentException>(() => Manager(new FakeModule("core")));
        }

        [Fact]
        public async Task An_unknown_id_throws()
        {
            var manager = Manager(new FakeModule("alpha"));

            await Assert.ThrowsAsync<ArgumentException>(() => manager.SetEnabledAsync("nope", true));
            await Assert.ThrowsAsync<ArgumentException>(() => manager.IsEnabledAsync("nope"));
        }

        [Fact]
        public async Task Concurrent_switches_do_not_start_a_module_twice()
        {
            var module = new FakeModule("alpha") { StartDelay = TimeSpan.FromMilliseconds(50) };
            var manager = Manager(module);

            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => manager.SetEnabledAsync("alpha", true)));

            Assert.All(results, r => Assert.True(r.Succeeded));
            Assert.Equal(1, module.StartCount);
            Assert.Equal(1, module.MaxConcurrentCalls);
        }

        private sealed class FakeModule(string id) : IBotModule
        {
            private int _concurrent;
            private int _starts;
            private int _stops;

            public string Id { get; } = id;
            public MigrationSet? Migrations => null;
            public bool StartThrows { get; init; }
            public bool StopThrows { get; init; }
            public TimeSpan StartDelay { get; init; }
            public List<string>? Log { get; init; }
            public int StartCount => _starts;
            public int StopCount => _stops;
            public int MaxConcurrentCalls { get; private set; }

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _starts);
                Log?.Add($"start {Id}");
                var now = Interlocked.Increment(ref _concurrent);
                MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, now);
                try
                {
                    if (StartDelay > TimeSpan.Zero)
                        await Task.Delay(StartDelay, cancellationToken);
                    if (StartThrows)
                        throw new InvalidOperationException("start failed");
                }
                finally
                {
                    Interlocked.Decrement(ref _concurrent);
                }
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _stops);
                Log?.Add($"stop {Id}");
                if (StopThrows)
                    throw new InvalidOperationException("stop failed");
                return Task.CompletedTask;
            }
        }
    }
}
