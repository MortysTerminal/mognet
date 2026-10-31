using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Mognet.Modules.TwitchSchedule.Sync
{
    /// <summary>
    /// Numeric sync settings, validated once at start-up. An invalid value stops the bot before it can
    /// hammer the APIs (interval) or delete every event (look-ahead, event cap).
    /// </summary>
    public record SyncOptions(int IntervalMinutes, int LookaheadDays, int MaxEventsPerChannel)
    {
        public static readonly Setting Interval = new("Sync:IntervalMinutes", Default: 30, Min: 5, Max: 1440);
        public static readonly Setting Lookahead = new("Sync:LookaheadDays", Default: 14, Min: 1, Max: 60);
        public static readonly Setting MaxEvents = new("Sync:MaxEventsPerChannel", Default: 10, Min: 1, Max: 50);

        /// <summary>
        /// Reads and validates the settings. Missing or empty values use the defaults (Docker passes unset compose
        /// variables as empty strings). Throws <see cref="InvalidOperationException"/> naming the variable, the value and
        /// the allowed range.
        /// </summary>
        public static SyncOptions FromConfiguration(IConfiguration configuration) =>
            new(
                Interval.Read(configuration),
                Lookahead.Read(configuration),
                MaxEvents.Read(configuration)
            );

        /// <param name="Max">The interval's upper bound also keeps the wait below Task.Delay's limit (about 49 days).</param>
        public record Setting(string Key, int Default, int Min, int Max)
        {
            // Name of the environment variable that sets this key.
            public string EnvironmentVariable => Key.Replace(":", "__");

            public int Read(IConfiguration configuration)
            {
                var raw = configuration[Key];
                if (string.IsNullOrWhiteSpace(raw))
                    return Default;

                return Validate(raw, EnvironmentVariable);
            }

            /// <summary>
            /// Parses a raw setting value. Throws <see cref="InvalidOperationException"/> naming
            /// <paramref name="name"/>, the value and the allowed range.
            /// </summary>
            public int Validate(string raw, string name)
            {
                if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
                    value < Min || value > Max)
                {
                    throw new InvalidOperationException(
                        $"{name} = '{raw}' is invalid: use a whole number from {Min} to {Max} " +
                        $"(default {Default} when not set)."
                    );
                }

                return value;
            }
        }
    }
}
