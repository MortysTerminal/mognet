using System.Globalization;

namespace Mognet.Bot.Core.Health
{
    /// <summary>Location and format of the health file shared by the writer and the check.</summary>
    public static class HealthFile
    {
        public const string Ok = "ok";
        public const string StalledPrefix = "stalled:";
        private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public static string DefaultPath => Path.Combine(Path.GetTempPath(), "mognet-health");

        public static string Format(DateTimeOffset now, HealthStatus status) =>
            now.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture) + "\n" +
            (status.IsOk ? Ok : StalledPrefix + string.Join(",", status.Stalled)) + "\n";

        public static bool TryParseTimestamp(string text, out DateTimeOffset timestamp) =>
            DateTimeOffset.TryParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }
}
