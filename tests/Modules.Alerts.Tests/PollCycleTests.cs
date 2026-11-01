using Microsoft.Extensions.Logging;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Services;
using Mognet.Modules.Alerts.Storage;
using Mognet.Modules.Alerts.YouTube;

namespace Mognet.Modules.Alerts.Tests
{
    public class PollCycleTests : IDisposable
    {
        private static readonly DateTimeOffset StreamStart = new(2026, 4, 1, 18, 0, 0, TimeSpan.Zero);

        private PollHarness _h = null!;
        private AlertSource _source = null!;

        public void Dispose() => _h?.Dispose();

        // A source with a recorded baseline of three ordinary videos, ready for ordinary cycles.
        private async Task<PollService> ReadyAsync()
        {
            _h = await PollHarness.CreateAsync();
            _h.Http.Uploads[PollHarness.Uploads] = ["vid003", "vid002", "vid001"];
            foreach (var id in _h.Http.Uploads[PollHarness.Uploads])
                _h.Http.AddOrdinary(id);
            _source = await _h.AddSourceAsync();
            var service = _h.NewService();
            await service.RunCycleAsync();
            _h.Http.Requests.Clear();
            _h.Log.Entries.Clear();
            _h.Clock.Advance(TimeSpan.FromMinutes(5));
            return service;
        }

        private async Task<KnownContent> KnownAsync(string id) => (await _h.Store.GetKnownContentAsync(_source.Id, id))!;

        [Fact]
        public async Task A_new_video_on_the_first_page_is_reported_once_with_id_and_kind_and_stored()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "vid004");

            await service.RunCycleAsync();
            await service.RunCycleAsync();

            Assert.Single(_h.ChangeLines);
            Assert.Contains("vid004", _h.ChangeLines.Single());
            Assert.Contains("New", _h.ChangeLines.Single());
            Assert.Equal(_h.Clock.Now, (await KnownAsync("vid004")).FirstSeenAt);
        }

        [Fact]
        public async Task Log_lines_carry_the_id_and_kind_at_information_level_and_no_title()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "vid004");

            await service.RunCycleAsync();

            Assert.Contains(_h.Log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("vid004") && e.Message.Contains("New"));
            Assert.DoesNotContain(_h.Log.Messages, m => m.Contains("Neutral test"));
        }

        [Fact]
        public async Task A_cycle_with_nothing_new_asks_for_one_playlist_page_and_nothing_else()
        {
            var service = await ReadyAsync();

            await service.RunCycleAsync();

            Assert.Equal(["playlistItems"], _h.Http.Requests.Select(r => r.Endpoint));
            Assert.Empty(_h.ChangeLines);
        }

        [Fact]
        public async Task A_known_id_on_the_page_does_not_refresh_the_stored_item()
        {
            var service = await ReadyAsync();
            var before = await KnownAsync("vid001");

            await service.RunCycleAsync();

            Assert.Equal(before, await KnownAsync("vid001"));
        }

        [Fact]
        public async Task Only_data_from_the_videos_call_reaches_the_store()
        {
            var service = await ReadyAsync();
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "str001");

            await service.RunCycleAsync();

            var stored = await KnownAsync("str001");
            Assert.Equal(StreamStart, stored.ScheduledStart);
            Assert.Equal(LiveState.Upcoming, stored.LiveState);
            Assert.Contains(_h.Http.To("videos"), r => r.Ids.Contains("str001"));
        }

        [Fact]
        public async Task A_new_id_that_the_videos_call_does_not_return_is_not_stored()
        {
            var service = await ReadyAsync();
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "private1");

            await service.RunCycleAsync();

            Assert.Null(await _h.Store.GetKnownContentAsync(_source.Id, "private1"));
            Assert.Empty(_h.ChangeLines);
        }

        [Fact]
        public async Task A_scheduled_stream_found_by_the_search_is_stored_and_then_tracked_by_id()
        {
            var service = await ReadyAsync();
            _h.Http.Upcoming[PollHarness.ChannelId] = ["str001"];
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            _h.Clock.Advance(TimeSpan.FromMinutes(25));

            await service.RunCycleAsync();

            Assert.Single(_h.Http.To("search"));
            Assert.Equal(LiveState.Upcoming, (await KnownAsync("str001")).LiveState);
            Assert.Contains("New", _h.ChangeLines.Single());

            // Within the search interval no search runs, but the stream is still read by id and its start-up is seen.
            _h.Http.Requests.Clear();
            _h.Clock.Advance(TimeSpan.FromMinutes(5));
            _h.Http.AddLive("str001", StreamStart.ToString("O"));
            await service.RunCycleAsync();

            Assert.Empty(_h.Http.To("search"));
            Assert.Contains(_h.Http.To("videos"), r => r.Ids.Contains("str001"));
            Assert.Equal(LiveState.Live, (await KnownAsync("str001")).LiveState);
            Assert.Contains(_h.ChangeLines, l => l.Contains("str001") && l.Contains("LiveStateChanged"));
        }

        [Fact]
        public async Task A_stream_that_has_ended_is_no_longer_read_by_id_every_cycle()
        {
            var service = await ReadyAsync();
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "str001");
            await service.RunCycleAsync();
            _h.Http.AddEnded("str001", StreamStart.ToString("O"));
            await service.RunCycleAsync();
            Assert.Equal(LiveState.Ended, (await KnownAsync("str001")).LiveState);
            _h.Http.Requests.Clear();

            await service.RunCycleAsync();

            Assert.Empty(_h.Http.To("videos"));
        }

        [Fact]
        public async Task A_moved_scheduled_start_is_reported_and_stored()
        {
            var service = await ReadyAsync();
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "str001");
            await service.RunCycleAsync();
            _h.Log.Entries.Clear();

            _h.Http.AddUpcoming("str001", StreamStart.AddHours(2).ToString("O"));
            await service.RunCycleAsync();

            Assert.Contains(_h.ChangeLines, l => l.Contains("str001") && l.Contains("ScheduledStartChanged"));
            Assert.Equal(StreamStart.AddHours(2), (await KnownAsync("str001")).ScheduledStart);
        }

        [Fact]
        public async Task A_moved_start_is_detected_across_a_restart()
        {
            var service = await ReadyAsync();
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "str001");
            await service.RunCycleAsync();
            _h.Log.Entries.Clear();

            var restarted = _h.NewService();
            _h.Http.AddUpcoming("str001", StreamStart.AddHours(2).ToString("O"));
            await restarted.RunCycleAsync();

            Assert.Contains(_h.ChangeLines, l => l.Contains("str001") && l.Contains("ScheduledStartChanged"));
            Assert.DoesNotContain(_h.ChangeLines, l => l.Contains("New"));
        }

        [Fact]
        public async Task A_start_and_live_state_that_did_not_change_are_not_reported_after_a_restart()
        {
            var service = await ReadyAsync();
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "str001");
            await service.RunCycleAsync();
            _h.Log.Entries.Clear();

            await _h.NewService().RunCycleAsync();

            Assert.Empty(_h.ChangeLines);
        }

        [Fact]
        public async Task A_restart_reports_no_known_item_as_new()
        {
            await ReadyAsync();
            var restarted = _h.NewService();

            await restarted.RunCycleAsync();

            Assert.Empty(_h.ChangeLines);
        }

        [Fact]
        public async Task New_content_is_detected_across_a_restart()
        {
            await ReadyAsync();
            var restarted = _h.NewService();
            _h.Http.AddOrdinary("vid004");
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "vid004");

            await restarted.RunCycleAsync();

            Assert.Contains("vid004", Assert.Single(_h.ChangeLines));
        }

        [Fact]
        public async Task An_item_not_refreshed_for_25_days_is_read_again_and_its_refresh_time_moves()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromDays(25) + TimeSpan.FromMinutes(1));

            await service.RunCycleAsync();

            Assert.Equal(["vid001", "vid002", "vid003"], _h.Http.To("videos").Single().Ids.Order());
            Assert.All(await _h.KnownAsync(_source.Id), k => Assert.Equal(_h.Clock.Now, k.LastRefreshedAt));
            Assert.Empty(_h.ChangeLines);
        }

        [Fact]
        public async Task An_item_refreshed_24_days_ago_is_left_alone()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromDays(24));

            await service.RunCycleAsync();

            Assert.Empty(_h.Http.To("videos"));
        }

        [Fact]
        public async Task An_id_missing_from_a_successful_answer_is_deleted_and_logged()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromDays(26));
            _h.Http.Videos.Remove("vid002");

            await service.RunCycleAsync();

            Assert.Null(await _h.Store.GetKnownContentAsync(_source.Id, "vid002"));
            Assert.NotNull(await _h.Store.GetKnownContentAsync(_source.Id, "vid001"));
            Assert.NotNull(await _h.Store.GetKnownContentAsync(_source.Id, "vid003"));
            Assert.Contains(_h.Log.Messages, m => m.Contains("vid002") && m.Contains("removed"));
        }

        [Fact]
        public async Task A_single_id_with_an_empty_answer_is_deleted()
        {
            var service = await ReadyAsync();
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "str001");
            await service.RunCycleAsync();
            _h.Http.Videos.Remove("str001");

            await service.RunCycleAsync();

            Assert.Null(await _h.Store.GetKnownContentAsync(_source.Id, "str001"));
        }

        [Fact]
        public async Task An_empty_answer_for_several_ids_deletes_nothing_and_logs_a_warning()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromDays(26));
            _h.Http.Videos.Clear();

            await service.RunCycleAsync();

            Assert.Equal(3, (await _h.KnownAsync(_source.Id)).Count);
            Assert.Contains(_h.Log.At(LogLevel.Warning), m => m.Contains("returned no videos") && m.Contains("nothing is deleted"));
        }

        [Fact]
        public async Task An_answer_with_some_items_for_several_ids_still_deletes_the_missing_ones()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromDays(26));
            _h.Http.Videos.Remove("vid001");
            _h.Http.Videos.Remove("vid002");

            await service.RunCycleAsync();

            Assert.Equal(["vid003"], (await _h.KnownAsync(_source.Id)).Select(k => k.ContentId));
        }

        [Fact]
        public async Task A_failed_videos_call_deletes_nothing()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromDays(26));
            _h.Http.Videos.Remove("vid002");
            _h.Http.Intercept = r => r.Endpoint == "videos" ? Reply.QuotaExceeded() : null;

            await service.RunCycleAsync();

            Assert.Equal(3, (await _h.KnownAsync(_source.Id)).Count);
        }

        [Fact]
        public async Task More_than_50_ids_to_read_are_split_into_calls_of_at_most_50()
        {
            _h = await PollHarness.CreateAsync();
            var ids = Enumerable.Range(1, 120).Select(i => $"vid{i:D3}").ToList();
            foreach (var id in ids)
                _h.Http.AddOrdinary(id);
            _h.Http.Uploads[PollHarness.Uploads] = ids.Take(1).ToList();
            _source = await _h.AddSourceAsync(baselineRecorded: true);
            foreach (var id in ids)
                await _h.Store.UpsertKnownContentAsync(_source.Id, id, null, _h.Clock.Now);
            _h.Clock.Advance(TimeSpan.FromDays(26));

            await _h.NewService().RunCycleAsync();

            var calls = _h.Http.To("videos").ToList();
            Assert.Equal([50, 50, 20], calls.Select(c => c.Ids.Length));
            Assert.Equal(120, calls.SelectMany(c => c.Ids).Distinct().Count());
        }

        [Fact]
        public async Task The_search_runs_again_after_its_own_interval_and_not_before()
        {
            var service = await ReadyAsync();

            await service.RunCycleAsync();
            Assert.Empty(_h.Http.To("search"));

            _h.Clock.Advance(TimeSpan.FromMinutes(25));
            await service.RunCycleAsync();
            Assert.Single(_h.Http.To("search"));
        }

        [Fact]
        public async Task The_search_interval_comes_from_the_setting()
        {
            var service = await ReadyAsync();
            await _h.Settings.SetAsync(AlertsSettings.Scope, AlertsSettings.SearchIntervalMinutes, "60");
            _h.Clock.Advance(TimeSpan.FromMinutes(30));

            await service.RunCycleAsync();
            Assert.Empty(_h.Http.To("search"));

            _h.Clock.Advance(TimeSpan.FromMinutes(30));
            await service.RunCycleAsync();
            Assert.Single(_h.Http.To("search"));
        }

        [Fact]
        public async Task A_search_interval_that_exceeds_the_daily_limit_is_rejected_and_the_last_valid_one_stays()
        {
            var service = await ReadyAsync();
            await _h.Settings.SetAsync(AlertsSettings.Scope, AlertsSettings.SearchIntervalMinutes, "5");
            _h.Clock.Advance(TimeSpan.FromMinutes(30));

            await service.RunCycleAsync();

            Assert.Single(_h.Http.To("search"));
            Assert.Contains(_h.Log.At(LogLevel.Warning), m => m.Contains("search_interval_minutes = 5"));

            _h.Clock.Advance(TimeSpan.FromMinutes(10));
            await service.RunCycleAsync();
            Assert.Single(_h.Http.To("search"));
        }

        [Fact]
        public async Task A_stored_interval_that_is_not_a_number_is_logged_and_the_last_value_is_kept()
        {
            var service = await ReadyAsync();
            await _h.Settings.SetAsync(AlertsSettings.Scope, AlertsSettings.IntervalMinutes, "10");
            await service.RunCycleAsync();
            Assert.Equal(10, service.IntervalMinutes);

            await _h.Settings.SetAsync(AlertsSettings.Scope, AlertsSettings.IntervalMinutes, "often");
            await service.RunCycleAsync();

            Assert.Equal(10, service.IntervalMinutes);
            Assert.Contains(_h.Log.At(LogLevel.Warning), m => m.Contains("interval_minutes") && m.Contains("often"));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("1441")]
        [InlineData("-5")]
        public async Task A_stored_interval_out_of_range_is_rejected(string value)
        {
            var service = await ReadyAsync();
            await _h.Settings.SetAsync(AlertsSettings.Scope, AlertsSettings.IntervalMinutes, value);

            await service.RunCycleAsync();

            Assert.Equal(5, service.IntervalMinutes);
        }

        [Fact]
        public async Task The_list_interval_defaults_to_five_minutes()
        {
            var service = await ReadyAsync();

            Assert.Equal(5, service.IntervalMinutes);
        }
    }
}
