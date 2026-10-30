using System.Text;
using System.Text.Json;

namespace Mognet.Modules.TwitchSchedule.Sync
{
    /// <summary>
    /// One segment of a Twitch streaming schedule, independent of the API client's types.
    /// </summary>
    /// <param name="EndTime">Null when Twitch has no end time for the segment.</param>
    /// <param name="IsCanceled">True when the broadcaster canceled this occurrence of a recurring segment.</param>
    /// <param name="IsRecurring">True when the segment is one weekly occurrence of a recurring series.</param>
    public record TwitchSegment(
        string Id,
        string Title,
        string CategoryName,
        DateTimeOffset StartTime,
        DateTimeOffset? EndTime,
        bool IsCanceled,
        bool IsRecurring = false
    )
    {
        /// <summary>
        /// Identifies the recurring series this occurrence belongs to; occurrences of one series share it.
        /// </summary>
        /// <remarks>
        /// Not documented by Twitch: segment IDs are base64-encoded JSON such as
        /// <c>{"segmentID":"e4acc724-…","isoYear":2021,"isoWeek":26}</c>, where <c>segmentID</c> is the same for every
        /// week of a series. If the ID cannot be decoded, the fallback follows Twitch's documented definition of a
        /// recurring segment (same weekday and time every week) plus the exact title, compared in UTC. Around a
        /// daylight-saving switch the fallback may show two occurrences of one series for that week.
        /// One-time segments are never merged.
        /// </remarks>
        public string SeriesKey => IsRecurring ? TryDecodeSeriesId(Id) ?? FallbackSeriesKey() : Id;

        private string FallbackSeriesKey()
        {
            var start = StartTime.UtcDateTime;
            return $"recurring:{start.DayOfWeek}|{start:HH:mm}|{Title}";
        }

        private static string? TryDecodeSeriesId(string segmentId)
        {
            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(segmentId));
                using var document = JsonDocument.Parse(json);

                return document.RootElement.ValueKind == JsonValueKind.Object &&
                       document.RootElement.TryGetProperty("segmentID", out var series) &&
                       series.ValueKind == JsonValueKind.String &&
                       !string.IsNullOrEmpty(series.GetString())
                    ? "series:" + series.GetString()
                    : null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The broadcaster's vacation, during which no segments take place.
    /// </summary>
    public record VacationWindow(DateTimeOffset StartTime, DateTimeOffset EndTime)
    {
        public bool Contains(DateTimeOffset time) => time >= StartTime && time < EndTime;
    }
}
