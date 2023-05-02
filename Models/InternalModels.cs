using System.Collections.Generic;
using System.Net.Http;

namespace Crony.Models
{
    public class Webhook
    {
        public string Url { get; set; }
        public int Timeout { get; set; } = 15;
        public Dictionary<string, Microsoft.Extensions.Primitives.StringValues> Headers { get; set; } = new();
        public HttpMethod HttpMethod { get; set; }
        public string Content { get; set; }
        public bool PollIf202 { get; set; }
        public int StatusCodeReplyForCompletion { get; set; }
        public Webhook CompletionWebhook { get; set; }
        public RetryOptions RetryOptions { get; set; }
    }

    //public class Timer : Webhook
    //{
    //    public int StatusCodeReplyForCompletion { get; set; }
    //    public Webhook CompletionWebhook { get; set; }
    //}

    public class TimerCRON : Webhook
    {
        public string CRON { get; set; }
        public int MaxNumberOfAttempts { get; set; }
    }

    public class TimerRetry : Webhook
    {
        public RetryOptions TimerOptions { get; set; }
    }
}
