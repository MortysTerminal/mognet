using System.Net;
using Microsoft.Extensions.Logging;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.YouTube;

namespace Mognet.Modules.Alerts.Tests
{
    public class YouTubeApiTests
    {
        private readonly StubYouTube _http = new();
        private readonly QuotaCounter _quota = new(new TestClock(PollHarness.Start));

        private YouTubeApi Api() => new(new HttpClient(_http), PollHarness.ApiKey, _quota);

        [Fact]
        public async Task The_key_is_sent_in_the_header_and_never_in_a_url()
        {
            _http.AddOrdinary("vid001");
            _http.Uploads[PollHarness.Uploads] = ["vid001"];
            var api = Api();

            await api.ListPlaylistPageAsync(PollHarness.Uploads, "p1", default);
            await api.GetVideosAsync(["vid001"], default);
            await api.SearchUpcomingAsync(PollHarness.ChannelId, default);

            Assert.Equal(3, _http.Requests.Count);
            Assert.All(_http.Requests, r =>
            {
                Assert.Equal(PollHarness.ApiKey, r.ApiKey);
                Assert.DoesNotContain(PollHarness.ApiKey, r.Uri.OriginalString);
                Assert.DoesNotContain("key=", r.Uri.Query, StringComparison.OrdinalIgnoreCase);
            });
        }

        [Fact]
        public async Task The_playlist_call_asks_for_content_details_and_50_items()
        {
            _http.Uploads[PollHarness.Uploads] = ["vid001"];

            await Api().ListPlaylistPageAsync(PollHarness.Uploads, null, default);

            var request = Assert.Single(_http.Requests);
            Assert.Equal("playlistItems", request.Endpoint);
            Assert.Equal("contentDetails", request.Query["part"]);
            Assert.Equal("50", request.Query["maxResults"]);
            Assert.Equal(PollHarness.Uploads, request.Query["playlistId"]);
            Assert.Null(request.Query["pageToken"]);
        }

        [Fact]
        public async Task The_videos_call_asks_for_the_four_parts_and_joins_the_ids()
        {
            _http.AddOrdinary("vid001");
            _http.AddOrdinary("vid002");

            await Api().GetVideosAsync(["vid001", "vid002"], default);

            var request = Assert.Single(_http.Requests);
            Assert.Equal("snippet,contentDetails,status,liveStreamingDetails", request.Query["part"]);
            Assert.Equal("vid001,vid002", request.Query["id"]);
        }

        [Fact]
        public async Task The_search_call_asks_for_upcoming_videos_of_the_channel()
        {
            await Api().SearchUpcomingAsync(PollHarness.ChannelId, default);

            var request = Assert.Single(_http.Requests);
            Assert.Equal("search", request.Endpoint);
            Assert.Equal("id", request.Query["part"]);
            Assert.Equal(PollHarness.ChannelId, request.Query["channelId"]);
            Assert.Equal("video", request.Query["type"]);
            Assert.Equal("upcoming", request.Query["eventType"]);
            Assert.Equal("50", request.Query["maxResults"]);
        }

        [Fact]
        public async Task More_than_50_ids_in_one_call_are_refused_before_anything_is_sent()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Api().GetVideosAsync(Enumerable.Range(0, 51).Select(i => $"v{i}").ToList(), default));
            await Assert.ThrowsAsync<ArgumentException>(() => Api().GetVideosAsync([], default));

            Assert.Empty(_http.Requests);
        }

        [Fact]
        public async Task An_error_answer_throws_with_status_and_reason_and_none_of_the_body()
        {
            _http.Intercept = _ => Reply.QuotaExceeded();

            var ex = await Assert.ThrowsAsync<YouTubeApiException>(() => Api().GetVideosAsync(["vid001"], default));

            Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.Equal("quotaExceeded", ex.Reason);
            Assert.Equal("HTTP 403 quotaExceeded", ex.Message);
            Assert.DoesNotContain("Do not log this text", ex.ToString());
        }

        [Fact]
        public async Task An_error_answer_without_a_readable_reason_still_throws_with_the_status()
        {
            _http.Intercept = _ => new Reply(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

            var ex = await Assert.ThrowsAsync<YouTubeApiException>(() => Api().SearchUpcomingAsync(PollHarness.ChannelId, default));

            Assert.Equal("HTTP 502", ex.Message);
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("[]")]
        [InlineData("{\"kind\":\"youtube#videoListResponse\"}")]
        [InlineData("{\"items\":[{\"kind\":\"youtube#video\"}]}")]
        public async Task A_malformed_answer_throws_a_json_error(string body)
        {
            _http.Intercept = _ => Reply.Ok(body);

            await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => Api().GetVideosAsync(["vid001"], default));
        }

        [Fact]
        public async Task A_playlist_item_without_a_video_id_is_malformed()
        {
            _http.Intercept = _ => Reply.Ok("{\"items\":[{\"contentDetails\":{}}]}");

            await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => Api().ListPlaylistPageAsync(PollHarness.Uploads, null, default));
        }

        [Fact]
        public async Task A_call_counts_one_unit_and_a_search_counts_one_search_call_only()
        {
            _http.Uploads[PollHarness.Uploads] = [];
            var api = Api();

            await api.ListPlaylistPageAsync(PollHarness.Uploads, null, default);
            await api.GetVideosAsync(["vid001"], default);
            await api.SearchUpcomingAsync(PollHarness.ChannelId, default);

            Assert.Equal(2, _quota.CycleUnits);
            Assert.Equal(1, _quota.CycleSearchCalls);
        }

        [Fact]
        public async Task A_failed_call_still_counts()
        {
            _http.Intercept = _ => Reply.QuotaExceeded();

            await Assert.ThrowsAsync<YouTubeApiException>(() => Api().SearchUpcomingAsync(PollHarness.ChannelId, default));

            Assert.Equal(1, _quota.CycleSearchCalls);
        }

        [Fact]
        public void An_empty_key_is_refused()
        {
            Assert.Throws<ArgumentException>(() => new YouTubeApi(new HttpClient(_http), " ", _quota));
        }

        // Video parsing: state and start come from the live fields; publishedAt is never used.

        private async Task<ContentItem> ParseAsync(string item)
        {
            _http.Intercept = _ => Reply.Ok(Fixture.Wrap("videos-response.json", [item]));
            return Assert.Single(await Api().GetVideosAsync(["x"], default)).Item;
        }

        [Fact]
        public async Task An_ordinary_video_has_neither_start_nor_state()
        {
            Assert.Equal(new ContentItem("vid001", null, null), await ParseAsync(Fixture.Item("video-ordinary.json", "vid001")));
        }

        [Fact]
        public async Task A_scheduled_stream_is_upcoming_with_its_scheduled_start_not_its_published_time()
        {
            var item = await ParseAsync(Fixture.Item("video-upcoming.json", "str001", "2026-04-01T18:00:00Z"));

            Assert.Equal(new ContentItem("str001", new DateTimeOffset(2026, 4, 1, 18, 0, 0, TimeSpan.Zero), LiveState.Upcoming), item);
        }

        [Fact]
        public async Task A_running_stream_is_live()
        {
            var item = await ParseAsync(Fixture.Item("video-live.json", "str001", "2026-04-01T18:00:00Z"));

            Assert.Equal(LiveState.Live, item.LiveState);
        }

        [Fact]
        public async Task An_ended_stream_is_ended_by_its_actual_end_time()
        {
            var item = await ParseAsync(Fixture.Item("video-ended.json", "str001", "2026-04-01T18:00:00Z"));

            Assert.Equal(LiveState.Ended, item.LiveState);
            Assert.Equal(new DateTimeOffset(2026, 4, 1, 18, 0, 0, TimeSpan.Zero), item.ScheduledStart);
        }

        [Fact]
        public async Task A_moved_published_time_changes_nothing_for_an_ordinary_video_or_a_stream()
        {
            var ordinary = Fixture.Item("video-ordinary.json", "vid001").Replace("2026-01-05T10:00:00Z", "2030-01-01T00:00:00Z");
            var stream = Fixture.Item("video-upcoming.json", "str001", "2026-04-01T18:00:00Z").Replace("2026-01-02T08:15:00Z", "2030-01-01T00:00:00Z");

            Assert.Null((await ParseAsync(ordinary)).ScheduledStart);
            Assert.Equal(new DateTimeOffset(2026, 4, 1, 18, 0, 0, TimeSpan.Zero), (await ParseAsync(stream)).ScheduledStart);
        }

        private async Task<ClassificationFacts> FactsAsync(string item)
        {
            _http.Intercept = _ => Reply.Ok(Fixture.Wrap("videos-response.json", [item]));
            return Assert.Single(await Api().GetVideosAsync(["x"], default)).Facts;
        }

        [Fact]
        public async Task The_facts_of_an_ordinary_video_are_read_as_they_are()
        {
            Assert.Equal(new ClassificationFacts("none", false, false, false, false, "PT12M30S"), await FactsAsync(Fixture.Item("video-ordinary.json", "vid001")));
        }

        [Fact]
        public async Task The_facts_of_a_scheduled_stream_a_running_stream_and_an_ended_stream()
        {
            Assert.Equal(new ClassificationFacts("upcoming", true, true, false, false, "P0D"), await FactsAsync(Fixture.Item("video-upcoming.json", "s1")));
            Assert.Equal(new ClassificationFacts("live", true, true, true, false, "P0D"), await FactsAsync(Fixture.Item("video-live.json", "s1")));
            Assert.Equal(new ClassificationFacts("none", true, true, true, true, "PT3H1M"), await FactsAsync(Fixture.Item("video-ended.json", "s1")));
        }

        [Fact]
        public async Task A_value_or_a_field_the_documentation_does_not_list_is_kept_or_missing_not_mapped()
        {
            var odd = Fixture.Item("video-ordinary.json", "vid001").Replace("\"liveBroadcastContent\": \"none\"", "\"liveBroadcastContent\": \"paused\"");
            var missing = Fixture.Item("video-ordinary.json", "vid001").Replace("\"liveBroadcastContent\": \"none\"", "\"x\": 1").Replace("\"duration\": \"PT12M30S\"", "\"x\": 1");

            Assert.Equal("paused", (await FactsAsync(odd)).LiveBroadcastContent);
            var facts = await FactsAsync(missing);
            Assert.Null(facts.LiveBroadcastContent);
            Assert.Null(facts.Duration);
        }

        [Fact]
        public async Task A_start_that_is_not_a_date_makes_the_answer_malformed()
        {
            await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => ParseAsync(Fixture.Item("video-upcoming.json", "str001", "soon")));
        }
    }
}
