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
- Minimum polling intervals: 10 seconds for a ByRetry timer and 15 seconds for a CRON timer. A timer interval of 1 second has been tested and ran successfully. Note the interval timing behaviour as described below.
- When a timer is started by posting to the appropriate API, use the built in webhooks that is return in the payload to terminate, suspend and resume a timer instance:

### Crony Timer API:
```
[POST] SetTimerByRetry
[POST] SetTimerByCRON
```

### Interval timing behaviour:

- When an interval is executing and calling an API and the API is taking some time to return, the timer will not overlap and execute agian. It will wait for the current interval execution to complete and then calculate the next interval time form after the last completed interval execution. This prevents that calls overlap.
- There is a pollimg interval that is set to a maximum of 10 seconds when running in the Azure Functions Serverless plan. This means that the interval will execute any time within the 10 second timespan, and not necessarily exactly on the second.

### Retry Timer example POST:

POST Url: http://{yourDomain}/SetTimerByRetry

This will start a new timer with an interval of 10 seconds and will execute 3 times as set by MaxNumberOfAttempts. Reqbin.com is used for test API calls. This will execute every 10 seconds from when it started:

```json
// NOTE: All time values are in seconds.
{
  "Name": "Test-Retry-Timer",
  "Url": "https://reqbin.com/sample/get/json",
  "HttpMethod": "GET",
  "Content": "test content",
  "PollIf202": false,
  "StatusCodeReplyForCompletion": 500,
  "Timeout": 15,
  "TimerOptions": {
    "Interval": 10,
    "MaxRetryInterval": 15,
    "MaxNumberOfAttempts": 3,
    "BackoffCoefficient": 1.0
  },
  "Headers": {
    "testheader": [
      "testheadervalue"
    ]
  },
  "RetryOptions": {
    "Interval": 5,
    "MaxRetryInterval": 360,
    "MaxNumberOfAttempts": 3,
    "BackoffCoefficient": 1.2
  },
  "CompletionWebhook": {
    "Url": "https://reqbin.com/sample/get/json",
    "Timeout": 15,
    "HttpMethod": "GET",
    "Content": null,
    "PollIf202": false,
    "Headers": {
      "testheader": [
        "testheadervalue"
      ]
    },
    "RetryOptions": {
      "Interval": 10,
      "MaxRetryInterval": 30,
      "MaxNumberOfAttempts": 5,
      "BackoffCoefficient": 1.5
    }
  }
}
```
### CRON Timer example POST:

POST Url: http://{yourDomain}/SetTimerByCRON

This will start a new timer with an interval of 15 seconds and will execute 3 times as set by MaxNumberOfAttempts. Reqbin.com is used for test API calls. This will execute on every 15th second of a minute:

```json
// NOTE: All time values are in seconds.
{
  "Name": "CronZZZ",
  "CRON": "0/15 * * ? * * *",
  "HttpMethod": "GET",
  "Content": "test content",
  "PollIf202": false,
  "MaxNumberOfAttempts": 3,
  "StatusCodeReplyForCompletion": 201,
  "Url": "https://reqbin.com/sample/get/json",
  "Timeout": 15,
  "Headers": {
    "testheader": [
      "testheadervalue"
    ]
  },
  "RetryOptions": {
    "Interval": 5,
    "MaxRetryInterval": 360,
    "MaxNumberOfAttempts": 3,
    "BackoffCoefficient": 1.2
  },
  "CompletionWebhook": {
    "Url": "https://reqbin.com/sample/get/json",
    "HttpMethod": "GET",
    "Content": null,
    "PollIf202": false,
    "Timeout": 15,
    "Headers": {
      "testheader": [
        "testheadervalue"
      ]
    },
    "RetryOptions": {
      "Interval": 10,
      "MaxRetryInterval": 30,
      "MaxNumberOfAttempts": 5,
      "BackoffCoefficient": 1.5
    }
  }
}
```

### Example response from posting to start a new timer:

- "id" Is the name of the timer.
- Use "statusQueryGetUri" to get the current running status of the timer.
- Use "terminatePostUri" to cancel the timer.
- Use "suspendPostUri" to pause the timer from execution.
- Use "resumePostUri" to resume the suspended timer.

```json
{
    "id": "CronZZZ",
    "statusQueryGetUri": "http://localhost:7078/runtime/webhooks/durabletask/instances/CronZZZ?taskHub=DurableTimerTaskHub4&connection=Storage&code=7GhJy5v1tLH3LSCQJhP2sUl4Hrjl-9-JVQIlBp1KR1JdAzFunD2mcA==",
    "sendEventPostUri": "http://localhost:7078/runtime/webhooks/durabletask/instances/CronZZZ/raiseEvent/{eventName}?taskHub=DurableTimerTaskHub4&connection=Storage&code=7GhJy5v1tLH3LSCQJhP2sUl4Hrjl-9-JVQIlBp1KR1JdAzFunD2mcA==",
    "terminatePostUri": "http://localhost:7078/runtime/webhooks/durabletask/instances/CronZZZ/terminate?reason={text}&taskHub=DurableTimerTaskHub4&connection=Storage&code=7GhJy5v1tLH3LSCQJhP2sUl4Hrjl-9-JVQIlBp1KR1JdAzFunD2mcA==",
    "purgeHistoryDeleteUri": "http://localhost:7078/runtime/webhooks/durabletask/instances/CronZZZ?taskHub=DurableTimerTaskHub4&connection=Storage&code=7GhJy5v1tLH3LSCQJhP2sUl4Hrjl-9-JVQIlBp1KR1JdAzFunD2mcA==",
    "restartPostUri": "http://localhost:7078/runtime/webhooks/durabletask/instances/CronZZZ/restart?taskHub=DurableTimerTaskHub4&connection=Storage&code=7GhJy5v1tLH3LSCQJhP2sUl4Hrjl-9-JVQIlBp1KR1JdAzFunD2mcA==",
    "suspendPostUri": "http://localhost:7078/runtime/webhooks/durabletask/instances/CronZZZ/suspend?reason={text}&taskHub=DurableTimerTaskHub4&connection=Storage&code=7GhJy5v1tLH3LSCQJhP2sUl4Hrjl-9-JVQIlBp1KR1JdAzFunD2mcA==",
    "resumePostUri": "http://localhost:7078/runtime/webhooks/durabletask/instances/CronZZZ/resume?reason={text}&taskHub=DurableTimerTaskHub4&connection=Storage&code=7GhJy5v1tLH3LSCQJhP2sUl4Hrjl-9-JVQIlBp1KR1JdAzFunD2mcA=="
}
```
