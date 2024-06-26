using System.Threading;
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
                returnType: ApiTest.ContentType.Text,
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
                returnType: ApiTest.ContentType.Text,
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

        [Test]
        public void Get_Unrecognized_Throws()
        {
            Assert.Throws<System.Net.WebException>(
                () =>
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
