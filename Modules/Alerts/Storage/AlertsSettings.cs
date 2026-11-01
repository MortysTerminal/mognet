namespace Mognet.Modules.Alerts.Storage
{
    /// <summary>Scope and keys of the alerts module's rows in the shared settings table.</summary>
    public static class AlertsSettings
    {
        public const string Scope = AlertsModule.ModuleId;

        /// <summary>Minutes between two poll cycles.</summary>
        public const string IntervalMinutes = "interval_minutes";

        /// <summary>Minutes between two searches for scheduled streams, per source.</summary>
        public const string SearchIntervalMinutes = "search_interval_minutes";

        public const int DefaultIntervalMinutes = 5;
        public const int DefaultSearchIntervalMinutes = 30;

        /// <summary>One day; also keeps the wait below the limit of <see cref="Task.Delay(TimeSpan)"/>.</summary>
        public const int MaxIntervalMinutes = 1440;
    }
}
