using System.Threading;
using Bloom.Api;
using Bloom.Book;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using NUnit.Framework;

namespace BloomTests.web
{
    [TestFixture]
    public class ExternalApiTests
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

            var externalApi = new ExternalApi(new BloomLibraryBookApiClient());
            externalApi.RegisterWithApiHandler(_server.ApiHandler);
        }

        [TearDown]
        public void Teardown()
        {
            _server.Dispose();
            _server = null;
            Monitor.Exit(_portMonitor);
        }

        [Test]
        public void ExternalLogin_PostMissingEmail_IgnoredAndReturnsOk()
        {
            var result = ApiTest.PostString(
                _server,
                endPoint: "external/login",
                data: "{\"sessionToken\":\"abc\",\"userId\":\"123\"}",
                returnType: ApiTest.ContentType.JSON
            );

            Assert.That(result, Is.EqualTo("OK"));
        }
    }
}
