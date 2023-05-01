using System;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Quartz;
using static Microsoft.AspNetCore.Hosting.Internal.HostingApplication;

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

            (CronyTimerByCRON timerObject, int count, DateTime deadline) = context.GetInput<(CronyTimerByCRON, int, DateTime)>(); 
            
            if (timerObject.MaxNumberOfAttempts <= count)
            {
                slog.LogCronDone(context.InstanceId);

                await TerminateAndCleanup.CompleteTimer(context, timerObject.CompletionWebhook);

                return;
            }

            CronExpression expression = new(timerObject.CRON);

            deadline = context.CurrentUtcDateTime.AddSeconds(1);//.AddSeconds(20)

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

                context.ContinueAsNew((timerObject, count, deadline));
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
                                                                     [DurableClient] IDurableOrchestrationClient starter,
                                                                     string timerName,
                                                                     ILogger log)
        {
            //List<HttpStatusCode> codes = new()
            //{
            //    HttpStatusCode.Conflict, HttpStatusCode.BadGateway, HttpStatusCode.GatewayTimeout, HttpStatusCode.RequestTimeout, HttpStatusCode.ServiceUnavailable
            //};

            if (req.Method == HttpMethod.Get)
            {
                log.LogCronStart(timerName);

                CronyTimerByCRON GETtimer = new()
                {
                    Content = "wappa",
                    Url = "https://reqbin.com/sample/get/json",
                    HttpMethod = "get",
                    //CRON = "0 0/1 * * * ?",
                    //CRON = "0 5,55 12,13 1 MAY ? 2023", meeting reminder
                    CRON = "0/30 * * ? * * *",
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

                await starter.StartNewAsync("OrchestrateTimerByCRON", timerName, (GETtimer, 0));

                return starter.CreateCheckStatusResponse(req, timerName);
            }
            else
            {
                CronyTimerByCRON timer = JsonConvert.DeserializeObject<CronyTimerByCRON>(await req.Content.ReadAsStringAsync());

                CronExpression expression = new(timer.CRON);

                if (expression.GetNextValidTimeAfter(DateTime.UtcNow) == null)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                if (timer.RetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                await starter.StartNewAsync("OrchestrateTimerByCRON", timerName, (timer, 0));

                return starter.CreateCheckStatusResponse(req, timerName);
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