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

        [Test]
        public void CallLongRunningAction_StartTimesOut_ThrowsServerNotResponding()
        {
            var client = new ClientWithCannedResponses(
                new RestResponse { ResponseStatus = ResponseStatus.TimedOut }
            );

            Assert.Throws<ServerNotRespondingException>(() =>
                client.CallLongRunningAction(
                    new RestRequest("start"),
                    new NullProgress(),
                    "Unable to start"
                )
            );
            Assert.That(client.RequestCount, Is.EqualTo(1));
        }

        [Test]
        public void CallLongRunningAction_PollGetsServerError_ThrowsServerNotResponding()
        {
            var client = new ClientWithCannedResponses(
                AcceptedResponse(),
                new RestResponse
                {
                    ResponseStatus = ResponseStatus.Completed,
                    StatusCode = HttpStatusCode.ServiceUnavailable,
                }
            );

            Assert.Throws<ServerNotRespondingException>(() =>
                client.CallLongRunningAction(
                    new RestRequest("start"),
                    new NullProgress(),
                    "Unable to start"
                )
            );
            Assert.That(client.RequestCount, Is.EqualTo(2), "should have failed on the poll");
        }

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
