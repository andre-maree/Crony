using System.Collections.Generic;

namespace Durable.Crony.Microservice
{
    //public class Status
    //{
    //    public string RuntimeStatus { get; set; }
    //}
    public class CronyTimer
    {
        public string Url { get; set; }
        public int Timeout { get; set; } = 15;
        public Dictionary<string, string[]> Headers { get; set; } = new();
        public bool IsHttpGet { get; set; }
        public string Content { get; set; }
        public bool PollIf202 { get; set; }
        public RetryOptions WebhookRetryOptions { get; set; }
    }

    public class CronyTimerByCRON : CronyTimer
    {
        public string CRON { get; set; }
    }

    public class CronyTimerByRetry : CronyTimer
    {
        public RetryOptions TimerRetryOptions { get; set; }
    }

    public class RetryOptions
    {
        public int Interval { get; set; }
        public int MaxRetryInterval { get; set; }
        public int MaxNumberOfAttempts { get; set; }
        public double BackoffCoefficient { get; set; }
    }
}
