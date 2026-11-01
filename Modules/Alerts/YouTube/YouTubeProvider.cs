using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>
    /// Reads YouTube channels. A channel's uploads playlist is its channel id with the <c>UC</c> prefix replaced by
    /// <c>UU</c>. Listings only supply ids; everything stored comes from <c>videos.list</c>. Every call may fail:
    /// the failure is logged with the status and reason only, the result simply lacks that step's data, and the next
    /// cycle repeats it.
    /// </summary>
    public sealed class YouTubeProvider : IContentProvider
    {
        public const string PlatformName = "youtube";

        /// <summary>The first read of a channel stops after this many playlist pages.</summary>
        public const int MaxBaselinePages = 200;

        /// <summary>Known content is read again once its last refresh is older than this.</summary>
        public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(25);

        private readonly YouTubeApi _api;
        private readonly QuotaCounter _quota;
        private readonly SearchSchedule _search;
        private readonly ILogger<YouTubeProvider> _logger;
        private readonly TimeProvider _time;

        public YouTubeProvider(YouTubeApi api, QuotaCounter quota, ILogger<YouTubeProvider> logger, TimeProvider? time = null)
        {
            _api = api;
            _quota = quota;
            _logger = logger;
            _time = time ?? TimeProvider.System;
            _search = new SearchSchedule(logger);
        }

        public string Platform => PlatformName;

        public void BeginCycle(CycleContext context)
        {
            _quota.StartCycle();
            _search.Apply(context.DiscoveryIntervalMinutes, context.SourceCount);
        }

        public void EndCycle() =>
            _logger.LogInformation(
                "YouTube quota use: this cycle units={CycleUnits} search_calls={CycleSearchCalls}; today (UTC) units={DayUnits} search_calls={DaySearchCalls}.",
                _quota.CycleUnits, _quota.CycleSearchCalls, _quota.DayUnits, _quota.DaySearchCalls);

        public async Task<SourcePoll> PollAsync(AlertSource source, IReadOnlyList<KnownContent> known, CancellationToken cancellationToken)
        {
            if (!source.ExternalId.StartsWith("UC", StringComparison.Ordinal) || source.ExternalId.Length < 3)
            {
                _logger.LogWarning("YouTube source {SourceId} has a channel id that does not start with UC; it is skipped.", source.Id);
                return source.BaselineRecorded ? new SourcePoll([], [], Complete: true) : SourcePoll.Incomplete;
            }

            var uploads = "UU" + source.ExternalId[2..];
            return source.BaselineRecorded
                ? await ReadAsync(source, uploads, known, cancellationToken)
                : await ReadBaselineAsync(source, uploads, cancellationToken);
        }

        // The first read takes every page of the uploads playlist and one search, then reads all of it through
        // videos.list. Anything failing returns Incomplete, so nothing is stored and the whole read is repeated.
        // The search is marked when it is sent, so a read that fails after it waits for the search interval and
        // cannot spend the daily search calls; a read that fails before it is repeated at once.
        private async Task<SourcePoll> ReadBaselineAsync(AlertSource source, string uploads, CancellationToken cancellationToken)
        {
            var now = _time.GetUtcNow();
            if (!_search.IsDue(source.Id, now))
            {
                _logger.LogDebug("The first read of YouTube source {SourceId} waits for the search interval.", source.Id);
                return SourcePoll.Incomplete;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                string? pageToken = null;
                var pages = 0;
                do
                {
                    if (pages == MaxBaselinePages)
                    {
                        _logger.LogError("The uploads playlist of YouTube source {SourceId} has more than {Pages} pages; the first read failed and is repeated.", source.Id, MaxBaselinePages);
                        return SourcePoll.Incomplete;
                    }

                    var page = await _api.ListPlaylistPageAsync(uploads, pageToken, cancellationToken);
                    pages++;
                    ids.UnionWith(page.VideoIds);
                    pageToken = page.NextPageToken;
                }
                while (pageToken is not null);

                _search.MarkSearched(source.Id, now);
                ids.UnionWith(await _api.SearchUpcomingAsync(source.ExternalId, cancellationToken));

                var items = new List<ContentItem>();
                foreach (var chunk in ids.Chunk(YouTubeApi.MaxIdsPerCall))
                    items.AddRange(await _api.GetVideosAsync(chunk, cancellationToken));

                return new SourcePoll(items, [], Complete: true);
            }
            catch (Exception ex) when (IsApiFailure(ex, cancellationToken))
            {
                _logger.LogWarning("The first read of YouTube source {SourceId} failed ({Failure}) and is repeated.", source.Id, Describe(ex));
                return SourcePoll.Incomplete;
            }
        }

        private async Task<SourcePoll> ReadAsync(AlertSource source, string uploads, IReadOnlyList<KnownContent> known, CancellationToken cancellationToken)
        {
            var now = _time.GetUtcNow();
            var knownById = known.ToDictionary(k => k.ContentId, StringComparer.Ordinal);

            // New ids: first page of the uploads playlist, then the scheduled streams the search finds. A failing
            // call is logged and skipped; the other path and the known items still run.
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            await TryAsync(source, "playlist", async () =>
            {
                var page = await _api.ListPlaylistPageAsync(uploads, pageToken: null, cancellationToken);
                wanted.UnionWith(page.VideoIds.Where(id => !knownById.ContainsKey(id)));
            }, cancellationToken);

            if (_search.IsDue(source.Id, now))
            {
                _search.MarkSearched(source.Id, now);
                await TryAsync(source, "search", async () =>
                {
                    var found = await _api.SearchUpcomingAsync(source.ExternalId, cancellationToken);
                    wanted.UnionWith(found.Where(id => !knownById.ContainsKey(id)));
                }, cancellationToken);
            }

            // Known items that must be read again: streams that have not ended, and items about to pass the
            // storage limit.
            var staleBefore = now - RefreshAfter;
            wanted.UnionWith(known
                .Where(k => k.LiveState is LiveState.Upcoming or LiveState.Live || k.LastRefreshedAt < staleBefore)
                .Select(k => k.ContentId));

            var items = new List<ContentItem>();
            var gone = new List<string>();
            foreach (var chunk in wanted.Order(StringComparer.Ordinal).Chunk(YouTubeApi.MaxIdsPerCall))
            {
                await TryAsync(source, "videos", async () =>
                {
                    var answer = await _api.GetVideosAsync(chunk, cancellationToken);

                    // An empty answer for several ids is more likely a fault on YouTube's side than that all of
                    // them vanished, so nothing is deleted on it.
                    if (answer.Count == 0 && chunk.Length > 1)
                    {
                        _logger.LogWarning("YouTube returned no videos for {Count} requested ids of source {SourceId}; nothing is deleted.", chunk.Length, source.Id);
                        return;
                    }

                    items.AddRange(answer);
                    var returned = answer.Select(a => a.ContentId).ToHashSet(StringComparer.Ordinal);
                    gone.AddRange(chunk.Where(id => !returned.Contains(id) && knownById.ContainsKey(id)));
                }, cancellationToken);
            }

            return new SourcePoll(items, gone, Complete: true);
        }

        private async Task TryAsync(AlertSource source, string step, Func<Task> call, CancellationToken cancellationToken)
        {
            try
            {
                await call();
            }
            catch (Exception ex) when (IsApiFailure(ex, cancellationToken))
            {
                _logger.LogWarning("YouTube {Step} call for source {SourceId} failed ({Failure}); it is repeated in a later cycle.", step, source.Id, Describe(ex));
            }
        }

        // Cancelling the service is not a failed call; everything else a call can throw is.
        private static bool IsApiFailure(Exception ex, CancellationToken cancellationToken) =>
            !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested);

        // Status and reason or the kind of failure only. Exception messages and response bodies stay out of the log.
        internal static string Describe(Exception ex) => ex switch
        {
            YouTubeApiException api => api.Message,
            JsonException => "malformed answer",
            OperationCanceledException => "timeout",
            HttpRequestException => "request failed",
            _ => ex.GetType().Name,
        };
    }
}
