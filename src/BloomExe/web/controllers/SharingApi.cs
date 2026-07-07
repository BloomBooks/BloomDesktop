using System;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.History;
using Bloom.MiscUI;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Cloud Team Collections (task 06): the "sharing/*" and "collections/*" endpoints the UI
    /// (task 07's sharingApi.ts, task 08's teamCollectionApi.tsx) already calls against mocked
    /// versions of. Thin pass-throughs to CloudCollectionClient/CloudJoinFlow/CloudAuth per
    /// CONTRACTS.md and the task brief -- no Cloud-backend business logic lives here, only wire-
    /// shape adaptation (RPC snake_case -&gt; the camelCase shapes sharingApi.ts already committed
    /// to) and dispatch to whichever collection is relevant.
    ///
    /// Registered at the APPLICATION level (ApplicationContainer.cs), like CollectionChooserApi --
    /// NOT per-project like TeamCollectionApi. This is required, not a style choice:
    /// collections/mine, collections/pullDown, and sharing/loginState/login/logout must all work
    /// from the collection chooser BEFORE any project is loaded (see MyCloudCollectionsSection /
    /// useSharingLoginState, which the chooser renders pre-project-load). Endpoints that are only
    /// meaningful for an already-open cloud collection (members/addApproval/removeApproval/
    /// setRole/forceUnlock/history) reach whichever project happens to be currently open via
    /// <see cref="TeamCollectionApi.TheOneInstance"/> -- the same static hook TeamCollectionApi
    /// already exposes for exactly this purpose (TcManager/SocketServer/CurrentBookFolderName).
    /// Since at most one project is ever open in a Bloom.exe process, this is a legitimate cross-
    /// class hook-up, not a duplicate of TeamCollectionApi's own book-status logic.
    /// </summary>
    public class SharingApi
    {
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("sharing/loginState", HandleLoginState, false);
            apiHandler.RegisterEndpointHandler("sharing/login", HandleLogin, false);
            apiHandler.RegisterEndpointHandler("sharing/logout", HandleLogout, false);
            // Not yet backed by a dedicated sign-in dialog (none exists in the UI today -- see the
            // final report); reuses the cloud create-collection dialog, whose first state IS a
            // sign-in form, as the best currently-available UI surface.
            apiHandler.RegisterEndpointHandler("sharing/showSignIn", HandleShowSignIn, true);

            apiHandler.RegisterEndpointHandler("sharing/members", HandleMembers, false);
            apiHandler.RegisterEndpointHandler("sharing/addApproval", HandleAddApproval, false);
            apiHandler.RegisterEndpointHandler(
                "sharing/removeApproval",
                HandleRemoveApproval,
                false
            );
            apiHandler.RegisterEndpointHandler("sharing/setRole", HandleSetRole, false);

            // Same handler TeamCollectionApi registers under "teamCollection/forceUnlock" --
            // ForceUnlock already dispatches to the audited RPC for a cloud backend via
            // UnlockInRepo(force: true), so there is exactly one implementation either way.
            apiHandler.RegisterEndpointHandler("sharing/forceUnlock", HandleForceUnlock, false);

            apiHandler.RegisterEndpointHandler("sharing/history", HandleHistory, false);
            apiHandler.RegisterEndpointHandler("sharing/historyCache", HandleHistoryCache, false);

            apiHandler.RegisterEndpointHandler("collections/mine", HandleMyCollections, false);
            apiHandler.RegisterEndpointHandler("collections/pullDown", HandlePullDown, true);
        }

        // ------------------------------------------------------------------
        // Identity: which CloudAuth/CloudCollectionClient to use for a given call.
        // ------------------------------------------------------------------

        // Process-wide fallback identity for chooser-time use (no project loaded, or the loaded
        // project isn't itself a cloud TC). Lazily created on first use; deliberately NOT the same
        // object as any open CloudTeamCollection's own auth (that one is preferred whenever it
        // exists -- see CurrentAuth below), so this only matters before/between cloud collections.
        private static CloudAuth _globalAuth;
        private static CloudCollectionClient _globalClient;
        private static readonly object _globalAuthLock = new object();

        private static CloudTeamCollection CurrentCloudCollection() =>
            TeamCollectionApi.TheOneInstance?.TcManager?.CurrentCollection as CloudTeamCollection;

        /// <summary>The auth session to use: the CURRENTLY OPEN cloud collection's own (the single
        /// source of truth for "am I signed in", since that's what's actually driving its book
        /// locks), or a lazily-created process-wide fallback for chooser-time use before any cloud
        /// collection is open.</summary>
        private static CloudAuth CurrentAuth() =>
            CurrentCloudCollection()?.Auth ?? GetOrCreateGlobalAuth();

        private static CloudCollectionClient CurrentClient() =>
            CurrentCloudCollection()?.Client ?? GetOrCreateGlobalClient();

        private static CloudAuth GetOrCreateGlobalAuth()
        {
            lock (_globalAuthLock)
            {
                if (_globalAuth == null)
                {
                    var environment = CloudEnvironment.Current;
                    _globalAuth = new CloudAuth(CloudAuth.CreateProvider(environment));
                    _globalAuth.InitializeAtStartup(environment);
                }
                return _globalAuth;
            }
        }

        private static CloudCollectionClient GetOrCreateGlobalClient()
        {
            lock (_globalAuthLock)
            {
                return _globalClient ??= new CloudCollectionClient(
                    CloudEnvironment.Current,
                    GetOrCreateGlobalAuth()
                );
            }
        }

        /// <summary>Pushes a websocket event on the SAME connection the currently-open project's UI
        /// listens on (see TeamCollectionApi.SocketServer's own doc comment for why this indirect
        /// path is necessary). A no-op when no project is loaded -- nothing is listening then
        /// anyway.</summary>
        private static void NotifyClients(string clientContext, string eventId) =>
            TeamCollectionApi.TheOneInstance?.SocketServer?.SendEvent(clientContext, eventId);

        // ------------------------------------------------------------------
        // Sign-in state
        // ------------------------------------------------------------------

        private void HandleLoginState(ApiRequest request)
        {
            var loginState = CurrentAuth().GetLoginState(CloudEnvironment.Current);
            request.ReplyWithJson(
                new
                {
                    mode = loginState.AuthMode,
                    signedIn = loginState.SignedIn,
                    email = loginState.Email,
                }
            );
        }

        private class LoginBody
        {
            public string email;
            public string password;
        }

        private void HandleLogin(ApiRequest request)
        {
            var body = request.RequiredPostObject<LoginBody>();
            try
            {
                CurrentAuth().SignIn(body.email, body.password);
                NotifyClients("sharing", "loginState");
                request.PostSucceeded();
            }
            catch (CloudAuthException e)
            {
                Logger.WriteError("SharingApi.HandleLogin: sign-in failed", e);
                request.Failed(e.Message);
            }
        }

        private void HandleLogout(ApiRequest request)
        {
            CurrentAuth().SignOut();
            NotifyClients("sharing", "loginState");
            request.PostSucceeded();
        }

        /// <summary>Delegates to TeamCollectionApi's own HandleForceUnlock (see its doc comment):
        /// exactly one implementation, registered under two endpoint names.</summary>
        private void HandleForceUnlock(ApiRequest request)
        {
            var teamCollectionApi = TeamCollectionApi.TheOneInstance;
            if (teamCollectionApi == null)
            {
                request.Failed("No project is currently open.");
                return;
            }
            teamCollectionApi.HandleForceUnlock(request);
        }

        private void HandleShowSignIn(ApiRequest request)
        {
            ReactDialog.ShowOnIdle(
                "createTeamCollectionDialogBundle",
                new { },
                600,
                580,
                null,
                null,
                "Sign In"
            );
            request.PostSucceeded();
        }

        // ------------------------------------------------------------------
        // Approved-accounts management (admin-only server-side; RLS/RPCs enforce it, so this
        // class doesn't need its own permission check)
        // ------------------------------------------------------------------

        /// <summary>Maps one tc.members row (members_list's snake_case RPC shape) to the camelCase
        /// IApprovedMember shape sharingApi.ts expects. There is no display-name data source for
        /// members server-side (see the 20260707000006 migration's own comment on this same gap for
        /// book locks) -- name is always omitted/null here, exactly as CONTRACTS.md documents
        /// ("Only known once claimed" -- in practice, not known at all yet). Internal (not private)
        /// so SharingApiTests can verify the mapping without a live server.</summary>
        internal static object ToApprovedMember(JObject row) =>
            new
            {
                email = (string)row["email"],
                name = (string)null,
                role = (string)row["role"],
                // The (string) cast maps a JSON-null token to a real null; a bare `!= null`
                // check is TRUE for JTokenType.Null and made every pending invitation look
                // claimed in the sharing panel.
                claimed = (string)row["user_id"] != null,
            };

        private void HandleMembers(ApiRequest request)
        {
            var collectionId = request.RequiredParam("collectionId");
            var members = CurrentClient()
                .MembersList(collectionId)
                .OfType<JObject>()
                .Select(ToApprovedMember)
                .ToList();
            request.ReplyWithJson(members);
        }

        private class MemberApprovalBody
        {
            public string collectionId;
            public string email;
            public string role;
        }

        private void HandleAddApproval(ApiRequest request)
        {
            var body = request.RequiredPostObject<MemberApprovalBody>();
            CurrentClient().MembersAdd(body.collectionId, body.email, body.role ?? "member");
            NotifyClients("sharing", "membersChanged");
            request.PostSucceeded();
        }

        /// <summary>Resolves an email to its tc.members row id within a collection -- needed
        /// because the deployed members_remove/members_set_role RPCs key off the numeric member
        /// row id, not email (see CloudCollectionClient.MembersRemove/MembersSetRole's own doc
        /// comments on this task-06 live-verification fix). Throws if the email isn't an approved
        /// member of the collection -- a genuine caller error (AGENTS.md: fail fast rather than
        /// silently swallow), not something to report as an ordinary "not found" JSON result. Takes
        /// the already-fetched member list rather than a collectionId so SharingApiTests can
        /// exercise the resolution logic without a live server.
        /// </summary>
        internal static long ResolveMemberIdFromList(
            JArray members,
            string collectionId,
            string email
        )
        {
            var row = members
                .OfType<JObject>()
                .FirstOrDefault(m =>
                    string.Equals((string)m["email"], email, StringComparison.OrdinalIgnoreCase)
                );
            if (row == null)
                throw new ApplicationException(
                    $"'{email}' is not an approved member of collection {collectionId}."
                );
            return (long)row["id"];
        }

        private static long ResolveMemberId(string collectionId, string email) =>
            ResolveMemberIdFromList(CurrentClient().MembersList(collectionId), collectionId, email);

        private void HandleRemoveApproval(ApiRequest request)
        {
            var body = request.RequiredPostObject<MemberApprovalBody>();
            var memberId = ResolveMemberId(body.collectionId, body.email);
            CurrentClient().MembersRemove(body.collectionId, memberId);
            NotifyClients("sharing", "membersChanged");
            request.PostSucceeded();
        }

        private void HandleSetRole(ApiRequest request)
        {
            var body = request.RequiredPostObject<MemberApprovalBody>();
            var memberId = ResolveMemberId(body.collectionId, body.email);
            CurrentClient().MembersSetRole(body.collectionId, memberId, body.role);
            NotifyClients("sharing", "membersChanged");
            request.PostSucceeded();
        }

        // ------------------------------------------------------------------
        // History (CONTRACTS.md's get_changes RPC, surfaced for CollectionHistoryTable.tsx's cloud
        // path). Maps events to the exact same BookHistoryEvent shape (Title/ThumbnailPath/When/
        // Message/Type/UserId/UserName) folder TCs already return from teamCollection/getHistory,
        // so the UI's rendering code is unchanged either way.
        // ------------------------------------------------------------------

        internal static BookHistoryEvent ToBookHistoryEvent(JObject e) =>
            new BookHistoryEvent
            {
                BookId = (string)e["book_id"],
                Title = (string)e["book_name"],
                ThumbnailPath = "", // no local thumbnail for a server-originated event; see report.
                Message = (string)e["message"],
                Type = (BookHistoryEventType)(int)e["type"],
                UserId = (string)e["by_user_id"],
                UserName = (string)e["by_user_name"] ?? (string)e["by_email"],
                When = (DateTime)e["occurred_at"],
                BloomVersion = (string)e["bloom_version"],
            };

        /// <summary>Full collection history since the beginning (get_changes(sinceEventId: 0)).
        /// CONTRACTS.md defines no dedicated "whole history" RPC; get_changes with a 0 cursor is
        /// the documented mechanism for catch-up from scratch. Optionally filtered to one book,
        /// resolving Bloom's currently-selected book folder to the server's book id via the
        /// currently-open CloudTeamCollection's own index (there is no other way to make that
        /// translation from outside that class).</summary>
        private System.Collections.Generic.List<BookHistoryEvent> FetchAndCacheHistory(
            string collectionId,
            bool currentBookOnly
        )
        {
            var changes = CurrentClient().GetChanges(collectionId, 0);
            var events = ((JArray)changes["events"])
                .OfType<JObject>()
                .Select(ToBookHistoryEvent)
                .OrderByDescending(e => e.When)
                .ToList();

            SaveHistoryCache(collectionId, events);

            if (currentBookOnly)
            {
                var bookId = CurrentCloudCollection()
                    ?.TryGetBookIdForHistoryFilter(
                        TeamCollectionApi.TheOneInstance?.CurrentBookFolderName
                    );
                events = events.Where(e => e.BookId == bookId).ToList();
            }
            return events;
        }

        private void HandleHistory(ApiRequest request)
        {
            var collectionId = request.RequiredParam("collectionId");
            var currentBookOnly = request.GetParamOrNull("currentBookOnly") == "true";
            request.ReplyWithJson(
                JsonConvert.SerializeObject(FetchAndCacheHistory(collectionId, currentBookOnly))
            );
        }

        private static string HistoryCachePath(string collectionId)
        {
            var folder = CurrentCloudCollection()?.LocalCollectionFolder;
            return folder == null ? null : Path.Combine(folder, ".bloom-cloud-history-cache.json");
        }

        /// <summary>Persists the last-fetched history so <see cref="HandleHistoryCache"/> has
        /// something to serve while disconnected (task 08's "stand-in for a Wave-3 on-disk cache" --
        /// this task provides the real thing). Best-effort: a failure to write the cache must never
        /// break the live history fetch that just succeeded.</summary>
        private static void SaveHistoryCache(
            string collectionId,
            System.Collections.Generic.List<BookHistoryEvent> events
        )
        {
            var path = HistoryCachePath(collectionId);
            if (path == null)
                return;
            try
            {
                RobustFile.WriteAllText(path, JsonConvert.SerializeObject(events));
            }
            catch (Exception e)
            {
                NonFatalProblem.ReportSentryOnly(e, "SharingApi: failed to write history cache");
            }
        }

        private void HandleHistoryCache(ApiRequest request)
        {
            var collectionId = request.RequiredParam("collectionId");
            var currentBookOnly = request.GetParamOrNull("currentBookOnly") == "true";
            var path = HistoryCachePath(collectionId);
            if (path == null || !RobustFile.Exists(path))
            {
                request.ReplyWithJson(new System.Collections.Generic.List<BookHistoryEvent>());
                return;
            }
            var events =
                JsonConvert.DeserializeObject<System.Collections.Generic.List<BookHistoryEvent>>(
                    RobustFile.ReadAllText(path)
                );
            if (currentBookOnly)
            {
                var bookId = CurrentCloudCollection()
                    ?.TryGetBookIdForHistoryFilter(
                        TeamCollectionApi.TheOneInstance?.CurrentBookFolderName
                    );
                events = events.Where(e => e.BookId == bookId).ToList();
            }
            request.ReplyWithJson(JsonConvert.SerializeObject(events));
        }

        // ------------------------------------------------------------------
        // Collections (chooser)
        // ------------------------------------------------------------------

        /// <summary>Maps one my_collections() row (snake_case RPC shape, incl. its "my_role" alias)
        /// to the camelCase ICloudCollectionSummary shape sharingApi.ts expects.</summary>
        internal static object ToCollectionSummary(JObject row) =>
            new
            {
                collectionId = (string)row["id"],
                name = (string)row["name"],
                role = (string)row["my_role"],
            };

        private void HandleMyCollections(ApiRequest request)
        {
            var collections = CurrentClient()
                .MyCollections()
                .OfType<JObject>()
                .Select(ToCollectionSummary)
                .ToList();
            request.ReplyWithJson(collections);
        }

        private class PullDownBody
        {
            public string collectionId;
        }

        /// <summary>Joins (pulls down) a cloud collection the signed-in user is approved for.
        /// Looks up its name via the same my_collections list the chooser already showed (the UI
        /// only sends the id), then delegates to CloudJoinFlow -- the six-scenario matching logic
        /// (task 05) already handles "already joined"/name-collision cases by throwing
        /// CloudJoinConflictException, surfaced here as a plain failure message pending the
        /// dedicated resolution dialog noted in task 07's final report.</summary>
        private void HandlePullDown(ApiRequest request)
        {
            var body = request.RequiredPostObject<PullDownBody>();
            var client = CurrentClient();
            var summary = client
                .MyCollections()
                .OfType<JObject>()
                .FirstOrDefault(c => (string)c["id"] == body.collectionId);
            if (summary == null)
            {
                request.Failed(
                    "You are not approved for this Team Collection (or it no longer exists)."
                );
                return;
            }

            try
            {
                var joinFlow = new CloudJoinFlow(client);
                // May be null if no project is currently loaded (pull-down from the pre-startup
                // collection chooser) -- CloudJoinFlow.JoinCollection only stores this in the new
                // CloudTeamCollection's base-class field for the narrow download-everything
                // operation it performs here (never dereferenced along that path today), matching
                // how it's also never exercised by any existing unit test with a real manager
                // either. See the final report for a recommended live smoke test of this specific
                // path.
                var manager = TeamCollectionApi.TheOneInstance?.TcManager;
                joinFlow.JoinCollection(body.collectionId, (string)summary["name"], manager);
                request.PostSucceeded();
            }
            catch (CloudJoinConflictException e)
            {
                Logger.WriteError("SharingApi.HandlePullDown: join conflict", e);
                request.Failed(e.Message);
            }
            catch (Exception e)
            {
                Logger.WriteError("SharingApi.HandlePullDown: failed", e);
                NonFatalProblem.ReportSentryOnly(e, "SharingApi.HandlePullDown");
                request.Failed(e.Message);
            }
        }
    }
}
