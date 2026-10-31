using System.Data.Common;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.TwitchSchedule.Models;
using Mognet.Modules.TwitchSchedule.Services;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Storage
{
    /// <summary>Reads the module's channels and settings from the database. Nothing is cached.</summary>
    public sealed class ScheduleConfigStore : IScheduleConfigSource
    {
        private readonly SqliteStore _store;

        public ScheduleConfigStore(SqliteStore store)
        {
            _store = store;
        }

        /// <summary>
        /// Absent settings use the defaults. A stored number that is not a whole number in range throws
        /// <see cref="InvalidOperationException"/> naming the key and the allowed range.
        /// </summary>
        public async Task<ScheduleConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await _store.OpenConnectionAsync(cancellationToken);
            var settings = await ReadSettingsAsync(connection, cancellationToken);
            var channels = await ReadChannelsAsync(connection, cancellationToken);

            var options = new SyncOptions(
                Number(settings, ScheduleSettings.IntervalMinutes, SyncOptions.Interval),
                Number(settings, ScheduleSettings.LookaheadDays, SyncOptions.Lookahead),
                Number(settings, ScheduleSettings.MaxEventsPerChannel, SyncOptions.MaxEvents));

            var language = settings.GetValueOrDefault(ScheduleSettings.EventLanguage);
            return new ScheduleConfig(
                options,
                string.IsNullOrWhiteSpace(language) ? "en" : language,
                channels,
                DryRun: settings.GetValueOrDefault(ScheduleSettings.DryRun) == "true");
        }

        private static int Number(Dictionary<string, string> settings, string key, SyncOptions.Setting setting) =>
            settings.TryGetValue(key, out var raw)
                ? setting.Validate(raw, $"Setting '{key}'")
                : setting.Default;

        private static async Task<Dictionary<string, string>> ReadSettingsAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM settings WHERE scope = $scope";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$scope";
            parameter.Value = ScheduleSettings.Scope;
            command.Parameters.Add(parameter);

            var settings = new Dictionary<string, string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                settings[reader.GetString(0)] = reader.GetString(1);
            return settings;
        }

        private static async Task<List<TwitchChannelConfig>> ReadChannelsAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT login, display_name FROM channels ORDER BY position";
            var channels = new List<TwitchChannelConfig>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                channels.Add(new TwitchChannelConfig { Username = reader.GetString(0), DisplayName = reader.GetString(1) });
            return channels;
        }
    }
}
