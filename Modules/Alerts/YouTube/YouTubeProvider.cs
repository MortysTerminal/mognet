using System.Net;
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
    /// <para>
    /// Types come from the live details where they decide alone, otherwise from the first page of the channel's
    /// <c>UUSH</c>, <c>UULF</c> and <c>UULV</c> playlists. Those three are read at most once per source and cycle, and
    /// only when an item of that cycle needs them.
    /// </para>
    /// </summary>
    public sealed class YouTubeProvider : IContentProvider
    {
        public const string PlatformName = "youtube";

        /// <summary>The first read of a channel stops after this many playlist pages.</summary>
        public const int MaxBaselinePages = 200;

        /// <summary>Known content is read again once its last refresh is older than this.</summary>
        public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(25);

        /// <summary>An item that is still unclassified is checked again every cycle until it is this old.</summary>
        public static readonly TimeSpan RecheckWindow = TimeSpan.FromHours(24);

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

                // No playlist membership is read here: these items existed before tracking started and are never
                // alerted, so their type stays null. A stream that is still scheduled or live is the exception; its
                // live details decide alone and it keeps being tracked.
                var items = new List<ContentItem>();
                foreach (var chunk in ids.Chunk(YouTubeApi.MaxIdsPerCall))
                    items.AddRange((await _api.GetVideosAsync(chunk, cancellationToken)).Select(BaselineItem));

                return new SourcePoll(items, [], Complete: true);
            }
            catch (Exception ex) when (IsApiFailure(ex, cancellationToken))
            {
                _logger.LogWarning("The first read of YouTube source {SourceId} failed ({Failure}) and is repeated.", source.Id, Describe(ex));
                return SourcePoll.Incomplete;
            }
        }

        private static ContentItem BaselineItem(YouTubeVideo video) =>
            YouTubeClassifier.DependsOnMembership(video.Facts) ? video.Item : video.Item with { Type = YouTubeClassifier.Classify(video.Facts) };

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
                .Where(k => k.LiveState is LiveState.Upcoming or LiveState.Live || k.LastRefreshedAt < staleBefore || IsOpenForClassification(k, now))
                .Select(k => k.ContentId));

            var membership = new MembershipReader(_api, _logger, source, uploads, cancellationToken);

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

                    foreach (var video in answer)
                        items.Add(await ClassifyAsync(video, knownById.GetValueOrDefault(video.Item.ContentId), membership, now));

                    var returned = answer.Select(a => a.Item.ContentId).ToHashSet(StringComparer.Ordinal);
                    gone.AddRange(chunk.Where(id => !returned.Contains(id) && knownById.ContainsKey(id)));
                }, cancellationToken);
            }

            return new SourcePoll(items, gone, Complete: true);
        }

        // Still to be classified: unclassified and younger than the window, or a stream whose type is a stage before
        // the end although it has ended (its membership read failed).
        private static bool IsOpenForClassification(KnownContent k, DateTimeOffset now) =>
            (k.Type == ContentType.Unclassified && now - k.FirstSeenAt < RecheckWindow) ||
            (k.Type is ContentType.StreamScheduled or ContentType.StreamLive && k.LiveState == LiveState.Ended);

        // The playlist lists are read only for an item they can decide: a new one, one that was a stream a moment
        // ago, or one still open for classification. A refresh of an ordinary known item keeps its type.
        private static bool NeedsMembership(ClassificationFacts facts, KnownContent? known, DateTimeOffset now) =>
            YouTubeClassifier.DependsOnMembership(facts) &&
            (known is null ||
             known.LiveState is LiveState.Upcoming or LiveState.Live ||
             known.Type is ContentType.StreamScheduled or ContentType.StreamLive ||
             (known.Type == ContentType.Unclassified && now - known.FirstSeenAt < RecheckWindow));

        // A known item whose lists could not all be read keeps its stored type: a failed read changes nothing. A new
        // item takes the classifier's answer, which is unclassified unless the lists it did read are conclusive.
        private async Task<ContentItem> ClassifyAsync(YouTubeVideo video, KnownContent? known, MembershipReader membership, DateTimeOffset now)
        {
            if (!NeedsMembership(video.Facts, known, now))
                return video.Item with { Type = YouTubeClassifier.DependsOnMembership(video.Facts) ? null : YouTubeClassifier.Classify(video.Facts) };

            var lists = await membership.GetAsync();
            var facts = video.Facts with
            {
                InShortsList = lists.Shorts.Of(video.Item.ContentId),
                InVideosList = lists.Videos.Of(video.Item.ContentId),
                InStreamsList = lists.Streams.Of(video.Item.ContentId),
            };
            var type = known is not null && lists.AnyUnknown ? (ContentType?)null : YouTubeClassifier.Classify(facts);
            return video.Item with { Type = type };
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

        private sealed record Memberships(IReadOnlySet<string>? Shorts, IReadOnlySet<string>? Videos, IReadOnlySet<string>? Streams)
        {
            public bool AnyUnknown => Shorts is null || Videos is null || Streams is null;
        }

        /// <summary>Reads the three lists once, on first use, and hands the same answer to every later item of the cycle.</summary>
        private sealed class MembershipReader(YouTubeApi api, ILogger logger, AlertSource source, string uploads, CancellationToken cancellationToken)
        {
            private Task<Memberships>? _read;

            public Task<Memberships> GetAsync() => _read ??= ReadAsync();

            private async Task<Memberships> ReadAsync()
            {
                var suffix = uploads[2..];
                return new Memberships(
                    await ReadListAsync("UUSH" + suffix),
                    await ReadListAsync("UULF" + suffix),
                    await ReadListAsync("UULV" + suffix));
            }

            // The channel having no such content answers 404 playlistNotFound: then membership is known and empty.
            // Any other failure leaves the list unknown.
            private async Task<IReadOnlySet<string>?> ReadListAsync(string playlistId)
            {
                try
                {
                    var page = await api.ListPlaylistPageAsync(playlistId, pageToken: null, cancellationToken);
                    return page.VideoIds.ToHashSet(StringComparer.Ordinal);
                }
                catch (YouTubeApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.Reason == "playlistNotFound")
                {
                    return new HashSet<string>(StringComparer.Ordinal);
                }
                catch (Exception ex) when (IsApiFailure(ex, cancellationToken))
                {
                    logger.LogWarning("YouTube membership call for source {SourceId} failed ({Failure}); the list counts as unknown.", source.Id, Describe(ex));
                    return null;
                }
            }
        }
    }
}
