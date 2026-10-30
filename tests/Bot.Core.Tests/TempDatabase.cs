using Microsoft.Data.Sqlite;
using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core.Tests
{
    /// <summary>A real SQLite file in a unique temp directory, removed on dispose.</summary>
    public sealed class TempDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "mognet-tests-" + Guid.NewGuid().ToString("N"));

        public string FilePath => Path.Combine(_directory, "nested", "bot.db");

        public SqliteStore Store() => new(FilePath);

        public async Task<List<object?[]>> QueryAsync(string sql)
        {
            await using var connection = await Store().OpenConnectionAsync();
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

        public async Task<bool> TableExistsAsync(string name) =>
            (await QueryAsync($"SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{name}'")).Count == 1;

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = await Store().OpenConnectionAsync();
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
