using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.TwitchSchedule.Models;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Storage
{
    /// <summary>
    /// Copies the channels and sync settings from the environment into the database once. After the marker is
    /// written the database is the only source, so later changes to the environment have no effect. Tokens and
    /// secrets are never read here.
    /// </summary>
    public sealed class ScheduleImporter
    {
        private readonly SqliteStore _store;
        private readonly IConfiguration _configuration;
        private readonly TimeProvider _time;
        private readonly ILogger<ScheduleImporter> _logger;

        public ScheduleImporter(SqliteStore store, IConfiguration configuration, TimeProvider time, ILogger<ScheduleImporter> logger)
        {
            _store = store;
            _configuration = configuration;
            _time = time;
            _logger = logger;
        }

        /// <summary>
        /// Returns <see langword="true"/> when rows were written. Writes nothing when the import is done already or
        /// the environment holds no channel, so a later start can still import. Rows and marker commit together.
        /// </summary>
        public async Task<bool> ImportAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);

            if (await MarkerExistsAsync(connection, cancellationToken))
                return false;

            var settings = ReadSettings();
            var channels = ReadChannels();
            if (channels.Count == 0)
            {
                _logger.LogWarning("No Twitch channels are set in the environment, so nothing was imported.");
                return false;
            }

            var at = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            for (var position = 0; position < channels.Count; position++)
            {
                await ExecuteAsync(connection, transaction,
                    "INSERT INTO channels (login, display_name, position) VALUES ($login, $name, $position)",
                    cancellationToken,
                    ("$login", channels[position].Username),
                    ("$name", channels[position].DisplayName),
                    ("$position", position));
            }

            settings.Add((ScheduleSettings.ImportDone, "true"));
            foreach (var (key, value) in settings)
            {
                await ExecuteAsync(connection, transaction,
                    "INSERT INTO settings (scope, key, value, updated_at) VALUES ($scope, $key, $value, $at)",
                    cancellationToken,
                    ("$scope", ScheduleSettings.Scope), ("$key", key), ("$value", value), ("$at", at));
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Imported {Count} Twitch channel(s) from the environment.", channels.Count);
            return true;
        }

        // Same binding and filter as the old bot: blank usernames are slots Docker passed as empty strings.
        // On a duplicate login the first one wins.
        private List<TwitchChannelConfig> ReadChannels()
        {
            var bound = _configuration.GetSection("Twitch:Channels").Get<List<TwitchChannelConfig>>() ?? [];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return bound
                .Where(c => !string.IsNullOrWhiteSpace(c.Username))
                .Where(c => seen.Add(c.Username))
                .ToList();
        }

        private List<(string Key, string Value)> ReadSettings()
        {
            var settings = new List<(string, string)>();
            AddNumber(settings, SyncOptions.Interval, ScheduleSettings.IntervalMinutes);
            AddNumber(settings, SyncOptions.Lookahead, ScheduleSettings.LookaheadDays);
            AddNumber(settings, SyncOptions.MaxEvents, ScheduleSettings.MaxEventsPerChannel);

            var language = _configuration["Discord:EventLanguage"];
            if (!string.IsNullOrWhiteSpace(language))
                settings.Add((ScheduleSettings.EventLanguage, language.Trim()));

            return settings;
        }

        private void AddNumber(List<(string, string)> settings, SyncOptions.Setting setting, string key)
        {
            var raw = _configuration[setting.Key];
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var value = setting.Validate(raw, setting.EnvironmentVariable);
            settings.Add((key, value.ToString(CultureInfo.InvariantCulture)));
        }

        private static async Task<bool> MarkerExistsAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM settings WHERE scope = $scope AND key = $key";
            AddParameter(command, "$scope", ScheduleSettings.Scope);
            AddParameter(command, "$key", ScheduleSettings.ImportDone);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }

        private static async Task ExecuteAsync(
            DbConnection connection,
            DbTransaction transaction,
            string sql,
            CancellationToken cancellationToken,
            params (string Name, object Value)[] parameters)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                AddParameter(command, name, value);
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
