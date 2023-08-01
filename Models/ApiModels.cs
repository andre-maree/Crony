using System;
using System.Collections.Generic;

namespace Crony.Models
{
    public class CronyWebhook
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public int Timeout { get; set; } = 15;
        public Dictionary<string, string[]> Headers { get; set; } = new();
        public string HttpMethod { get; set; }
        public string Content { get; set; }
        public bool PollIf202 { get; set; }
        public RetryOptions RetryOptions { get; set; }
    }

    public class CronyTimer : CronyWebhook
    {
        public int StatusCodeReplyForCompletion { get; set; }
        public CronyWebhook CompletionWebhook { get; set; }
    }

#if DEBUG_NORETRY || RELEASE_NORETRY || DEBUG || RELEASE
    public class CronyTimerCRON : CronyTimer
    {
        public string CRON { get; set; }
        public int MaxNumberOfAttempts { get; set; }
    }
#endif

#if DEBUG_NOCRON || RELEASE_NOCRON || DEBUG || RELEASE
    public class CronyTimerRetry : CronyTimer
    {
        public RetryOptions TimerOptions { get; set; }
    }
#endif

    public class RetryOptions
    {
        public int Interval { get; set; }
        public int MaxRetryInterval { get; set; }
        public int MaxNumberOfAttempts { get; set; }
        public double BackoffCoefficient { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
