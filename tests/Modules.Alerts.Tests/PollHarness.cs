using Mognet.Bot.Core.Health;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.Alerts.Services;
using Mognet.Modules.Alerts.Storage;
using Mognet.Modules.Alerts.YouTube;

namespace Mognet.Modules.Alerts.Tests
{
    /// <summary>
    /// A real SQLite file, a stub YouTube and the real provider and service on top. <see cref="NewService"/> builds a
    /// fresh service and provider on the same file, which is what a restart does.
    /// </summary>
    internal sealed class PollHarness : IDisposable
    {
        public const string ApiKey = "test-key-not-a-real-secret-9f3a1c";
        public const string ChannelId = "UCtestchannel0000000001";
        public const string Uploads = "UUtestchannel0000000001";
        public const string OtherChannelId = "UCtestchannel0000000002";
        public const string OtherUploads = "UUtestchannel0000000002";

        public static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        private readonly AlertsDb _db;

        private PollHarness(AlertsDb db)
        {
            _db = db;
            Store = db.Sources;
            Settings = new SettingsStore(db.Store, Clock);
        }

        public static async Task<PollHarness> CreateAsync() => new(await AlertsDb.CreateAsync());

        public StubYouTube Http { get; } = new();

        public TestClock Clock { get; } = new(Start);

        public LogSink Log { get; } = new();

        public HealthRegistry Health { get; } = new();

        public AlertSourceStore Store { get; }

        public SettingsStore Settings { get; }

        public AlertsDb Db => _db;

        public async Task<AlertSource> AddSourceAsync(string channelId = ChannelId, bool baselineRecorded = false)
        {
            var source = await Store.AddSourceAsync(YouTubeProvider.PlatformName, channelId, Clock.Now);
            if (baselineRecorded)
                await Store.MarkBaselineRecordedAsync(source.Id, Clock.Now);
            return source;
        }

        public QuotaCounter? Quota { get; private set; }

        public PollService NewService(TimeSpan? requestTimeout = null)
        {
            Quota = new QuotaCounter(Clock);
            var api = new YouTubeApi(new HttpClient(Http) { Timeout = requestTimeout ?? TimeSpan.FromSeconds(30) }, ApiKey, Quota);
            var provider = new YouTubeProvider(api, Quota, Log.Logger<YouTubeProvider>(), Clock);
            return new PollService([provider], Store, Settings, Log.Logger<PollService>(), Health, Clock);
        }

        public async Task<IReadOnlyList<KnownContent>> KnownAsync(long sourceId) => await Store.ListKnownContentAsync(sourceId);

        public IEnumerable<string> ChangeLines => Log.Messages.Where(m => m.StartsWith("Content "));

        public void Dispose() => _db.Dispose();
    }
}
