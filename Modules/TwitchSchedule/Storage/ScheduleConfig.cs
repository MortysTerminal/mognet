using Mognet.Modules.TwitchSchedule.Models;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Storage
{
    /// <summary>The schedule module's settings and channels as stored, read at the start of a sync cycle.</summary>
    public sealed record ScheduleConfig(
        SyncOptions Options,
        string Language,
        IReadOnlyList<TwitchChannelConfig> Channels,
        bool DryRun);
}
