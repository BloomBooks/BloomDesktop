using System;
using System.Collections.Generic;
using System.Net;
using Bloom.WebLibraryIntegration;
using NUnit.Framework;
using RestSharp;
using SIL.Progress;

namespace BloomTests.WebLibraryIntegration
{
    /// <summary>
    /// Tests for how BloomLibraryBookApiClient classifies responses and statuses from
    /// api.bloomlibrary.org, without calling the live service (BL-17051).
    /// </summary>
    [TestFixture]
    public class BloomLibraryBookApiClientResponseTests
    {
        /// <summary>A request that timed out means the server isn't responding.</summary>
        [Test]
        public void IsServerNotResponding_TimedOut_ReturnsTrue()
        {
            var response = new RestResponse { ResponseStatus = ResponseStatus.TimedOut };
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)0));

            Assert.That(BloomLibraryBookApiClient.IsServerNotResponding(response), Is.True);
        }

        /// <summary>A request that got no response at all means the server isn't responding.</summary>
        [Test]
        public void IsServerNotResponding_NoResponse_ReturnsTrue()
        {
            // This is what RestSharp gives when the connection fails before any response.
            var response = new RestResponse { ResponseStatus = ResponseStatus.Error };
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)0));

            Assert.That(BloomLibraryBookApiClient.IsServerNotResponding(response), Is.True);
        }

        /// <summary>A 5xx server error means the server isn't responding usefully.</summary>
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

        /// <summary>A success or a 4xx is a real answer from the server, not a lack of one.</summary>
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

        /// <summary>Both spellings of a canceled operation fail, like a failed one.</summary>
        [TestCase("Failed")]
        [TestCase("Canceled")]
        [TestCase("Cancelled")]
        public void IsStatusFailure_FailureStatuses_ReturnsTrue(string status)
        {
            Assert.That(BloomLibraryBookApiClient.IsStatusFailure(status), Is.True);
        }

        /// <summary>Success and in-progress statuses are not failures.</summary>
        [TestCase("Succeeded")]
        [TestCase("Running")]
        [TestCase("NotStarted")]
        [TestCase(null)]
        public void IsStatusFailure_OtherStatuses_ReturnsFalse(string status)
        {
            Assert.That(BloomLibraryBookApiClient.IsStatusFailure(status), Is.False);
        }

        /// <summary>A timeout on the initial call throws a WebException, which BookUpload shows as a network problem.</summary>
        [Test]
        public void CallLongRunningAction_StartTimesOut_ThrowsWebException()
        {
            var client = new ClientWithCannedResponses(
                new RestResponse { ResponseStatus = ResponseStatus.TimedOut }
            );

            Assert.Throws<WebException>(() =>
                client.CallLongRunningAction(
                    new RestRequest("start"),
                    new NullProgress(),
                    "Unable to start"
                )
            );
            Assert.That(client.RequestCount, Is.EqualTo(1));
        }

        /// <summary>A server error on a status poll throws a WebException, which BookUpload shows as a network problem.</summary>
        [Test]
        public void CallLongRunningAction_PollGetsServerError_ThrowsWebException()
        {
            var client = new ClientWithCannedResponses(
                AcceptedResponse(),
                new RestResponse
                {
                    ResponseStatus = ResponseStatus.Completed,
                    StatusCode = HttpStatusCode.ServiceUnavailable,
                }
            );

            Assert.Throws<WebException>(() =>
                client.CallLongRunningAction(
                    new RestRequest("start"),
                    new NullProgress(),
                    "Unable to start"
                )
            );
            Assert.That(client.RequestCount, Is.EqualTo(2), "should have failed on the poll");
        }

        /// <summary>A 4xx on the initial call still throws the plain upload error.</summary>
        [Test]
        public void CallLongRunningAction_StartRejected_ThrowsPlainApplicationException()
        {
            var client = new ClientWithCannedResponses(
                new RestResponse
                {
                    ResponseStatus = ResponseStatus.Completed,
                    StatusCode = HttpStatusCode.BadRequest,
                }
            );

            var e = Assert.Throws<ApplicationException>(() =>
                client.CallLongRunningAction(
                    new RestRequest("start"),
                    new NullProgress(),
                    "Unable to start"
                )
            );
            Assert.That(e.Message, Is.EqualTo("Unable to start"));
        }

        /// <summary>
        /// A canceled status, in either spelling, throws rather than returning null
        /// (which used to make InitiateBookUpload fail reading the result).
        /// </summary>
        [TestCase("Canceled")]
        [TestCase("Cancelled")]
        public void CallLongRunningAction_StatusCanceled_ThrowsInsteadOfReturningNull(string status)
        {
            var client = new ClientWithCannedResponses(
                AcceptedResponse(),
                new RestResponse
                {
                    ResponseStatus = ResponseStatus.Completed,
                    StatusCode = HttpStatusCode.OK,
                    Content = "{\"status\":\"" + status + "\"}",
                }
            );

            var e = Assert.Throws<ApplicationException>(() =>
                client.CallLongRunningAction(
                    new RestRequest("start"),
                    new NullProgress(),
                    "Unable to start"
                )
            );
            Assert.That(e.Message, Is.EqualTo("Unable to start"));
            Assert.That(client.RequestCount, Is.EqualTo(2));
        }

        /// <summary>
        /// The 202 response upload-start gives when it has begun, pointing at the status URL to poll.
        /// </summary>
        private static RestResponse AcceptedResponse()
        {
            var response = new RestResponse
            {
                ResponseStatus = ResponseStatus.Completed,
                StatusCode = HttpStatusCode.Accepted,
            };
            response.Headers.Add(
                new Parameter("Operation-Location", "status", ParameterType.HttpHeader)
            );
            return response;
        }

        /// <summary>
        /// A BloomLibraryBookApiClient whose requests get the given responses, in order,
        /// instead of going to the live service.
        /// </summary>
        private class ClientWithCannedResponses : BloomLibraryBookApiClient
        {
            private readonly CannedRestClient _restClient;

            public ClientWithCannedResponses(params IRestResponse[] responses)
            {
                _restClient = new CannedRestClient(responses);
                _azureRestClient = _restClient;
            }

            public int RequestCount => _restClient.RequestCount;
        }

        /// <summary>
        /// A RestClient that answers each request with the next of the given responses.
        /// </summary>
        private class CannedRestClient : RestClient
        {
            private readonly Queue<IRestResponse> _responses;

            public CannedRestClient(IEnumerable<IRestResponse> responses)
            {
                _responses = new Queue<IRestResponse>(responses);
            }

            public int RequestCount { get; private set; }

            public override IRestResponse Execute(IRestRequest request)
            {
                RequestCount++;
                return _responses.Dequeue();
            }
        }
    }
}
