# Nightly failures: details

Findings that are too detailed for the entries in [README.md](README.md). Each section belongs to
one entry there.

## C# upload integration tests: the live service was slow

`BookUploadAndDownloadTests` runs against the live upload service (`api.bloomlibrary.org`, the
`bloom-functions` Function App) on purpose. The tests' requests carry the query parameter
`env=unit-test`, which makes them easy to find in Application Insights.

### 2026-10-27

- **Which tests, and when (UTC):**
  - `DownloadUrl_NoCollectionSettings_GetsDocument_AndSettings`: 09:40:28–09:42:09.
  - `UploadBook_FillsInMetaData_AndDownloadUrl_GetsDocumentAndSettings`: 09:42:15–09:44:24.
  - `UploadBook_SameId_ReplacesFilesAndSetsParseFieldsCorrectly`: 09:44:24–09:46:05.
  - The e2e suite's uploads succeeded from 09:48:14 on.
- **What the client saw:** each test spent about 100 s on its call to `books/new:upload-start`,
  which is RestSharp's default timeout, then reported "Unable to initiate book upload on the
  server."
- **What Application Insights shows:**
  - Between 09:38 and 09:50 the Function App was degraded across functions: 5 failed requests in
    `books`, 21 in `stats` and 1 in `status`, all HTTP 499 (the client closed the connection).
  - Two of the tests' `upload-start` calls ran for 111.8 s and 117.9 s on the server before ending
    with `FunctionInvocationException` wrapping `TaskCanceledException`. That is the Functions host
    noticing the client had gone while it still waited on the Node worker, not a timeout on the
    server.
  - Other `upload-start` calls in the same window succeeded, in 260 ms and in 20.7 s.
  - The 499s were spread across several instances, not just one.
  - CPU and memory were normal on every instance.
  - No dependency failures were recorded, and nothing pointed at a shared outbound dependency.
  - The host logs show worker and host churn around the window ("Task hub worker stopped.
    IsGracefulStop: False", "Job host stopped", then "Worker process started and initialized",
    "Job host started"), tightly clustered at 09:37–09:43. The 499s line up with instances that
    had just started.
  - The app is on the Consumption plan (Dynamic Y1), where instances come and go as the load
    changes, so churn alone is not a sign of a platform fault. The number of starts and stops that
    day was not unusual either: 25 starts and 21 stops, against 1 to 33 a day over the previous
    two weeks. What stands out is only the tight cluster, and that the 499s fell on new instances.
  - **The stuck requests on each instance were all released at the same instant, about 3.5
    minutes after that instance's host started.** Adding each request's duration to its start
    time gives:

    | Instance | Host started | Stuck requests | All ended at | Host start to release |
    | --- | --- | --- | --- | --- |
    | `4e0f…` | 09:34:26 | 4 `stats` (and a 75 s "success") | 09:38:00.1–.8 | 3 m 34 s |
    | `ed30…` | 09:38:59 | 2 `books`, 6 `stats` | 09:42:21.24–.25 | 3 m 22 s |
    | `7b58…` | 09:39:00 | 2 `books`, 4 `stats` | 09:42:35.26 | 3 m 35 s |
    | `461e…` | 09:42:37 | 1 `books`, 11 `stats`, 1 `status` | 09:46:23.62–.67 | 3 m 46 s |

    So this was not a cold start finishing late, not an instance stopping mid-request, and not a
    slow step in each request. Every request that needed something was waiting on one shared
    thing per instance, which took about 3.5 minutes after the host started to complete or fail.
    Nothing got through while an instance was stuck. Requests first reported as 4–10 ms successes
    turned out to be the 20.7 s `upload-start`, a 4.3 s `stats` call that started after its
    instance's release, and a `longRunningActionOrchestrator` run that took 220 s and ended with
    the rest on `461e…`.
  - The 20.7 s `upload-start` on `7b58…` (09:42:16) ended just after that instance's release at
    09:42:35, so it was stuck the same way and its client had not yet given up.
  - The tests' failed `upload-start` calls were among the stuck requests: 09:40:29 on `ed30…`
    (111.8 s) and 09:44:25 on `461e…` (117.9 s).
  - The release times differ by instance and follow each one's start time, so the wait was inside
    each new instance, not on an outside service: a service recovering would have released every
    instance at the same moment.
  - **What the bloom-azure-functions code says (origin/master, last commit 2026-07-27, so no
    deployment near the incident):**
    - `books`, `stats` and `status` share no dependency. `stats` opens a new Postgres connection
      per request and queries Parse; `books` calls Parse and starts a Durable Functions
      orchestration; `status` only reads Durable Functions state.
    - It is deployed by Kudu (`deploy.cmd`: `npm install --production`, then `tsc` on the
      server), not as a package. On the Consumption plan that means each new instance loads the
      code and `node_modules` (including `googleapis`, the AWS SDK and `durable-functions`) from an
      Azure Files share. Loading a module blocks the Node worker, so every request queued on that
      instance waits until loading finishes, and then they all run at once.
    - That made slow code loading the first suspect: each new instance taking about 3.5 minutes
      to load its code from a slow share. Running from a package (`WEBSITE_RUN_FROM_PACKAGE=1`)
      is Microsoft's usual fix for slow Node cold starts on Consumption.
    - Separately, neither the Parse calls (axios) nor the Postgres connection set a timeout, so a
      hung dependency would hold a request until the client gives up.
    - **Each instance's release came with a connection error.** On every stuck instance, the first `Connection error occurred. Waiting
      for ~2000 ms before retrying...` warning (category `Host.Function.Console`) is logged
      within 100 ms of the release, and a second one about 70–100 s later:

      | Instance | Release | First warning | Second warning |
      | --- | --- | --- | --- |
      | `4e0f…` | 09:38:00 | 09:38:00.09 | 09:39:36 |
      | `ed30…` | 09:42:21 | 09:42:21.18 | 09:43:58 |
      | `7b58…` | 09:42:35 | 09:42:35.19 | 09:43:43 |
      | `461e…` | 09:46:23 | 09:46:23.58 | 09:48:03 |

      The warning has no operation id: the worker process wrote it to its console outside any
      invocation.
    - **The warning comes from our own startup code, calling Contentful.** It is the network-error
      retry in `contentful-sdk-core` 6.10.3 (the Contentful client's dependency): "Connection
      error occurred. Waiting for N ms before retrying...". The first retry waits 1,914–2,114 ms,
      which matches the 2,054 ms and 1,922 ms seen. The call comes from
      `contentfulToCrowdin/index.ts`, which calls `readTransformUpload()` at the top level of the
      module (since `c863ed2`, 2020-11-19), as well as from its 22:30 timer. So every time a
      worker loads that module, which is every new instance, it runs the whole Contentful-to-Crowdin
      sync, and the sync's Contentful query fails with a network error about 3 minutes in. The
      sync's "Querying Contentful..." line is logged with no operation id on new instances
      (`461e…` at 09:44:16; an ordinary instance on 10-05 about 70 s after start).
    - **The warning is routine, about once per new instance.** Over 30 days it appears on most
      days, and its daily count tracks the number of instance starts (10-15: 12 starts, 12
      warnings; 10-18: 12 and 12; 10-24: 4 and 4; 10-26: 3 and 3; busier days somewhat fewer
      warnings than starts). So it marks something every new instance goes through, not
      something peculiar to 10-27.
    - **On every day, the warning comes about 3 minutes after an instance's host starts** (daily
      median 150–230 s over 30 days; 10-27's 207 s is typical).
    - **Requests are held until the warning on ordinary days too.** On an instance that started
      on 10-05 at 01:39:08, two `GET /v1/books/` requests arrived 2 s later, and both failed at
      01:41:47.8, 40–50 ms after its warning. So holding requests until the warning is what a new
      instance normally does, not something new on 10-27. 10-27 was only worse because many
      requests landed on new instances.
    - **Our code did not run for the held requests.** On `461e…` the host logged "Executing
      'Functions.X'" as each request arrived (the first at 09:42:41.97, 4 s after host start), but
      none of those invocations logged anything from our code until the release. The instance's
      CPU was low (about 2% normalized) throughout, so it was waiting, not computing. All 26
      exceptions at the release were `TaskCanceledException` from the host's
      `WorkerFunctionInvoker`.
    - **What held them: loading the code.** Low CPU with handlers not starting means the Node
      worker's event loop was blocked by synchronous I/O, and the obvious candidate is loading
      modules from the Azure Files share. While it was blocked, the open Contentful connection
      died. When the loop freed, the first thing it ran was that error's retry, which is why the
      warning marks the release. The timing confirms it. `contentfulToCrowdin` holds about half
      of the modules the worker loads (1,054 of 2,037 cumulatively in alphabetical function
      order; `googleapis` is most of the rest). So if loading is the hold, its "Querying
      Contentful..." line should fall at a fixed fraction of each instance's host-start-to-warning
      span, whatever the span's length. Across 96 instance starts over 30 days, it fell at
      0.429–0.464 (p10–p90, median 0.446). The span itself ranged from 150 s to 246 s (median
      190 s). `461e…` was at 0.439, an ordinary start. On `461e…` the worker logged nothing
      between function loads apart from that line, while the host kept logging requests arriving.
    - So **every new instance holds its requests for about 3 minutes (150–246 s) while it loads
      its code**, and 10-27 was a morning when many requests, the tests' among them, landed on
      new instances. The warning is a symptom. Running from a package and loading `googleapis`
      only when needed are the fixes to try.
    - **Parse returned errors just before the stalls.** At 09:32:47 two `GET /v1/books/` requests,
      on two instances that were already running, waited about 65 s and then failed with "caught
      axios.get error: Request failed with status code 500". `books` queries Parse with axios, so
      that 500 came from a Parse server. Which one depends on the request's `env` parameter
      (production, dev, or the unit-test server), and the reports don't show query strings. No
      C# test calls `GET /v1/books/`; Bloom's collection tab and bloomlibrary.org do, against
      production.
    - Parse does not explain the stall either way. The stuck requests used different backends:
      the tests' `upload-start` calls (`env=unit-test`) use the unit-test Parse server, the
      `stats` calls use production Parse and the analytics Postgres, and `status` uses no Parse
      at all. Yet all of them were held on the same instances and released at the same instant,
      so what held them was inside bloom-functions.
    - Application Insights shows no file-share, mount or storage-throttling messages in the
      window. The storage account's own metrics have not been checked.
    - Unrelated to the stall: `stats/events.ts` calls `context.done()` inside an `async`
      function, which the Node worker reports as "Choose either to return a promise or call
      'done'".
  - Why each new instance hung only in this window is not known. A
    plausible chain is that a dependency was slow to accept new connections, stuck requests made
    the platform add instances, and each new instance got stuck the same way. The app records no
    dependency telemetry, so the dependency is not identified.
  - No deployment or configuration change showed up in the telemetry.
- **What the telemetry can't show:** `Functions.books` logs only its start and end, so a slow call
  shows up as one long span with nothing inside it.

### 2026-10-15

- `DownloadUrl_NoCollectionSettings_GetsDocument_AndSettings` failed at 09:08:31 UTC after 35 s,
  with the same message. That is too short for the 100 s client timeout, so this was a different
  failure: a quick error from `upload-start`, a failed status check, or an upload the server
  marked as Failed.
- Application Insights shows no 499 burst and no exceptions that morning. The `upload-start`
  calls it has around then (09:08:39, 09:08:53 and 09:09:10) all returned 202 within about a
  second, and their status checks returned 200. Those are probably the later tests' calls. The
  failing test's own call, between about 09:07:56 and 09:08:31, is not there at all, so it may
  never have reached the Function App.

### 2026-09-28

- The tests failed around 10:40 UTC. Application Insights shows six 499s across `books`, `status`
  and `stats`, all on a single instance, so this looks like one instance going bad.

### Still open

- How much the fix shortens the ~3-minute cold-start hold on new production instances.
  bloom-azure-functions PR #91 (`12b0915`), in production since 2026-10-27 about 23:00 UTC,
  runs from a package, loads `googleapis` on first use (startup modules 2,037 to 1,077), drops
  the startup Contentful-to-Crowdin sync and moves to Node 22. A restarted slot answered its
  first request in 3.2 s, but that was one sample, not a new instance. Measure it from first
  request durations on new instances, since the warning no longer appears.
- Not done: dependency tracking, per-step timing in `books` and `stats`, and timeouts on the
  Parse and Postgres calls.
- What failed on 10-15, given that the request apparently never reached the Function App.
- Whether bloom-functions should log each step of `books` and record its dependency calls, so a
  stall shows where the time went.

### How to look

Since `c54bbfb094`, a failed upload in these tests lists each API call's resource, status and
error from the API client's log, with query strings, bodies and headers left out. That shows
which call failed, and whether it timed out, without Azure.

To find the requests on the service side, open the `bloom-functions` Application Insights resource,
go to **Logs**, and run KQL. Times are in UTC. Production and the staging slot log to the same
resource, so the queries keep to production with `cloud_RoleName == "bloom-functions"` (staging is
`bloom-functions-staging`). Sampling was not evident on 10-27 (`itemCount` was 1 throughout).

```kusto
requests
| where timestamp between (datetime(2026-10-27 09:38:00) .. datetime(2026-10-27 09:50:00))
| where cloud_RoleName == "bloom-functions"
| where url has "upload-start" or url has "env=unit-test"
| project timestamp, name, url, resultCode, duration, success, operation_Id, cloud_RoleInstance
| order by timestamp asc
```

What happened inside one request:

```kusto
union exceptions, traces, dependencies
| where operation_Id == "<operation_Id from above>"
| order by timestamp asc
```

How long after its host starts each new instance logs its first "Connection error occurred"
warning, per day:

```kusto
let starts = traces
    | where timestamp > ago(30d) and message startswith "Job host started"
    | where cloud_RoleName == "bloom-functions"
    | project cloud_RoleInstance, started = timestamp;
let warnings = traces
    | where timestamp > ago(30d) and message has "Connection error occurred"
    | where cloud_RoleName == "bloom-functions"
    | project cloud_RoleInstance, warned = timestamp;
starts
| join kind=inner warnings on cloud_RoleInstance
| where warned between (started .. started + 30m)
| summarize warned = min(warned) by cloud_RoleInstance, started
| extend blockedSeconds = datetime_diff("second", warned, started)
| summarize instances = count(), p50 = percentile(blockedSeconds, 50),
    p90 = percentile(blockedSeconds, 90), longest = max(blockedSeconds) by day = bin(started, 1d)
| order by day asc
```

Whether failures cluster on one instance:

```kusto
requests
| where timestamp between (datetime(2026-10-27 09:30) .. datetime(2026-10-27 10:00))
| where cloud_RoleName == "bloom-functions"
| summarize total=count(), failed=countif(resultCode == "499"), p95=percentile(duration, 95)
    by cloud_RoleInstance, bin(timestamp, 2m)
| order by timestamp asc
```
