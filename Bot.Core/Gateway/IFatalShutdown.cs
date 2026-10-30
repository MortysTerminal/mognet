namespace Mognet.Bot.Core.Gateway
{
    /// <summary>Ends the process with a failure exit code, so the container is restarted.</summary>
    public interface IFatalShutdown
    {
        void Shutdown();
    }
}
