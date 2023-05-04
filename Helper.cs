using Crony.Models;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace Crony
{
    public static class Helper
    {
        public static (TimerRetry timer, Webhook webhook) CopyRetryModel(this CronyTimerRetry cronyTimer)
        {
            (Timer timer, Webhook webhook) = CreateBaseTimer(cronyTimer);

            TimerRetry timerRetry = (TimerRetry)timer;

            timerRetry.TimerOptions = cronyTimer.TimerOptions;

            return (timerRetry, webhook);
        }

        public static (TimerCRON, Webhook) CopyCronModel(this CronyTimerCRON cronyTimer)
        {
            (Timer timer, Webhook webhook) = CreateBaseTimer(cronyTimer);

            TimerCRON timerCRON = (TimerCRON)timer;

            timerCRON.CRON = cronyTimer.CRON;

            return (timerCRON, webhook);
        }

        private static (Timer, Webhook) CreateBaseTimer(CronyTimer cronyTimer)
        {
            Webhook webhook = null;

            Timer timer = new()
            {
                Url = cronyTimer.Url,
                Content = cronyTimer.Content,
                HttpMethod = GetHttpMethod(cronyTimer.HttpMethod),
                PollIf202 = cronyTimer.PollIf202,
                RetryOptions = cronyTimer.RetryOptions,
                Timeout = cronyTimer.Timeout,
                StatusCodeReplyForCompletion = (HttpStatusCode)cronyTimer.StatusCodeReplyForCompletion
            };

            if (cronyTimer.CompletionWebhook != null)
            {
                webhook = new()
                {
                    Url = cronyTimer.CompletionWebhook.Url,
                    Content = cronyTimer.Content,
                    HttpMethod = GetHttpMethod(cronyTimer.CompletionWebhook.HttpMethod),
                    PollIf202 = cronyTimer.CompletionWebhook.PollIf202,
                    Timeout = cronyTimer.CompletionWebhook.Timeout,
                    RetryOptions = cronyTimer.CompletionWebhook.RetryOptions
                };

                foreach (var headers in cronyTimer.CompletionWebhook.Headers)
                {
                    webhook.Headers.Add(headers.Key, headers.Value);
                };
            }

            foreach (var headers in cronyTimer.Headers)
            {
                timer.Headers.Add(headers.Key, headers.Value);
            };

            return (timer, webhook);
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
