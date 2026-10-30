namespace Mognet.Bot.Core.Storage
{
    /// <summary>A migration failed or the database is not compatible with this build. The bot must not start.</summary>
    public class MigrationException : InvalidOperationException
    {
        public MigrationException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
