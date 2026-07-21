using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Collection;
using Bloom.History;
using Bloom.MiscUI;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using Bloom.WebLibraryIntegration;
using DesktopAnalytics;
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
        /// <summary>Called once at startup (app-level registration) to register every sharing/*
        /// and collections/* endpoint this class serves.</summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("sharing/loginState", HandleLoginState, false);
            apiHandler.RegisterEndpointHandler("sharing/login", HandleLogin, false);
            apiHandler.RegisterEndpointHandler("sharing/logout", HandleLogout, false);
            apiHandler.RegisterEndpointHandler("sharing/showSignIn", HandleShowSignIn, true);
            apiHandler.RegisterEndpointHandler(
                "sharing/openBrowserSignIn",
                HandleOpenBrowserSignIn,
                false
            );

            apiHandler.RegisterEndpointHandler("sharing/members", HandleMembers, false);
            apiHandler.RegisterEndpointHandler("sharing/addApproval", HandleAddApproval, false);
            apiHandler.RegisterEndpointHandler(
                "sharing/removeApproval",
                HandleRemoveApproval,
                false
            );
            apiHandler.RegisterEndpointHandler("sharing/setRole", HandleSetRole, false);
            apiHandler.RegisterEndpointHandler(
                "sharing/setDisplayName",
                HandleSetDisplayName,
                false
            );

            // Same handler TeamCollectionApi registers under "teamCollection/forceUnlock" --
            // ForceUnlock already dispatches to the audited RPC for a cloud backend via
            // UnlockInRepo(force: true), so there is exactly one implementation either way.
            apiHandler.RegisterEndpointHandler("sharing/forceUnlock", HandleForceUnlock, false);

            apiHandler.RegisterEndpointHandler("sharing/history", HandleHistory, false);
            apiHandler.RegisterEndpointHandler("sharing/historyCache", HandleHistoryCache, false);

            apiHandler.RegisterEndpointHandler("collections/mine", HandleMyCollections, false);
            apiHandler.RegisterEndpointHandler("collections/pullDown", HandlePullDown, true);

            // Application-level ON PURPOSE (moved out of the project-level TeamCollectionApi;
            // post-batch defect, 10 Jul 2026): callers legitimately probe capabilities when no
            // project is open -- the E2E harness's readiness poll, or a late request from a
            // closing collection tab while the chooser is on screen -- and the project-level
            // registration made every such probe raise a "Cannot Find API Endpoint" toast.
            // Answering "no capabilities" (all false) is correct whenever no project is open.
            apiHandler.RegisterEndpointHandler(
                "teamCollection/capabilities",
                HandleCapabilities,
                false
            );
        }

        /// <summary>Backend capability flags (CONTRACTS.md, additive): tells the UI what the
        /// current Team Collection's backend can do, so components branch on capability rather
        /// than concrete backend type. All false for a folder TC, no collection, or no open
        /// project. Reaches the current project the same way this class's other handlers do
        /// (TeamCollectionApi.TheOneInstance); like them, it can briefly report the LAST project's
        /// capabilities between closing one collection and opening another, which is harmless for
        /// boolean capability flags.</summary>
        private void HandleCapabilities(ApiRequest request)
        {
            var collection = TeamCollectionApi.TheOneInstance?.TcManager?.CurrentCollection;
            request.ReplyWithJson(
                new
                {
                    supportsVersionHistory = collection?.SupportsVersionHistory ?? false,
                    supportsSharingUi = collection?.SupportsSharingUi ?? false,
                    requiresSignIn = collection?.RequiresSignIn ?? false,
                }
            );
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
                    _globalAuth = CloudAuth.CreateInitialized(CloudEnvironment.Current);
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
                    emailVerified = loginState.EmailVerified,
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

        /// <summary>
        /// The Bloom-side half of the token-receipt endpoint (ExternalApi's
        /// `external/cloudLogin`, task 12): turns a Firebase ID+refresh token pair -- forwarded
        /// by the BloomLibrary-hosted login page after a real sign-in, per CONTRACTS.md's "Auth
        /// (Option A)" section -- into a signed-in CloudAuth session, then notifies the UI
        /// exactly like <see cref="HandleLogin"/> does for the dev-mode password flow. Public
        /// and static (rather than an endpoint handler here) so ExternalApi.cs -- which already
        /// owns the browser-facing `external/*` conventions (CORS OPTIONS handling,
        /// Shell.ComeToFront) for the pre-existing Parse `external/login` -- can reuse this
        /// class's CurrentAuth()/NotifyClients() identity plumbing without duplicating it.
        /// Throws CloudAuthException on failure (e.g. a malformed token); the caller (ExternalApi)
        /// decides how to surface that over HTTP.
        /// </summary>
        public static void HandleCloudLoginTokens(string idToken, string refreshToken)
        {
            CurrentAuth().SignInWithExternalTokens(idToken, refreshToken);
            NotifyClients("sharing", "loginState");
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

        /// <summary>Opens the dedicated SignInDialog (SignInDialog.tsx), which shares the
        /// "createTeamCollectionDialogBundle" bundle/entry with the folder- and cloud-TC create
        /// dialogs -- see CreateTeamCollection.tsx's CreateTeamCollectionBundleDispatcher, which
        /// picks the right one via this dialogKind prop.</summary>
        private void HandleShowSignIn(ApiRequest request)
        {
            ReactDialog.ShowOnIdle(
                "createTeamCollectionDialogBundle",
                new { dialogKind = "signIn" },
                420,
                320,
                null,
                null,
                "Sign In"
            );
            request.PostSucceeded();
        }

        /// <summary>
        /// The "cloud" (Option A) sign-in mode's entry point from SignInDialog.tsx: there is no
        /// password form to submit, so the button there posts here instead, which just opens the
        /// same BloomLibrary-hosted login page Bloom already opens for BloomLibrary account
        /// sign-in (BloomLibraryAuthentication.LogIn -- see its own doc comment for the exact
        /// URL). That page forwards the resulting Firebase tokens back to Bloom's
        /// external/cloudLogin endpoint (CONTRACTS.md's "Auth (Option A)" section), which is what
        /// actually completes the sign-in; SignInDialog closes itself once that lands, via the
        /// same useSharingLoginState()/loginState websocket subscription every other sign-in path
        /// already relies on.
        /// </summary>
        private void HandleOpenBrowserSignIn(ApiRequest request)
        {
            BloomLibraryAuthentication.LogIn();
            request.PostSucceeded();
        }

        // ------------------------------------------------------------------
        // Approved-accounts management (admin-only server-side; RLS/RPCs enforce it, so this
        // class doesn't need its own permission check)
        // ------------------------------------------------------------------

        /// <summary>Maps one tc.members row (members_list's snake_case RPC shape) to the camelCase
        /// IApprovedMember shape sharingApi.ts expects. name comes from the durable
        /// tc.members.display_name column (20260713000001 migration; editable via
        /// sharing/setDisplayName below) and is null until someone sets it -- the UI falls back to
        /// the email. Internal (not private) so SharingApiTests can verify the mapping without a
        /// live server.</summary>
        internal static object ToApprovedMember(JObject row) =>
            new
            {
                email = (string)row["email"],
                name = (string)row["display_name"],
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

        private class SetDisplayNameBody
        {
            public string collectionId;
            public string email;
            public string displayName;
        }

        /// <summary>Sets a member's human-readable display name (shown wherever the member is
        /// displayed -- checkout status, history, sharing panel -- with email as the fallback).
        /// The server RPC (members_set_display_name, 20260713000001) allows an admin to set
        /// anyone's name and a claimed member to set their own; a blank name clears it.</summary>
        private void HandleSetDisplayName(ApiRequest request)
        {
            var body = request.RequiredPostObject<SetDisplayNameBody>();
            var memberId = ResolveMemberId(body.collectionId, body.email);
            CurrentClient().MembersSetDisplayName(body.collectionId, memberId, body.displayName);
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
                // Preference order: the member's CURRENT durable display name (by_display_name,
                // 20260713000001), then the JWT name claim frozen in at event time, then email.
                UserName =
                    (string)e["by_display_name"]
                    ?? (string)e["by_user_name"]
                    ?? (string)e["by_email"],
                When = (DateTime)e["occurred_at"],
                BloomVersion = (string)e["bloom_version"],
            };

        /// <summary>On-disk history cache: the merged events plus the get_changes cursor (the max
        /// event id they were fetched through). A later open fetches only NEW events via
        /// get_changes(cursor) and appends them, instead of re-downloading the whole log every time
        /// -- safe because the event log is append-only and get_changes' cursor is exclusive
        /// (pgTAP 7c). Trade-off accepted with John (16 Jul 2026): already-cached rows keep the book
        /// name / author display name they were fetched with, so a later rename or display-name edit
        /// isn't reflected on old rows until the cache is rebuilt.</summary>
        internal class CloudHistoryCache
        {
            public long MaxEventId;
            public List<BookHistoryEvent> Events = new List<BookHistoryEvent>();
        }

        /// <summary>Reads the history cache, tolerating a missing file and the pre-incremental
        /// on-disk format (a bare events array): an old-format file keeps its events (so the offline
        /// view still works) but reports cursor 0, so the next online fetch does one full refetch and
        /// re-saves in the new format.</summary>
        internal static CloudHistoryCache LoadHistoryCache(string path)
        {
            if (string.IsNullOrEmpty(path) || !RobustFile.Exists(path))
                return new CloudHistoryCache();
            try
            {
                var token = JToken.Parse(RobustFile.ReadAllText(path));
                if (token is JArray oldFormatEvents)
                    return new CloudHistoryCache
                    {
                        MaxEventId = 0,
                        Events = oldFormatEvents.ToObject<List<BookHistoryEvent>>(),
                    };
                return token.ToObject<CloudHistoryCache>() ?? new CloudHistoryCache();
            }
            catch (Exception e)
            {
                NonFatalProblem.ReportSentryOnly(e, "SharingApi: failed to read history cache");
                return new CloudHistoryCache();
            }
        }

        /// <summary>Persists the merged history + cursor so <see cref="HandleHistoryCache"/> can
        /// serve it while disconnected and the next fetch can resume from the cursor. Best-effort:
        /// a failure to write must never break the live history fetch that just succeeded.</summary>
        internal static void SaveHistoryCache(string path, CloudHistoryCache cache)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                RobustFile.WriteAllText(path, JsonConvert.SerializeObject(cache));
            }
            catch (Exception e)
            {
                NonFatalProblem.ReportSentryOnly(e, "SharingApi: failed to write history cache");
            }
        }

        /// <summary>Merges a get_changes response into the cached history. With a non-zero prior
        /// cursor the response holds only events AFTER it (exclusive), so they append; with cursor 0
        /// the response is the whole log, so it replaces. Result is sorted newest-first, as the UI
        /// expects. Pure/static so it can be unit-tested without a live client.</summary>
        internal static CloudHistoryCache MergeHistory(
            CloudHistoryCache cached,
            JArray newEventsJson,
            long? responseMaxEventId
        )
        {
            var newEvents = newEventsJson.OfType<JObject>().Select(ToBookHistoryEvent);
            var merged = (cached.MaxEventId > 0 ? cached.Events.Concat(newEvents) : newEvents)
                .OrderByDescending(e => e.When)
                .ToList();
            return new CloudHistoryCache
            {
                MaxEventId = responseMaxEventId ?? cached.MaxEventId,
                Events = merged,
            };
        }

        private List<BookHistoryEvent> FetchAndCacheHistory(
            string collectionId,
            bool currentBookOnly
        )
        {
            var path = HistoryCachePath(collectionId);
            var cached = LoadHistoryCache(path);
            // Fetch only events newer than the cursor (whole log when cursor is 0); merge + persist.
            var changes = CurrentClient().GetChanges(collectionId, cached.MaxEventId);
            var updated = MergeHistory(
                cached,
                (JArray)changes["events"],
                (long?)changes["max_event_id"]
            );
            SaveHistoryCache(path, updated);

            var events = updated.Events;
            if (currentBookOnly)
                events = FilterToCurrentBook(events);
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

        /// <summary>Keeps only the currently-selected book's events (resolving its server book id) --
        /// the "current book only" filter shared by the live and cached history paths.</summary>
        private static List<BookHistoryEvent> FilterToCurrentBook(List<BookHistoryEvent> events)
        {
            var bookId = CurrentCloudCollection()
                ?.TryGetBookIdForHistoryFilter(
                    TeamCollectionApi.TheOneInstance?.CurrentBookFolderName
                );
            return events.Where(e => e.BookId == bookId).ToList();
        }

        private void HandleHistoryCache(ApiRequest request)
        {
            var collectionId = request.RequiredParam("collectionId");
            var currentBookOnly = request.GetParamOrNull("currentBookOnly") == "true";
            var events = LoadHistoryCache(HistoryCachePath(collectionId)).Events;
            if (currentBookOnly)
                events = FilterToCurrentBook(events);
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

        /// <summary>The signed-in cloud account's email, or null when signed out. Used by
        /// CollectionChooserApi's join-card dedup (dogfood bug #11, John's ruling 13 Jul 2026):
        /// a local copy of a cloud collection only suppresses that collection's join card when
        /// the copy was most recently used by THIS account.</summary>
        internal static string SignedInEmailForJoinCards() => CurrentAuth().CurrentEmail;

        /// <summary>Used by CollectionChooserApi.HandleGetJoinCards to compute join cards for the
        /// collection chooser (dogfood batch 1, item 6): the cloud collections the signed-in user
        /// is approved for, in the minimal Id/Name shape CloudJoinFlow already defines. Returns an
        /// empty list WITHOUT any network call when not signed in (IsSignedIn is a local check,
        /// not an RPC) -- required so the chooser degrades silently for signed-out/folder-only
        /// users instead of making a doomed cloud call on every chooser render.</summary>
        public static IReadOnlyList<CloudCollectionSummary> GetMyCollectionsForJoinCards()
        {
            if (!CurrentAuth().IsSignedIn)
                return Array.Empty<CloudCollectionSummary>();
            try
            {
                return CurrentClient()
                    .MyCollections()
                    .OfType<JObject>()
                    .Select(o => new CloudCollectionSummary
                    {
                        Id = (string)o["id"],
                        Name = (string)o["name"],
                    })
                    .ToList();
            }
            catch (Exception e)
            {
                // Signed in but the server is unreachable (offline, outage, ...). Join cards are
                // decorative: the chooser must render its normal card list regardless, so this is
                // "no join cards right now", never an error surfaced to the user.
                Logger.WriteError("SharingApi: could not fetch collections for join cards", e);
                return Array.Empty<CloudCollectionSummary>();
            }
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
        /// dedicated resolution dialog noted in task 07's final report.
        ///
        /// Replies with the local .bloomCollection file path (task 10: "pull-down auto-open") so
        /// the caller (JoinCloudCollectionDialog) can invoke the same "workspace/openCollection"
        /// action the chooser's own cards use, instead of leaving the user to hunt for the newly
        /// pulled-down collection themselves. It must be the settings FILE, not the folder --
        /// that path flows through Program.SwitchToCollection, which expects what the chooser's
        /// MRU cards pass.</summary>
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
                var cloudTc = joinFlow.JoinCollection(
                    body.collectionId,
                    (string)summary["name"],
                    manager
                );

                // Analytics audit (task 10): the folder-TC join path tracks "TeamCollectionJoin"
                // from TeamCollectionApi.HandleJoinTeamCollection; this is that event's cloud
                // counterpart -- pull-down had no analytics at all before this.
                Analytics.Track(
                    "TeamCollectionJoin",
                    new Dictionary<string, string>()
                    {
                        { "CollectionId", body.collectionId },
                        { "CollectionName", (string)summary["name"] },
                        { "Backend", cloudTc.GetBackendType() },
                        { "User", CurrentAuth().GetLoginState(CloudEnvironment.Current).Email },
                        { "JoinType", "pullDown" },
                    }
                );

                request.ReplyWithJson(
                    new
                    {
                        collectionPath = CollectionSettings.FindSettingsFileInFolder(
                            cloudTc.LocalCollectionFolder
                        ),
                    }
                );
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
