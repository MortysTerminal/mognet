using Mognet.Bot.Core.Storage;

namespace Mognet.Modules.Alerts.Storage
{
    /// <summary>Tables owned by the alerts module.</summary>
    public static class AlertsMigrations
    {
        public static MigrationSet Set { get; } = new(AlertsModule.ModuleId,
        [
            new Migration(1, "sources_and_known_content", """
                CREATE TABLE sources (
                    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    platform TEXT NOT NULL,
                    external_id TEXT NOT NULL,
                    baseline_recorded_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    UNIQUE (platform, external_id)
                );
                CREATE TABLE known_content (
                    source_id INTEGER NOT NULL REFERENCES sources (id) ON DELETE CASCADE,
                    content_id TEXT NOT NULL,
                    scheduled_start TEXT NULL,
                    first_seen_at TEXT NOT NULL,
                    last_refreshed_at TEXT NOT NULL,
                    PRIMARY KEY (source_id, content_id)
                );
                CREATE INDEX known_content_last_refreshed ON known_content (last_refreshed_at);
                """),
        ]);
    }
}
