using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mognet.Bot.Core.Health;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.Alerts.Detection;
using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.Services
{
    /// <summary>
    /// Reads every source through its provider once per interval, stores what the platform returns and logs what
    /// changed. It sends nothing. The first read of a source only records a baseline. A failing source or provider
    /// is logged and never stops the others or the loop.
    /// </summary>
    public sealed class PollService : BackgroundService
    {
        // A cycle is late when it has not finished one interval plus this grace after the last report.
        private static readonly TimeSpan HealthGrace = TimeSpan.FromMinutes(15);

        private readonly IReadOnlyList<IContentProvider> _providers;
        private readonly AlertSourceStore _sources;
        private readonly SettingsStore _settings;
        private readonly ILogger<PollService> _logger;
        private readonly HealthRegistry? _health;
        private readonly TimeProvider _time;
        private int _intervalMinutes = AlertsSettings.DefaultIntervalMinutes;
        private int _searchIntervalMinutes = AlertsSettings.DefaultSearchIntervalMinutes;

        internal PollService(
            IReadOnlyList<IContentProvider> providers,
            AlertSourceStore sources,
            SettingsStore settings,
            ILogger<PollService> logger,
            HealthRegistry? health = null,
            TimeProvider? time = null)
        {
            _providers = providers;
            _sources = sources;
            _settings = settings;
            _logger = logger;
            _health = health;
            _time = time ?? TimeProvider.System;
        }

        internal int IntervalMinutes => _intervalMinutes;

        private void ReportHealth() =>
            _health?.Report(AlertsModule.ModuleId, _time.GetUtcNow() + TimeSpan.FromMinutes(_intervalMinutes) + HealthGrace);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ReportHealth();
            _logger.LogInformation("PollService started.");

            // Return to the caller first, so starting the module does not wait for the first cycle.
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), _time, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("PollService is stopping.");
        }

        // Settings are read first, so a changed interval applies from this cycle on. A cycle ends with a health
        // report whatever happened, also with no source.
        internal async Task RunCycleAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await ReadSettingsAsync(cancellationToken);

                foreach (var provider in _providers)
                {
                    var sources = await _sources.ListSourcesAsync(provider.Platform, cancellationToken);
                    if (sources.Count == 0)
                        continue;

                    provider.BeginCycle(new CycleContext(sources.Count, _searchIntervalMinutes));
                    foreach (var source in sources)
                    {
                        try
                        {
                            await PollSourceAsync(provider, source, cancellationToken);
                        }
                        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogError(ex, "Reading {Platform} source {SourceId} failed.", provider.Platform, source.Id);
                        }
                    }

                    provider.EndCycle();
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Poll cycle failed.");
            }

            ReportHealth();
        }

        private async Task PollSourceAsync(IContentProvider provider, AlertSource source, CancellationToken cancellationToken)
        {
            var known = await _sources.ListKnownContentAsync(source.Id, cancellationToken);
            var poll = await provider.PollAsync(source, known, cancellationToken);
            var now = _time.GetUtcNow();

            if (!source.BaselineRecorded)
            {
                if (!poll.Complete)
                    return;

                await StoreAsync(source, poll.Items, now, cancellationToken);
                await _sources.MarkBaselineRecordedAsync(source.Id, now, cancellationToken);
                _logger.LogInformation("Baseline recorded for {Platform} source {SourceId}: {Count} item(s), none reported.", provider.Platform, source.Id, poll.Items.Count);
                return;
            }

            foreach (var change in ChangeDetector.Detect(poll.Items, known))
            {
                if (change.Type is { } type)
                    _logger.LogInformation("Content {ContentId} of {Platform} source {SourceId}: {Change} ({Type}).", change.ContentId, provider.Platform, source.Id, change.Kind, type);
                else
                    _logger.LogInformation("Content {ContentId} of {Platform} source {SourceId}: {Change}.", change.ContentId, provider.Platform, source.Id, change.Kind);
            }

            await StoreAsync(source, poll.Items, now, cancellationToken);
            foreach (var id in poll.Gone)
            {
                if (await _sources.DeleteKnownContentAsync(source.Id, id, cancellationToken))
                    _logger.LogInformation("Content {ContentId} of {Platform} source {SourceId} is no longer returned and was removed.", id, provider.Platform, source.Id);
            }
        }

        private async Task StoreAsync(AlertSource source, IReadOnlyList<ContentItem> items, DateTimeOffset now, CancellationToken cancellationToken)
        {
            foreach (var item in items)
                await _sources.UpsertKnownContentAsync(source.Id, item.ContentId, item.ScheduledStart, now, item.LiveState, item.Type, cancellationToken);
        }

        // An invalid stored value keeps the last valid one and is logged; an unreadable database ends the cycle.
        private async Task ReadSettingsAsync(CancellationToken cancellationToken)
        {
            _intervalMinutes = await ReadMinutesAsync(AlertsSettings.IntervalMinutes, _intervalMinutes, AlertsSettings.DefaultIntervalMinutes, cancellationToken);
            _searchIntervalMinutes = await ReadMinutesAsync(AlertsSettings.SearchIntervalMinutes, _searchIntervalMinutes, AlertsSettings.DefaultSearchIntervalMinutes, cancellationToken);
        }

        private async Task<int> ReadMinutesAsync(string key, int current, int fallback, CancellationToken cancellationToken)
        {
            var raw = await _settings.GetAsync(AlertsSettings.Scope, key, cancellationToken);
            if (raw is null)
                return fallback;

            if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
                minutes is >= 1 and <= AlertsSettings.MaxIntervalMinutes)
            {
                return minutes;
            }

            _logger.LogWarning("Setting '{Key}' = '{Value}' is invalid: use a whole number from 1 to {Max}. Keeping {Current} minute(s).", key, raw, AlertsSettings.MaxIntervalMinutes, current);
            return current;
        }
    }
}
