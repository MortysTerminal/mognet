using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.YouTube;
using static Mognet.Modules.Alerts.Providers.ContentType;
using static Mognet.Modules.Alerts.YouTube.ListMembership;

namespace Mognet.Modules.Alerts.Tests
{
    public class YouTubeClassifierTests
    {
        private const string Ordinary = "PT12M30S";

        // Live details as the API reports them for each kind of item.
        private static ClassificationFacts Upcoming(string duration = "P0D", bool scheduled = true, bool started = false, bool ended = false) =>
            new("upcoming", true, scheduled, started, ended, duration);

        private static ClassificationFacts Live(bool started = true, bool ended = false) =>
            new("live", true, true, started, ended, "P0D");

        private static ClassificationFacts Finished(string duration = "PT3H1M") =>
            new("none", true, true, true, true, duration);

        private static ClassificationFacts NoDetails(string duration = Ordinary) =>
            new("none", false, false, false, false, duration);

        private static ClassificationFacts In(ClassificationFacts facts, ListMembership shorts, ListMembership videos, ListMembership streams) =>
            facts with { InShortsList = shorts, InVideosList = videos, InStreamsList = streams };

        public static TheoryData<string, ClassificationFacts, ContentType> Cases => new()
        {
            // upcoming
            { "upcoming, start, no actual times, P0D", Upcoming(), StreamScheduled },
            { "upcoming with a real duration (premiere)", Upcoming("PT10M"), Unclassified },
            { "upcoming with a real duration, in no list", In(Upcoming("PT10M"), No, No, No), Unclassified },
            { "upcoming with actualStartTime", Upcoming(started: true), Unclassified },
            { "upcoming with actualEndTime", Upcoming(ended: true), Unclassified },
            { "upcoming without scheduledStartTime", Upcoming(scheduled: false), Unclassified },
            { "upcoming without a duration", new("upcoming", true, true, false, false, null), Unclassified },
            { "upcoming without live details", new("upcoming", false, false, false, false, "P0D"), Unclassified },
            { "upcoming ignores membership", In(Upcoming(), Yes, Yes, Yes), StreamScheduled },

            // live
            { "live with actualStartTime", Live(), StreamLive },
            { "live with a real duration is not looked at", new("live", true, true, true, false, "PT10M"), StreamLive },
            { "live without actualStartTime", Live(started: false), Unclassified },
            { "live with actualEndTime", Live(ended: true), Unclassified },
            { "live ignores membership", In(Live(), Yes, Yes, Yes), StreamLive },

            // none with an end time
            { "ended stream in no list", In(Finished(), No, No, No), StreamEnded },
            { "ended stream in the streams list", In(Finished(), No, No, Yes), StreamEnded },
            { "ended premiere in the videos list", In(Finished("PT10M"), No, Yes, No), Video },
            { "ended in the videos and the streams list", In(Finished(), No, Yes, Yes), Unclassified },
            { "ended in the shorts list", In(Finished(), Yes, No, No), Unclassified },
            { "ended in the shorts and videos list", In(Finished(), Yes, Yes, No), Unclassified },
            { "ended with the videos list unknown", In(Finished(), No, Unknown, No), Unclassified },
            { "ended with the shorts list unknown", In(Finished(), Unknown, No, Yes), Unclassified },
            { "ended premiere with the streams list unknown", In(Finished("PT10M"), No, Yes, Unknown), Unclassified },
            { "ended with all lists unknown", Finished(), Unclassified },

            // none with details but no end
            { "details without an end", In(new("none", true, true, true, false, "PT3H"), No, Yes, No), Unclassified },
            { "details with a start only", In(new("none", true, true, false, false, "PT3H"), No, No, No), Unclassified },

            // none without details
            { "ordinary in the shorts list", In(NoDetails("PT45S"), Yes, No, No), Short },
            { "ordinary in the videos list", In(NoDetails(), No, Yes, No), Video },
            { "ordinary in the streams list", In(NoDetails(), No, No, Yes), Unclassified },
            { "ordinary in no list", In(NoDetails(), No, No, No), Unclassified },
            { "ordinary in the shorts and videos list", In(NoDetails(), Yes, Yes, No), Unclassified },
            { "ordinary in the shorts and streams list", In(NoDetails(), Yes, No, Yes), Unclassified },
            { "ordinary in the videos and streams list", In(NoDetails(), No, Yes, Yes), Unclassified },
            { "ordinary in all lists", In(NoDetails(), Yes, Yes, Yes), Unclassified },
            { "ordinary with all lists unknown", NoDetails(), Unclassified },
            { "ordinary in the shorts list, others unknown", In(NoDetails("PT45S"), Yes, Unknown, Unknown), Unclassified },
            { "ordinary in the videos list, streams unknown", In(NoDetails(), No, Yes, Unknown), Unclassified },
            { "ordinary in the videos list, shorts unknown", In(NoDetails(), Unknown, Yes, No), Unclassified },
            { "a short duration alone does not make a short", In(NoDetails("PT45S"), No, Yes, No), Video },
            { "a long duration alone does not make a video", In(NoDetails("PT3H"), Yes, No, No), Short },

            // anything else
            { "unknown broadcast value", In(new("scheduled", true, true, false, false, "P0D"), No, Yes, No), Unclassified },
            { "missing broadcast value", In(new(null, false, false, false, false, Ordinary), No, Yes, No), Unclassified },
            { "empty broadcast value", In(new("", false, false, false, false, Ordinary), No, Yes, No), Unclassified },
            { "broadcast value in another case", In(new("None", false, false, false, false, Ordinary), No, Yes, No), Unclassified },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void The_facts_decide_the_type(string name, ClassificationFacts facts, ContentType expected)
        {
            Assert.True(name.Length > 0);
            Assert.Equal(expected, YouTubeClassifier.Classify(facts));
        }

        [Theory]
        [InlineData("upcoming", false)]
        [InlineData("live", false)]
        [InlineData("none", true)]
        [InlineData("scheduled", false)]
        [InlineData(null, false)]
        public void Only_a_finished_or_ordinary_item_depends_on_the_playlists(string? broadcast, bool expected)
        {
            Assert.Equal(expected, YouTubeClassifier.DependsOnMembership(new ClassificationFacts(broadcast, false, false, false, false, null)));
        }
    }
}
