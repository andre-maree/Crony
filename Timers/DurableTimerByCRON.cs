using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Crony.Models;
using Durable.Crony.Microservice;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Quartz;

namespace Crony.Timers
{
    public static class DurableTimerByCRON
    {
        [Deterministic]
        [FunctionName("OrchestrateTimerByCRON")]
        public static async Task OrchestrateTimerByCRON([OrchestrationTrigger] IDurableOrchestrationContext context,
                                                        ILogger logger)
        {
#if DEBUG
            ILogger slog = context.CreateReplaySafeLogger(logger);
#endif

            (TimerCRON timerObject, int count, bool hasWebhook) = context.GetInput<(TimerCRON, int, bool)>();

            if (timerObject.MaxNumberOfAttempts <= count)
            {
#if DEBUG
                slog.LogCronDone(context.InstanceId);
#endif

                if (hasWebhook)
                {
                    await TerminateAndCleanup.CompleteTimer(context);
                }

                return;
            }

            CronExpression expression = new(timerObject.CRON);

            DateTime deadline = context.CurrentUtcDateTime.AddMilliseconds(2500);//.AddSeconds(20)

            DateTimeOffset? nextFireUTCTime = expression.GetNextValidTimeAfter(deadline);

            if (nextFireUTCTime == null)
            {
#if DEBUG
                slog.LogCronDone(context.InstanceId);
#endif

                if (hasWebhook)
                {
                    await TerminateAndCleanup.CompleteTimer(context);
                }

                return;
            }

            deadline = nextFireUTCTime.Value.UtcDateTime;

#if DEBUG
            slog.LogCronNext(context.InstanceId, deadline);
#endif

            await context.CreateTimer(deadline, default);

#if DEBUG
            slog.LogCronTimer(context.InstanceId, context.CurrentUtcDateTime);
#endif

            try
            {
                if (await timerObject.ExecuteTimer(context, deadline) == timerObject.StatusCodeReplyForCompletion)
                {
#if DEBUG
                    slog.LogCronDone(context.InstanceId);
#endif

                    await TerminateAndCleanup.CompleteTimer(context);

                    return;
                }

                count++;

                context.ContinueAsNew((timerObject, count, hasWebhook));
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == null)
                {
                    context.SetCustomStatus($"{ex.Message}");
                }
                else
                {
                    context.SetCustomStatus($"{ex.StatusCode} - {ex.Message}");
                }
            }
        }

        [FunctionName("SetTimerByCRON")]
        public static async Task<HttpResponseMessage> SetTimerByCRON([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "SetTimerByCRON")] HttpRequestMessage req,
                                                                     [DurableClient] IDurableClient client,
                                                                     ILogger log)
        {
            CronyTimerCRON timerModel = JsonConvert.DeserializeObject<CronyTimerCRON>(await req.Content.ReadAsStringAsync());

            bool? isStopped = await TerminateAndCleanup.IsReady(timerModel.Name, client);

            if (isStopped.HasValue && !isStopped.Value)
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }

            CronExpression expression = new(timerModel.CRON);

            if (expression.GetNextValidTimeAfter(DateTime.UtcNow) == null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            if (timerModel.RetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            (TimerCRON timer, Webhook webhook) = timerModel.CopyCronModel();

            bool hasWebhook = webhook != null;

            if (hasWebhook)
            {
                BlobServiceClient service = new(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));

                BlobContainerClient container = service.GetBlobContainerClient("crony-webhooks");

                BlobClient blob = container.GetBlobClient(timerModel.Name);

                try
                {
                    await blob.UploadAsync(BinaryData.FromObjectAsJson(webhook));
                }
                catch (Azure.RequestFailedException ex)
                {
                    if (ex.Status == 404)
                    {
                        await container.CreateIfNotExistsAsync();

                        await blob.UploadAsync(BinaryData.FromObjectAsJson(webhook));
                    }
                }
            }

            log.LogCronStart(timerModel.Name);

            await client.StartNewAsync("OrchestrateTimerByCRON", timerModel.Name, (timer, 0, hasWebhook));

            return client.CreateCheckStatusResponse(req, timerModel.Name);
        }

        #region Logging

        private static void LogCronStart(this ILogger logger, string text)
        {
            logger.LogError($"CRON: START {text} - {DateTime.UtcNow}");
        }

#if DEBUG
        private static void LogCronNext(this ILogger logger, string text, DateTime now)
        {
            logger.LogWarning($"CRON: NEXT >>> {text} - {now}");
        }

        private static void LogCronTimer(this ILogger logger, string text, DateTime now)
        {
            logger.LogCritical($"CRON: EXECUTING {text} - {now}");
        }

        private static void LogCronDone(this ILogger logger, string text)
        {
            logger.LogError($"CRON: DONE {text}");
        }
#endif

        #endregion
    }
}