namespace Mognet.Bot.Core.Gateway
{
    /// <summary>Start and stop of the gateway connection, so the order of start-up steps can be tested.</summary>
    public interface IGateway
    {
        Task StartAsync(CancellationToken cancellationToken);

        Task StopAsync(CancellationToken cancellationToken);
    }
}
