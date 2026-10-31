using Mognet.Bot.Core.Storage;

namespace Mognet.Modules.TwitchSchedule.Storage
{
    /// <summary>Tables owned by the schedule module.</summary>
    public static class ScheduleMigrations
    {
        public static MigrationSet Set { get; } = new(TwitchScheduleModule.ModuleId,
        [
            new Migration(1, "channels", """
                CREATE TABLE channels (
                    login TEXT NOT NULL PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    position INTEGER NOT NULL
                );
                """),
        ]);
    }
}
