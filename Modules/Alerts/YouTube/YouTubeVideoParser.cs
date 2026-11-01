using System.Globalization;
using System.Text.Json;
using Mognet.Modules.Alerts.Providers;

namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>
    /// Turns a <c>videos.list</c> item into a <see cref="ContentItem"/> and the facts the classifier needs. Live
    /// state and start come from <c>snippet.liveBroadcastContent</c> and <c>liveStreamingDetails</c>.
    /// <c>snippet.publishedAt</c> is never read: it is not a stream's time (on ended streams it lies after the end).
    /// The item carries no type; the classifier decides that once the playlist facts are known.
    /// </summary>
    internal static class YouTubeVideoParser
    {
        /// <exception cref="JsonException">The item has no id or a time that is not a date.</exception>
        public static YouTubeVideo Parse(JsonElement video)
        {
            if (video.ValueKind != JsonValueKind.Object ||
                !video.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(id.GetString()))
            {
                throw new JsonException("A video has no id.");
            }

            var details = Child(video, "liveStreamingDetails");
            var start = Time(details, "scheduledStartTime");
            var started = Time(details, "actualStartTime") is not null;
            var ended = Time(details, "actualEndTime") is not null;

            var broadcast = Text(Child(video, "snippet"), "liveBroadcastContent");

            LiveState? state = broadcast switch
            {
                "upcoming" => LiveState.Upcoming,
                "live" => LiveState.Live,
                _ when ended => LiveState.Ended,
                _ => null,
            };

            var facts = new ClassificationFacts(broadcast, details is not null, start is not null, started, ended, Text(Child(video, "contentDetails"), "duration"));
            return new YouTubeVideo(new ContentItem(id.GetString()!, start, state), facts);
        }

        private static string? Text(JsonElement? parent, string name) =>
            parent is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

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
