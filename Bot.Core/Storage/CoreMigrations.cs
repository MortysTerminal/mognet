namespace Mognet.Bot.Core.Storage
{
    /// <summary>Tables owned by the core: shared settings and the send-once record for alerts.</summary>
    public static class CoreMigrations
    {
        public const string Scope = "core";

        public static MigrationSet Set { get; } = new(Scope,
        [
            new Migration(1, "settings_and_sent_alerts", """
                CREATE TABLE settings (
                    scope TEXT NOT NULL,
                    key TEXT NOT NULL,
                    value TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY (scope, key)
                );
                CREATE TABLE sent_alerts (
                    rule_id TEXT NOT NULL,
                    stage TEXT NOT NULL,
                    content_id TEXT NOT NULL,
                    claimed_at TEXT NOT NULL,
                    PRIMARY KEY (rule_id, stage, content_id)
                );
                """),
        ]);
    }
}
