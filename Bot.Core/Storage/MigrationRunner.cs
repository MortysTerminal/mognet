using System.Data.Common;
using System.Globalization;

namespace Mognet.Bot.Core.Storage
{
    /// <summary>
    /// Applies <see cref="MigrationSet"/>s. Each migration and its <c>schema_migrations</c> row share one transaction;
    /// a failure rolls that migration back and stops before any later one.
    /// </summary>
    public sealed class MigrationRunner
    {
        private readonly SqliteStore _store;
        private readonly TimeProvider _time;

        public MigrationRunner(SqliteStore store, TimeProvider? time = null)
        {
            _store = store;
            _time = time ?? TimeProvider.System;
        }

        public async Task ApplyAsync(IEnumerable<MigrationSet> sets, CancellationToken cancellationToken = default)
        {
            var all = sets.ToList();
            var repeated = all.GroupBy(s => s.Scope).FirstOrDefault(g => g.Count() > 1);
            if (repeated is not null)
                throw new ArgumentException($"Scope '{repeated.Key}' is given more than once.", nameof(sets));

            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await ExecuteAsync(connection, null, """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    scope TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    applied_at TEXT NOT NULL,
                    PRIMARY KEY (scope, version)
                )
                """, cancellationToken);

            // Refuse a database written by a newer build before changing anything.
            var applied = new Dictionary<string, HashSet<int>>();
            foreach (var set in all)
            {
                var versions = await ReadAppliedVersionsAsync(connection, set.Scope, cancellationToken);
                var known = set.Migrations.Select(m => m.Version).ToHashSet();
                var unknown = versions.Where(v => !known.Contains(v)).OrderBy(v => v).ToList();
                if (unknown.Count > 0)
                {
                    throw new MigrationException(
                        $"The database holds migration {unknown[^1]} for scope '{set.Scope}', which this build does not know " +
                        $"(newest known: {(known.Count == 0 ? "none" : known.Max().ToString(CultureInfo.InvariantCulture))}). " +
                        "Run a build that is at least as new as the one that wrote the database.");
                }

                applied[set.Scope] = versions;
            }

            foreach (var set in all)
            {
                foreach (var migration in set.Migrations.Where(m => !applied[set.Scope].Contains(m.Version)))
                    await ApplyOneAsync(connection, set.Scope, migration, cancellationToken);
            }
        }

        private async Task ApplyOneAsync(DbConnection connection, string scope, Migration migration, CancellationToken cancellationToken)
        {
            try
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken);

                await using var record = connection.CreateCommand();
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO schema_migrations (scope, version, name, applied_at) VALUES ($scope, $version, $name, $at)";
                AddParameter(record, "$scope", scope);
                AddParameter(record, "$version", migration.Version);
                AddParameter(record, "$name", migration.Name);
                AddParameter(record, "$at", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
                await record.ExecuteNonQueryAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new MigrationException(
                    $"Migration {migration.Version} '{migration.Name}' in scope '{scope}' failed and was rolled back: {ex.Message}", ex);
            }
        }

        private static async Task<HashSet<int>> ReadAppliedVersionsAsync(DbConnection connection, string scope, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT version FROM schema_migrations WHERE scope = $scope";
            AddParameter(command, "$scope", scope);

            var versions = new HashSet<int>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                versions.Add(reader.GetInt32(0));
            return versions;
        }

        private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
