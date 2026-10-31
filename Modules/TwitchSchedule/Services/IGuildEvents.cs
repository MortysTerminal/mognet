using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Services
{
    /// <summary>The reads and writes on the guild's scheduled events that a sync cycle performs.</summary>
    internal interface IGuildEvents
    {
        Task<IReadOnlyList<ExistingEvent>> ListAsync();

        Task CreateAsync(DesiredEvent desired);

        Task UpdateAsync(ExistingEvent existing, DesiredEvent desired);

        Task DeleteAsync(ExistingEvent existing);
    }
}
