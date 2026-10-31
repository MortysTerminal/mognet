using Microsoft.Extensions.Logging;
using Mognet.Modules.TwitchSchedule.Models;
using Mognet.Modules.TwitchSchedule.Services;
using Mognet.Modules.TwitchSchedule.Storage;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class SyncCycleTests
    {
        private sealed class Source : IScheduleConfigSource
        {
            public Func<ScheduleConfig> Next { get; set; } = () => throw new InvalidOperationException("not set");

            public Task<ScheduleConfig> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Next());
        }

        private sealed class FakeTwitch : ITwitchSchedules
        {
            public List<ScheduleConfig> Calls { get; } = [];

            public Task<List<ChannelSchedule>> GetAllSchedulesAsync(ScheduleConfig config)
            {
                Calls.Add(config);
                return Task.FromResult(new List<ChannelSchedule>());
            }
        }

        private sealed class FakeDiscord : IDiscordSync
        {
            public int Syncs { get; private set; }

            public Task LoginAsync() => Task.CompletedTask;

            public Task SyncEventsAsync(IReadOnlyList<ChannelSchedule> schedules, ScheduleConfig config)
            {
                Syncs++;
                return Task.CompletedTask;
            }
        }

        private static ScheduleConfig Good(int interval = 60) =>
            new(new SyncOptions(interval, 14, 10), "en", [new TwitchChannelConfig { Username = "a", DisplayName = "A" }], false);

        private static (SyncService Sync, Source Source, FakeTwitch Twitch, FakeDiscord Discord, ListLogger<SyncService> Log) Build()
        {
            var source = new Source();
            var twitch = new FakeTwitch();
            var discord = new FakeDiscord();
            var log = new ListLogger<SyncService>();
            return (new SyncService(twitch, discord, source, new SyncOptions(30, 14, 10), log), source, twitch, discord, log);
        }

        [Fact]
        public async Task A_cycle_loads_the_config_and_hands_it_to_twitch_and_discord()
        {
            var (sync, source, twitch, discord, _) = Build();
            var config = Good(45);
            source.Next = () => config;

            await sync.RunSyncAsync();

            Assert.Same(config, Assert.Single(twitch.Calls));
            Assert.Equal(1, discord.Syncs);
            Assert.Equal(45, sync.IntervalMinutes);
        }

        [Fact]
        public async Task A_config_load_failure_skips_the_cycle_without_any_call_and_keeps_the_last_interval()
        {
            var (sync, source, twitch, discord, log) = Build();
            source.Next = () => Good(60);
            await sync.RunSyncAsync();

            source.Next = () => throw new InvalidOperationException("bad value");
            await sync.RunSyncAsync();

            Assert.Single(twitch.Calls);
            Assert.Equal(1, discord.Syncs);
            Assert.Equal(60, sync.IntervalMinutes);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
        }

        [Fact]
        public async Task An_empty_channel_list_skips_the_cycle_without_any_call()
        {
            var (sync, source, twitch, discord, log) = Build();
            source.Next = () => Good() with { Channels = [] };

            await sync.RunSyncAsync();

            Assert.Empty(twitch.Calls);
            Assert.Equal(0, discord.Syncs);
            Assert.Equal(30, sync.IntervalMinutes);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
        }
    }
}
