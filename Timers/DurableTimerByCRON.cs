using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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

            (TimerCRON timerObject, int count) = context.GetInput<(TimerCRON, int)>();

            if (timerObject.MaxNumberOfAttempts <= count)
            {
#if DEBUG
                slog.LogCronDone(context.InstanceId);
#endif

                await TerminateAndCleanup.CompleteTimer(context);

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

                await TerminateAndCleanup.CompleteTimer(context);

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

                context.ContinueAsNew((timerObject, count));
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

        //http://localhost:7078/SetTimerByCRON/cron-ZZZ
        [FunctionName("SetTimerByCRON")]
        public static async Task<HttpResponseMessage> SetTimerByCRON([HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "SetTimerByCRON/{timerName}")] HttpRequestMessage req,
                                                                     [DurableClient] IDurableClient client,
                                                                     string timerName,
                                                                     ILogger log)
        {
            //string[] arr = new 
            bool? isStopped = await TerminateAndCleanup.IsStopped(timerName, client);

            if (isStopped.HasValue && !isStopped.Value)
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }

            if (req.Method == HttpMethod.Get)
            {
                log.LogCronStart(timerName);

                TimerCRON GETtimer = new()
                {
                    Content = "wappa",
                    Url = "https://reqbin.com/sample/get/json",
                    HttpMethod = HttpMethod.Get,
                    //CRON = "0 0/1 * * * ?",
                    //CRON = "0 5,55 12,13 1 MAY ? 2023", meeting reminder
                    CRON = "0/15 * * ? * * *",
                    MaxNumberOfAttempts = 7,
                    RetryOptions = new()
                    {
                        BackoffCoefficient = 1.2,
                        MaxRetryInterval = 360,
                        MaxNumberOfAttempts = 20,
                        Interval = 5
                        //RetryTimeout
                    }
                };

                if (GETtimer.RetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                Webhook completionWebhook = new()
                {
                    HttpMethod = HttpMethod.Get,
                    Url = "https://reqbin.com/sample/get/json",
                    RetryOptions = new()
                    {
                        BackoffCoefficient = 1.5,
                        Interval = 10,
                        MaxNumberOfAttempts = 5,
                        MaxRetryInterval = 30
                    }
                };

                EntityId webhookId = new("CompletionWebhook", timerName);

                await client.SignalEntityAsync(webhookId, "set", operationInput: completionWebhook);

                await client.StartNewAsync("OrchestrateTimerByCRON", timerName, (GETtimer, 0));

                return client.CreateCheckStatusResponse(req, timerName);
            }
            else
            {
                CronyTimerCRON timerModel = JsonConvert.DeserializeObject<CronyTimerCRON>(await req.Content.ReadAsStringAsync());

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

                if (webhook != null)
                {
                    EntityId webhookId = new("Webhooks", timerName);

                    await client.SignalEntityAsync(webhookId, "set", operationInput: webhook);
                }

                await client.StartNewAsync("OrchestrateTimerByCRON", timerName, (timer, 0));

                return client.CreateCheckStatusResponse(req, timerName);
            }
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