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

namespace Durable.Crony.Microservice
{
    public static class DurableTimerByRetry
    {
        [Deterministic]
        [FunctionName("OrchestrateTimerByRetry")]
        public static async Task OrchestrateTimerByRetry(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger logger)
        {
            ILogger slog = context.CreateReplaySafeLogger(logger);

            (TimerRetry timerObject, int count, DateTime deadline) = context.GetInput<(TimerRetry, int, DateTime)>();

            if (timerObject.TimerOptions.MaxNumberOfAttempts <= count)
            {
                slog.LogRetryDone(context.InstanceId);

                await TerminateAndCleanup.CompleteTimer(context, timerObject.CompletionWebhook);

                return;
            }

            TimeSpan delay = ComputeNextDelay(timerObject.TimerOptions.Interval, timerObject.TimerOptions.BackoffCoefficient, timerObject.TimerOptions.MaxRetryInterval, count);

            deadline = deadline.Add(delay);

            slog.LogRetryNext(context.InstanceId, deadline);

            await context.CreateTimer(deadline, default);

            DateTime now = context.CurrentUtcDateTime;

            slog.LogRetryTimer(context.InstanceId, now);

            count++;

            try
            {
                if ((int)await timerObject.ExecuteTimer(context, deadline) == timerObject.StatusCodeReplyForCompletion)
                {
                    slog.LogRetryDone(context.InstanceId);

                    await TerminateAndCleanup.CompleteTimer(context, timerObject.CompletionWebhook);

                    return;
                }

                if (now > deadline.AddSeconds(delay.TotalSeconds)) {
                    deadline =  now;
                }

                context.ContinueAsNew((timerObject, count, deadline));//.AddSeconds(timerObject.WebhookRetryOptions.Interval) <= now ? now : deadline));
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

        //http://localhost:7078/SetTimerByRetry/XXX
        [FunctionName("SetTimerByRetry")]
        public static async Task<HttpResponseMessage> SetTimerByRetry(
                [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "SetTimerByRetry/{timerName}")
            ] HttpRequestMessage req,
                [DurableClient] IDurableClient client,
        string timerName,
                ILogger log)
        {
            bool? isStopped = await TerminateAndCleanup.IsStopped(timerName, client);

            if(isStopped.HasValue && !isStopped.Value)
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }

            if (req.Method == HttpMethod.Get)
            {
                log.LogRetryStart(timerName);

                TimerRetry GETtimer = new()
                {
                    Content = "wappa",
                    Url = "https://reqbin.com/sample/get/json",
                    HttpMethod = HttpMethod.Get,
                    StatusCodeReplyForCompletion = 500,
                    TimerOptions = new()
                    {
                        BackoffCoefficient = 1,
                        MaxRetryInterval = 300,
                        MaxNumberOfAttempts = 11,
                        Interval = 10
                        //RetryTimeout
                    },
                    RetryOptions = new()
                    {
                        BackoffCoefficient = 1.2,
                        MaxRetryInterval = 360,
                        MaxNumberOfAttempts = 3,
                        Interval = 5
                        //RetryTimeout
                    }
                };

                if (GETtimer.RetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                await client.StartNewAsync("OrchestrateTimerByRetry", timerName, (GETtimer, 0, DateTime.UtcNow));

                return client.CreateCheckStatusResponse(req, timerName);
            }

            CronyTimerRetry timerModel = JsonConvert.DeserializeObject<CronyTimerRetry>(await req.Content.ReadAsStringAsync());

            if (timerModel.RetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            TimerRetry timer = timerModel.CopyRetryModel();

            await client.StartNewAsync("OrchestrateTimerByRetry", timerName, (timer, 0, DateTime.UtcNow));

            return client.CreateCheckStatusResponse(req, timerName);
        }

        private static TimeSpan ComputeNextDelay(int interval, double backoffCoefficient, int maxRetryInterval, int count)//, DateTime firstAttempt)
        {
            //DateTime retryExpiration = (retryOptions.RetryTimeout != TimeSpan.MaxValue)
            //    ? firstAttempt.Add(retryOptions.RetryTimeout)
            //    : DateTime.MaxValue;

            //if (DateTime.Now < retryExpiration)
            //    {
            double nextDelayInMilliseconds = TimeSpan.FromSeconds(interval).TotalMilliseconds * (Math.Pow(backoffCoefficient, count));

            TimeSpan nextDelay = TimeSpan.FromMilliseconds(nextDelayInMilliseconds);

            TimeSpan maxDelay = TimeSpan.FromSeconds(maxRetryInterval);

            return nextDelay.TotalMilliseconds > maxDelay.TotalMilliseconds
                ? maxDelay
                : nextDelay;
        }

        #region Logging
        private static void LogRetryStart(this ILogger logger, string text)
        {
#if DEBUG
            logger.LogError($"RETRY: STARTED {text} - {DateTime.Now}");
#endif
        }

        private static void LogRetryDone(this ILogger logger, string text)
        {
#if DEBUG
            logger.LogError($"RETRY: DONE {text}");
#endif
        }

        private static void LogRetryNext(this ILogger logger, string text, DateTime now)
        {
#if DEBUG
            logger.LogWarning($"RETRY: NEXT >>> {text} - {now}");
#endif
        }

        private static void LogRetryTimer(this ILogger logger, string text, DateTime now)
        {
#if DEBUG
            logger.LogCritical($"RETRY: EXECUTING {text} - {now}");
#endif
        }
        #endregion

        // Uncomment this Api to help with retry backoff calculations
        ////http://localhost:7078/CalculateRetryBackoffs/5/10/11599999/2
        //[FunctionName("CalculateRetryBackoffs")]
        //public static void CalculateRetryBackoffs(
        //        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "CalculateRetryBackoffs/{interval}/{maxAttempts}/{maxInterval}/{backoffCofecient}")] HttpRequestMessage req,
        //        int interval,
        //        int maxAttempts,
        //        int maxInterval,
        //        double backoffCofecient,
        //        ILogger log)
        //{
        //    HttpRetryOptions ro = new(TimeSpan.FromSeconds(interval), maxAttempts)
        //    {
        //        BackoffCoefficient = backoffCofecient,

        //        MaxRetryInterval = TimeSpan.FromSeconds(maxInterval),
        //        //RetryTimeout
        //    };
        //    var now = DateTime.UtcNow;
        //    log.LogCritical(now.ToString());
        //    for (int i = 1; i <= maxAttempts; i++)
        //    {
        //        TimeSpan ts = ro.ComputeNextDelay(i - 1);
        //        now = now.Add(ts);
        //        log.LogWarning($"{ts.ToString()}  -  {now.ToString()}");
        //    }
        //}

        //[FunctionName("SetTimerForDurableFunction")]
        //public static async Task<HttpResponseMessage> SetTimerForDurableFunctionCheck(
        //    [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "SetTimerForDurableFunctionStatusCheck/{timerName}")
        //    ] HttpRequestMessage req,
        //    [DurableClient] IDurableOrchestrationClient starter,
        //        string timerName,
        //    ILogger log)
        //{
        //    if (req.Method == HttpMethod.Get)
        //    {
        //        log.LogRetryStart(timerName);

        //        CronyTimerByRetry GETtimer = new()
        //        {
        //            Content = "wappa",
        //            Url = "https://reqbin.com/sample/get/json",
        //            HttpMethod = "get",
        //            TimerOptions = new()
        //            {
        //                BackoffCoefficient = 1.2,
        //                MaxRetryInterval = 30,
        //                MaxNumberOfAttempts = 1,
        //                Interval = 20
        //                //RetryTimeout
        //            },
        //            RetryOptions = new()
        //            {
        //                BackoffCoefficient = 1.2,
        //                MaxRetryInterval = 360,
        //                MaxNumberOfAttempts = 3,
        //                Interval = 10
        //                //RetryTimeout
        //            }
        //        };

        //        if (GETtimer.RetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
        //        {
        //            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        //        }

        //        await starter.StartNewAsync("OrchestrateTimerByRetry", timerName, (GETtimer, 0, DateTime.UtcNow));

        //        return starter.CreateCheckStatusResponse(req, timerName);
        //    }

        //    CronyTimerByRetry timer = JsonConvert.DeserializeObject<CronyTimerByRetry>(await req.Content.ReadAsStringAsync());

        //    if (timer.RetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
        //    {
        //        return new HttpResponseMessage(HttpStatusCode.BadRequest);
        //    }

        //    await starter.StartNewAsync("OrchestrateTimerByRetry", timerName, (timer, 0, DateTime.UtcNow));

        //    return starter.CreateCheckStatusResponse(req, timerName);
        //}
    }
}