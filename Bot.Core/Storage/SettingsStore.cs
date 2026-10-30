using System.Data.Common;
using System.Globalization;

namespace Mognet.Bot.Core.Storage
{
    /// <summary>
    /// Key-value settings grouped by scope. Every call goes to the database, so a write is visible at once,
    /// also to another instance on the same file.
    /// </summary>
    public sealed class SettingsStore
    {
        private readonly SqliteStore _store;
        private readonly TimeProvider _time;

        public SettingsStore(SqliteStore store, TimeProvider? time = null)
        {
            _store = store;
            _time = time ?? TimeProvider.System;
        }

        public async Task<string?> GetAsync(string scope, string key, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, "SELECT value FROM settings WHERE scope = $scope AND key = $key", scope, key);
            return await command.ExecuteScalarAsync(cancellationToken) as string;
        }

        public async Task SetAsync(string scope, string key, string value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(value);
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, """
                INSERT INTO settings (scope, key, value, updated_at) VALUES ($scope, $key, $value, $at)
                ON CONFLICT (scope, key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at
                """, scope, key);
            Add(command, "$value", value);
            Add(command, "$at", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DeleteAsync(string scope, string key, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, "DELETE FROM settings WHERE scope = $scope AND key = $key", scope, key);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static DbCommand Command(DbConnection connection, string sql, string scope, string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            var command = connection.CreateCommand();
            command.CommandText = sql;
            Add(command, "$scope", scope);
            Add(command, "$key", key);
            return command;
        }

        private static void Add(DbCommand command, string name, string value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
