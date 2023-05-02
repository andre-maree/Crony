using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Crony;
using Crony.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Quartz;

namespace Durable.Crony.Microservice
{
    public static class DurableTimerByCRON
    {
        [Deterministic]
        [FunctionName("OrchestrateTimerByCRON")]
        public static async Task OrchestrateTimerByCRON(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger logger)
        {
            ILogger slog = context.CreateReplaySafeLogger(logger);

            (TimerCRON timerObject, int count) = context.GetInput<(TimerCRON, int)>(); 
            
            if (timerObject.MaxNumberOfAttempts <= count)
            {
                slog.LogCronDone(context.InstanceId);

                await TerminateAndCleanup.CompleteTimer(context, timerObject.CompletionWebhook);

                return;
            }

            CronExpression expression = new(timerObject.CRON);

            DateTime deadline = context.CurrentUtcDateTime.AddMilliseconds(2500);//.AddSeconds(20)

            DateTimeOffset? nextFireUTCTime = expression.GetNextValidTimeAfter(deadline);

            if (nextFireUTCTime == null)
            {
                slog.LogCronDone(context.InstanceId);

                await TerminateAndCleanup.CompleteTimer(context, timerObject.CompletionWebhook);

                return;
            }

            deadline = nextFireUTCTime.Value.UtcDateTime;

            slog.LogCronNext(context.InstanceId, deadline);

            await context.CreateTimer(deadline, default);

            slog.LogCronTimer(context.InstanceId, context.CurrentUtcDateTime);

            count++;

            try
            {
                if ((int)await timerObject.ExecuteTimer(context, deadline) == timerObject.StatusCodeReplyForCompletion)
                {
                    slog.LogCronDone(context.InstanceId);

                    await TerminateAndCleanup.CompleteTimer(context, timerObject.CompletionWebhook);

                    return;
                }

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
                    MaxNumberOfAttempts = 20,
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

                TimerCRON timer = timerModel.CopyCronModel();

                await client.StartNewAsync("OrchestrateTimerByCRON", timerName, (timer, 0));

                return client.CreateCheckStatusResponse(req, timerName);
            }
        }

        #region Logging
        private static void LogCronStart(this ILogger logger, string text)
        {
#if DEBUG
            logger.LogError($"CRON: START {text} - {DateTime.Now}");
#endif
        }

        private static void LogCronNext(this ILogger logger, string text, DateTime now)
        {
#if DEBUG
            logger.LogWarning($"CRON: NEXT >>> {text} - {now}");
#endif
        }

        private static void LogCronTimer(this ILogger logger, string text, DateTime now)
        {
#if DEBUG
            logger.LogCritical($"CRON: EXECUTING {text} - {now}");
#endif
        }

        private static void LogCronDone(this ILogger logger, string text)
        {
#if DEBUG
            logger.LogError($"CRON: DONE {text}");
#endif
        }
        #endregion
    }
}