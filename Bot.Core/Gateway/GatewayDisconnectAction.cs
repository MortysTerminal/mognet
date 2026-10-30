namespace Mognet.Bot.Core.Gateway
{
    public enum GatewayDisconnectAction
    {
        /// <summary>The library reconnects by itself; the bot only logs.</summary>
        Reconnect,

        /// <summary>Reconnecting cannot help: the bot must stop.</summary>
        Fatal,
    }
}
