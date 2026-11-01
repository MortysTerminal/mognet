using Microsoft.Data.Sqlite;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.Tests
{
    /// <summary>A real SQLite file in a temp directory with the core and alerts tables, removed on dispose.</summary>
    internal sealed class AlertsDb : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "mognet-alerts-tests-" + Guid.NewGuid().ToString("N"));

        public SqliteStore Store { get; }

        public AlertSourceStore Sources => new(Store);

        private AlertsDb() => Store = new SqliteStore(Path.Combine(_directory, "bot.db"));

        public static async Task<AlertsDb> CreateAsync()
        {
            var db = new AlertsDb();
            await new MigrationRunner(db.Store).ApplyAsync([CoreMigrations.Set, AlertsMigrations.Set]);
            return db;
        }

        public async Task<List<object?[]>> QueryAsync(string sql)
        {
            await using var connection = await Store.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var rows = new List<object?[]>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = await Store.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
