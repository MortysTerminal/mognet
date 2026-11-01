using System.Text.Json;
using Mognet.Modules.Alerts.Providers;

namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>
    /// The three YouTube Data API calls the module needs. The key travels only in the <c>X-Goog-Api-Key</c> header,
    /// so it never appears in a URL, a log line or an exception message. Every call counts against
    /// <see cref="QuotaCounter"/> when it is sent. A non-success answer throws <see cref="YouTubeApiException"/>;
    /// a body that does not have the documented shape throws <see cref="JsonException"/>.
    /// </summary>
    public sealed class YouTubeApi
    {
        public const string BaseUrl = "https://www.googleapis.com/youtube/v3/";

        /// <summary>The most ids <c>videos.list</c> takes in one call.</summary>
        public const int MaxIdsPerCall = 50;

        private const string KeyHeader = "X-Goog-Api-Key";

        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly QuotaCounter _quota;

        public YouTubeApi(HttpClient http, string apiKey, QuotaCounter quota)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            _http = http;
            _apiKey = apiKey;
            _quota = quota;
        }

        /// <summary>One page of up to 50 video ids of a playlist, newest first, and the token of the next page.</summary>
        public async Task<(IReadOnlyList<string> VideoIds, string? NextPageToken)> ListPlaylistPageAsync(string playlistId, string? pageToken, CancellationToken cancellationToken)
        {
            var query = $"part=contentDetails&maxResults=50&playlistId={Uri.EscapeDataString(playlistId)}";
            if (pageToken is not null)
                query += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var document = await GetAsync("playlistItems", query, isSearch: false, cancellationToken);
            var ids = new List<string>();
            foreach (var item in Items(document))
            {
                if (!item.TryGetProperty("contentDetails", out var details) || details.ValueKind != JsonValueKind.Object ||
                    !details.TryGetProperty("videoId", out var id) || id.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException("A playlist item has no video id.");
                }

                ids.Add(id.GetString()!);
            }

            var next = document.RootElement.TryGetProperty("nextPageToken", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : null;
            return (ids, next);
        }

        /// <summary>The videos YouTube returns for up to 50 ids. An id it does not return is missing from the list.</summary>
        public async Task<IReadOnlyList<ContentItem>> GetVideosAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
        {
            if (ids.Count is 0 or > MaxIdsPerCall)
                throw new ArgumentException($"Ask for 1 to {MaxIdsPerCall} ids per call.", nameof(ids));

            var query = $"part=snippet,contentDetails,status,liveStreamingDetails&id={string.Join(",", ids.Select(Uri.EscapeDataString))}";
            using var document = await GetAsync("videos", query, isSearch: false, cancellationToken);
            return Items(document).Select(YouTubeVideoParser.Parse).ToList();
        }

        /// <summary>Ids of the channel's upcoming broadcasts, up to 50.</summary>
        public async Task<IReadOnlyList<string>> SearchUpcomingAsync(string channelId, CancellationToken cancellationToken)
        {
            var query = $"part=id&type=video&eventType=upcoming&maxResults=50&channelId={Uri.EscapeDataString(channelId)}";
            using var document = await GetAsync("search", query, isSearch: true, cancellationToken);
            var ids = new List<string>();
            foreach (var item in Items(document))
            {
                if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Object ||
                    !id.TryGetProperty("videoId", out var videoId) || videoId.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException("A search result has no video id.");
                }

                ids.Add(videoId.GetString()!);
            }

            return ids;
        }

        private async Task<JsonDocument> GetAsync(string endpoint, string query, bool isSearch, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{endpoint}?{query}");
            request.Headers.Add(KeyHeader, _apiKey);

            if (isSearch)
                _quota.AddSearchCall();
            else
                _quota.AddUnits(1);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new YouTubeApiException(response.StatusCode, await ReadReasonAsync(body, cancellationToken));

            return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        }

        // The documented error body is {"error":{"errors":[{"reason":"quotaExceeded"}]}}. Only the reason is kept.
        private static async Task<string?> ReadReasonAsync(Stream body, CancellationToken cancellationToken)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
                var first = document.RootElement.GetProperty("error").GetProperty("errors")[0];
                var reason = first.GetProperty("reason").GetString();
                return reason is { Length: > 0 and <= 64 } && reason.All(char.IsLetterOrDigit) ? reason : null;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private static JsonElement.ArrayEnumerator Items(JsonDocument document) =>
            document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                ? items.EnumerateArray()
                : throw new JsonException("The answer has no items list.");
    }
}
