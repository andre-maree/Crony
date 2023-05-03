using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DurableTask.Core;
using Microsoft.Extensions.Logging;
using Crony.Models;
using Crony;

namespace Durable.Crony.Microservice
{
    public static class TerminateAndCleanup
    {
        [FunctionName("Webhooks")]
        public static void Counter([EntityTrigger] IDurableEntityContext ctx)
        {
            switch (ctx.OperationName.ToLowerInvariant())
            {
                case "set":
                    ctx.SetState(ctx.GetInput<Webhook>());
                    break;
                case "del":
                    ctx.DeleteState();
                    break;
                case "get":
                    ctx.Return(ctx.GetState<Webhook>());
                    break;
            }
        }

        public static async Task CompleteTimer(IDurableOrchestrationContext context)//, Webhook webhook)
        {
            try
            {
                EntityId webhookId = new("Webhooks", context.InstanceId);

                Webhook webhook = await context.CallEntityAsync<Webhook>(webhookId, "get");

                if (webhook != null)
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

                    await context.CallHttpAsync(durquest);

                    await context.CallEntityAsync(webhookId, "del");
                }
            }
            catch (Exception ex)
            {
                // log error
            }

            await context.PurgeInstanceHistory();
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

            if (count == 6)
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

            if (isStopped.Value)
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