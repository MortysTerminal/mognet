using Microsoft.Extensions.Logging.Abstractions;
using Mognet.Bot.Core.Health;

namespace Mognet.Bot.Core.Tests
{
    public class HealthFileWriterTests : IDisposable
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 5, TimeSpan.Zero);
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "mognet-health-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        private string FilePath => Path.Combine(_directory, "mognet-health");

        private HealthFileWriter Writer(HealthRegistry registry, string? path = null, TimeSpan? interval = null, TimeProvider? time = null)
        {
            Directory.CreateDirectory(_directory);
            return new HealthFileWriter(registry, time ?? new FixedTime(Now), NullLogger<HealthFileWriter>.Instance, path ?? FilePath, interval ?? TimeSpan.FromSeconds(30));
        }

        [Fact]
        public void The_default_path_is_mognet_health_in_the_temp_directory()
        {
            Assert.Equal(Path.Combine(Path.GetTempPath(), "mognet-health"), HealthFile.DefaultPath);
        }

        [Fact]
        public void A_healthy_host_writes_a_timestamp_line_and_ok()
        {
            Writer(new HealthRegistry()).WriteOnce();

            Assert.Equal(["2026-10-31T12:00:05Z", "ok"], File.ReadAllLines(FilePath));
        }

        [Fact]
        public void A_stalled_module_is_written_with_its_id()
        {
            var registry = new HealthRegistry();
            registry.Report("twitch_schedule", Now.AddSeconds(-1));

            Writer(registry).WriteOnce();

            Assert.Equal(["2026-10-31T12:00:05Z", "stalled:twitch_schedule"], File.ReadAllLines(FilePath));
        }

        [Fact]
        public void Several_stalled_modules_are_joined_by_commas()
        {
            var registry = new HealthRegistry();
            registry.Report("b", Now.AddSeconds(-1));
            registry.Report("a", Now.AddSeconds(-1));

            Writer(registry).WriteOnce();

            Assert.Equal("stalled:a,b", File.ReadAllLines(FilePath)[1]);
        }

        [Fact]
        public void A_write_replaces_the_file_and_leaves_no_temp_file_behind()
        {
            var registry = new HealthRegistry();
            var writer = Writer(registry);
            writer.WriteOnce();
            registry.Report("a", Now.AddSeconds(-1));
            writer.WriteOnce();

            Assert.Equal("stalled:a", File.ReadAllLines(FilePath)[1]);
            Assert.Equal([FilePath], Directory.GetFiles(_directory));
        }

        [Fact]
        public void A_write_failure_does_not_throw()
        {
            // The parent directory does not exist.
            var writer = Writer(new HealthRegistry(), Path.Combine(_directory, "missing", "mognet-health"));

            writer.WriteOnce();
        }

        [Fact]
        public async Task The_service_writes_at_start_and_keeps_writing()
        {
            var registry = new HealthRegistry();
            var writer = Writer(registry, interval: TimeSpan.FromMilliseconds(50), time: TimeProvider.System);

            await writer.StartAsync(CancellationToken.None);
            try
            {
                await WaitForAsync(() => File.Exists(FilePath));
                registry.Report("a", DateTimeOffset.UtcNow.AddMinutes(-1));
                await WaitForAsync(() => File.Exists(FilePath) && TryReadLine2() == "stalled:a");
            }
            finally
            {
                await writer.StopAsync(CancellationToken.None);
            }
        }

        private string? TryReadLine2()
        {
            try
            {
                var lines = File.ReadAllLines(FilePath);
                return lines.Length > 1 ? lines[1] : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
                await Task.Delay(20);
            }
        }
    }
}
