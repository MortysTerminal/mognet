using mortys_twitch_discord_schedulebot.Sync;
using static mortys_twitch_discord_schedulebot.Tests.TestData;

namespace mortys_twitch_discord_schedulebot.Tests
{
    public class EventFormatterTests
    {
        [Fact]
        public void Name_combines_display_name_and_title()
        {
            Assert.Equal("Some Channel – Minecraft Monday", English.BuildName(Entry()));
        }

        [Theory]
        [InlineData(false, "Some Channel – Stream by Some Channel")]
        [InlineData(true, "Some Channel – Stream von Some Channel")]
        public void Name_falls_back_when_title_is_blank(bool german, string expected)
        {
            var formatter = german ? German : English;

            Assert.Equal(expected, formatter.BuildName(Entry(title: "   ")));
        }

        [Fact]
        public void Name_is_cut_to_100_characters()
        {
            var name = English.BuildName(Entry(title: new string('x', 200)));

            Assert.Equal(100, name.Length);
            Assert.EndsWith("...", name);
            Assert.StartsWith("Some Channel – xxx", name);
        }

        [Fact]
        public void Name_of_exactly_100_characters_is_kept()
        {
            var prefixLength = "Some Channel – ".Length;
            var name = English.BuildName(Entry(title: new string('x', 100 - prefixLength)));

            Assert.Equal(100, name.Length);
            Assert.DoesNotContain("...", name);
        }

        [Fact]
        public void English_description_matches_the_published_format()
        {
            var description = English.BuildDescription(Entry(segmentId: "abc123", category: "Just Chatting"));

            Assert.Equal(
                "🎬 Content: Just Chatting\n" +
                "📺 Channel: https://www.twitch.tv/somechannel\n" +
                "🕐 Start: 01.07.2026 20:00\n" +
                "\n" +
                "[twitch-segment-id:abc123] [twitch-channel:somechannel]",
                description);
        }

        [Fact]
        public void German_description_matches_the_published_format()
        {
            var description = German.BuildDescription(Entry(segmentId: "abc123", category: "Just Chatting"));

            Assert.Equal(
                "🎬 Inhalt: Just Chatting\n" +
                "📺 Kanal: https://www.twitch.tv/somechannel\n" +
                "🕐 Start: 01.07.2026 20:00 Uhr\n" +
                "\n" +
                "[twitch-segment-id:abc123] [twitch-channel:somechannel]",
                description);
        }

        [Theory]
        [InlineData(false, "🎬 Content: No Category")]
        [InlineData(true, "🎬 Inhalt: Keine Kategorie")]
        public void Description_falls_back_when_category_is_empty(bool german, string expectedFirstLine)
        {
            var formatter = german ? German : English;

            var description = formatter.BuildDescription(Entry(category: ""));

            Assert.StartsWith(expectedFirstLine + "\n", description);
        }

        [Theory]
        // Winter time: UTC+1
        [InlineData("2026-01-15T18:00:00Z", "15.01.2026 19:00")]
        // Summer time: UTC+2
        [InlineData("2026-07-01T18:00:00Z", "01.07.2026 20:00")]
        // Around the switch to summer time on 2026-03-29 at 01:00 UTC
        [InlineData("2026-03-29T00:30:00Z", "29.03.2026 01:30")]
        [InlineData("2026-03-29T01:30:00Z", "29.03.2026 03:30")]
        public void Description_shows_start_in_Berlin_time(string startUtc, string expectedLocal)
        {
            var description = English.BuildDescription(Entry(start: DateTimeOffset.Parse(startUtc)));

            Assert.Contains($"🕐 Start: {expectedLocal}\n", description);
        }

        [Fact]
        public void End_time_is_taken_from_the_schedule_when_after_start()
        {
            var start = new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero);

            Assert.Equal(start.AddHours(5), EventFormatter.BuildEndTime(Entry(start: start, end: start.AddHours(5))));
        }

        [Fact]
        public void End_time_defaults_to_three_hours_without_end()
        {
            var start = new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero);

            Assert.Equal(start.AddHours(3), EventFormatter.BuildEndTime(Entry(start: start, end: null)));
        }

        [Fact]
        public void End_time_defaults_to_three_hours_when_end_is_not_after_start()
        {
            var start = new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero);

            Assert.Equal(start.AddHours(3), EventFormatter.BuildEndTime(Entry(start: start, end: start)));
        }

        [Fact]
        public void Channel_tag_uses_the_lower_case_login()
        {
            var description = English.BuildDescription(Entry(channel: "SomeChannel"));

            Assert.EndsWith("[twitch-channel:somechannel]", description);
        }

        [Theory]
        [InlineData("text\n\n[twitch-segment-id:x] [twitch-channel:somechannel]", "somechannel")]
        [InlineData("[twitch-channel:SomeChannel]", "somechannel")]
        [InlineData("text\n\n[twitch-segment-id:x]", null)]
        [InlineData("[twitch-channel:]", null)]
        [InlineData("[twitch-channel:unterminated", null)]
        [InlineData(null, null)]
        public void Channel_tag_is_read_back_from_descriptions(string? description, string? expected)
        {
            Assert.Equal(expected, EventFormatter.ReadChannelTag(description));
        }

        [Theory]
        [InlineData("de", true)]
        [InlineData("DE", true)]
        [InlineData("en", false)]
        [InlineData("fr", false)]
        [InlineData(null, false)]
        public void Language_setting_selects_German_only_for_de(string? language, bool expectedGerman)
        {
            Assert.Equal(expectedGerman, EventFormatter.FromLanguage(language).IsGerman);
        }
    }
}
