using Crony.Models;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace Crony
{
    public static class Helper
    {
        public static TimerRetry CopyRetryModel(this CronyTimerRetry cronyTimer)
        {
            TimerRetry timerRetry = new()
            {
                Url = cronyTimer.Url,
                Content = cronyTimer.Content,
                HttpMethod = GetHttpMethod(cronyTimer.HttpMethod),
                PollIf202 = cronyTimer.PollIf202,
                RetryOptions = cronyTimer.RetryOptions,
                Timeout = cronyTimer.Timeout,
                CompletionWebhook = new()
                {
                    Content = cronyTimer.Content,
                    HttpMethod = GetHttpMethod(cronyTimer.CompletionWebhook.HttpMethod),
                    PollIf202 = cronyTimer.PollIf202,
                    RetryOptions = cronyTimer.RetryOptions,
                    Timeout = cronyTimer.Timeout,
                    Url = cronyTimer.Url
                }
            };

            foreach (var headers in cronyTimer.Headers)
            {
                timerRetry.Headers.Add(headers.Key, new(headers.Value));
            };

            foreach (var headers in cronyTimer.CompletionWebhook.Headers)
            {
                timerRetry.CompletionWebhook.Headers.Add(headers.Key, new(headers.Value));
            };

            return timerRetry;
        }

        public static TimerCRON CopyCronModel(this CronyTimerCRON cronyTimer)
        {
            TimerCRON timerCRON = new()
            {
                Url = cronyTimer.Url,
                Content = cronyTimer.Content,
                HttpMethod = GetHttpMethod(cronyTimer.HttpMethod),
                PollIf202 = cronyTimer.PollIf202,
                RetryOptions = cronyTimer.RetryOptions,
                Timeout = cronyTimer.Timeout,
                CRON = cronyTimer.CRON,
                MaxNumberOfAttempts = cronyTimer.MaxNumberOfAttempts,
                StatusCodeReplyForCompletion = cronyTimer.StatusCodeReplyForCompletion,
                CompletionWebhook = new()
                {
                    Content = cronyTimer.Content,
                    HttpMethod = GetHttpMethod(cronyTimer.CompletionWebhook.HttpMethod),
                    PollIf202 = cronyTimer.PollIf202,
                    RetryOptions = cronyTimer.RetryOptions,
                    Timeout = cronyTimer.Timeout,
                    Url = cronyTimer.Url
                }
            };

            foreach (var headers in cronyTimer.Headers)
            {
                timerCRON.Headers.Add(headers.Key, new(headers.Value));
            };

            foreach (var headers in cronyTimer.CompletionWebhook.Headers)
            {
                timerCRON.CompletionWebhook.Headers.Add(headers.Key, new(headers.Value));
            };

            return timerCRON;
        }

        private static HttpMethod GetHttpMethod(this string method) => method[..2].ToUpper()
        switch
        {
            "GE" => HttpMethod.Get,
            "PO" => HttpMethod.Post,
            "PU" => HttpMethod.Put,
            "DE" => HttpMethod.Delete,
            _ => HttpMethod.Get
        };

        public static List<HttpStatusCode> GetRetryEnabledStatusCodes(this Webhook webhook) => new()
        {
            HttpStatusCode.Conflict, HttpStatusCode.BadGateway, HttpStatusCode.GatewayTimeout, HttpStatusCode.RequestTimeout, HttpStatusCode.ServiceUnavailable
        };
    }
}
