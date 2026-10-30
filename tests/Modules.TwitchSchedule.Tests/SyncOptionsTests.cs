using Microsoft.Extensions.Configuration;
using Mognet.Modules.TwitchSchedule.Sync;

namespace Mognet.Modules.TwitchSchedule.Tests
{
    public class SyncOptionsTests
    {
        private static IConfiguration Config(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
                .Build();

        [Fact]
        public void Defaults_apply_when_nothing_is_set()
        {
            Assert.Equal(new SyncOptions(30, 14, 10), SyncOptions.FromConfiguration(Config()));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_values_use_the_defaults(string empty)
        {
            // Docker passes unset compose variables as empty strings.
            var config = Config(
                ("Sync:IntervalMinutes", empty),
                ("Sync:LookaheadDays", empty),
                ("Sync:MaxEventsPerChannel", empty));

            Assert.Equal(new SyncOptions(30, 14, 10), SyncOptions.FromConfiguration(config));
        }

        [Fact]
        public void Valid_values_are_used()
        {
            var config = Config(
                ("Sync:IntervalMinutes", "15"),
                ("Sync:LookaheadDays", " 7 "),
                ("Sync:MaxEventsPerChannel", "3"));

            Assert.Equal(new SyncOptions(15, 7, 3), SyncOptions.FromConfiguration(config));
        }

        [Theory]
        [InlineData("Sync:IntervalMinutes", "5")]
        [InlineData("Sync:IntervalMinutes", "1440")]
        [InlineData("Sync:LookaheadDays", "1")]
        [InlineData("Sync:LookaheadDays", "60")]
        [InlineData("Sync:MaxEventsPerChannel", "1")]
        [InlineData("Sync:MaxEventsPerChannel", "50")]
        public void Bounds_are_inclusive(string key, string value)
        {
            SyncOptions.FromConfiguration(Config((key, value)));
        }

        [Theory]
        [InlineData("Sync:IntervalMinutes", "0")]
        [InlineData("Sync:IntervalMinutes", "-5")]
        [InlineData("Sync:IntervalMinutes", "4")]
        [InlineData("Sync:IntervalMinutes", "1441")]
        [InlineData("Sync:LookaheadDays", "0")]
        [InlineData("Sync:LookaheadDays", "61")]
        [InlineData("Sync:MaxEventsPerChannel", "0")]
        [InlineData("Sync:MaxEventsPerChannel", "51")]
        [InlineData("Sync:IntervalMinutes", "thirty")]
        [InlineData("Sync:LookaheadDays", "7.5")]
        [InlineData("Sync:MaxEventsPerChannel", "99999999999")]
        public void Invalid_values_stop_the_bot_with_a_clear_message(string key, string value)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => SyncOptions.FromConfiguration(Config((key, value))));

            // Names the environment variable an operator sets in Portainer, the value and the allowed range.
            Assert.Contains(key.Replace(":", "__"), ex.Message);
            Assert.Contains($"'{value}'", ex.Message);
            Assert.Contains("from", ex.Message);
        }

        [Fact]
        public void Message_states_the_range()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => SyncOptions.FromConfiguration(Config(("Sync:IntervalMinutes", "0"))));

            Assert.Equal(
                "Sync__IntervalMinutes = '0' is invalid: use a whole number from 5 to 1440 (default 30 when not set).",
                ex.Message);
        }
    }
}
