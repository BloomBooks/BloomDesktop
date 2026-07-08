using System;
using System.Collections.Generic;
using System.Net;
using Bloom.TeamCollection.Cloud;
using NUnit.Framework;
using RestSharp;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// A scriptable <see cref="IRestExecutor"/> that returns whatever <see cref="Handler"/>
    /// produces instead of making a real HTTP call, so <see cref="CloudCollectionClient"/>'s
    /// header injection, 401-retry, and error-mapping logic can be unit tested without a live
    /// server. Records every request it was asked to execute so tests can assert on headers.
    /// </summary>
    internal class FakeRestExecutor : IRestExecutor
    {
        public List<IRestRequest> RequestsSeen = new List<IRestRequest>();
        public Func<IRestRequest, IRestResponse> Handler;

        public IRestResponse Execute(IRestRequest request)
        {
            RequestsSeen.Add(request);
            return Handler(request);
        }
    }

    /// <summary>An <see cref="ICloudAuthProvider"/> that never actually calls out; used only to build a signed-in CloudAuth for these tests.</summary>
    internal class StubCloudAuthProvider : ICloudAuthProvider
    {
        public Func<string, CloudSession> RefreshHandler;

        public CloudSession SignIn(string email, string password) =>
            new CloudSession
            {
                AccessToken = "access-1",
                RefreshToken = "refresh-1",
                Email = email,
                UserId = "user-1",
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            };

        public CloudSession Refresh(string refreshToken) =>
            RefreshHandler != null
                ? RefreshHandler(refreshToken)
                : throw new CloudAuthException("refresh not configured for this test");
    }

    internal static class FakeResponses
    {
        public static IRestResponse Make(HttpStatusCode statusCode, string content) =>
            new RestResponse
            {
                StatusCode = statusCode,
                Content = content,
                ResponseStatus = ResponseStatus.Completed,
            };
    }

    [TestFixture]
    public class CloudCollectionClientTests
    {
        private static CloudEnvironment MakeEnvironment() =>
            new CloudEnvironment(name => name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null);

        private static (
            CloudCollectionClient client,
            FakeRestExecutor executor,
            CloudAuth auth
        ) MakeSignedInClient(StubCloudAuthProvider provider = null)
        {
            provider = provider ?? new StubCloudAuthProvider();
            var auth = new CloudAuth(provider, new InMemoryCloudTokenStore());
            auth.SignIn("alice@dev.local", "BloomDev123!");
            var client = new CloudCollectionClient(MakeEnvironment(), auth);
            var executor = new FakeRestExecutor();
            client.SetRestClientForTests(executor);
            return (client, executor, auth);
        }

        [Test]
        public void CallRpc_AttachesApiKeyBearerAndContentProfileHeaders()
        {
            var (client, executor, _) = MakeSignedInClient();
            executor.Handler = req => FakeResponses.Make(HttpStatusCode.OK, "[]");

            client.CallRpc("my_collections", new { });

            Assert.That(executor.RequestsSeen, Has.Count.EqualTo(1));
            var request = executor.RequestsSeen[0];
            var headers = request.Parameters.FindAll(p => p.Type == ParameterType.HttpHeader);

            Assert.That(
                headers,
                Has.Some.Matches<Parameter>(h =>
                    h.Name == "apikey" && (string)h.Value == "test-anon-key"
                )
            );
            Assert.That(
                headers,
                Has.Some.Matches<Parameter>(h =>
                    h.Name == "Authorization" && (string)h.Value == "Bearer access-1"
                )
            );
            Assert.That(
                headers,
                Has.Some.Matches<Parameter>(h =>
                    h.Name == "Content-Profile" && (string)h.Value == "tc"
                )
            );
            Assert.That(
                headers,
                Has.Some.Matches<Parameter>(h =>
                    h.Name == "Accept-Profile" && (string)h.Value == "tc"
                )
            );
        }

        [Test]
        public void CallRpc_Success_ReturnsParsedJson()
        {
            var (client, executor, _) = MakeSignedInClient();
            executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.OK,
                    "[{\"id\":\"abc\",\"name\":\"My Collection\"}]"
                );

            var result = client.CallRpc("my_collections", new { });

            Assert.That(result, Is.Not.Null);
            Assert.That((string)result[0]["id"], Is.EqualTo("abc"));
        }

        [Test]
        public void CallRpc_NoContent_ReturnsNull()
        {
            var (client, executor, _) = MakeSignedInClient();
            executor.Handler = req => FakeResponses.Make(HttpStatusCode.NoContent, "");

            var result = client.CallRpc("unlock_book", new { p_book_id = "abc" });

            Assert.That(result, Is.Null);
        }

        [Test]
        public void CallRpc_401ThenRefreshSucceeds_RetriesWithNewTokenAndSucceeds()
        {
            var provider = new StubCloudAuthProvider
            {
                RefreshHandler = refreshToken => new CloudSession
                {
                    AccessToken = "access-2",
                    RefreshToken = "refresh-2",
                    Email = "alice@dev.local",
                    UserId = "user-1",
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                },
            };
            var (client, executor, auth) = MakeSignedInClient(provider);

            var callCount = 0;
            executor.Handler = req =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // Sanity check: the first (failing) call really did use the original token.
                    Assert.That(
                        HeaderValue(req, "Authorization"),
                        Is.EqualTo("Bearer access-1"),
                        "first attempt should use the pre-refresh token"
                    );
                    return FakeResponses.Make(
                        HttpStatusCode.Unauthorized,
                        "{\"message\":\"JWT expired\"}"
                    );
                }

                Assert.That(
                    HeaderValue(req, "Authorization"),
                    Is.EqualTo("Bearer access-2"),
                    "retry should use the refreshed token"
                );
                return FakeResponses.Make(HttpStatusCode.OK, "[]");
            };

            var result = client.CallRpc("my_collections", new { });

            Assert.That(
                callCount,
                Is.EqualTo(2),
                "should retry exactly once after a successful refresh"
            );
            Assert.That(result, Is.Not.Null);
            Assert.That(auth.GetAccessTokenOrNull(), Is.EqualTo("access-2"));
        }

        [Test]
        public void CallRpc_401AndRefreshFails_ThrowsNotSignedInWithoutLooping()
        {
            var provider = new StubCloudAuthProvider
            {
                RefreshHandler = refreshToken =>
                    throw new CloudAuthException("refresh token expired"),
            };
            var (client, executor, auth) = MakeSignedInClient(provider);

            var callCount = 0;
            executor.Handler = req =>
            {
                callCount++;
                return FakeResponses.Make(
                    HttpStatusCode.Unauthorized,
                    "{\"message\":\"JWT expired\"}"
                );
            };

            var ex = Assert.Throws<CloudCollectionClientException>(() =>
                client.CallRpc("my_collections", new { })
            );

            // This is the "refresh failure mid-operation aborts cleanly and surfaces 'please
            // sign in'" acceptance criterion at the client layer.
            Assert.That(ex.Code, Is.EqualTo(CloudErrorCode.NotSignedIn));
            Assert.That(
                callCount,
                Is.EqualTo(1),
                "must not retry when there is nothing to retry with"
            );
            Assert.That(
                auth.IsSignedIn,
                Is.False,
                "the failed refresh should have signed the user out"
            );
        }

        [TestCase("LockHeldByOther", CloudErrorCode.LockHeldByOther)]
        [TestCase("BaseVersionSuperseded", CloudErrorCode.BaseVersionSuperseded)]
        [TestCase("NameConflict", CloudErrorCode.NameConflict)]
        [TestCase("MissingOrBadUploads", CloudErrorCode.MissingOrBadUploads)]
        [TestCase("VersionConflict", CloudErrorCode.VersionConflict)]
        public void CallEdgeFunction_TypedErrorCodes_MapToExpectedCloudErrorCode(
            string serverCode,
            CloudErrorCode expected
        )
        {
            // `{error: "<Code>"}` is CONTRACTS.md's standard envelope, and the shape every edge
            // function REALLY sends (supabase/functions/_shared/errors.ts's errorResponse). An
            // earlier version of this test asserted against `{code: "<Code>"}` -- a shape the
            // real server never produces -- which is exactly how MapError's matching `code`-only
            // lookup shipped broken: the test validated the client's wrong assumption instead of
            // the server's actual contract. E2E-9's same-name race caught it live (the
            // NameConflict retry loop in PutBookInRepo never engaged).
            var (client, executor, _) = MakeSignedInClient();
            executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.Conflict,
                    $"{{\"error\":\"{serverCode}\",\"message\":\"conflict\"}}"
                );

            var ex = Assert.Throws<CloudCollectionClientException>(() =>
                client.CallEdgeFunction("checkin-start", new { })
            );

            Assert.That(ex.Code, Is.EqualTo(expected));
            Assert.That(ex.Details, Is.Not.Null);
        }

        [Test]
        public void CallEdgeFunction_CodeShapedErrorBody_StillMapsAsFallback()
        {
            // MapError keeps `code` as a fallback key (PostgREST RPC bodies use it, and any
            // hypothetical old server build might); make sure that path still classifies.
            var (client, executor, _) = MakeSignedInClient();
            executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.Conflict,
                    "{\"code\":\"NameConflict\",\"message\":\"conflict\"}"
                );

            var ex = Assert.Throws<CloudCollectionClientException>(() =>
                client.CallEdgeFunction("checkin-start", new { })
            );

            Assert.That(ex.Code, Is.EqualTo(CloudErrorCode.NameConflict));
        }

        [Test]
        public void CallEdgeFunction_426_MapsToClientOutOfDate()
        {
            var (client, executor, _) = MakeSignedInClient();
            executor.Handler = req =>
                FakeResponses.Make((HttpStatusCode)426, "{\"message\":\"upgrade Bloom\"}");

            var ex = Assert.Throws<CloudCollectionClientException>(() =>
                client.CallEdgeFunction("checkin-start", new { })
            );

            Assert.That(ex.Code, Is.EqualTo(CloudErrorCode.ClientOutOfDate));
            Assert.That(ex.Message, Is.EqualTo("upgrade Bloom"));
        }

        [Test]
        public void CallRpc_PostgrestStyleError_MapsToUnknownWithServerMessagePreserved()
        {
            // This is the actual shape live-verified against the local Supabase stack: RAISE
            // EXCEPTION 'book_not_found' USING ERRCODE = 'P0002' comes back as this JSON body.
            var (client, executor, _) = MakeSignedInClient();
            executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.InternalServerError,
                    "{\"code\":\"P0002\",\"details\":null,\"hint\":null,\"message\":\"book_not_found\"}"
                );

            var ex = Assert.Throws<CloudCollectionClientException>(() =>
                client.CallRpc("checkout_book", new { p_book_id = "abc", p_machine = "m" })
            );

            Assert.That(ex.Code, Is.EqualTo(CloudErrorCode.Unknown));
            Assert.That(ex.Message, Is.EqualTo("book_not_found"));
        }

        [Test]
        public void CallRpc_NotSignedIn_StillSendsApiKeyButNoAuthorizationHeader()
        {
            var auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            var client = new CloudCollectionClient(MakeEnvironment(), auth);
            var executor = new FakeRestExecutor();
            client.SetRestClientForTests(executor);
            executor.Handler = req =>
                FakeResponses.Make(HttpStatusCode.Unauthorized, "{\"message\":\"no token\"}");

            Assert.That(
                auth.IsSignedIn,
                Is.False,
                "sanity check: this test is specifically the not-signed-in case"
            );

            var ex = Assert.Throws<CloudCollectionClientException>(() =>
                client.CallRpc("my_collections", new { })
            );

            Assert.That(ex.Code, Is.EqualTo(CloudErrorCode.NotSignedIn));
            var request = executor.RequestsSeen[0];
            Assert.That(HeaderValue(request, "apikey"), Is.EqualTo("test-anon-key"));
            Assert.That(HeaderValue(request, "Authorization"), Is.Null);
        }

        private static string HeaderValue(IRestRequest request, string name)
        {
            var param = request.Parameters.Find(p =>
                p.Type == ParameterType.HttpHeader && p.Name == name
            );
            return param == null ? null : (string)param.Value;
        }

        // Regression for the first two-instance smoke test (7 Jul 2026): the live members_add
        // RPC returns a bare bigint scalar (PostgREST serializes it as e.g. "3"), and the
        // wrapper's old (JObject) cast crashed with InvalidCastException AFTER the row had
        // already committed server-side. Mocked shapes must match the live wire shape.
        [Test]
        public void MembersAdd_ScalarBigintResponse_ReturnsId()
        {
            var (client, executor, _) = MakeSignedInClient();
            executor.Handler = _ => FakeResponses.Make(HttpStatusCode.OK, "3");

            Assert.That(client.MembersAdd("c-1", "bob@dev.local"), Is.EqualTo(3L));
        }

        [Test]
        public void MembersAdd_NullResponseForAlreadyApprovedEmail_ReturnsNull()
        {
            // members_add is idempotent: on conflict it inserts nothing and returns SQL NULL,
            // which PostgREST serializes as the JSON literal null.
            var (client, executor, _) = MakeSignedInClient();
            executor.Handler = _ => FakeResponses.Make(HttpStatusCode.OK, "null");

            Assert.That(client.MembersAdd("c-1", "bob@dev.local"), Is.Null);
        }
    }
}
