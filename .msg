Make BloomServer's blocked-worker bookkeeping sound (BL-16612)

Publishing could hang forever, and take every later publish in the
session with it, until Bloom was restarted. Stacks captured at the moment
of failure on CI showed four workers and none idle: three waiting for an
api lock a publish held for minutes, and one waiting for the off-screen
browser that needed a page only a worker could serve.

BloomServer already had the mechanism to prevent that. It counts workers
that report themselves blocked and adds one when they are all blocked, so
that somebody can still take the request which would let the others
finish. Four things were wrong with it.

1. The check ran only as a request ARRIVED. That cannot be sufficient on
   its own: when the request that would break the deadlock is already in
   the queue, no new request need ever arrive to trigger it. It now also
   runs at the other moment the pool can run out -- when a worker becomes
   blocked -- because the check moved into the reporting method itself.
   QueueRequest calls the same helper, so the rule lives in one place.

2. OffScreenBrowser.RunAndBlock never reported its block at all, though
   publishing runs as an api request and blocks a worker there for as
   long as the browser takes. That is why the check evaluated 3 >= 4 and
   concluded a worker was free.

3. A reported block could leak permanently. Both halves decided whether
   to count by looking at the name of the thread they happened to run on,
   so the pair only balanced when both ran on the same worker. In
   ApiRequest.InvokeWithErrorHandling they do not: the second half runs
   after an await, and a worker carries no synchronization context, so
   the continuation resumes on a thread-pool thread and the decrement was
   silently skipped. The same method leaked again when anything other
   than ObjectDisposedException came out of Invoke, since that path
   skipped both halves. Reporting a block now returns an IDisposable
   scope that closes over whether it counted; disposing it undoes exactly
   that block, from any thread, on every exit path. Disposal is
   idempotent, so a nested `using` cannot decrement twice and leave the
   server believing workers are free when they are not.
   RegisterThreadUnblocked is gone rather than kept alongside: a second
   way to end a block is the same trap as a second way to report one.
   IBloomServer changes with it, so BloomHarvester needs the same
   one-line update if it calls these.

4. The shortage was judged against workers that may already be dead.
   _workers keeps an entry for a thread that has died until the listener
   prunes it, so the count could claim a worker was available when
   nothing of it remained but the entry. Both the fast path and the
   under-lock check now count live workers. It has to be both: the fast
   path exists only to avoid taking the queue lock, so while it
   over-counted it returned early and made the accurate check
   unreachable.

Five tests, in BloomServerWorkerPoolTests, driving a real listening
server. Two were confirmed to fail without their fix rather than passing
for the wrong reason: the cross-thread disposal test fails if the
thread-identity check is restored, and the dead-entry test fails if the
live count is applied in only one of the two places.

Comments record three decisions so they are not rediscovered as bugs: why
the top-up deliberately catches everything (a failed log write must not
become a permanently mis-counted server), that workers are never retired
so the pool can end a session larger than it started, and that a worker
added late in shutdown is only survivable while Dispose signals _stop
without disposing the handles -- which is itself conditional, and is left
as a known gap rather than papered over.

The catch-all around the top-up stays, and its comment now says why
honestly. The earlier justification claimed the alternative would oblige
every caller to get the increment right; review showed a cheaper one that
touches only ReportThreadBlocking (hand the caller its scope first, then
let this method fail fast). It was declined on judgement rather than
cost: topping up the pool is an opportunistic safety net, and a net
failing is not a reason to fail the user's request that triggered it --
publishing should not die because the server could not create a spare
thread it may not need. The comment now records that, so the next reader
weighs the real trade instead of a cost that was overstated.

Points raised in review and addressed here: the fast path's comment now
distinguishes the two directions of error, since counting too low is
self-correcting (we take the lock and get the better answer) while only
counting too high skips the check -- and it no longer calls the
under-lock check "accurate", because the lock covers changes to _workers,
not thread liveness, so neither reading is authoritative by the time we
act on it. The invariant that makes the unlocked read safe -- entries are
only ever removed for threads already dead, never for live ones -- is
documented on _workers along with the fact that additions happen only
under the lock. LiveWorkerCount is a one-line LINQ count over the
dictionary rather than a hand-rolled walk of .Values, which materialises
a snapshot list; the previous justification for the walk was both no
reason at all and stale. And IsWorkerThread uses StartsWith with
StringComparison.Ordinal instead of IndexOf(...) == 0, since the default
overloads of both are culture-sensitive, which is the wrong comparison
for a thread name we generated ourselves.

This deliberately does nothing else. It does not bound how long
publishing waits for the off-screen browser, retry it, or change what
publishing does when the browser fails; those have user-visible
consequences and were decided against for this change. Apart from no
longer deadlocking, there is no user-visible difference.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>

