using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DurableTask.Core;
using Microsoft.Extensions.Logging;

namespace Durable.Crony.Microservice
{
    public static class TerminateAndCleanup
    {
        public static async Task CompleteTimer(IDurableOrchestrationContext context, CronyWebhook httpObject)
        {
            Task cleanupTask = context.PurgeInstanceHistory();

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

        public static async Task PurgeInstanceHistory(this IDurableOrchestrationContext context)
        {
                await context.CallActivityWithRetryAsync(nameof(PurgeTimer), new Microsoft.Azure.WebJobs.Extensions.DurableTask.RetryOptions(TimeSpan.FromSeconds(5), 10)
                {
                    BackoffCoefficient = 2,
                    MaxRetryInterval = TimeSpan.FromMinutes(5),
                }, context.InstanceId);
        }

        [Deterministic]
        [FunctionName("OrchestrateCleanup")]
        public static async Task OrchestrateCleanup(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger logger)
        {
            (string instance, int count, bool delete) = context.GetInput<(string, int, bool)>();

            if(count == 6)
            {
                await context.PurgeInstanceHistory();

                return;
            }

            ILogger slog = context.CreateReplaySafeLogger(logger);

            DateTime date = context.CurrentUtcDateTime.AddSeconds(5);

            await context.CreateTimer(date, default);

            bool? isStopped = await context.CallActivityAsync<bool?>(nameof(IsStopped), instance);

            if (!isStopped.HasValue)
            {
                await context.PurgeInstanceHistory();

                slog.LogError("Timer not found to terminate");

                return;
            }

            if(isStopped.Value)
            {
                if (delete)
                {
                    slog.LogError("Deleting timer history: " + instance);

                    await context.CallActivityAsync(nameof(PurgeTimer), instance);

                    await context.PurgeInstanceHistory(); 
                }

                return;
            }

            count++;

            context.ContinueAsNew((instance, count));
        }

        [FunctionName(nameof(IsStopped))]
        public static async Task<bool?> IsStopped([ActivityTrigger] string timerName,
            [DurableClient] IDurableClient client)
        {
            DurableOrchestrationStatus status = await client.GetStatusAsync(timerName, showInput: false);

            if (status == null)
            {
                return null;
            }

            return status.RuntimeStatus == OrchestrationRuntimeStatus.Terminated
                   || status.RuntimeStatus == OrchestrationRuntimeStatus.Completed
                   || status.RuntimeStatus == OrchestrationRuntimeStatus.Failed;
        }

        [FunctionName(nameof(PurgeTimer))]
        public static async Task PurgeTimer([ActivityTrigger] string timerName,
            [DurableClient] IDurableClient client, ILogger slog)
        {
            await client.PurgeInstanceHistoryAsync(timerName);

            slog.LogError("Timer delete completed: " + timerName);
        }

        [FunctionName(nameof(CancelTimer))]
        public static async Task<HttpResponseMessage> CancelTimer(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "CancelTimer/{timerName}/{delete}")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient client,
                string timerName)//, bool delete)
        {
            await client.StartNewAsync("OrchestrateCleanup", "delete_" + timerName, (timerName, 0, true));

            await client.TerminateAsync(timerName, null);

            return client.CreateCheckStatusResponse(req, "delete_" + timerName);
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