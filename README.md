# Crony Serverless Scheduler Service

Crony is a Durable Function timer scheduler service that can call a webhook that is defined with each timer instance. Timer instances are created by posting a JSON timer definition to an API endpoint. This is a modernized rework of the Azure WebJobs scheduler that runs on the App Service. This service can also run on the App Service, but for best scalability, deploy it to one of the Azure Serverless Function plans.

- Schedule a background timer trigger by HTTP posting a timer JSON object to this service.
- There are two types of timers - timers set by CRON expression, and retry timers:
    * CRON timers are eternally recurring and are created by HTTP posting a timer definition to the SetTimerByCRON endpoint.
    * Retry timers are not eternally recurring and will end when the maximum number of retries is reached. These are created by posting to the SetTimerByRetry endpoint.
- Timers can be deleted by calling the DeleteTimer endpoint.
- A webhook can be set to call when the timer event fires. The URL, headers, HTTP method, content, and retries can be set for the webhook call.
- Use a timer naming convention to query timers by name prefix. Timer name example: "MyApp_MyReminderTimer_00000000000031".

Timer API:
```r
[POST] SetTimerByRetry
[POST] SetTimerByCRON
[DEL]  DeleteTimer
```

The timer model classes:
```csharp
// NOTE: All timer values are in seconds.
public class CronyTimer
 {
     public string Url { get; set; }
     public int Timeout { get; set; } = 15;
     public Dictionary<string, string[]> Headers { get; set; } = new();
     public bool IsHttpGet { get; set; }
     public string Content { get; set; }
     public bool PollIf202 { get; set; }
     public RetryOptions WebhookRetryOptions { get; set; }
 }

 public class CronyTimerByCRON : CronyTimer
 {
     public string CRON { get; set; }
     public int MaxNumberOfAttempts { get; set; }
 }

 public class CronyTimerByRetry : CronyTimer
 {
     public RetryOptions TimerRetryOptions { get; set; }
 }

 public class RetryOptions
 {
     public int Interval { get; set; }
     public int MaxRetryInterval { get; set; }
     public int MaxNumberOfAttempts { get; set; }
     public double BackoffCoefficient { get; set; }
 }
```
