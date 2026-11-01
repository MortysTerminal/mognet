using Mognet.Modules.Alerts.Providers;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts.Detection
{
    /// <summary>Compares freshly read content with what is stored. It has no state and touches no database.</summary>
    public static class ChangeDetector
    {
        /// <summary>
        /// One <see cref="ChangeKind.New"/> for an unknown id; for a known id one change per differing field, so
        /// an item can report both a moved start and a new live state. Equal items report nothing.
        /// </summary>
        public static IReadOnlyList<ContentChange> Detect(IEnumerable<ContentItem> fetched, IReadOnlyList<KnownContent> known)
        {
            var byId = known.ToDictionary(k => k.ContentId);
            var changes = new List<ContentChange>();
            foreach (var item in fetched)
            {
                if (!byId.TryGetValue(item.ContentId, out var stored))
                {
                    changes.Add(new ContentChange(item.ContentId, ChangeKind.New));
                    continue;
                }

                if (item.ScheduledStart != stored.ScheduledStart)
                    changes.Add(new ContentChange(item.ContentId, ChangeKind.ScheduledStartChanged));
                if (item.LiveState != stored.LiveState)
                    changes.Add(new ContentChange(item.ContentId, ChangeKind.LiveStateChanged));
            }

            return changes;
        }
    }
}
