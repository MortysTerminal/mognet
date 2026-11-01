using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;
using Microsoft.Extensions.Logging;

namespace Mognet.Modules.Alerts.Tests
{
    /// <summary>A clock the test moves by hand.</summary>
    internal sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    internal static class Wait
    {
        /// <summary>Polls until the condition holds. A hosted service may start its loop on another thread.</summary>
        public static async Task UntilAsync(Func<bool> condition)
        {
            for (var i = 0; i < 400 && !condition(); i++)
                await Task.Delay(25);
            Assert.True(condition(), "The condition did not become true in time.");
        }
    }

    /// <summary>Collects the log lines of every logger made from it, so a test can search all output.</summary>
    internal sealed class LogSink
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IEnumerable<string> Messages => Entries.Select(e => e.Message);

        public IEnumerable<string> At(LogLevel level) => Entries.Where(e => e.Level == level).Select(e => e.Message);

        public ILogger<T> Logger<T>() => new SinkLogger<T>(this);

        private sealed class SinkLogger<T>(LogSink sink) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            // The exception text is part of the line, so a secret hidden in an exception would be found too.
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : " " + exception)));
        }
    }

    /// <summary>Loads hand-written answers in the documented response shape and fills in the invented ids and times.</summary>
    internal static class Fixture
    {
        public static string Load(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

        public static string Item(string name, string id, string start = "2026-02-01T18:00:00Z") =>
            Load(name).Replace("__ID__", id).Replace("__START__", start);

        public static string Wrap(string wrapper, IEnumerable<string> items, string next = "") =>
            Load(wrapper).Replace("__ITEMS__", string.Join(",", items)).Replace("__NEXT__", next);
    }

    public sealed record Reply(HttpStatusCode Status, string Body)
    {
        /// <summary>Headers arrive but the body never does.</summary>
        public bool Stall { get; init; }

        public static Reply Stalled() => new(HttpStatusCode.OK, "") { Stall = true };

        public static Reply Ok(string body) => new(HttpStatusCode.OK, body);

        public static Reply QuotaExceeded() => new(HttpStatusCode.Forbidden, Fixture.Load("error-quota-exceeded.json"));

        public static Reply PlaylistNotFound() => new(HttpStatusCode.NotFound, Fixture.Load("error-playlist-not-found.json"));

        public static Reply Malformed() => Ok("{ this is not json");
    }

    internal sealed class StalledContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.Delay(Timeout.Infinite);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    public sealed record Recorded(string Endpoint, Uri Uri, string? ApiKey, NameValueCollection Query)
    {
        public string[] Ids => (Query["id"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Plays YouTube. Answers come from the fixture files; no request leaves the process. Uploads are served in
    /// pages, videos by id, and a search returns the configured upcoming ids per channel.
    /// </summary>
    internal sealed class StubYouTube : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        public Dictionary<string, string> Videos { get; } = [];

        public Dictionary<string, List<string>> Uploads { get; } = [];

        public Dictionary<string, List<string>> Upcoming { get; } = [];

        public int PageSize { get; set; } = 50;

        /// <summary>Called first for every request; a non-null reply (or a thrown exception) replaces the normal answer.</summary>
        public Func<Recorded, Reply?>? Intercept { get; set; }

        public IEnumerable<Recorded> To(string endpoint) => Requests.Where(r => r.Endpoint == endpoint);

        public void AddOrdinary(string id) => Videos[id] = Fixture.Item("video-ordinary.json", id);

        public void AddUpcoming(string id, string start) => Videos[id] = Fixture.Item("video-upcoming.json", id, start);

        public void AddShort(string id) => Videos[id] = Fixture.Item("video-short.json", id);

        public void AddPremiere(string id, string start) => Videos[id] = Fixture.Item("video-premiere.json", id, start);

        public void AddEndedPremiere(string id, string start) => Videos[id] = Fixture.Item("video-premiere-ended.json", id, start);

        public void AddLive(string id, string start) => Videos[id] = Fixture.Item("video-live.json", id, start);

        public void AddEnded(string id, string start) => Videos[id] = Fixture.Item("video-ended.json", id, start);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var recorded = new Recorded(uri.Segments[^1], uri,
                request.Headers.TryGetValues("X-Goog-Api-Key", out var keys) ? keys.Single() : null,
                HttpUtility.ParseQueryString(uri.Query));
            Requests.Add(recorded);

            var reply = Intercept?.Invoke(recorded) ?? Answer(recorded);
            return Task.FromResult(new HttpResponseMessage(reply.Status)
            {
                Content = reply.Stall ? new StalledContent() : new StringContent(reply.Body, Encoding.UTF8, "application/json"),
            });
        }

        private Reply Answer(Recorded request)
        {
            switch (request.Endpoint)
            {
                case "playlistItems":
                    var all = Uploads.GetValueOrDefault(request.Query["playlistId"]!) ?? [];
                    var page = (request.Query["pageToken"] is { } token ? int.Parse(token[1..]) : 1) - 1;
                    var ids = all.Skip(page * PageSize).Take(PageSize);
                    var next = (page + 1) * PageSize < all.Count ? $"\n  \"nextPageToken\": \"p{page + 2}\"," : "";
                    return Reply.Ok(Fixture.Wrap("playlist-items-page.json", ids.Select(id => Fixture.Item("playlist-item.json", id)), next));

                case "videos":
                    var found = request.Ids.Where(Videos.ContainsKey).Select(id => Videos[id]);
                    return Reply.Ok(Fixture.Wrap("videos-response.json", found));

                case "search":
                    var upcoming = Upcoming.GetValueOrDefault(request.Query["channelId"]!) ?? [];
                    return Reply.Ok(Fixture.Wrap("search-response.json", upcoming.Select(id => Fixture.Item("search-item.json", id))));

                default:
                    throw new InvalidOperationException($"Unexpected request to {request.Endpoint}.");
            }
        }
    }
}
