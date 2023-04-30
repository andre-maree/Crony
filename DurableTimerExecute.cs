using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace Durable.Crony.Microservice
{
    public static class DurableTimerExecute
    {
        [Deterministic]
        public static async Task ExecuteTimer(this CronyTimer timerObject, IDurableOrchestrationContext context, DateTime deadline)
        {
            try
            {
                HttpStatusCode code = await timerObject.ExecuteTimer(context);

                context.SetCustomStatus($"{code} - {deadline}");
            }
            catch (Exception ex)
            {
                context.SetCustomStatus(ex.GetBaseException().Message);

                throw;
            }
        }

        [Deterministic]
        private static async Task<HttpStatusCode> ExecuteTimer(this CronyTimer timerObject, IDurableOrchestrationContext context)//, string statusUrl, bool isDurableCheck)
        {
            DurableHttpRequest durquest = new(GetHttpMethod(timerObject.HttpMethod),
                                              new Uri(timerObject.Url),
                                              content: timerObject.Content,
                                              httpRetryOptions: new HttpRetryOptions(TimeSpan.FromSeconds(timerObject.WebhookRetryOptions.Interval), timerObject.WebhookRetryOptions.MaxNumberOfAttempts)
                                              {
                                                  BackoffCoefficient = timerObject.WebhookRetryOptions.BackoffCoefficient,
                                                  MaxRetryInterval = TimeSpan.FromSeconds(timerObject.WebhookRetryOptions.MaxRetryInterval),
                                                  StatusCodesToRetry = GetRetryEnabledStatusCodes()

                                              },
                                              asynchronousPatternEnabled: timerObject.PollIf202,
                                              timeout: TimeSpan.FromSeconds(timerObject.Timeout));

            foreach (var headers in timerObject.Headers)
            {
                durquest.Headers.Add(headers.Key, new(headers.Value));
            }

            DurableHttpResponse response = await context.CallHttpAsync(durquest);

            return response.StatusCode;
        }

        [Deterministic]
        private static HttpMethod GetHttpMethod(string method) => method[..2].ToUpper()
        switch
        {
            "GE" => HttpMethod.Get, "PO" => HttpMethod.Post, "PU" => HttpMethod.Put, "DE" => HttpMethod.Delete, _ => HttpMethod.Get
        };

        private static List<HttpStatusCode> GetRetryEnabledStatusCodes() => new()
        {
            HttpStatusCode.Conflict, HttpStatusCode.BadGateway, HttpStatusCode.GatewayTimeout, HttpStatusCode.RequestTimeout, HttpStatusCode.ServiceUnavailable
        };
        
        //monitor orch for status
        //private static async Task WaitForDurableFunctionRunning(this TimerObject timerObject, string statusUrl, IDurableOrchestrationContext context)
        //{
        //DurableHttpResponse statusResponse = await context.CallHttpAsync(new DurableHttpRequest(HttpMethod.Get, new Uri(statusUrl), asynchronousPatternEnabled: false, httpRetryOptions: timerObject.WebhookRetryOptions));

        //if (statusResponse.StatusCode == HttpStatusCode.Accepted)
        //{
        //    return true;
        //}

        //if (isDurableCheck)
        //{
        //    if (statusResponse.StatusCode == HttpStatusCode.OK)
        //    {
        //        Status runtimeStatus = JsonConvert.DeserializeObject<Status>(statusResponse.Content);

        //        if (runtimeStatus.RuntimeStatus.Equals("Running"))
        //        {
        //            return await TriigerAction(timerObject, context);
        //        }

        //        if (runtimeStatus.Equals("Pending"))
        //        {
        //            return true;
        //        }
        //    }
        //}
        //else if (statusResponse.StatusCode == HttpStatusCode.OK)
        //{
        //}

        //private static async Task<bool> TriigerAction(TimerObject timerObject, IDurableOrchestrationContext context)
        //{
        //    DurableHttpRequest durquest = new(timerObject.IsHttpGet ? HttpMethod.Get : HttpMethod.Post,
        //                                      new Uri(timerObject.WebhookUrl),
        //                                      content: timerObject.Content,
        //                                      httpRetryOptions: timerObject.WebhookRetryOptions,
        //                                      asynchronousPatternEnabled: timerObject.PollWebhookIf202);


        //    if (timerObject.Headers != null && timerObject.Headers.Count > 0)
        //    {
        //        foreach (var header in timerObject.Headers)
        //        {
        //            durquest.Headers.Add(header.Key, header.Value);
        //        }
        //    }

        //    await context.CallHttpAsync(durquest);

        //    return true;
        //}
    }
}