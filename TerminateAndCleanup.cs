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
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Azure.Storage.Blobs.Models;

namespace Durable.Crony.Microservice
{
    public static class TerminateAndCleanup
    {
        //[FunctionName("CompletionWebhook")]
        //public static void CompletionWebhook([EntityTrigger] IDurableEntityContext ctx)
        //{
        //    switch (ctx.OperationName.ToLowerInvariant())
        //    {
        //        case "set":
        //            ctx.SetState(ctx.GetInput<Webhook>());
        //            break;
        //        case "get":
        //            ctx.Return(ctx.GetState<Webhook>());
        //            break;
        //    }
        //}

        [Deterministic]
        [FunctionName("OrchestrateCompletionWebook")]
        public static async Task OrchestrateCompletionWebook([OrchestrationTrigger] IDurableOrchestrationContext context,
                                                             ILogger logger)
        {
            string name = context.GetInput<string>();

            Microsoft.Azure.WebJobs.Extensions.DurableTask.RetryOptions ro = new(TimeSpan.FromSeconds(10), 5)
            {
                BackoffCoefficient = 1.2
            };

            Webhook webhook = await context.CallActivityWithRetryAsync<Webhook>(nameof(GetWebhook), ro, name);

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

            try
            {
                await context.CallHttpAsync(durquest);
            }
            catch(Exception ex)
            {
                context.SetCustomStatus($"Webhook call error: {ex.Message}");
            }

            await context.CallActivityWithRetryAsync<Webhook>(nameof(DeleteWebhook), ro, name);
        }

        public static async Task CompleteTimer(IDurableOrchestrationContext context)
        {
            await context.CallSubOrchestratorAsync("OrchestrateCompletionWebook", $"Completion_{context.InstanceId}", context.InstanceId);
        }

        [FunctionName(nameof(GetWebhook))]
        public static async Task<Webhook> GetWebhook([ActivityTrigger] string timerName)
        {
            BlobServiceClient service = new(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));

            BlobContainerClient container = service.GetBlobContainerClient("crony-webhooks");

            BlobClient blobClient = container.GetBlobClient(timerName);

            BlobDownloadResult downloadResult = await blobClient.DownloadContentAsync();

            return JsonConvert.DeserializeObject<Webhook>(downloadResult.Content.ToString());
        }

        [FunctionName(nameof(DeleteWebhook))]
        public static async Task DeleteWebhook([ActivityTrigger] string timerName)
        {
            BlobServiceClient service = new(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));

            BlobContainerClient container = service.GetBlobContainerClient("crony-webhooks");

            BlobClient blobClient = container.GetBlobClient(timerName);

            await blobClient.DeleteAsync();
        }

        [FunctionName(nameof(IsReady))]
        public static async Task<bool?> IsReady([ActivityTrigger] string timerName,
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

        [FunctionName(nameof(CancelTimer))]
        public static async Task<HttpResponseMessage> CancelTimer([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "CancelTimer/{timerName}")] HttpRequestMessage req,
                                                                  [DurableClient] IDurableOrchestrationClient client,
                                                                  string timerName)
        {
            await client.TerminateAsync(timerName, null);

            await DeleteWebhook(timerName);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
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
                            OrchestrationStatus.Completed,
                            OrchestrationStatus.Terminated
                    });

                //clear failed history
                await client.PurgeInstanceHistoryAsync(
                    DateTime.MinValue, DateTime.UtcNow.AddDays(-7),
                    new List<OrchestrationStatus>
                    {
                            OrchestrationStatus.Failed,
                            OrchestrationStatus.Canceled
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