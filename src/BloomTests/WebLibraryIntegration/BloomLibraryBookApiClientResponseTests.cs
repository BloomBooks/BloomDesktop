using System.Net;
using Bloom.WebLibraryIntegration;
using NUnit.Framework;
using RestSharp;

namespace BloomTests.WebLibraryIntegration
{
    /// <summary>
    /// Tests for how BloomLibraryBookApiClient classifies responses and statuses from
    /// api.bloomlibrary.org, without calling the live service (BL-17051).
    /// </summary>
    [TestFixture]
    public class BloomLibraryBookApiClientResponseTests
    {
        [Test]
        public void IsServerNotResponding_TimedOut_ReturnsTrue()
        {
            var response = new RestResponse { ResponseStatus = ResponseStatus.TimedOut };
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)0));

            Assert.That(BloomLibraryBookApiClient.IsServerNotResponding(response), Is.True);
        }

        [Test]
        public void IsServerNotResponding_NoResponse_ReturnsTrue()
        {
            // This is what RestSharp gives when the connection fails before any response.
            var response = new RestResponse { ResponseStatus = ResponseStatus.Error };
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)0));

            Assert.That(BloomLibraryBookApiClient.IsServerNotResponding(response), Is.True);
        }

        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.BadGateway)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.GatewayTimeout)]
        public void IsServerNotResponding_ServerError_ReturnsTrue(HttpStatusCode statusCode)
        {
            var response = new RestResponse
            {
                ResponseStatus = ResponseStatus.Completed,
                StatusCode = statusCode,
            };

            Assert.That(BloomLibraryBookApiClient.IsServerNotResponding(response), Is.True);
        }

        [TestCase(HttpStatusCode.OK)]
        [TestCase(HttpStatusCode.Accepted)]
        [TestCase(HttpStatusCode.BadRequest)]
        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        [TestCase(HttpStatusCode.NotFound)]
        public void IsServerNotResponding_ServerAnswered_ReturnsFalse(HttpStatusCode statusCode)
        {
            var response = new RestResponse
            {
                ResponseStatus = ResponseStatus.Completed,
                StatusCode = statusCode,
            };

            Assert.That(BloomLibraryBookApiClient.IsServerNotResponding(response), Is.False);
        }

        [TestCase("Failed")]
        [TestCase("Canceled")]
        [TestCase("Cancelled")]
        public void IsStatusFailure_FailureStatuses_ReturnsTrue(string status)
        {
            Assert.That(BloomLibraryBookApiClient.IsStatusFailure(status), Is.True);
        }

        [TestCase("Succeeded")]
        [TestCase("Running")]
        [TestCase("NotStarted")]
        [TestCase(null)]
        public void IsStatusFailure_OtherStatuses_ReturnsFalse(string status)
        {
            Assert.That(BloomLibraryBookApiClient.IsStatusFailure(status), Is.False);
        }
    }
}
