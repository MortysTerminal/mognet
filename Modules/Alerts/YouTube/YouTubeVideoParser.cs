using System.Globalization;
using System.Text.Json;
using Mognet.Modules.Alerts.Providers;

namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>
    /// Turns a <c>videos.list</c> item into a <see cref="ContentItem"/>. Live state and start come from
    /// <c>snippet.liveBroadcastContent</c> and <c>liveStreamingDetails</c>. <c>snippet.publishedAt</c> is never
    /// read: it is not a stream's time (on ended streams it lies after the end).
    /// </summary>
    internal static class YouTubeVideoParser
    {
        /// <exception cref="JsonException">The item has no id or a time that is not a date.</exception>
        public static ContentItem Parse(JsonElement video)
        {
            if (video.ValueKind != JsonValueKind.Object ||
                !video.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(id.GetString()))
            {
                throw new JsonException("A video has no id.");
            }

            var details = Child(video, "liveStreamingDetails");
            var start = Time(details, "scheduledStartTime");
            var ended = Time(details, "actualEndTime") is not null;

            var broadcast = Child(video, "snippet") is { } snippet && snippet.TryGetProperty("liveBroadcastContent", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

            LiveState? state = broadcast switch
            {
                "upcoming" => LiveState.Upcoming,
                "live" => LiveState.Live,
                _ when ended => LiveState.Ended,
                _ => null,
            };

            return new ContentItem(id.GetString()!, start, state);
        }

        private static JsonElement? Child(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object ? child : null;

        private static DateTimeOffset? Time(JsonElement? parent, string name)
        {
            if (parent is not { } element || !element.TryGetProperty(name, out var value))
                return null;

            if (value.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
            {
                throw new JsonException($"{name} is not a date.");
            }

            return time;
        }
    }
}
