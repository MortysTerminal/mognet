using System.Collections;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.TwitchSchedule.Storage;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    /// <summary>A real SQLite file in a temp directory with the core and schedule tables, removed on dispose.</summary>
    internal sealed class ScheduleDb : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "mognet-schedule-tests-" + Guid.NewGuid().ToString("N"));

        public SqliteStore Store { get; }

        private ScheduleDb() => Store = new SqliteStore(Path.Combine(_directory, "bot.db"));

        public static async Task<ScheduleDb> CreateAsync()
        {
            var db = new ScheduleDb();
            await new MigrationRunner(db.Store).ApplyAsync([CoreMigrations.Set, ScheduleMigrations.Set]);
            return db;
        }

        public ScheduleImporter Importer(IConfiguration configuration) =>
            new(Store, configuration, TimeProvider.System, new ListLogger<ScheduleImporter>());

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

        public async Task<List<(string Login, string DisplayName, long Position)>> ChannelsAsync() =>
            (await QueryAsync("SELECT login, display_name, position FROM channels ORDER BY position"))
                .Select(r => ((string)r[0]!, (string)r[1]!, (long)r[2]!)).ToList();

        public async Task<Dictionary<string, string>> SettingsAsync() =>
            (await QueryAsync($"SELECT key, value FROM settings WHERE scope = '{ScheduleSettings.Scope}'"))
                .ToDictionary(r => (string)r[0]!, r => (string)r[1]!);

        public Task SetSettingAsync(string key, string value) =>
            ExecuteAsync($"INSERT INTO settings (scope, key, value, updated_at) VALUES ('{ScheduleSettings.Scope}', '{key}', '{value}', '2026-01-01')");

        /// <summary>Every row of every table as text, to compare states and to search for values.</summary>
        public async Task<List<string>> DumpAsync()
        {
            var lines = new List<string>();
            var tables = await QueryAsync("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");
            foreach (var table in tables)
            {
                foreach (var row in await QueryAsync($"SELECT * FROM \"{table[0]}\""))
                    lines.Add(table[0] + "|" + string.Join("|", row.Select(v => v?.ToString())));
            }
            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    internal static class Env
    {
        public static IConfiguration Config(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
                .Build();

        public static (string, string?)[] Channels(params (string Login, string Display)[] channels) =>
            channels.SelectMany((c, i) => new (string, string?)[]
            {
                ($"Twitch:Channels:{i}:Username", c.Login),
                ($"Twitch:Channels:{i}:DisplayName", c.Display),
            }).ToArray();
    }

    internal sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        public IEnumerable<string> Messages => Entries.Select(e => e.Message);
    }

    internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
