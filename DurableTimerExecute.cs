using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Crony;
using Crony.Models;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace Durable.Crony.Microservice
{
    public static class DurableTimerExecute
    {
        [Deterministic]
        public static async Task<HttpStatusCode> ExecuteTimer(this Webhook webhook, IDurableOrchestrationContext context, DateTime deadline)
        {
            try
            {
                HttpStatusCode code = await webhook.ExecuteTimer(context);

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
        private static async Task<HttpStatusCode> ExecuteTimer(this Webhook webhook, IDurableOrchestrationContext context)//, string statusUrl, bool isDurableCheck)
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

            DurableHttpResponse response = await context.CallHttpAsync(durquest);

            return response.StatusCode;
        }        

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