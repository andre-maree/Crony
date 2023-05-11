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
            (Timer timer, Webhook webhook) = CreateBaseTimer(cronyTimer, true);

            TimerRetry timerRetry = timer as TimerRetry;

            timerRetry.TimerOptions = cronyTimer.TimerOptions;

            return (timerRetry, webhook);
        }

        public static (TimerCRON, Webhook) CopyCronModel(this CronyTimerCRON cronyTimer)
        {
            (Timer timer, Webhook webhook) = CreateBaseTimer(cronyTimer, false);

            TimerCRON timerCRON = timer as TimerCRON;

            timerCRON.CRON = cronyTimer.CRON;
            timerCRON.MaxNumberOfAttempts = cronyTimer.MaxNumberOfAttempts;

            return (timerCRON, webhook);
        }

        private static (Timer, Webhook) CreateBaseTimer(CronyTimer cronyTimer, bool isRetry)
        {
            Webhook webhook = null;
            Timer timer;

            if (isRetry)
            {
                timer = new TimerRetry();
            }
            else
            {
                timer = new TimerCRON();
            }

            timer.Url = cronyTimer.Url;
            timer.Content = cronyTimer.Content;
            timer.HttpMethod = cronyTimer.HttpMethod.GetHttpMethod();
            timer.PollIf202 = cronyTimer.PollIf202;
            timer.RetryOptions = cronyTimer.RetryOptions;
            timer.Timeout = cronyTimer.Timeout;
            timer.StatusCodeReplyForCompletion = (HttpStatusCode)cronyTimer.StatusCodeReplyForCompletion;

            if (cronyTimer.CompletionWebhook != null)
            {
                webhook = new()
                {
                    Url = cronyTimer.CompletionWebhook.Url,
                    Content = cronyTimer.Content,
                    HttpMethod = cronyTimer.CompletionWebhook.HttpMethod.GetHttpMethod(),
                    PollIf202 = cronyTimer.CompletionWebhook.PollIf202,
                    Timeout = cronyTimer.CompletionWebhook.Timeout,
                    RetryOptions = cronyTimer.CompletionWebhook.RetryOptions
                };

                if (cronyTimer.CompletionWebhook != null && cronyTimer.CompletionWebhook.Headers != null)
                {
                    foreach (var headers in cronyTimer.CompletionWebhook.Headers)
                    {
                        webhook.Headers.Add(headers.Key, headers.Value);
                    };
                }
            }

            if (cronyTimer.Headers != null)
            {
                foreach (var headers in cronyTimer.Headers)
                {
                    timer.Headers.Add(headers.Key, headers.Value);
                };
            }

            return (timer, webhook);
        }

        private static HttpMethod GetHttpMethod(this string method) => method[..2].ToUpper()
        switch
        {
            "GE" => HttpMethod.Get,
            "PO" => HttpMethod.Post,
            "PU" => HttpMethod.Put,
            "DE" => HttpMethod.Delete,
            "PA" => HttpMethod.Patch,
            "OP" => HttpMethod.Options,
            "HE" => HttpMethod.Head,
            _ => HttpMethod.Get
        };

        public static List<HttpStatusCode> GetRetryEnabledStatusCodes(this Webhook webhook) => new()
        {
            HttpStatusCode.Conflict, HttpStatusCode.BadGateway, HttpStatusCode.GatewayTimeout, HttpStatusCode.RequestTimeout, HttpStatusCode.ServiceUnavailable
        };
    }
}
