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
        public RetryOptions RetryOptions { get; set; }
    }

    public class Timer : Webhook
    {
        public int StatusCodeReplyForCompletion { get; set; }
        public Webhook CompletionWebhook { get; set; }
    }

    public class TimerCRON : Timer
    {
        public string CRON { get; set; }
        public int MaxNumberOfAttempts { get; set; }
    }

    public class TimerRetry : Timer
    {
        public RetryOptions TimerOptions { get; set; }
    }

    public class RetryOptions
    {
        public int Interval { get; set; }
        public int MaxRetryInterval { get; set; }
        public int MaxNumberOfAttempts { get; set; }
        public double BackoffCoefficient { get; set; }
    }
}
