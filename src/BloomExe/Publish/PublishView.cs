using System;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.CollectionTab;
using Bloom.Publish.BloomLibrary;
using Bloom.Publish.BloomPub;
using Bloom.Publish.Epub;
using Bloom.Publish.Video;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;

namespace Bloom.Publish
{
    public class PublishView : IBloomTabArea, IDisposable
    {
        public readonly PublishModel _model;
        private BookUpload _bookTransferrer;
        private PublishToBloomPubApi _publishToBloomPubApi;
        private PublishAudioVideoAPI _publishToVideoApi;
        private PublishEpubApi _publishEpubApi;
        private BloomWebSocketServer _webSocketServer;
        private readonly WorkspaceTabSelection _tabSelection;
        private bool _isActive;

        internal WorkspaceView WorkspaceView { get; set; }

        public delegate PublishView Factory(); //autofac uses this

        public PublishView(
            PublishModel model,
            WorkspaceTabSelection tabSelection,
            SelectedTabChangedEvent selectedTabChangedEvent,
            LocalizationChangedEvent localizationChangedEvent,
            BookUpload bookTransferrer,
            PublishToBloomPubApi publishToBloomPubApi,
            PublishEpubApi publishEpubApi,
            PublishAudioVideoAPI publishToVideoApi,
            BloomWebSocketServer webSocketServer
        )
        {
            _bookTransferrer = bookTransferrer;
            _publishToBloomPubApi = publishToBloomPubApi;
            _publishEpubApi = publishEpubApi;
            _publishToVideoApi = publishToVideoApi;
            _model = model;
            _model.View = this;
            _webSocketServer = webSocketServer;
            _tabSelection = tabSelection;

            //NB: just triggering off "VisibilityChanged" was unreliable. So now we trigger
            //off the tab itself changing, either to us or away from us.
            selectedTabChangedEvent.Subscribe(_ =>
            {
                var shouldBeActive = _tabSelection.ActiveTab == WorkspaceTab.publish;
                if (shouldBeActive == _isActive)
                    return;
                // Record where we are going before doing the work, not after. If some part of
                // activating or deactivating throws, we must not be left believing we are still in
                // the state we just left: that stale belief would silently skip the *next*
                // Activate(), and with it the SetTabsEnabled(true) below that releases the Edit
                // tab's save lock -- leaving the Collection and Edit tabs greyed out until the user
                // restarts Bloom. That was half of BL-16174. The exception itself still propagates.
                _isActive = shouldBeActive;
                if (shouldBeActive)
                    Activate();
                else
                    Deactivate();
            });

            //TODO: find a way to call this just once, at the right time:

            //			DeskAnalytics.Track("Publish");
        }

        private void Deactivate()
        {
            if (_model.IsMakingPdf)
                _model.CancelMakingPdf();
            _publishEpubApi?.AbortMakingEpub();
            _publishToVideoApi.AbortMakingVideo();
            // TODO-WV2: Can we clear the cache for WV2? Do we need to?
            PublishHelper.Cancel();
            // Note: PublishHelper.InPublishTab is not ours to clear; WorkspaceTabSelection.ActiveTab
            // has already done it. See the comment there (BL-16174).
            _webSocketServer.SendEvent("publish", "switchOutOfPublishTab");
        }

        public void Dispose()
        {
            _publishEpubApi?.EpubMaker?.Dispose();
            _publishToBloomPubApi?.Dispose();
        }

        internal Control GetHostControlForInvoke()
        {
            var hostForm = WorkspaceView?.FindForm();
            if (hostForm != null)
                return hostForm;

            return WorkspaceView;
        }

        private void Activate()
        {
            // Safety net: any Edit-tab save lock must be complete before we reach Publish,
            // so ensure tab switching is enabled in case the re-enable callback was missed.
            WorkspaceView?.SetTabsEnabled(true);
            // Note: PublishHelper.InPublishTab is not ours to set; WorkspaceTabSelection.ActiveTab
            // has already done it, before any of this event's subscribers ran. See the comment
            // there (BL-16174).
            var hostForm = GetHostControlForInvoke() as Form;
            PublishEpubApi.ControlForInvoke = hostForm;
            LibraryPublishApi.Model = new BloomLibraryPublishModel(
                _bookTransferrer,
                _model.BookSelection.CurrentSelection,
                _model
            );
            PublishApi.Model = new BloomLibraryPublishModel(
                _bookTransferrer,
                _model.BookSelection.CurrentSelection,
                _model
            );
            _webSocketServer.SendEvent("publish", "switchToPublishTab");
        }

        // This property is invoked in WorkspaceView as "CurrentTabView.HelpTopicUrl".  Until the
        // tab view mechanism and overall WorkspaceView is converted to typescript, carrying the
        // help menu with it, this property needs to stay in C#.

        public string HelpTopicUrl
        {
            get { return "/Tasks/Publish_tasks/Publish_tasks_overview.htm"; }
        }
    }
}
