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
        public static async Task<HttpStatusCode> ExecuteTimer(this HttpObject httpObject, IDurableOrchestrationContext context, DateTime deadline)
        {
            try
            {
                HttpStatusCode code = await httpObject.ExecuteTimer(context);

                context.SetCustomStatus($"{code} - {deadline}");

                return code;
            }
            catch (Exception ex)
            {
                context.SetCustomStatus(ex.GetBaseException().Message);

                throw;
            }
        }

        [Deterministic]
        private static async Task<HttpStatusCode> ExecuteTimer(this HttpObject httpObject, IDurableOrchestrationContext context)//, string statusUrl, bool isDurableCheck)
        {
            DurableHttpRequest durquest = new(GetHttpMethod(httpObject.HttpMethod),
                                              new Uri(httpObject.Url),
                                              content: httpObject.Content,
                                              httpRetryOptions: new HttpRetryOptions(TimeSpan.FromSeconds(httpObject.RetryOptions.Interval), httpObject.RetryOptions.MaxNumberOfAttempts)
                                              {
                                                  BackoffCoefficient = httpObject.RetryOptions.BackoffCoefficient,
                                                  MaxRetryInterval = TimeSpan.FromSeconds(httpObject.RetryOptions.MaxRetryInterval),
                                                  StatusCodesToRetry = httpObject.GetRetryEnabledStatusCodes()
                                              },
                                              asynchronousPatternEnabled: httpObject.PollIf202,
                                              timeout: TimeSpan.FromSeconds(httpObject.Timeout));

            foreach (var headers in httpObject.Headers)
            {
                durquest.Headers.Add(headers.Key, new(headers.Value));
            }

            DurableHttpResponse response = await context.CallHttpAsync(durquest);

            return response.StatusCode;
        }

        [Deterministic]
        public static HttpMethod GetHttpMethod(this string method) => method[..2].ToUpper()
        switch
        {
            "GE" => HttpMethod.Get, "PO" => HttpMethod.Post, "PU" => HttpMethod.Put, "DE" => HttpMethod.Delete, _ => HttpMethod.Get
        };

        public static List<HttpStatusCode> GetRetryEnabledStatusCodes(this HttpObject httpObject) => new()
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