using System;
using System.Collections.Generic;
using System.Net;
using Bloom.Properties;
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
        private string _origWebUserId;
        private string _origLastLoginSessionToken;
        private string _origLastLoginUserId;
        private string _origLastLoginDest;

        /// <summary>
        /// A rejected session makes the client sign out, which writes Settings.Default, the
        /// developer's real persisted configuration, so save the login settings to restore later.
        /// </summary>
        [SetUp]
        public void Setup()
        {
            _origWebUserId = Settings.Default.WebUserId;
            _origLastLoginSessionToken = Settings.Default.LastLoginSessionToken;
            _origLastLoginUserId = Settings.Default.LastLoginUserId;
            _origLastLoginDest = Settings.Default.LastLoginDest;
        }

        /// <summary>Put back the login settings Setup saved.</summary>
        [TearDown]
        public void TearDown()
        {
            Settings.Default.WebUserId = _origWebUserId;
            Settings.Default.LastLoginSessionToken = _origLastLoginSessionToken;
            Settings.Default.LastLoginUserId = _origLastLoginUserId;
            Settings.Default.LastLoginDest = _origLastLoginDest;
            Settings.Default.Save();
        }

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
        /// Only the API's 400 refusing our Authentication-Token means the session was rejected;
        /// other 400s, and other statuses carrying that text, do not.
        /// </summary>
        [TestCase(HttpStatusCode.BadRequest, kUnableToValidateUser, true)]
        [TestCase(HttpStatusCode.BadRequest, "Invalid book id", false)]
        [TestCase(HttpStatusCode.BadRequest, null, false)]
        [TestCase(HttpStatusCode.Forbidden, kUnableToValidateUser, false)]
        public void IsSessionRejected_ReturnsWhetherTheServerRefusedTheToken(
            HttpStatusCode statusCode,
            string content,
            bool expected
        )
        {
            var response = new RestResponse
            {
                ResponseStatus = ResponseStatus.Completed,
                StatusCode = statusCode,
                Content = content,
            };

            Assert.That(
                BloomLibraryBookApiClient.IsSessionRejected(response),
                Is.EqualTo(expected)
            );
        }

        /// <summary>
        /// When the server refuses the sign-in, on upload-start or on the permissions check (the
        /// call that failed in BL-17085), the client signs the user out, clearing the saved token,
        /// and throws a SessionRejectedException (asking them to sign in again) rather than the
        /// plain error.
        /// </summary>
        [TestCase("upload-start")]
        [TestCase("permissions")]
        public void Call_SessionRejected_SignsOutAndThrowsSessionRejected(string call)
        {
            var client = new ClientWithCannedResponses(
                new RestResponse
                {
                    ResponseStatus = ResponseStatus.Completed,
                    StatusCode = HttpStatusCode.BadRequest,
                    Content = kUnableToValidateUser,
                }
            );
            client.SetLoginData("someone@example.com", "someUserId", "expiredToken", "production");
            Assert.That(client.LoggedIn, Is.True, "test setup should have signed in");
            Assert.That(Settings.Default.LastLoginSessionToken, Is.EqualTo("expiredToken"));

            Assert.Throws<SessionRejectedException>(() =>
            {
                if (call == "upload-start")
                    client.CallLongRunningAction(
                        RequestWithToken("expiredToken"),
                        new NullProgress(),
                        "Unable to start"
                    );
                else
                    client.GetBookPermissions("someBookId");
            });
            Assert.That(client.LoggedIn, Is.False, "should have signed the user out");
            Assert.That(Settings.Default.LastLoginSessionToken, Is.EqualTo(""));
        }

        /// <summary>
        /// If the user signed in again while a request with the old token was on its way, the
        /// server's refusal of the old token leaves the new sign-in alone, and the caller gets
        /// the plain error, which trying again fixes.
        /// </summary>
        [Test]
        public void CallLongRunningAction_OldTokenRejectedAfterNewSignIn_StaysSignedIn()
        {
            var client = new ClientWithCannedResponses(
                new RestResponse
                {
                    ResponseStatus = ResponseStatus.Completed,
                    StatusCode = HttpStatusCode.BadRequest,
                    Content = kUnableToValidateUser,
                }
            );
            client.SetLoginData("someone@example.com", "someUserId", "newToken", "production");
            Assert.That(client.LoggedIn, Is.True, "test setup should have signed in");

            var e = Assert.Throws<ApplicationException>(() =>
                client.CallLongRunningAction(
                    RequestWithToken("oldToken"),
                    new NullProgress(),
                    "Unable to start"
                )
            );
            Assert.That(e.Message, Is.EqualTo("Unable to start"));
            Assert.That(client.LoggedIn, Is.True, "should have kept the new sign-in");
            Assert.That(Settings.Default.LastLoginSessionToken, Is.EqualTo("newToken"));
        }

        /// <summary>A request carrying the given sign-in token, as the client's own requests do.</summary>
        private static RestRequest RequestWithToken(string token)
        {
            var request = new RestRequest("start");
            request.AddHeader("Authentication-Token", token);
            return request;
        }

        private const string kUnableToValidateUser =
            "Unable to validate user. Did you include a valid Authentication-Token header?";

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
