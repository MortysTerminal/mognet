using Microsoft.Extensions.Logging;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Services;
using Mognet.Modules.Alerts.Storage;
using Mognet.Modules.Alerts.YouTube;

namespace Mognet.Modules.Alerts.Tests
{
    public class ClassificationCycleTests : IDisposable
    {
        private static readonly DateTimeOffset StreamStart = new(2026, 4, 1, 18, 0, 0, TimeSpan.Zero);
        private static readonly string[] MembershipLists = [PollHarness.ShortsList, PollHarness.VideosList, PollHarness.StreamsList];

        private PollHarness _h = null!;
        private AlertSource _source = null!;

        public void Dispose() => _h?.Dispose();

        // A source with a recorded baseline of three ordinary videos.
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

        // The id appears at the top of the uploads and, when given, in one of the generated lists.
        private void Publish(string id, string? list = null)
        {
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, id);
            if (list is not null)
                AddToList(id, list);
        }

        private void AddToList(string id, string list)
        {
            if (!_h.Http.Uploads.TryGetValue(list, out var ids))
                _h.Http.Uploads[list] = ids = [];
            ids.Add(id);
        }

        private async Task<KnownContent> KnownAsync(string id) => (await _h.Store.GetKnownContentAsync(_source.Id, id))!;

        private IEnumerable<string> PlaylistsRead() => _h.Http.To("playlistItems").Select(r => r.Query["playlistId"]!);

        private int MembershipReads() => PlaylistsRead().Count(p => MembershipLists.Contains(p));

        private async Task AdvanceAsync(PollService service, TimeSpan by)
        {
            _h.Clock.Advance(by);
            await service.RunCycleAsync();
        }

        [Fact]
        public async Task A_new_video_in_the_videos_list_is_a_video_and_its_lists_cost_three_units()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004", PollHarness.VideosList);

            await service.RunCycleAsync();

            Assert.Equal(ContentType.Video, (await KnownAsync("vid004")).Type);
            Assert.Equal([PollHarness.Uploads, PollHarness.ShortsList, PollHarness.VideosList, PollHarness.StreamsList], PlaylistsRead());
            // Uploads page, videos call, three lists.
            Assert.Equal(5, _h.Quota!.CycleUnits);
            Assert.Contains(_h.ChangeLines, l => l.Contains("vid004") && l.Contains("New") && l.Contains("Video"));
        }

        [Fact]
        public async Task A_new_video_in_the_shorts_list_is_a_short()
        {
            var service = await ReadyAsync();
            _h.Http.AddShort("sho001");
            Publish("sho001", PollHarness.ShortsList);

            await service.RunCycleAsync();

            Assert.Equal(ContentType.Short, (await KnownAsync("sho001")).Type);
            Assert.Contains(_h.ChangeLines, l => l.Contains("sho001") && l.Contains("New") && l.Contains("Short"));
        }

        [Fact]
        public async Task A_new_scheduled_stream_is_decided_by_its_live_details_without_a_list_read()
        {
            var service = await ReadyAsync();
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            Publish("str001");

            await service.RunCycleAsync();

            Assert.Equal(ContentType.StreamScheduled, (await KnownAsync("str001")).Type);
            Assert.Equal(0, MembershipReads());
            Assert.Equal(2, _h.Quota!.CycleUnits);
        }

        [Fact]
        public async Task A_premiere_never_becomes_a_scheduled_stream()
        {
            var service = await ReadyAsync();
            _h.Http.AddPremiere("pre001", StreamStart.ToString("O"));
            Publish("pre001");

            await service.RunCycleAsync();

            Assert.Equal(ContentType.Unclassified, (await KnownAsync("pre001")).Type);
            Assert.Equal(0, MembershipReads());
        }

        [Fact]
        public async Task A_premiere_that_has_ended_and_joined_the_videos_list_becomes_a_video_and_the_change_is_reported()
        {
            var service = await ReadyAsync();
            _h.Http.AddPremiere("pre001", StreamStart.ToString("O"));
            Publish("pre001");
            await service.RunCycleAsync();
            _h.Log.Entries.Clear();

            _h.Http.AddEndedPremiere("pre001", StreamStart.ToString("O"));
            AddToList("pre001", PollHarness.VideosList);
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));

            Assert.Equal(ContentType.Video, (await KnownAsync("pre001")).Type);
            Assert.Contains(_h.ChangeLines, l => l.Contains("pre001") && l.Contains("TypeChanged") && l.Contains("Video"));
        }

        [Fact]
        public async Task An_item_in_no_list_yet_is_unclassified_then_gets_its_type_later_and_is_not_checked_again()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004");

            await service.RunCycleAsync();
            Assert.Equal(ContentType.Unclassified, (await KnownAsync("vid004")).Type);
            Assert.Contains(_h.ChangeLines, l => l.Contains("vid004") && l.Contains("New") && l.Contains("Unclassified"));

            // Still in no list: checked again, nothing changes and nothing is reported.
            _h.Http.Requests.Clear();
            _h.Log.Entries.Clear();
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));
            Assert.Equal(ContentType.Unclassified, (await KnownAsync("vid004")).Type);
            Assert.Contains(_h.Http.To("videos"), r => r.Ids.Contains("vid004"));
            Assert.Equal(3, MembershipReads());
            Assert.Empty(_h.ChangeLines);

            // It joins the list: the type changes and is reported once.
            AddToList("vid004", PollHarness.VideosList);
            _h.Log.Entries.Clear();
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));
            Assert.Equal(ContentType.Video, (await KnownAsync("vid004")).Type);
            Assert.Contains(_h.ChangeLines, l => l.Contains("vid004") && l.Contains("TypeChanged") && l.Contains("Video"));

            // Classified: no videos call and no list read any more.
            _h.Http.Requests.Clear();
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));
            Assert.Empty(_h.Http.To("videos"));
            Assert.Equal(0, MembershipReads());
        }

        [Fact]
        public async Task An_item_still_unclassified_after_24_hours_is_no_longer_checked_by_membership()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004");
            await service.RunCycleAsync();

            // Just inside the window it is still checked.
            _h.Http.Requests.Clear();
            await AdvanceAsync(service, YouTubeProvider.RecheckWindow - TimeSpan.FromMinutes(10));
            Assert.Equal(3, MembershipReads());

            _h.Http.Requests.Clear();
            await AdvanceAsync(service, TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

            Assert.Equal(0, MembershipReads());
            Assert.Empty(_h.Http.To("videos"));

            // Joining a list later changes nothing: the type stays unclassified.
            AddToList("vid004", PollHarness.VideosList);
            _h.Log.Entries.Clear();
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));
            Assert.Equal(ContentType.Unclassified, (await KnownAsync("vid004")).Type);
            Assert.Empty(_h.ChangeLines);
        }

        [Fact]
        public async Task An_unclassified_item_read_again_after_the_window_for_other_reasons_reads_no_list()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004");
            await service.RunCycleAsync();
            AddToList("vid004", PollHarness.VideosList);
            _h.Http.Requests.Clear();

            // Past 25 days the item is read again as stale, but its type is no longer worked on.
            await AdvanceAsync(service, TimeSpan.FromDays(26));

            Assert.Contains(_h.Http.To("videos"), r => r.Ids.Contains("vid004"));
            Assert.Equal(0, MembershipReads());
            Assert.Equal(ContentType.Unclassified, (await KnownAsync("vid004")).Type);
        }

        [Fact]
        public async Task A_premiere_unclassified_for_days_is_classified_when_it_ends()
        {
            var service = await ReadyAsync();
            _h.Http.AddPremiere("pre001", StreamStart.ToString("O"));
            Publish("pre001");
            await service.RunCycleAsync();
            await AdvanceAsync(service, TimeSpan.FromDays(3));
            Assert.Equal(ContentType.Unclassified, (await KnownAsync("pre001")).Type);

            _h.Http.AddEndedPremiere("pre001", StreamStart.ToString("O"));
            AddToList("pre001", PollHarness.VideosList);
            _h.Http.Requests.Clear();
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));

            Assert.Equal(ContentType.Video, (await KnownAsync("pre001")).Type);
            Assert.Equal(3, MembershipReads());
        }

        [Fact]
        public async Task A_stream_stored_with_a_state_but_no_type_is_classified_when_it_ends()
        {
            var service = await ReadyAsync();
            await _h.Store.UpsertKnownContentAsync(_source.Id, "str001", StreamStart, _h.Clock.Now, LiveState.Live);
            _h.Http.AddEnded("str001", StreamStart.ToString("O"));
            AddToList("str001", PollHarness.StreamsList);

            await service.RunCycleAsync();

            Assert.Equal(ContentType.StreamEnded, (await KnownAsync("str001")).Type);
        }

        [Fact]
        public void The_recheck_window_is_24_hours()
        {
            Assert.Equal(TimeSpan.FromHours(24), YouTubeProvider.RecheckWindow);
        }

        [Fact]
        public async Task A_failed_membership_read_changes_no_stored_type()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004");
            await service.RunCycleAsync();
            _h.Log.Entries.Clear();

            // The item is now in the videos list, but the read of that list fails.
            AddToList("vid004", PollHarness.VideosList);
            _h.Http.Intercept = r => r.Query["playlistId"] == PollHarness.VideosList ? Reply.QuotaExceeded() : null;
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));

            Assert.Equal(ContentType.Unclassified, (await KnownAsync("vid004")).Type);
            Assert.Empty(_h.ChangeLines);
            Assert.Contains(_h.Log.At(LogLevel.Warning), m => m.Contains("membership") && m.Contains("unknown") && m.Contains("quotaExceeded"));
            Assert.DoesNotContain(_h.Log.Messages, m => m.Contains("Do not log this text"));

            // The next cycle reads again and succeeds.
            _h.Http.Intercept = null;
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));
            Assert.Equal(ContentType.Video, (await KnownAsync("vid004")).Type);
        }

        [Fact]
        public async Task A_new_item_whose_lists_all_failed_is_stored_as_unclassified_and_checked_again()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004", PollHarness.VideosList);
            _h.Http.Intercept = r => MembershipLists.Contains(r.Query["playlistId"]) ? Reply.Malformed() : null;

            await service.RunCycleAsync();
            Assert.Equal(ContentType.Unclassified, (await KnownAsync("vid004")).Type);

            _h.Http.Intercept = null;
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));
            Assert.Equal(ContentType.Video, (await KnownAsync("vid004")).Type);
        }

        [Fact]
        public async Task A_stream_whose_end_could_not_be_classified_keeps_its_type_and_is_read_again()
        {
            var service = await ReadyAsync();
            _h.Http.AddLive("str001", StreamStart.ToString("O"));
            Publish("str001");
            await service.RunCycleAsync();
            Assert.Equal(ContentType.StreamLive, (await KnownAsync("str001")).Type);

            _h.Http.AddEnded("str001", StreamStart.ToString("O"));
            AddToList("str001", PollHarness.StreamsList);
            _h.Http.Intercept = r => MembershipLists.Contains(r.Query["playlistId"]) ? Reply.QuotaExceeded() : null;
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));

            var stored = await KnownAsync("str001");
            Assert.Equal(ContentType.StreamLive, stored.Type);
            Assert.Equal(LiveState.Ended, stored.LiveState);

            // Although the state is ended, the stream is read again until its type is settled.
            _h.Http.Intercept = null;
            _h.Http.Requests.Clear();
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));
            Assert.Contains(_h.Http.To("videos"), r => r.Ids.Contains("str001"));
            Assert.Equal(ContentType.StreamEnded, (await KnownAsync("str001")).Type);

            _h.Http.Requests.Clear();
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));
            Assert.Empty(_h.Http.To("videos"));
        }

        [Fact]
        public async Task A_list_that_does_not_exist_answers_404_and_counts_as_not_a_member()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004", PollHarness.VideosList);
            _h.Http.Intercept = r => r.Query["playlistId"] is var p && (p == PollHarness.ShortsList || p == PollHarness.StreamsList) ? Reply.PlaylistNotFound() : null;

            await service.RunCycleAsync();

            Assert.Equal(ContentType.Video, (await KnownAsync("vid004")).Type);
            Assert.DoesNotContain(_h.Log.At(LogLevel.Warning), m => m.Contains("membership"));
        }

        [Fact]
        public async Task A_404_with_another_reason_is_an_unknown_list()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004", PollHarness.VideosList);
            _h.Http.Intercept = r => r.Query["playlistId"] == PollHarness.ShortsList
                ? new Reply(System.Net.HttpStatusCode.NotFound, Fixture.Load("error-playlist-not-found.json").Replace("playlistNotFound", "videoNotFound"))
                : null;

            await service.RunCycleAsync();

            Assert.Equal(ContentType.Unclassified, (await KnownAsync("vid004")).Type);
            Assert.Contains(_h.Log.At(LogLevel.Warning), m => m.Contains("membership") && m.Contains("videoNotFound"));
        }

        [Fact]
        public async Task Lists_are_read_once_per_source_and_cycle_however_many_items_need_them()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            _h.Http.AddShort("sho001");
            _h.Http.AddOrdinary("vid005");
            Publish("vid004", PollHarness.VideosList);
            Publish("sho001", PollHarness.ShortsList);
            Publish("vid005");

            await service.RunCycleAsync();

            Assert.Equal(1, PlaylistsRead().Count(p => p == PollHarness.ShortsList));
            Assert.Equal(1, PlaylistsRead().Count(p => p == PollHarness.VideosList));
            Assert.Equal(1, PlaylistsRead().Count(p => p == PollHarness.StreamsList));

            // A recheck of the unclassified item shares one read with a new item of the next cycle.
            _h.Http.Requests.Clear();
            _h.Http.AddOrdinary("vid006");
            Publish("vid006", PollHarness.VideosList);
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));
            Assert.Equal(3, MembershipReads());
        }

        [Fact]
        public async Task Each_source_reads_its_own_lists()
        {
            var service = await ReadyAsync();
            var other = await _h.AddSourceAsync(PollHarness.OtherChannelId, baselineRecorded: true);
            _h.Http.AddOrdinary("vid004");
            _h.Http.AddOrdinary("oth001");
            Publish("vid004", PollHarness.VideosList);
            _h.Http.Uploads[PollHarness.OtherUploads] = ["oth001"];
            _h.Http.Uploads["UULFtestchannel0000000002"] = ["oth001"];
            _h.Clock.Advance(TimeSpan.FromMinutes(30));

            await service.RunCycleAsync();

            Assert.Equal(ContentType.Video, (await KnownAsync("vid004")).Type);
            Assert.Equal(ContentType.Video, (await _h.Store.GetKnownContentAsync(other.Id, "oth001"))!.Type);
            Assert.Equal(6, PlaylistsRead().Count(p => p.StartsWith("UUSH") || p.StartsWith("UULF") || p.StartsWith("UULV")));
            Assert.Equal(3, MembershipReads());
        }

        [Fact]
        public async Task A_cycle_with_no_new_item_reads_no_list()
        {
            var service = await ReadyAsync();

            await service.RunCycleAsync();

            Assert.Equal([PollHarness.Uploads], PlaylistsRead());
        }

        [Fact]
        public async Task Refreshing_an_ordinary_known_item_reads_no_list_and_keeps_its_type()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004", PollHarness.VideosList);
            await service.RunCycleAsync();
            _h.Http.Requests.Clear();
            _h.Log.Entries.Clear();

            await AdvanceAsync(service, TimeSpan.FromDays(26));

            Assert.Contains(_h.Http.To("videos"), r => r.Ids.Contains("vid004"));
            Assert.Equal(0, MembershipReads());
            Assert.Equal(ContentType.Video, (await KnownAsync("vid004")).Type);
            Assert.Equal(_h.Clock.Now, (await KnownAsync("vid004")).LastRefreshedAt);
            Assert.Empty(_h.ChangeLines);
        }

        [Fact]
        public async Task Baseline_items_get_no_list_read_and_no_type_except_streams_still_tracked()
        {
            _h = await PollHarness.CreateAsync();
            _h.Http.Uploads[PollHarness.Uploads] = ["str002", "str001", "vid001"];
            _h.Http.AddOrdinary("vid001");
            _h.Http.AddEnded("str001", StreamStart.ToString("O"));
            _h.Http.AddLive("str002", StreamStart.ToString("O"));
            _h.Http.Upcoming[PollHarness.ChannelId] = ["str003"];
            _h.Http.AddUpcoming("str003", StreamStart.AddDays(1).ToString("O"));
            _source = await _h.AddSourceAsync();

            await _h.NewService().RunCycleAsync();

            Assert.Equal(0, MembershipReads());
            Assert.Null((await KnownAsync("vid001")).Type);
            Assert.Null((await KnownAsync("str001")).Type);
            Assert.Equal(ContentType.StreamLive, (await KnownAsync("str002")).Type);
            Assert.Equal(ContentType.StreamScheduled, (await KnownAsync("str003")).Type);
            Assert.Empty(_h.ChangeLines);
        }

        [Fact]
        public async Task A_baseline_item_is_never_checked_by_membership_later()
        {
            var service = await ReadyAsync();
            AddToList("vid001", PollHarness.VideosList);

            await service.RunCycleAsync();

            Assert.Equal(0, MembershipReads());
            Assert.Null((await KnownAsync("vid001")).Type);
        }

        [Fact]
        public async Task A_stream_goes_from_scheduled_to_live_to_ended_across_restarts_and_each_change_is_reported()
        {
            _h = await PollHarness.CreateAsync();
            _h.Http.Uploads[PollHarness.Uploads] = ["str001"];
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            _source = await _h.AddSourceAsync();
            await _h.NewService().RunCycleAsync();
            Assert.Equal(ContentType.StreamScheduled, (await KnownAsync("str001")).Type);
            _h.Log.Entries.Clear();
            _h.Http.Requests.Clear();

            _h.Http.AddLive("str001", StreamStart.ToString("O"));
            _h.Clock.Advance(TimeSpan.FromMinutes(5));
            await _h.NewService().RunCycleAsync();
            Assert.Equal(ContentType.StreamLive, (await KnownAsync("str001")).Type);
            Assert.Contains(_h.ChangeLines, l => l.Contains("str001") && l.Contains("TypeChanged") && l.Contains("StreamLive"));
            Assert.Equal(0, MembershipReads());

            _h.Log.Entries.Clear();
            _h.Http.AddEnded("str001", StreamStart.ToString("O"));
            AddToList("str001", PollHarness.StreamsList);
            _h.Clock.Advance(TimeSpan.FromMinutes(5));
            await _h.NewService().RunCycleAsync();
            Assert.Equal(ContentType.StreamEnded, (await KnownAsync("str001")).Type);
            Assert.Contains(_h.ChangeLines, l => l.Contains("str001") && l.Contains("TypeChanged") && l.Contains("StreamEnded"));
            Assert.DoesNotContain(_h.Log.Messages, m => m.Contains("Neutral test"));
            Assert.Equal(3, MembershipReads());

            // Settled: a restart reads nothing more and reports nothing.
            _h.Log.Entries.Clear();
            _h.Http.Requests.Clear();
            _h.Clock.Advance(TimeSpan.FromMinutes(5));
            await _h.NewService().RunCycleAsync();
            Assert.Empty(_h.Http.To("videos"));
            Assert.Equal(0, MembershipReads());
            Assert.Empty(_h.ChangeLines);
        }

        [Fact]
        public async Task A_stream_that_ended_in_the_videos_list_as_well_is_unclassified()
        {
            var service = await ReadyAsync();
            _h.Http.AddLive("str001", StreamStart.ToString("O"));
            Publish("str001");
            await service.RunCycleAsync();

            _h.Http.AddEnded("str001", StreamStart.ToString("O"));
            AddToList("str001", PollHarness.StreamsList);
            AddToList("str001", PollHarness.VideosList);
            await AdvanceAsync(service, TimeSpan.FromMinutes(5));

            Assert.Equal(ContentType.Unclassified, (await KnownAsync("str001")).Type);
        }

        [Fact]
        public async Task Change_lines_carry_the_type_and_never_a_title()
        {
            var service = await ReadyAsync();
            _h.Http.AddOrdinary("vid004");
            Publish("vid004", PollHarness.VideosList);

            await service.RunCycleAsync();

            Assert.Contains(_h.Log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("vid004") && e.Message.Contains("New") && e.Message.Contains("Video"));
            Assert.DoesNotContain(_h.Log.Messages, m => m.Contains("Neutral test"));
        }
    }
}
