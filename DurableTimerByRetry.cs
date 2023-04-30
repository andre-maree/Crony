using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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

            (CronyTimerByRetry timerObject, int count, DateTime deadline) = context.GetInput<(CronyTimerByRetry, int, DateTime)>();
            
            if (timerObject.TimerRetryOptions.MaxNumberOfAttempts <= count)
            {
                slog.LogRetryDone(context.InstanceId);

                await context.CleanupInstanceHistory();

                return;
            }

            TimeSpan delay = timerObject.TimerRetryOptions.ComputeNextDelay(count);

            deadline = deadline.Add(delay);

            slog.LogRetryNext(context.InstanceId, deadline);

            await context.CreateTimer(deadline, default);

            DateTime now = context.CurrentUtcDateTime;

            
            //else
            //{
                slog.LogRetryTimer(context.InstanceId, now);

                try
                {
                    await timerObject.ExecuteTimer(context, deadline); 
                
                if (now > deadline.AddSeconds(timerObject.TimerRetryOptions.Interval * 1.5))
                {
                    deadline = now;
                }

                count++;

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
            //}
        }

        [FunctionName("SetTimerForDurableFunction")]
        public static async Task<HttpResponseMessage> SetTimerForDurableFunctionCheck(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "SetTimerForDurableFunctionStatusCheck/{timerName}")
            ] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
                string timerName,
            ILogger log)
        {
            if (req.Method == HttpMethod.Get)
            {
                log.LogRetryStart(timerName);

                CronyTimerByRetry GETtimer = new()
                {
                    Content = "wappa",
                    Url = "https://reqbin.com/sample/get/json",
                    IsHttpGet = true,
                    TimerRetryOptions = new()
                    {
                        BackoffCoefficient = 1.2,
                        MaxRetryInterval = 360,
                        MaxNumberOfAttempts = 1,
                        Interval = 10
                        //RetryTimeout
                    },
                    WebhookRetryOptions = new()
                    {
                        BackoffCoefficient = 1.2,
                        MaxRetryInterval = 360,
                        MaxNumberOfAttempts = 3,
                        Interval = 5
                        //RetryTimeout
                    }
                };

                if (GETtimer.WebhookRetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                await starter.StartNewAsync("OrchestrateTimerByRetry", timerName, (GETtimer, 0, DateTime.UtcNow));

                return starter.CreateCheckStatusResponse(req, timerName);
            }

            CronyTimerByRetry timer = JsonConvert.DeserializeObject<CronyTimerByRetry>(await req.Content.ReadAsStringAsync());

            if (timer.WebhookRetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            await starter.StartNewAsync("OrchestrateTimerByRetry", timerName, (timer, 0, DateTime.UtcNow));

            return starter.CreateCheckStatusResponse(req, timerName);
        }

        //http://localhost:7078/SetTimerByRetry/byretry_test_0000000031
        [FunctionName("SetTimerByRetry")]
        public static async Task<HttpResponseMessage> SetTimerByRetry(
                [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "SetTimerByRetry/{timerName}")
            ] HttpRequestMessage req,
                [DurableClient] IDurableOrchestrationClient starter,
                string timerName,
                ILogger log)
        {
            if (req.Method == HttpMethod.Get)
            {
                log.LogRetryStart(timerName);

                CronyTimerByRetry GETtimer = new()
                {
                    Content = "wappa",
                    Url = "https://reqbin.com/sample/get/json",
                    IsHttpGet = true,
                    TimerRetryOptions = new()
                    {
                        BackoffCoefficient = 1,
                        MaxRetryInterval = 360,
                        MaxNumberOfAttempts = 1000,
                        Interval = 30
                        //RetryTimeout
                    },
                    WebhookRetryOptions = new()
                    {
                        BackoffCoefficient = 1.2,
                        MaxRetryInterval = 360,
                        MaxNumberOfAttempts = 3,
                        Interval = 5
                        //RetryTimeout
                    }
                };

                if (GETtimer.WebhookRetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                await starter.StartNewAsync("OrchestrateTimerByRetry", timerName, (GETtimer, 0, DateTime.UtcNow));

                return starter.CreateCheckStatusResponse(req, timerName);
            }

            CronyTimerByRetry timer = JsonConvert.DeserializeObject<CronyTimerByRetry>(await req.Content.ReadAsStringAsync());

            if (timer.WebhookRetryOptions.MaxRetryInterval > TimeSpan.FromDays(6).TotalSeconds)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            await starter.StartNewAsync("OrchestrateTimerByRetry", timerName, (timer, 0, DateTime.UtcNow));

            return starter.CreateCheckStatusResponse(req, timerName);
        }

        public static async Task CleanupInstanceHistory(this IDurableOrchestrationContext context)
        {
            try
            {
                await context.CallActivityWithRetryAsync("CompleteTimer", new Microsoft.Azure.WebJobs.Extensions.DurableTask.RetryOptions(TimeSpan.FromSeconds(5), 15)
                {
                    BackoffCoefficient = 2,
                    MaxRetryInterval = TimeSpan.FromHours(1),
                }, context.InstanceId);
            }
            catch (Exception ex)
            {
                // log instnce history not deleted
            }
        }

        private static TimeSpan ComputeNextDelay(this RetryOptions retryOptions, int attempt)//, DateTime firstAttempt)
        {
            //DateTime retryExpiration = (retryOptions.RetryTimeout != TimeSpan.MaxValue)
            //    ? firstAttempt.Add(retryOptions.RetryTimeout)
            //    : DateTime.MaxValue;

            //if (DateTime.Now < retryExpiration)
            //    {
            double nextDelayInMilliseconds = TimeSpan.FromSeconds(retryOptions.Interval).TotalMilliseconds * (Math.Pow(retryOptions.BackoffCoefficient, attempt));

            TimeSpan nextDelay = TimeSpan.FromMilliseconds(nextDelayInMilliseconds);

            TimeSpan maxDelay = TimeSpan.FromSeconds(retryOptions.MaxRetryInterval);

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
    }
}