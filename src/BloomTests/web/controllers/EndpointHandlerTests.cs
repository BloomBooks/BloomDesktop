using System.Threading;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.web
{
    [TestFixture]
    public class EndpointHandlerTests
    {
        public static readonly object _portMonitor = new object();
        private BloomServer _server;

        [SetUp]
        public void Setup()
        {
            // as long as we're only using one, fixed port number, we need to prevent unit test runner
            // from running these tests in parallel.
            Monitor.Enter(_portMonitor);
            _server = new BloomServer(new BookSelection());
        }

        [TearDown]
        public void Teardown()
        {
            _server.Dispose();
            _server = null;
            Monitor.Exit(_portMonitor);
        }

        [Test]
        public void Get_OneParameter_KeyValueReceived()
        {
            var result = ApiTest.GetString(
                _server,
                endPoint: "test",
                query: "color=blue",
                handler: request =>
                {
                    Assert.That(request.RequiredParam("color"), Is.EqualTo("blue"));
                    request.ReplyWithText(request.RequiredParam("color"));
                }
            );
            Assert.That(result, Is.EqualTo("blue"));
        }

        [Test]
        public void Post_JSON_JSONReceived()
        {
            var result = ApiTest.PostString(
                _server,
                endPoint: "test",
                data: "{\"color\": \"blue\"}",
                returnType: ApiTest.ContentType.JSON,
                handler: request =>
                {
                    var requiredPostJson = request.RequiredPostJson();
                    request.ReplyWithText(DynamicJson.Parse(requiredPostJson).color);
                }
            );
            Assert.That(result, Is.EqualTo("blue"));
        }

        [Test]
        public void Get_EndPointHasTwoSegments_Works()
        {
            var result = ApiTest.GetString(
                _server,
                endPoint: "parent/child",
                query: "color=blue",
                handler: request => request.PostSucceeded()
            );
            Assert.That(result, Is.EqualTo("OK"));
        }

        [Test]
        public void Get_EndPointCaseIsIgnored()
        {
            var result = ApiTest.GetString(
                _server,
                endPoint: "fooBAR",
                endOfUrlForTest: "FOObar",
                handler: request => request.PostSucceeded()
            );
            Assert.That(result, Is.EqualTo("OK"));
        }

        /// <summary>
        /// An endpoint whose job is to interrupt long-running work must be registered with
        /// requiresSync false, or it queues behind the very request it exists to interrupt and
        /// the flag it sets arrives too late to stop anything. libraryPublish/cancel is the case
        /// this was written for (BL-16340); progress/cancel relies on the same property.
        ///
        /// Parking one request inside the lock needs a second worker to be free to serve the
        /// interrupting one. BloomServer starts Math.Max(ProcessorCount, 2) of them, so there is
        /// always at least one spare; were that ever reduced to a single worker this would fail
        /// for a reason that has nothing to do with the lock.
        /// </summary>
        [Test]
        public void RequestNotRequiringSync_IsServedWhileAnotherRequestHoldsTheSyncLock()
        {
            using (var slowRequestStarted = new ManualResetEventSlim())
            using (var letSlowRequestFinish = new ManualResetEventSlim())
            {
                var slowRequestWasReleased = false;
                _server.ApiHandler.RegisterEndpointHandler(
                    "test/slow",
                    request =>
                    {
                        slowRequestStarted.Set();
                        slowRequestWasReleased = letSlowRequestFinish.Wait(20000);
                        request.PostSucceeded();
                    },
                    handleOnUiThread: false,
                    requiresSync: true
                );
                _server.ApiHandler.RegisterEndpointHandler(
                    "test/interrupt",
                    request => request.PostSucceeded(),
                    handleOnUiThread: false,
                    requiresSync: false
                );

                var slowRequest = Task.Run(() =>
                    ApiTest.GetString(_server, "test/slow", timeoutInMilliseconds: 30000)
                );
                // Sanity check: unless the slow request is actually running, it isn't holding the
                // lock and the rest of the test would prove nothing.
                Assert.That(
                    slowRequestStarted.Wait(10000),
                    Is.True,
                    "the slow request never started, so it never held the lock"
                );
                Assert.That(
                    slowRequest.IsCompleted,
                    Is.False,
                    "the slow request should still have been in flight"
                );

                var interruptResult = ApiTest.GetString(
                    _server,
                    "test/interrupt",
                    timeoutInMilliseconds: 5000
                );

                Assert.That(interruptResult, Is.EqualTo("OK"));
                Assert.That(
                    slowRequest.IsCompleted,
                    Is.False,
                    "the interrupting request should have been served while the slow one still held the lock"
                );

                letSlowRequestFinish.Set();
                Assert.That(
                    slowRequest.Wait(20000),
                    Is.True,
                    "the slow request never finished once released"
                );
                Assert.That(slowRequestWasReleased, Is.True);
                Assert.That(slowRequest.Result, Is.EqualTo("OK"));
            }
        }

        [Test]
        public void Get_Unrecognized_Throws()
        {
            Assert.Throws<System.Net.Http.HttpRequestException>(() =>
                ApiTest.GetString(
                    _server,
                    endPoint: "foo88bar",
                    endOfUrlForTest: "foobar",
                    handler: request => request.PostSucceeded()
                )
            );
        }
    }
}
