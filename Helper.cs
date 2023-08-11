using Crony.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace Crony
{
    public static class Helper
    {
#if DEBUG_NOCRON || RELEASE_NOCRON || DEBUG || RELEASE
        public static (TimerRetry timer, Webhook webhook) CopyRetryModel(this CronyTimerRetry cronyTimer)
        {
            (Timer timer, Webhook webhook) = CreateBaseTimer(cronyTimer, true);

            TimerRetry timerRetry = timer as TimerRetry;

            timerRetry.TimerOptions = cronyTimer.TimerOptions;
            timerRetry.TimerOptions.EndDate = cronyTimer.TimerOptions.EndDate ?? DateTime.MaxValue;

            return (timerRetry, webhook);
        }
#endif

#if DEBUG_NORETRY || RELEASE_NORETRY || DEBUG || RELEASE
        public static (TimerCRON, Webhook) CopyCronModel(this CronyTimerCRON cronyTimer)
        {
            (Timer timer, Webhook webhook) = CreateBaseTimer(cronyTimer, false);

            TimerCRON timerCRON = timer as TimerCRON;

            timerCRON.CRON = cronyTimer.CRON;
            timerCRON.MaxNumberOfAttempts = cronyTimer.MaxNumberOfAttempts;

            return (timerCRON, webhook);
        }
#endif

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

        public static string ValidateRetryOptions(RetryOptions timerRetryRO, string type)
        {
            if (timerRetryRO.Interval < 1)
            {
                return $"{type} interval must be set to more than 0.";
            }

            if (timerRetryRO.BackoffCoefficient <= 0)
            {
                return $"{type} backoff coefficient must be greater than 0.";
            }

            if (timerRetryRO.MaxNumberOfAttempts < 1)
            {
                return $"{type} MaxNumberOfAttempts must have a minimum of 1.";
            }

            if (timerRetryRO.MaxRetryInterval < 1)
            {
                return $"{type} MaxRetryInterval must have a minimum of 1.";
            }

            if (timerRetryRO.EndDate != null && timerRetryRO.EndDate < DateTime.UtcNow)
            {
                return $"{type} end date must be in the future.";
            }

            return null;
        }

        public static string ValidateBase(CronyTimer timerModel)
        {
            string error;

            if (string.IsNullOrWhiteSpace(timerModel.Name))
            {
                return "The timer name is invalid.";
            }

            if (!Uri.IsWellFormedUriString(timerModel.Url, UriKind.Absolute))
            {
                return "The timer has an invalid timer url.";
            }

            if (timerModel.Timeout < 1)
            {
                return "The timer must have a timeout bigger than 0.";
            }

            error = ValidateRetryOptions(timerModel.RetryOptions, "Timer RetryOptions");

            if (error != null)
            {
                return error;
            }

            //-----------------------------------------------------------
            if (timerModel.CompletionWebhook != null)
            {
                if (timerModel.CompletionWebhook.RetryOptions == null)
                {
                    return "The completion webhook RetryOptions can not be null.";
                }

                error = ValidateRetryOptions(timerModel.CompletionWebhook.RetryOptions, "CompletionWebhook RetryOptions");

                if (error != null)
                {
                    return error;
                }

                if (!Uri.IsWellFormedUriString(timerModel.CompletionWebhook.Url, UriKind.Absolute))
                {
                    return "The completion webhook must have a valid url.";
                }

                if (timerModel.CompletionWebhook.Timeout < 1)
                {
                    return "The completion webhook must have a timeout bigger than 0.";
                }
            }

            return null;
        }

        public static HttpResponseMessage Error(string error)
        {
            return new(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(error)
            };
        }
    }
}
