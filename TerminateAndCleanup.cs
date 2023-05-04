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
using Microsoft.Extensions.Primitives;

namespace Durable.Crony.Microservice
{
    public static class TerminateAndCleanup
    {
        [FunctionName("CompletionWebhook")]
        public static void CompletionWebhook([EntityTrigger] IDurableEntityContext ctx)
        {
            switch (ctx.OperationName.ToLowerInvariant())
            {
                case "set":
                    ctx.SetState(ctx.GetInput<Webhook>());
                    break;
                case "get":
                    ctx.Return(ctx.GetState<Webhook>());
                    break;
            }
        }

        [Deterministic]
        [FunctionName("OrchestrateCompletionWebook")]
        public static async Task OrchestrateCompletionWebook([OrchestrationTrigger] IDurableOrchestrationContext context,
                                                             ILogger logger)
        {
            string name = context.GetInput<string>();

            EntityId webhookId = new("CompletionWebhook", name);

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

                foreach(var h in webhook.Headers)
                {
                    durquest.Headers.Add(h.Key, h.Value);
                }

                await context.CallHttpAsync(durquest);
            }
        }

        public static async Task CompleteTimer(IDurableOrchestrationContext context)//, Webhook webhook)
        {
            await context.CallSubOrchestratorAsync("OrchestrateCompletionWebook", $"Completion_{context.InstanceId}", context.InstanceId);

            await context.CallActivityAsync("StartCleanup", context.InstanceId);
        }

        public static async Task PurgeInstanceHistory(this IDurableOrchestrationContext context,
                                                      string instanceId)
            =>
            await context.CallActivityWithRetryAsync(nameof(PurgeTimer), new Microsoft.Azure.WebJobs.Extensions.DurableTask.RetryOptions(TimeSpan.FromSeconds(5), 10)
            {
                BackoffCoefficient = 2,
                MaxRetryInterval = TimeSpan.FromMinutes(5),
            },
            instanceId);

        [Deterministic]
        [FunctionName("OrchestrateCleanup")]
        public static async Task OrchestrateCleanup([OrchestrationTrigger] IDurableOrchestrationContext context,
                                                    ILogger logger)
        {
            (string instance, int count, bool delete) = context.GetInput<(string, int, bool)>();

            if (count == 20)
            {
                //drop a queue message for delete
                //await context.PurgeInstanceHistory(context.InstanceId);

                return;
            }

            ILogger slog = context.CreateReplaySafeLogger(logger);

            DateTime date = context.CurrentUtcDateTime.AddSeconds(3);

            await context.CreateTimer(date, default);

            bool? isStopped = await context.CallActivityAsync<bool?>(nameof(IsStopped), instance);

            if (!isStopped.HasValue)
            {
                //drop a queue message for delete
                //await context.PurgeInstanceHistory(context.InstanceId);

                slog.LogError("Timer not found to terminate");

                return;
            }

            if (isStopped.Value)
            {
                if (delete)
                {
                    slog.LogError("Deleting timer history: " + instance);

                    var t1 = context.PurgeInstanceHistory($"@completionwebhook@{instance}");
                    var t2 = context.PurgeInstanceHistory($"Completion_{instance}");
                    var t3 = context.PurgeInstanceHistory(instance);

                    await t1;
                    await t2;
                    await t3;

                    //drop a queue message for delete
                    //await context.PurgeInstanceHistory(context.InstanceId);
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

        [FunctionName(nameof(StartCleanup))]
        public static async Task StartCleanup([ActivityTrigger] string timerName,
            [DurableClient] IDurableClient client, ILogger slog)
        {
            await client.StartNewAsync("OrchestrateCleanup", "delete_" + timerName, (timerName, 0, true));

            slog.LogError("Timer delete started: " + timerName);
        }

        [FunctionName(nameof(PurgeTimer))]
        public static async Task PurgeTimer([ActivityTrigger] string timerName,
            [DurableClient] IDurableClient client, ILogger slog)
        {
            await client.PurgeInstanceHistoryAsync(timerName);

            slog.LogError("Timer delete completed: " + timerName);
        }

        [FunctionName(nameof(CancelTimer))]
        public static async Task<HttpResponseMessage> CancelTimer([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "CancelTimer/{timerName}/{delete}")] HttpRequestMessage req,
                                                                  [DurableClient] IDurableOrchestrationClient client,
                                                                  string timerName)//, bool delete)
        {
            await client.StartNewAsync("OrchestrateCleanup", "delete_" + timerName, (timerName, 0, true));

            await client.TerminateAsync(timerName, null);

            return client.CreateCheckStatusResponse(req, "delete_" + timerName);
        }

        /// <summary>
        /// Cleanup timer trigger daily at 01:00
        /// </summary>
        [FunctionName(nameof(CleanupTimerTrigger))]
        public static async Task CleanupTimerTrigger([TimerTrigger("0 0 1 * * *")] TimerInfo myTimer,
                                                     [DurableClient] IDurableOrchestrationClient client,
                                                     ILogger logger)
        {
            try
            {
                //clear non-failed history
                await client.PurgeInstanceHistoryAsync(DateTime.MinValue, DateTime.UtcNow.AddDays(-1),
                    new List<OrchestrationStatus>
                    {
                            OrchestrationStatus.Completed
                    });

                //clear failed history
                await client.PurgeInstanceHistoryAsync(
                    DateTime.MinValue, DateTime.UtcNow.AddDays(-7),
                    new List<OrchestrationStatus>
                    {
                            OrchestrationStatus.Failed
                    });
            }
            catch (Exception ex)
            {
                Exception x = ex.GetBaseException();

                logger.LogError(null, x.Message + " - " + x.GetType().Name, null);
            }
        }
    }
}