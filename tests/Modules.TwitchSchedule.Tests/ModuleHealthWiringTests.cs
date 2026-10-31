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
    public class ModuleHealthWiringTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

        private sealed class Source : IScheduleConfigSource
        {
            public Task<ScheduleConfig> LoadAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new ScheduleConfig(new SyncOptions(30, 14, 10), "en", [new TwitchChannelConfig { Username = "a", DisplayName = "A" }], false));
        }

        private sealed class Twitch : ITwitchSchedules
        {
            public Task<List<ChannelSchedule>> GetAllSchedulesAsync(ScheduleConfig config) => Task.FromResult(new List<ChannelSchedule>());
        }

        private sealed class Discord : IDiscordSync
        {
            public Task LoginAsync() => Task.CompletedTask;

            public Task SyncEventsAsync(IReadOnlyList<ChannelSchedule> schedules, ScheduleConfig config) => Task.CompletedTask;
        }

        private sealed class NoFatalShutdown : IFatalShutdown
        {
            public void Shutdown()
            {
            }
        }

        [Fact]
        public async Task The_sync_service_the_module_builds_reports_to_the_registered_registry()
        {
            var registry = new HealthRegistry();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new FixedTime(Now));
            services.AddSingleton(registry);
            var module = new TwitchScheduleModule(services.BuildServiceProvider(), NullLogger<TwitchScheduleModule>.Instance, new NoFatalShutdown());

            var sync = module.CreateSync(new Twitch(), new Discord(), new Source(), new SyncOptions(30, 14, 10));
            await sync.RunSyncAsync();

            Assert.True(registry.Evaluate(Now).IsOk);
            Assert.Equal([TwitchScheduleModule.ModuleId], registry.Evaluate(Now.AddYears(1)).Stalled);
        }
    }
}
