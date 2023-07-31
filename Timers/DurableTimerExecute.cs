using System;
using System.Net;
using System.Threading.Tasks;
using Crony.Models;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace Crony.Timers
{
    public static class DurableTimerExecute
    {
        [Deterministic]
        public static async Task<HttpStatusCode> ExecuteTimer(this Webhook webhook,
                                                              IDurableOrchestrationContext context,
                                                              DateTime deadline)
        {
            try
            {
                HttpStatusCode code = await webhook.ExecuteTimer(context);

                context.SetCustomStatus($"{code} - {deadline}");

                return code;
            }
            catch (Exception ex)
            {
                context.SetCustomStatus(ex.GetBaseException().Message);

                throw;
            }
        }

        [Deterministic]
        private static async Task<HttpStatusCode> ExecuteTimer(this Webhook webhook,
                                                               IDurableOrchestrationContext context)
        {
            DurableHttpRequest durquest = new(webhook.HttpMethod,
                                              new Uri(webhook.Url),
                                              content: webhook.Content,
                                              httpRetryOptions: new HttpRetryOptions(TimeSpan.FromSeconds(webhook.RetryOptions.Interval), webhook.RetryOptions.MaxNumberOfAttempts)
                                              {
                                                  BackoffCoefficient = webhook.RetryOptions.BackoffCoefficient,
                                                  MaxRetryInterval = TimeSpan.FromSeconds(webhook.RetryOptions.MaxRetryInterval),
                                                  StatusCodesToRetry = webhook.GetRetryEnabledStatusCodes()
                                              },
                                              asynchronousPatternEnabled: webhook.PollIf202,
                                              timeout: TimeSpan.FromSeconds(webhook.Timeout)); 
            
            foreach (var h in webhook.Headers)
            {
                durquest.Headers.Add(h.Key, h.Value);
            }

            DurableHttpResponse response = await context.CallHttpAsync(durquest);

            return response.StatusCode;
        }
    }
}