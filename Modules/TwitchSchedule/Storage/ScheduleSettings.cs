namespace Mognet.Modules.TwitchSchedule.Storage
{
    /// <summary>Scope and keys of the schedule module's rows in the shared settings table.</summary>
    public static class ScheduleSettings
    {
        public const string Scope = TwitchScheduleModule.ModuleId;

        public const string IntervalMinutes = "interval_minutes";
        public const string LookaheadDays = "lookahead_days";
        public const string MaxEventsPerChannel = "max_events_per_channel";
        public const string EventLanguage = "event_language";
        public const string DryRun = "dry_run";

        /// <summary>Present once the one-time import from the environment has run.</summary>
        public const string ImportDone = "import_done";
    }
}
