using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DurableTask.Core;

namespace Durable.Crony.Microservice
{
    public static class TerminateAndCleanup
    {
        public static async Task CompleteTimer(IDurableOrchestrationContext context, HttpObject httpObject)
        {
            Task cleanupTask =  context.CleanupInstanceHistory();

            if (httpObject != null)
            {
                DurableHttpRequest durquest = new(httpObject.HttpMethod.GetHttpMethod(),
                                                  new Uri(httpObject.Url),
                                                  content: httpObject.Content,
                                                  httpRetryOptions: new HttpRetryOptions(TimeSpan.FromSeconds(httpObject.RetryOptions.Interval), httpObject.RetryOptions.MaxNumberOfAttempts)
                                                  {
                                                      BackoffCoefficient = httpObject.RetryOptions.BackoffCoefficient,
                                                      MaxRetryInterval = TimeSpan.FromSeconds(httpObject.RetryOptions.MaxRetryInterval),
                                                      StatusCodesToRetry = httpObject.GetRetryEnabledStatusCodes()
                                                  },
                                                  asynchronousPatternEnabled: httpObject.PollIf202,
                                                  timeout: TimeSpan.FromSeconds(httpObject.Timeout));

                foreach (var headers in httpObject.Headers)
                {
                    durquest.Headers.Add(headers.Key, new(headers.Value));
                }

                await context.CallHttpAsync(durquest);
            }

            await cleanupTask;
        }

        public static async Task CleanupInstanceHistory(this IDurableOrchestrationContext context)
        {
            try
            {
                await context.CallActivityWithRetryAsync("CleanupTimer", new Microsoft.Azure.WebJobs.Extensions.DurableTask.RetryOptions(TimeSpan.FromSeconds(5), 10)
                {
                    BackoffCoefficient = 2,
                    MaxRetryInterval = TimeSpan.FromMinutes(5),
                }, context.InstanceId);
            }
            catch (Exception ex)
            {
                // log instnce history not deleted
            }
        }

        [FunctionName(nameof(CleanupTimer))]
        public static async Task CleanupTimer([ActivityTrigger] string timerName,
            [DurableClient] IDurableClient client)
        {
            await client.PurgeInstanceHistoryAsync(timerName);
        }

        [FunctionName(nameof(DeleteTimer))]
        public static async Task DeleteTimer(
            [HttpTrigger(AuthorizationLevel.Anonymous, "del", Route = "DeleteTimer/{timerName}")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient client,
                string timerName)
        {
            await client.TerminateAsync(timerName, null);

            await client.PurgeInstanceHistoryAsync(timerName);
        }

        /// <summary>
        /// Cleanup timer trigger daily at 23:00
        /// </summary>
        [FunctionName(nameof(CleanupTimerTrigger))]
        public static async Task CleanupTimerTrigger([TimerTrigger("0 0 23 * * *")] TimerInfo myTimer, [DurableClient] IDurableOrchestrationClient client)
        {
            //clear non-failed history
            //await client.PurgeInstanceHistoryAsync(DateTime.MinValue, DateTime.UtcNow.AddDays(-1),
            //    new List<OrchestrationStatus>
            //    {
            //                OrchestrationStatus.Completed
            //    });

            //clear failed history
            await client.PurgeInstanceHistoryAsync(
                DateTime.MinValue, DateTime.UtcNow.AddDays(-7),
                new List<OrchestrationStatus>
                {
                            OrchestrationStatus.Failed
                });
        }
    }
}