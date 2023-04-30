using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DurableTask.Core;

namespace Durable.Crony.Microservice
{
    public static class TerminateAndCleanup
    {
        [FunctionName(nameof(CompleteTimer))]
        public static async Task CompleteTimer([ActivityTrigger] string timerName,
            [DurableClient] IDurableClient client)
        {
            await client.PurgeInstanceHistoryAsync(timerName);
        }

        [FunctionName(nameof(DeleteTimer))]
        public static async Task DeleteTimer(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "DeleteTimer/{timerName}")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient client,
                string timerName)
        {
            await client.TerminateAsync(timerName, null);

            await client.PurgeInstanceHistoryAsync(timerName);
        }

        /// <summary>
        /// Cleanup timer trigger daily at 23:00
        /// </summary>
        [FunctionName(nameof(CleanupTimer))]
        public static async Task CleanupTimer([TimerTrigger("0 0 23 * * *")] TimerInfo myTimer, [DurableClient] IDurableOrchestrationClient client)
        {
            //clear non-failed history
            //await client.PurgeInstanceHistoryAsync(DateTime.MinValue, DateTime.UtcNow.AddDays(-1),
            //    new List<OrchestrationStatus>
            //    {
            //                OrchestrationStatus.Completed
            //    });

            //clear failed history
            await client.PurgeInstanceHistoryAsync(
                DateTime.MinValue, DateTime.UtcNow.AddDays(-7),
                new List<OrchestrationStatus>
                {
                            OrchestrationStatus.Failed
                });
        }
    }
}