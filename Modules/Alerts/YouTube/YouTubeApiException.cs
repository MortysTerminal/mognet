using System.Net;

namespace Mognet.Modules.Alerts.YouTube
{
    /// <summary>
    /// YouTube answered with an error. The message holds the status code and YouTube's reason code (for example
    /// <c>quotaExceeded</c>) and nothing else, so it is safe to log.
    /// </summary>
    public sealed class YouTubeApiException : Exception
    {
        public YouTubeApiException(HttpStatusCode statusCode, string? reason)
            : base($"HTTP {(int)statusCode}{(reason is null ? "" : " " + reason)}")
        {
            StatusCode = statusCode;
            Reason = reason;
        }

        public HttpStatusCode StatusCode { get; }

        public string? Reason { get; }
    }
}
