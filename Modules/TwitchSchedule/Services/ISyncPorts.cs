using Mognet.Modules.TwitchSchedule.Storage;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Services
{
    internal interface IScheduleConfigSource
    {
        Task<ScheduleConfig> LoadAsync(CancellationToken cancellationToken = default);
    }

    internal interface ITwitchSchedules
    {
        Task<List<ChannelSchedule>> GetAllSchedulesAsync(ScheduleConfig config);
    }

    internal interface IDiscordSync
    {
        Task LoginAsync();

        Task SyncEventsAsync(IReadOnlyList<ChannelSchedule> schedules, ScheduleConfig config);
    }
}
