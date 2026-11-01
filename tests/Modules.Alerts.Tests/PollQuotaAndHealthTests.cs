using Microsoft.Extensions.Logging;
using Mognet.Modules.Alerts.Services;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.Tests
{
    public class PollQuotaAndHealthTests : IDisposable
    {
        private PollHarness _h = null!;
        private AlertSource _source = null!;

        public void Dispose() => _h?.Dispose();

        private async Task<PollService> ReadyAsync()
        {
            _h = await PollHarness.CreateAsync();
            _h.Http.Uploads[PollHarness.Uploads] = ["vid001"];
            _h.Http.AddOrdinary("vid001");
            _source = await _h.AddSourceAsync();
            var service = _h.NewService();
            await service.RunCycleAsync();
            _h.Log.Entries.Clear();
            _h.Http.Requests.Clear();
            _h.Clock.Advance(TimeSpan.FromMinutes(5));
            return service;
        }

        private string QuotaLine() => Assert.Single(_h.Log.Messages, m => m.StartsWith("YouTube quota use"));

        [Fact]
        public async Task The_first_cycle_logs_units_and_search_calls_separately()
        {
            _h = await PollHarness.CreateAsync();
            _h.Http.Uploads[PollHarness.Uploads] = ["vid001"];
            _h.Http.AddOrdinary("vid001");
            await _h.AddSourceAsync();

            await _h.NewService().RunCycleAsync();

            // Baseline: 1 playlist page and 1 videos call, plus 1 search call.
            Assert.Equal(
                "YouTube quota use: this cycle units=2 search_calls=1; today (UTC) units=2 search_calls=1.",
                QuotaLine());
        }

        [Fact]
        public async Task The_cycle_figures_restart_every_cycle_and_the_day_figures_add_up()
        {
            var service = await ReadyAsync();

            await service.RunCycleAsync();
            Assert.Equal("YouTube quota use: this cycle units=1 search_calls=0; today (UTC) units=3 search_calls=1.", QuotaLine());

            _h.Log.Entries.Clear();
            _h.Clock.Advance(TimeSpan.FromMinutes(30));
            await service.RunCycleAsync();
            Assert.Equal("YouTube quota use: this cycle units=1 search_calls=1; today (UTC) units=4 search_calls=2.", QuotaLine());
        }

        [Fact]
        public async Task The_day_figures_restart_at_midnight_utc()
        {
            var service = await ReadyAsync();
            await service.RunCycleAsync();
            _h.Log.Entries.Clear();

            _h.Clock.Now = new DateTimeOffset(2026, 3, 2, 0, 5, 0, TimeSpan.Zero);
            await service.RunCycleAsync();

            Assert.Contains("today (UTC) units=1 search_calls=1.", QuotaLine());
        }

        [Fact]
        public async Task Failed_calls_count_in_the_cycle_figures()
        {
            var service = await ReadyAsync();
            _h.Http.Intercept = _ => Reply.QuotaExceeded();

            await service.RunCycleAsync();

            Assert.Contains("this cycle units=1 search_calls=0", QuotaLine());
        }

        [Fact]
        public async Task With_no_sources_no_call_is_made_and_the_cycle_still_reports_health()
        {
            _h = await PollHarness.CreateAsync();
            var service = _h.NewService();

            await service.RunCycleAsync();

            Assert.Empty(_h.Http.Requests);
            Assert.DoesNotContain(_h.Log.Messages, m => m.StartsWith("YouTube quota use"));
            AssertDeadline(TimeSpan.FromMinutes(5 + 15));
        }

        // A module is stalled once now is past its deadline, so probing just before and just after pins the deadline.
        private void AssertDeadline(TimeSpan expected)
        {
            var now = _h.Clock.Now;
            Assert.True(_h.Health.Evaluate(now + expected).IsOk);
            Assert.Equal([AlertsModule.ModuleId], _h.Health.Evaluate(now + expected + TimeSpan.FromSeconds(1)).Stalled);
        }

        [Fact]
        public async Task Every_cycle_moves_the_deadline_to_now_plus_the_interval_plus_fifteen_minutes()
        {
            var service = await ReadyAsync();
            await _h.Settings.SetAsync(AlertsSettings.Scope, AlertsSettings.IntervalMinutes, "10");

            await service.RunCycleAsync();

            AssertDeadline(TimeSpan.FromMinutes(10 + 15));

            _h.Clock.Advance(TimeSpan.FromHours(1));
            await service.RunCycleAsync();
            AssertDeadline(TimeSpan.FromMinutes(10 + 15));
        }

        [Fact]
        public async Task A_cycle_that_fails_reports_health_too()
        {
            var service = await ReadyAsync();
            _h.Http.Intercept = _ => Reply.QuotaExceeded();

            await service.RunCycleAsync();

            AssertDeadline(TimeSpan.FromMinutes(5 + 15));
        }

        [Fact]
        public async Task A_cycle_that_cannot_read_the_database_reports_health_and_logs()
        {
            var service = await ReadyAsync();
            await _h.Db.ExecuteAsync("DROP TABLE settings");

            await service.RunCycleAsync();

            Assert.Contains(_h.Log.At(LogLevel.Error), m => m.Contains("Poll cycle failed"));
            AssertDeadline(TimeSpan.FromMinutes(5 + 15));
        }

        [Fact]
        public async Task The_service_reports_when_it_starts_before_the_first_cycle_finishes()
        {
            _h = await PollHarness.CreateAsync();
            var service = _h.NewService();

            await service.StartAsync(CancellationToken.None);
            try
            {
                await Wait.UntilAsync(() => !_h.Health.Evaluate(_h.Clock.Now.AddYears(1)).IsOk);
                AssertDeadline(TimeSpan.FromMinutes(5 + 15));
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task The_service_runs_a_first_cycle_at_start()
        {
            _h = await PollHarness.CreateAsync();
            _h.Http.Uploads[PollHarness.Uploads] = ["vid001"];
            _h.Http.AddOrdinary("vid001");
            _source = await _h.AddSourceAsync();
            var service = _h.NewService();

            await service.StartAsync(CancellationToken.None);
            try
            {
                await Wait.UntilAsync(() => _h.Store.GetSourceAsync(_source.Id).GetAwaiter().GetResult()!.BaselineRecorded);

                Assert.True((await _h.Store.GetSourceAsync(_source.Id))!.BaselineRecorded);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }
}
