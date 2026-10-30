using Mognet.Bot.Core.Storage;

namespace Mognet.Bot.Core.Modules
{
    /// <summary>
    /// A compiled-in feature that can be switched on and off while the bot runs. Its migrations are applied at
    /// start-up whether or not it is switched on.
    /// </summary>
    public interface IBotModule
    {
        /// <summary>Stable id: lower-case letters, digits and underscores, starting with a letter. <c>core</c> is reserved.</summary>
        string Id { get; }

        /// <summary>Tables owned by the module, or <see langword="null"/> when it has none.</summary>
        MigrationSet? Migrations { get; }

        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>Releases everything <see cref="StartAsync"/> acquired, so the module can be started again.</summary>
        Task StopAsync(CancellationToken cancellationToken);
    }
}
