using Mognet.Modules.Alerts.Providers;

namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>
    /// Decides the type of a video from recorded API facts. It is pure: no I/O, no clock. The checks run in a fixed
    /// order and anything they do not name is <see cref="ContentType.Unclassified"/>. There is no duration
    /// heuristic for Shorts and <c>snippet.publishedAt</c> is not an input.
    /// </summary>
    public static class YouTubeClassifier
    {
        /// <summary>The duration YouTube reports for a stream that has not started.</summary>
        private const string ScheduledStreamDuration = "P0D";

        public static ContentType Classify(ClassificationFacts facts) => facts.LiveBroadcastContent switch
        {
            "upcoming" => ClassifyUpcoming(facts),
            "live" => ClassifyLive(facts),
            "none" => facts.HasActualEndTime ? ClassifyFinished(facts) : ClassifyWithoutEnd(facts),
            _ => ContentType.Unclassified,
        };

        /// <summary>
        /// Whether the playlist facts can change the answer. Only for <c>none</c>: an upcoming or live item is
        /// decided by its live details alone.
        /// </summary>
        public static bool DependsOnMembership(ClassificationFacts facts) => facts.LiveBroadcastContent == "none";

        // A premiere is also upcoming, but has a real duration; it must never become a stream.
        private static ContentType ClassifyUpcoming(ClassificationFacts facts) =>
            facts.HasScheduledStartTime && !facts.HasActualStartTime && !facts.HasActualEndTime && facts.Duration == ScheduledStreamDuration
                ? ContentType.StreamScheduled
                : ContentType.Unclassified;

        private static ContentType ClassifyLive(ClassificationFacts facts) =>
            facts.HasActualStartTime && !facts.HasActualEndTime
                ? ContentType.StreamLive
                : ContentType.Unclassified;

        // A finished premiere has live details too and sits in the videos list. Any other list that claims the item
        // makes it ambiguous.
        private static ContentType ClassifyFinished(ClassificationFacts facts)
        {
            if (facts.InVideosList == ListMembership.Yes)
                return facts.InStreamsList == ListMembership.No && facts.InShortsList == ListMembership.No
                    ? ContentType.Video
                    : ContentType.Unclassified;

            // A recording can join the streams list late, so it does not have to be in it.
            return facts.InVideosList == ListMembership.No && facts.InShortsList == ListMembership.No
                ? ContentType.StreamEnded
                : ContentType.Unclassified;
        }

        private static ContentType ClassifyWithoutEnd(ClassificationFacts facts)
        {
            if (facts.HasLiveStreamingDetails)
                return ContentType.Unclassified;

            // Exactly one of the shorts and videos lists, and the other two known to be without it.
            return (facts.InShortsList, facts.InVideosList, facts.InStreamsList) switch
            {
                (ListMembership.Yes, ListMembership.No, ListMembership.No) => ContentType.Short,
                (ListMembership.No, ListMembership.Yes, ListMembership.No) => ContentType.Video,
                _ => ContentType.Unclassified,
            };
        }
    }
}
