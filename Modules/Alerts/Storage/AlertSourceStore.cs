using System.Data.Common;
using System.Globalization;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.Alerts.Providers;

namespace Mognet.Modules.Alerts.Storage
{
    /// <summary>
    /// Sources and their known content. Every call goes to the database. Times are passed in and stored as UTC
    /// text, so they sort and compare as instants.
    /// </summary>
    public sealed class AlertSourceStore
    {
        private const string SourceColumns = "id, platform, external_id, baseline_recorded_at IS NOT NULL, created_at";
        private const string ContentColumns = "source_id, content_id, scheduled_start, first_seen_at, last_refreshed_at, live_state";

        private readonly SqliteStore _store;

        public AlertSourceStore(SqliteStore store)
        {
            _store = store;
        }

        /// <exception cref="InvalidOperationException">The platform already has a source with this external id.</exception>
        public async Task<AlertSource> AddSourceAsync(string platform, string externalId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(platform);
            ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, """
                INSERT INTO sources (platform, external_id, created_at) VALUES ($platform, $external_id, $at)
                ON CONFLICT (platform, external_id) DO NOTHING
                RETURNING id
                """);
            Add(command, "$platform", platform);
            Add(command, "$external_id", externalId);
            Add(command, "$at", Format(now));

            if (await command.ExecuteScalarAsync(cancellationToken) is not long id)
                throw new InvalidOperationException($"A {platform} source with id '{externalId}' already exists.");

            return new AlertSource(id, platform, externalId, BaselineRecorded: false, now.ToUniversalTime());
        }

        public async Task<AlertSource?> GetSourceAsync(long id, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, $"SELECT {SourceColumns} FROM sources WHERE id = $id");
            Add(command, "$id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadSource(reader) : null;
        }

        /// <summary>All sources in the order they were added, or only those of <paramref name="platform"/>.</summary>
        public async Task<IReadOnlyList<AlertSource>> ListSourcesAsync(string? platform = null, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, $"SELECT {SourceColumns} FROM sources WHERE $platform IS NULL OR platform = $platform ORDER BY id");
            Add(command, "$platform", platform);
            var sources = new List<AlertSource>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                sources.Add(ReadSource(reader));
            return sources;
        }

        /// <summary>Removes the source and, through the foreign key, all its known content. False when there is no such source.</summary>
        public async Task<bool> RemoveSourceAsync(long id, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, "DELETE FROM sources WHERE id = $id");
            Add(command, "$id", id);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }

        /// <summary>Sets the baseline marker. False when there is no such source.</summary>
        public async Task<bool> MarkBaselineRecordedAsync(long id, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, "UPDATE sources SET baseline_recorded_at = $at WHERE id = $id");
            Add(command, "$id", id);
            Add(command, "$at", Format(now));
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }

        /// <summary>
        /// Stores an item or updates the start, the live state and the refresh time of a known one. The first-seen time
        /// never changes. A <see langword="null"/> start or state replaces a stored one, so call this only with data
        /// from the platform's detail call. The source must exist.
        /// </summary>
        public async Task UpsertKnownContentAsync(long sourceId, string contentId, DateTimeOffset? scheduledStart, DateTimeOffset now, LiveState? liveState = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, """
                INSERT INTO known_content (source_id, content_id, scheduled_start, first_seen_at, last_refreshed_at, live_state)
                VALUES ($source_id, $content_id, $start, $at, $at, $live_state)
                ON CONFLICT (source_id, content_id) DO UPDATE SET
                    scheduled_start = excluded.scheduled_start,
                    last_refreshed_at = excluded.last_refreshed_at,
                    live_state = excluded.live_state
                """);
            Add(command, "$source_id", sourceId);
            Add(command, "$content_id", contentId);
            Add(command, "$start", scheduledStart is { } start ? Format(start) : null);
            Add(command, "$live_state", liveState is { } state ? FormatState(state) : null);
            Add(command, "$at", Format(now));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<KnownContent?> GetKnownContentAsync(long sourceId, string contentId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, $"SELECT {ContentColumns} FROM known_content WHERE source_id = $source_id AND content_id = $content_id");
            Add(command, "$source_id", sourceId);
            Add(command, "$content_id", contentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadContent(reader) : null;
        }

        /// <summary>All known content of a source, first seen first.</summary>
        public async Task<IReadOnlyList<KnownContent>> ListKnownContentAsync(long sourceId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, $"SELECT {ContentColumns} FROM known_content WHERE source_id = $source_id ORDER BY first_seen_at, content_id");
            Add(command, "$source_id", sourceId);
            return await ReadContentListAsync(command, cancellationToken);
        }

        /// <summary>Items of all sources whose last refresh lies before <paramref name="cutoff"/>, oldest first.</summary>
        public async Task<IReadOnlyList<KnownContent>> ListNotRefreshedSinceAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, $"SELECT {ContentColumns} FROM known_content WHERE last_refreshed_at < $cutoff ORDER BY last_refreshed_at, source_id, content_id");
            Add(command, "$cutoff", Format(cutoff));
            return await ReadContentListAsync(command, cancellationToken);
        }

        /// <summary>Sets the refresh time of one item. False when the item is not known.</summary>
        public async Task<bool> MarkRefreshedAsync(long sourceId, string contentId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, "UPDATE known_content SET last_refreshed_at = $at WHERE source_id = $source_id AND content_id = $content_id");
            Add(command, "$source_id", sourceId);
            Add(command, "$content_id", contentId);
            Add(command, "$at", Format(now));
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }

        /// <summary>Deletes one item. False when the item is not known.</summary>
        public async Task<bool> DeleteKnownContentAsync(long sourceId, string contentId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            await using var command = Command(connection, "DELETE FROM known_content WHERE source_id = $source_id AND content_id = $content_id");
            Add(command, "$source_id", sourceId);
            Add(command, "$content_id", contentId);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }

        private static async Task<IReadOnlyList<KnownContent>> ReadContentListAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var items = new List<KnownContent>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(ReadContent(reader));
            return items;
        }

        private static AlertSource ReadSource(DbDataReader reader) =>
            new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0, Parse(reader.GetString(4)));

        private static KnownContent ReadContent(DbDataReader reader) =>
            new(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : Parse(reader.GetString(2)), Parse(reader.GetString(3)), Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : ParseState(reader.GetString(5)));

        private static string FormatState(LiveState state) => state switch
        {
            LiveState.Upcoming => "upcoming",
            LiveState.Live => "live",
            LiveState.Ended => "ended",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        private static LiveState ParseState(string text) => text switch
        {
            "upcoming" => LiveState.Upcoming,
            "live" => LiveState.Live,
            "ended" => LiveState.Ended,
            _ => throw new InvalidOperationException($"Unknown live state '{text}' in the database."),
        };

        // Fixed-width UTC text: string order equals time order.
        private static string Format(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        private static DateTimeOffset Parse(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        private static DbCommand Command(DbConnection connection, string sql)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            return command;
        }

        private static void Add(DbCommand command, string name, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
