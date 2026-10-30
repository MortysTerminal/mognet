using Discord.Net;

namespace Mognet.Bot.Core.Gateway
{
    /// <summary>
    /// Decides from a disconnect exception whether the client keeps reconnecting or the bot must stop.
    /// A missing exception, a missing close code or an unknown close code means reconnect.
    /// </summary>
    public static class GatewayDisconnectPolicy
    {
        // 4004 authentication failed, 4010 invalid shard, 4011 sharding required, 4012 invalid API version,
        // 4013 invalid intents, 4014 disallowed intents: Discord documents these as not reconnectable.
        // 4006: the library ends its own reconnect loop for this code, so only a restart brings the bot back.
        private static readonly HashSet<int> FatalCloseCodes = [4004, 4006, 4010, 4011, 4012, 4013, 4014];

        public static GatewayDisconnectAction Classify(Exception? exception)
        {
            var code = FindCloseCode(exception);
            return code is { } c && FatalCloseCodes.Contains(c) ? GatewayDisconnectAction.Fatal : GatewayDisconnectAction.Reconnect;
        }

        // The library wraps the close exception, so the whole chain is searched.
        private static int? FindCloseCode(Exception? exception)
        {
            while (exception is not null)
            {
                if (exception is WebSocketClosedException closed)
                    return closed.CloseCode;

                if (exception is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        if (FindCloseCode(inner) is { } found)
                            return found;
                    }
                }

                exception = exception.InnerException;
            }

            return null;
        }
    }
}
