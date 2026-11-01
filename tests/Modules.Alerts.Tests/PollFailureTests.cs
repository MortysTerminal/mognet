using System.Net;
using Microsoft.Extensions.Logging;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Services;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.Tests
{
    public class PollFailureTests : IDisposable
    {
        private static readonly DateTimeOffset StreamStart = new(2026, 4, 1, 18, 0, 0, TimeSpan.Zero);

        private PollHarness _h = null!;
        private AlertSource _source = null!;

        public void Dispose() => _h?.Dispose();

        private async Task<PollService> ReadyAsync()
        {
            _h = await PollHarness.CreateAsync();
            _h.Http.Uploads[PollHarness.Uploads] = ["vid002", "vid001"];
            _h.Http.AddOrdinary("vid001");
            _h.Http.AddOrdinary("vid002");
            _h.Http.AddUpcoming("str001", StreamStart.ToString("O"));
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "str001");
            _source = await _h.AddSourceAsync();
            var service = _h.NewService();
            await service.RunCycleAsync();
            _h.Http.Requests.Clear();
            _h.Log.Entries.Clear();
            _h.Clock.Advance(TimeSpan.FromMinutes(5));
            return service;
        }

        private async Task<string> SnapshotAsync() =>
            string.Join("|", (await _h.KnownAsync(_source.Id)).Select(k => k.ToString()));

        public static TheoryData<string, Func<Recorded, Reply?>> Failures => new()
        {
            { "quota exceeded", _ => Reply.QuotaExceeded() },
            { "server error", _ => new Reply(HttpStatusCode.InternalServerError, "") },
            { "malformed body", _ => Reply.Malformed() },
            { "timeout", _ => throw new TaskCanceledException("timed out", new TimeoutException()) },
            { "connection error", _ => throw new HttpRequestException("connection refused") },
        };

        [Theory]
        [MemberData(nameof(Failures))]
        public async Task A_failed_fetch_changes_no_stored_state_and_does_not_stop_the_service(string name, Func<Recorded, Reply?> failure)
        {
            _ = name;
            var service = await ReadyAsync();
            var before = await SnapshotAsync();
            _h.Http.AddUpcoming("str001", StreamStart.AddHours(3).ToString("O"));
            _h.Http.AddOrdinary("vid004");
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "vid004");
            _h.Clock.Advance(TimeSpan.FromDays(26));
            _h.Http.Intercept = failure;

            await service.RunCycleAsync();

            Assert.Equal(before, await SnapshotAsync());
            Assert.Empty(_h.ChangeLines);

            // The next cycle picks everything up again.
            _h.Http.Intercept = null;
            await service.RunCycleAsync();
            Assert.Equal(4, (await _h.KnownAsync(_source.Id)).Count);
            Assert.Equal(StreamStart.AddHours(3), (await _h.Store.GetKnownContentAsync(_source.Id, "str001"))!.ScheduledStart);
        }

        [Theory]
        [InlineData(403)]
        [InlineData(500)]
        public async Task A_failed_videos_call_for_one_tracked_stream_keeps_it(int status)
        {
            var service = await ReadyAsync();
            _h.Http.Intercept = r => r.Endpoint == "videos" ? new Reply((HttpStatusCode)status, "{}") : null;

            await service.RunCycleAsync();

            Assert.Equal([PollHarness.Uploads, "videos"], _h.Http.Requests.Select(r => r.Query["playlistId"] ?? r.Endpoint));
            Assert.NotNull(await _h.Store.GetKnownContentAsync(_source.Id, "str001"));
        }

        [Fact]
        public async Task A_failed_playlist_call_does_not_stop_the_tracked_stream_from_being_read()
        {
            var service = await ReadyAsync();
            _h.Http.AddLive("str001", StreamStart.ToString("O"));
            _h.Http.Intercept = r => r.Endpoint == "playlistItems" ? Reply.QuotaExceeded() : null;

            await service.RunCycleAsync();

            Assert.Equal(LiveState.Live, (await _h.Store.GetKnownContentAsync(_source.Id, "str001"))!.LiveState);
        }

        [Fact]
        public async Task A_failed_search_does_not_stop_the_list_path()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromMinutes(30));
            _h.Http.AddOrdinary("vid004");
            _h.Http.Uploads[PollHarness.Uploads].Insert(0, "vid004");
            _h.Http.Intercept = r => r.Endpoint == "search" ? Reply.QuotaExceeded() : null;

            await service.RunCycleAsync();

            Assert.Single(_h.Http.To("search"));
            Assert.NotNull(await _h.Store.GetKnownContentAsync(_source.Id, "vid004"));
        }

        [Fact]
        public async Task A_failed_playlist_call_does_not_stop_the_search_path()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromMinutes(30));
            _h.Http.Upcoming[PollHarness.ChannelId] = ["str002"];
            _h.Http.AddUpcoming("str002", StreamStart.AddDays(1).ToString("O"));
            _h.Http.Intercept = r => r.Endpoint == "playlistItems" ? Reply.Malformed() : null;

            await service.RunCycleAsync();

            Assert.NotNull(await _h.Store.GetKnownContentAsync(_source.Id, "str002"));
        }

        [Fact]
        public async Task A_failed_search_is_not_repeated_every_cycle()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromMinutes(30));
            _h.Http.Intercept = r => r.Endpoint == "search" ? Reply.QuotaExceeded() : null;
            await service.RunCycleAsync();
            _h.Clock.Advance(TimeSpan.FromMinutes(5));

            await service.RunCycleAsync();

            Assert.Single(_h.Http.To("search"));
        }

        [Fact]
        public async Task A_source_that_fails_does_not_stop_the_other_sources()
        {
            var service = await ReadyAsync();
            var other = await _h.AddSourceAsync(PollHarness.OtherChannelId, baselineRecorded: true);
            _h.Http.AddOrdinary("oth002");
            _h.Http.Uploads[PollHarness.OtherUploads] = ["oth002"];
            _h.Http.Intercept = r => r.Query["playlistId"] == PollHarness.Uploads ? Reply.QuotaExceeded() : null;

            await service.RunCycleAsync();

            Assert.NotNull(await _h.Store.GetKnownContentAsync(other.Id, "oth002"));
            Assert.Contains("oth002", Assert.Single(_h.ChangeLines));
        }

        [Fact]
        public async Task A_source_whose_read_throws_is_logged_and_the_other_sources_still_run()
        {
            var service = await ReadyAsync();
            var other = await _h.AddSourceAsync(PollHarness.OtherChannelId, baselineRecorded: true);
            _h.Http.AddOrdinary("oth002");
            _h.Http.Uploads[PollHarness.OtherUploads] = ["oth002"];
            await _h.Db.ExecuteAsync($"DROP TABLE known_content");
            await _h.Db.ExecuteAsync("CREATE TABLE known_content (source_id INTEGER)");

            await service.RunCycleAsync();

            Assert.Contains(_h.Log.At(LogLevel.Error), m => m.Contains($"source {_source.Id}"));
            Assert.Contains(_h.Log.At(LogLevel.Error), m => m.Contains($"source {other.Id}"));
            Assert.Contains(_h.Log.Messages, m => m.StartsWith("YouTube quota use"));
        }

        [Fact]
        public async Task A_channel_id_without_the_UC_prefix_is_skipped_and_the_others_run()
        {
            var service = await ReadyAsync();
            await _h.AddSourceAsync("not-a-channel", baselineRecorded: true);

            await service.RunCycleAsync();

            Assert.Contains(_h.Log.At(LogLevel.Warning), m => m.Contains("does not start with UC"));
            Assert.DoesNotContain(_h.Http.Requests, r => r.Query["playlistId"]?.Contains("not-a-channel") == true);
        }

        [Fact]
        public async Task The_api_key_never_appears_in_a_request_url_or_in_any_log_line_even_when_every_call_fails()
        {
            var service = await ReadyAsync();
            _h.Clock.Advance(TimeSpan.FromDays(26));
            await service.RunCycleAsync();
            _h.Http.Intercept = r => r.Endpoint == "search" ? Reply.Malformed() : Reply.QuotaExceeded();
            _h.Clock.Advance(TimeSpan.FromMinutes(30));
            await service.RunCycleAsync();
            _h.Http.Intercept = _ => throw new HttpRequestException("connection refused");
            await service.RunCycleAsync();

            Assert.NotEmpty(_h.Http.Requests);
            Assert.All(_h.Http.Requests, r =>
            {
                Assert.DoesNotContain(PollHarness.ApiKey, r.Uri.OriginalString);
                Assert.Equal(PollHarness.ApiKey, r.ApiKey);
            });
            Assert.NotEmpty(_h.Log.Entries);
            Assert.DoesNotContain(_h.Log.Messages, m => m.Contains(PollHarness.ApiKey));
        }

        [Fact]
        public async Task Log_lines_of_failed_calls_hold_status_and_reason_but_no_response_body()
        {
            var service = await ReadyAsync();
            _h.Http.Intercept = _ => Reply.QuotaExceeded();

            await service.RunCycleAsync();

            Assert.Contains(_h.Log.At(LogLevel.Warning), m => m.Contains("HTTP 403 quotaExceeded"));
            Assert.DoesNotContain(_h.Log.Messages, m => m.Contains("Do not log this text"));
        }
    }
}
