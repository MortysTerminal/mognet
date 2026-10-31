using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mognet.Bot.Core.Gateway;
using Mognet.Bot.Core.Health;
using Mognet.Modules.TwitchSchedule.Models;
using Mognet.Modules.TwitchSchedule.Services;
using Mognet.Modules.TwitchSchedule.Storage;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class SyncHealthTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

        private sealed class Source : IScheduleConfigSource
        {
            public Func<ScheduleConfig> Next { get; set; } = () => throw new InvalidOperationException("not set");

            public Task<ScheduleConfig> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Next());
        }

        private sealed class FakeTwitch : ITwitchSchedules
        {
            public bool Fail { get; set; }

            public Task<List<ChannelSchedule>> GetAllSchedulesAsync(ScheduleConfig config) =>
                Fail ? throw new InvalidOperationException("twitch down") : Task.FromResult(new List<ChannelSchedule>());
        }

        private sealed class FakeDiscord : IDiscordSync
        {
            public bool FailLogin { get; set; }

            public Task LoginAsync() => FailLogin ? throw new InvalidOperationException("rejected") : Task.CompletedTask;

            public Task SyncEventsAsync(IReadOnlyList<ChannelSchedule> schedules, ScheduleConfig config) => Task.CompletedTask;
        }

        private sealed class NoFatalShutdown : IFatalShutdown
        {
            public void Shutdown()
            {
            }
        }

        private sealed class Disposable : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private static ScheduleConfig Good(int interval) =>
            new(new SyncOptions(interval, 14, 10), "en", [new TwitchChannelConfig { Username = "a", DisplayName = "A" }], false);

        private static (SyncService Sync, Source Source, FakeTwitch Twitch, FakeDiscord Discord, HealthRegistry Health) Build()
        {
            var source = new Source();
            var twitch = new FakeTwitch();
            var discord = new FakeDiscord();
            var health = new HealthRegistry();
            var sync = new SyncService(twitch, discord, source, new SyncOptions(30, 14, 10), new ListLogger<SyncService>(), health, new FixedTime(Now));
            return (sync, source, twitch, discord, health);
        }

        // A module is stalled once now is past its deadline, so probing just before and just after pins the deadline.
        private static void AssertDeadline(HealthRegistry health, TimeSpan expected)
        {
            Assert.True(health.Evaluate(Now + expected).IsOk);
            Assert.Equal([TwitchScheduleModule.ModuleId], health.Evaluate(Now + expected + TimeSpan.FromSeconds(1)).Stalled);
        }

        [Fact]
        public async Task A_cycle_reports_a_deadline_of_the_current_interval_plus_fifteen_minutes()
        {
            var (sync, source, _, _, health) = Build();
            source.Next = () => Good(45);

            await sync.RunSyncAsync();

            AssertDeadline(health, TimeSpan.FromMinutes(45 + 15));
        }

        [Fact]
        public async Task A_skipped_cycle_reports_with_the_last_interval()
        {
            var (sync, source, _, _, health) = Build();
            source.Next = () => throw new InvalidOperationException("db locked");

            await sync.RunSyncAsync();

            AssertDeadline(health, TimeSpan.FromMinutes(30 + 15));
        }

        [Fact]
        public async Task A_failed_cycle_reports_too()
        {
            var (sync, source, twitch, _, health) = Build();
            source.Next = () => Good(60);
            twitch.Fail = true;

            await sync.RunSyncAsync();

            AssertDeadline(health, TimeSpan.FromMinutes(60 + 15));
        }

        [Fact]
        public async Task The_service_reports_when_it_starts()
        {
            var (sync, _, _, discord, health) = Build();
            discord.FailLogin = true;

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await sync.StartAsync(CancellationToken.None);
                await sync.ExecuteTask!;
            });

            AssertDeadline(health, TimeSpan.FromMinutes(30 + 15));
        }

        [Fact]
        public async Task Stopping_the_module_clears_its_deadline()
        {
            var (sync, source, _, _, health) = Build();
            source.Next = () => Good(30);
            await sync.RunSyncAsync();
            var services = new ServiceCollection().AddSingleton(health).BuildServiceProvider();
            var module = new TwitchScheduleModule(services, NullLogger<TwitchScheduleModule>.Instance, new NoFatalShutdown());
            module.Attach(sync, new Disposable());

            await module.StopAsync(CancellationToken.None);

            Assert.True(health.Evaluate(Now.AddDays(1)).IsOk);
        }
    }
}
