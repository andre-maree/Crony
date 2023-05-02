# Crony Serverless Scheduler Service

Crony is a Durable Function timer scheduler service that can call a webhook that is defined with each timer instance. Timer instances are created by posting a JSON timer definition to the Crony create timer API endpoint. This is a modernized rework of the Azure WebJobs scheduler that runs on the App Service. This service can also run on the App Service, but for best scalability, deploy it to one of the Azure Serverless Function plans.

- Schedule a background timer trigger by HTTP posting a timer JSON object to this service.
- There are two types of timers - timers set by CRON expression, and retry timers:
    * CRON timers can be eternally recurring and are created by HTTP posting a timer definition to the SetTimerByCRON endpoint.
    * Retry timers are not eternally recurring and will end when the maximum number of retries is reached. These are created by posting to the SetTimerByRetry endpoint.
- Timers can be deleted by calling the CancelTimer endpoint.
- A webhook can be set to call when the timer event fires. The URL, headers, HTTP method, content, and retries can be set for the webhook call.
- A timer completion webhook can be set (CompletionWebhook property) to be called when a timer completes it`s life cycle (when maximium number of webhook calls reached or completed by received status code from the webhook).
- An HTTP status code can be set (StatusCodeReplyForCompletion property) to complete the timer when it matches the webhook returned status code. For example: this can be used to call the webhook until it returns HTTP 200 OK after it was returning 202 Accepted codes.
- Use a timer naming convention to query timers by name prefix. Timer name example: "MyApp_MyReminderTimer_00000000000031".
- The timer by CRON expression can be set to have a maximum number of webhook triggers. This is an added feature to normal CRON expressions.
- Quartz.NET is used for CRON calculations: https://www.freeformatter.com/cron-expression-generator-quartz.html
- When running in a serverless function app plan, the queue polling will be fixed to 10 seconds.
- Minimum polling intervals: 10 seconds for a ByRetry timer and 15 seconds for a CRON timer.

Timer API:
```r
[POST] SetTimerByRetry
[POST] SetTimerByCRON
[DELETE] CancelTimer
```

The timer model classes:
```csharp
// NOTE: All time values are in seconds.
public class CronyWebhook
{
    public string Url { get; set; }
    public int Timeout { get; set; } = 15;
    public Dictionary<string, string[]> Headers { get; set; } = new();
    public string HttpMethod { get; set; }
    public string Content { get; set; }
    public bool PollIf202 { get; set; }
    public RetryOptions RetryOptions { get; set; }
}

public class CronyTimer : CronyWebhook
{
    public int StatusCodeReplyForCompletion { get; set; }
    public CronyWebhook CompletionWebhook { get; set; }
}

public class CronyTimerCRON : CronyTimer
{
    public string CRON { get; set; }
    public int MaxNumberOfAttempts { get; set; }
}

public class CronyTimerRetry : CronyTimer
{
    public RetryOptions TimerOptions { get; set; }
}

public class CronyRetryOptions
{
    public int Interval { get; set; }
    public int MaxRetryInterval { get; set; }
    public int MaxNumberOfAttempts { get; set; }
    public double BackoffCoefficient { get; set; }
}
```
