using Mognet.Bot.Core.Health;

namespace Mognet.Bot.Core.Tests
{
    public class HealthCheckTests : IDisposable
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(3);
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "mognet-check-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private string Write(string content)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "mognet-health");
            File.WriteAllText(path, content);
            return path;
        }

        private static (int Code, string Output) Run(string path)
        {
            var output = new StringWriter();
            var code = HealthCheck.Run(path, Now, MaxAge, output);
            return (code, output.ToString());
        }

        [Fact]
        public void A_missing_file_fails_with_one_line()
        {
            var (code, output) = Run(Path.Combine(_directory, "nope"));

            Assert.Equal(1, code);
            Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.Contains("not found", output);
        }

        [Theory]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("not a date\nok\n")]
        [InlineData("2026-10-31T11:59:30Z\n")]
        [InlineData("2026-10-31T11:59:30Z\nmaybe\n")]
        public void Unreadable_content_fails(string content)
        {
            var (code, output) = Run(Write(content));

            Assert.Equal(1, code);
            Assert.NotEmpty(output);
        }

        [Fact]
        public void A_file_older_than_the_limit_fails()
        {
            var (code, output) = Run(Write("2026-10-31T11:56:59Z\nok\n"));

            Assert.Equal(1, code);
            Assert.Contains("old", output);
        }

        [Fact]
        public void A_fresh_ok_file_passes()
        {
            var (code, output) = Run(Write("2026-10-31T11:59:30Z\nok\n"));

            Assert.Equal(0, code);
            Assert.Empty(output);
        }

        [Fact]
        public void A_file_exactly_at_the_limit_passes()
        {
            Assert.Equal(0, Run(Write("2026-10-31T11:57:00Z\nok\n")).Code);
        }

        [Fact]
        public void A_fresh_stalled_file_fails_and_names_the_module()
        {
            var (code, output) = Run(Write("2026-10-31T11:59:30Z\nstalled:twitch_schedule\n"));

            Assert.Equal(1, code);
            Assert.Contains("twitch_schedule", output);
        }

        [Fact]
        public void A_directory_instead_of_a_file_fails_without_throwing()
        {
            Directory.CreateDirectory(_directory);

            Assert.Equal(1, Run(_directory).Code);
        }
    }
}
