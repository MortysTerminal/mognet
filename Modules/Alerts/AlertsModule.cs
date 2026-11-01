using Mognet.Bot.Core.Modules;
using Mognet.Bot.Core.Storage;
using Mognet.Modules.Alerts.Storage;

namespace Mognet.Modules.Alerts
{
    /// <summary>
    /// Alerts for new and upcoming content on other platforms. It owns the tables for sources and known content,
    /// which are created at start-up whether or not the module is switched on. Switched on, it does nothing yet:
    /// there is no polling, no rule and no message.
    /// </summary>
    public sealed class AlertsModule : IBotModule
    {
        public const string ModuleId = "alerts";

        public string Id => ModuleId;

        public MigrationSet? Migrations => AlertsMigrations.Set;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
