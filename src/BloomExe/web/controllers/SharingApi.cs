using System.Collections.Generic;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.History;
using Bloom.Sharing;
using Bloom.TeamCollection;

namespace Bloom.web.controllers
{
    /// <summary>
    /// The sharing/* endpoints behind the Share dialog (ShareDialog.tsx): who has access to the
    /// current collection, and letting its admins invite people, change their roles, and remove
    /// them. The people involved are identified by their Bloom Library sign-in (see AccountApi),
    /// which is what the cloud backend will authenticate. Until that backend is deployed, the
    /// record lives in a local file (LocalFileCollectionSharingService).
    /// </summary>
    public class SharingApi
    {
        private const string kWebSocketContext = "sharing";
        private const string kWebSocketEventId_stateChanged = "stateChanged";

        private readonly CollectionSettings _settings;
        private readonly ITeamCollectionManager _tcManager;
        private readonly CurrentEditableCollectionSelection _collectionSelection;
        private readonly BloomWebSocketServer _webSocketServer;
        private readonly AccountApi _accountApi;
        private readonly ICollectionSharingService _sharingService;

        /// <summary>
        /// Created by autofac, which creates the one instance per open collection.
        /// </summary>
        public SharingApi(
            CollectionSettings settings,
            ITeamCollectionManager tcManager,
            CurrentEditableCollectionSelection collectionSelection,
            BloomWebSocketServer webSocketServer,
            AccountApi accountApi
        )
        {
            _settings = settings;
            _tcManager = tcManager;
            _collectionSelection = collectionSelection;
            _webSocketServer = webSocketServer;
            _accountApi = accountApi;
            _sharingService = new LocalFileCollectionSharingService(
                settings.FolderPath,
                settings.CollectionId,
                settings.CollectionName
            );
        }

        /// <summary>
        /// Register the sharing/* endpoints: state (GET), invite, setRole and remove (all POST).
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // On the UI thread, like teamCollection/getHistory, because when it starts sharing a
            // Team Collection it reads the history via the collection's book list
            // (BookCollection.GetBookInfos).
            apiHandler.RegisterEndpointHandler("sharing/state", HandleState, true);
            apiHandler.RegisterEndpointHandler("sharing/invite", HandleInvite, false);
            apiHandler.RegisterEndpointHandler("sharing/setRole", HandleSetRole, false);
            apiHandler.RegisterEndpointHandler("sharing/remove", HandleRemove, false);
            // This runs as the collection opens (after AccountApi has restored any saved
            // sign-in), which is when the cloud backend will note that a member is using it.
            RecordVisitIfSignedIn();
        }

        // Note that the signed-in person, if a member, is using the collection now. Done both
        // when the collection opens and when the Share dialog asks for the state (the user may
        // have signed in since opening the collection).
        private void RecordVisitIfSignedIn()
        {
            var email = SignedInEmail;
            if (!string.IsNullOrEmpty(email))
                _sharingService.RecordVisit(email, RegisteredName);
        }

        // The signed-in Bloom Library email, or empty when nobody is signed in.
        private string SignedInEmail => _accountApi.CurrentEmail;

        // The signed-in email, for an operation that needs someone signed in. The dialog only
        // offers these operations to a signed-in admin, so getting here without one is a bug.
        private string RequireSignedInEmail()
        {
            var email = SignedInEmail;
            if (string.IsNullOrEmpty(email))
                throw new SharingNotAllowedException("Nobody is signed in to Bloom Library.");
            return email;
        }

        // The user's name from their Bloom registration, for showing in the member list.
        private static string RegisteredName =>
            $"{TeamCollectionManager.CurrentUserFirstName} {TeamCollectionManager.CurrentUserSurname}".Trim();

        // Whether the signed-in person may manage sharing. Once the collection is shared, that
        // means being one of its admins. Before, it is whoever may edit the collection's settings
        // here: anyone, for an ordinary collection, or an administrator of a Team Collection.
        private bool CanManage(CollectionSharingRecord record)
        {
            var email = SignedInEmail;
            if (string.IsNullOrEmpty(email))
                return false;
            if (record == null)
                return _tcManager.OkToEditCollectionSettings;
            return record.Members.Any(m =>
                m.Role == SharingRole.Admin
                && string.Equals(m.Email, email, System.StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// GET sharing/state: everything the Share dialog shows. Also records that the signed-in
        /// person (if a member) is using the collection; see RecordVisitIfSignedIn. And the
        /// first time an administrator of a (folder) Team Collection looks, it shares the
        /// collection, with everyone its history shows has worked in it; see
        /// TeamCollectionHistoryMembers.StartSharingIfTeamCollection.
        /// </summary>
        private void HandleState(ApiRequest request)
        {
            var email = SignedInEmail;
            RecordVisitIfSignedIn();
            if (_sharingService.GetRecord() == null && CanManage(null))
            {
                TeamCollectionHistoryMembers.StartSharingIfTeamCollection(
                    _sharingService,
                    _tcManager.CurrentCollectionEvenIfDisconnected != null,
                    email,
                    RegisteredName,
                    () => CollectionHistory.GetAllEvents(_collectionSelection.CurrentSelection),
                    _settings.Administrators
                );
            }
            var record = _sharingService.GetRecord();
            request.ReplyWithJson(
                new
                {
                    collectionName = _settings.CollectionName,
                    signedInEmail = email,
                    signedInName = RegisteredName,
                    isShared = record != null,
                    canManage = CanManage(record),
                    members = record?.Members ?? new List<SharingMember>(),
                }
            );
        }

        private class InviteBody
        {
            public List<SharingInvitation> invitations { get; set; }
        }

        /// <summary>
        /// POST sharing/invite {invitations: [{email, role}]}: invite people. If the collection
        /// is not shared yet, this is what shares it, with the signed-in person as its admin.
        /// </summary>
        private void HandleInvite(ApiRequest request)
        {
            var body = request.RequiredPostObject<InviteBody>();
            var me = RequireSignedInEmail();
            if (_sharingService.GetRecord() == null)
            {
                if (!_tcManager.OkToEditCollectionSettings)
                    throw new SharingNotAllowedException(
                        "Only an administrator of this collection can share it."
                    );
                _sharingService.StartSharing(
                    me,
                    RegisteredName,
                    body.invitations,
                    new TeamCollectionHistoryMember[0]
                );
            }
            else
                _sharingService.Invite(me, body.invitations);
            ReportChange(request);
        }

        private class MemberRoleBody
        {
            public string email { get; set; }
            public SharingRole role { get; set; }
        }

        /// <summary>POST sharing/setRole {email, role}: change a member's role.</summary>
        private void HandleSetRole(ApiRequest request)
        {
            var body = request.RequiredPostObject<MemberRoleBody>();
            _sharingService.SetRole(RequireSignedInEmail(), body.email, body.role);
            ReportChange(request);
        }

        private class MemberBody
        {
            public string email { get; set; }
        }

        /// <summary>POST sharing/remove {email}: take away a member's access.</summary>
        private void HandleRemove(ApiRequest request)
        {
            var body = request.RequiredPostObject<MemberBody>();
            _sharingService.Remove(RequireSignedInEmail(), body.email);
            ReportChange(request);
        }

        // Finish a successful change: tell every open Share dialog to refresh.
        private void ReportChange(ApiRequest request)
        {
            _webSocketServer.SendEvent(kWebSocketContext, kWebSocketEventId_stateChanged);
            request.PostSucceeded();
        }
    }
}
