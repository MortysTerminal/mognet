using Mognet.Modules.Alerts.Providers;

namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>A video from <c>videos.list</c>: the neutral item and the facts the classifier needs.</summary>
    public sealed record YouTubeVideo(ContentItem Item, ClassificationFacts Facts);
}
