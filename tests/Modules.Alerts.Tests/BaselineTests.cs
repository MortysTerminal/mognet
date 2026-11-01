using Microsoft.Extensions.Logging;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.YouTube;

namespace Mognet.Modules.Alerts.Tests
{
    public class BaselineTests : IDisposable
    {
        private PollHarness _h = null!;

        public void Dispose() => _h?.Dispose();

        private async Task<PollHarness> HarnessAsync(int videos = 3)
        {
            _h = await PollHarness.CreateAsync();
            var ids = Enumerable.Range(1, videos).Select(i => $"vid{i:D3}").ToList();
            foreach (var id in ids)
                _h.Http.AddOrdinary(id);
            _h.Http.Uploads[PollHarness.Uploads] = ids;
            return _h;
        }

        [Fact]
        public async Task The_first_run_stores_everything_and_reports_nothing_as_new()
        {
            var h = await HarnessAsync();
            var source = await h.AddSourceAsync();

            await h.NewService().RunCycleAsync();

            Assert.Equal(["vid001", "vid002", "vid003"], (await h.KnownAsync(source.Id)).Select(k => k.ContentId).Order());
            Assert.Empty(h.ChangeLines);
            Assert.True((await h.Store.GetSourceAsync(source.Id))!.BaselineRecorded);
        }

        [Fact]
        public async Task The_baseline_takes_every_page_of_the_uploads_and_one_search()
        {
            var h = await HarnessAsync(videos: 120);
            h.Http.PageSize = 50;
            h.Http.Upcoming[PollHarness.ChannelId] = ["streamA"];
            h.Http.AddUpcoming("streamA", "2026-04-01T18:00:00Z");
            var source = await h.AddSourceAsync();

            await h.NewService().RunCycleAsync();

            Assert.Equal(3, h.Http.To("playlistItems").Count());
            Assert.Single(h.Http.To("search"));
            var known = await h.KnownAsync(source.Id);
            Assert.Equal(121, known.Count);
            Assert.Equal(LiveState.Upcoming, known.Single(k => k.ContentId == "streamA").LiveState);
            // 121 ids need three videos calls, none above the limit.
            Assert.Equal(3, h.Http.To("videos").Count());
            Assert.All(h.Http.To("videos"), r => Assert.True(r.Ids.Length <= 50));
        }

        [Fact]
        public async Task An_old_video_that_moves_onto_the_first_page_is_not_new_after_the_baseline()
        {
            var h = await HarnessAsync(videos: 120);
            h.Http.PageSize = 100;
            var source = await h.AddSourceAsync();
            var service = h.NewService();
            await service.RunCycleAsync();

            // The newest video is deleted, so the oldest one slides into the first page of 50.
            h.Http.Uploads[PollHarness.Uploads].RemoveAt(0);
            h.Http.PageSize = 50;
            await service.RunCycleAsync();

            Assert.Empty(h.ChangeLines);
        }

        [Fact]
        public async Task A_failing_videos_call_leaves_the_marker_unset_and_stores_nothing_then_the_repeat_completes()
        {
            var h = await HarnessAsync(videos: 120);
            var source = await h.AddSourceAsync();
            var service = h.NewService();
            var videoCalls = 0;
            h.Http.Intercept = r => r.Endpoint == "videos" && ++videoCalls == 2 ? Reply.QuotaExceeded() : null;

            await service.RunCycleAsync();

            Assert.False((await h.Store.GetSourceAsync(source.Id))!.BaselineRecorded);
            Assert.Empty(await h.KnownAsync(source.Id));

            h.Http.Intercept = null;
            await service.RunCycleAsync();

            Assert.True((await h.Store.GetSourceAsync(source.Id))!.BaselineRecorded);
            Assert.Equal(120, (await h.KnownAsync(source.Id)).Count);
            Assert.Empty(h.ChangeLines);
        }

        [Fact]
        public async Task A_failing_second_playlist_page_leaves_the_marker_unset()
        {
            var h = await HarnessAsync(videos: 120);
            h.Http.PageSize = 50;
            var source = await h.AddSourceAsync();
            h.Http.Intercept = r => r.Endpoint == "playlistItems" && r.Query["pageToken"] == "p2" ? Reply.Malformed() : null;

            await h.NewService().RunCycleAsync();

            Assert.False((await h.Store.GetSourceAsync(source.Id))!.BaselineRecorded);
            Assert.Empty(await h.KnownAsync(source.Id));
        }

        [Fact]
        public async Task A_failing_search_leaves_the_marker_unset()
        {
            var h = await HarnessAsync();
            var source = await h.AddSourceAsync();
            h.Http.Intercept = r => r.Endpoint == "search" ? Reply.QuotaExceeded() : null;

            await h.NewService().RunCycleAsync();

            Assert.False((await h.Store.GetSourceAsync(source.Id))!.BaselineRecorded);
            Assert.Empty(await h.KnownAsync(source.Id));
        }

        [Fact]
        public async Task The_baseline_stops_after_200_pages_fails_and_logs()
        {
            var h = await HarnessAsync();
            var source = await h.AddSourceAsync();
            h.Http.Intercept = r => r.Endpoint == "playlistItems"
                ? Reply.Ok(Fixture.Wrap("playlist-items-page.json", [Fixture.Item("playlist-item.json", "vid001")], "\n  \"nextPageToken\": \"more\","))
                : null;

            await h.NewService().RunCycleAsync();

            Assert.Equal(YouTubeProvider.MaxBaselinePages, h.Http.To("playlistItems").Count());
            Assert.Empty(h.Http.To("videos"));
            Assert.False((await h.Store.GetSourceAsync(source.Id))!.BaselineRecorded);
            Assert.Contains(h.Log.At(LogLevel.Error), m => m.Contains("more than 200 pages"));
        }

        [Fact]
        public async Task A_channel_with_exactly_200_pages_still_completes()
        {
            var h = await HarnessAsync(videos: 200);
            h.Http.PageSize = 1;
            var source = await h.AddSourceAsync();

            await h.NewService().RunCycleAsync();

            Assert.True((await h.Store.GetSourceAsync(source.Id))!.BaselineRecorded);
            Assert.Equal(200, (await h.KnownAsync(source.Id)).Count);
        }

        [Fact]
        public async Task A_baseline_that_fails_at_the_start_is_repeated_in_full_by_a_restarted_service()
        {
            var h = await HarnessAsync();
            var source = await h.AddSourceAsync();
            h.Http.Intercept = _ => Reply.QuotaExceeded();
            await h.NewService().RunCycleAsync();
            h.Http.Intercept = null;

            await h.NewService().RunCycleAsync();

            Assert.True((await h.Store.GetSourceAsync(source.Id))!.BaselineRecorded);
            Assert.Equal(3, (await h.KnownAsync(source.Id)).Count);
            Assert.Empty(h.ChangeLines);
        }
    }
}
