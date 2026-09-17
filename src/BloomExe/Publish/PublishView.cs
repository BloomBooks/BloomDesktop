using System;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
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
                if (_tabSelection.ActiveTab == WorkspaceTab.publish)
                {
                    if (!_isActive)
                    {
                        Activate();
                        _isActive = true;
                    }
                }
                else if (_isActive)
                {
                    Deactivate();
                    _isActive = false;
                }
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
            PublishHelper.InPublishTab = false;
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

            // Make sure the book has had the per-page updates that normally happen only when a page
            // is opened for editing, before we build anything (BloomPUB, ePUB, preview) from it. A
            // book that was never fully edited would otherwise publish with un-migrated pages
            // (BL-16852). No-op for a book already up to date, and for one we cannot save (e.g. a
            // Team Collection book not checked out), which publishes from what is already on disk.
            //
            // Two constraints pull against each other, so mind the structure here:
            //  - The pass must NOT run while this call holds Bloom's API sync lock. Activate runs
            //    inside the (UI-thread, sync-locked) workspace/selectTab handler, and ProcessBook
            //    drives off-screen pages that make their own sync-locked API calls as they load;
            //    under the held lock those would block (cf. external/process-book requiresSync:false).
            //  - Publishing must NOT start until the pass has finished, or a build could read the
            //    book while ProcessBook is still rewriting it.
            // So when the pass is due we defer BOTH it and the rest of activation, and run the rest
            // only after it returns. Its modal dialog blocks until done, so the publish tab does not
            // go live (InPublishTab, the publish APIs, switchToPublishTab) until the book is migrated.
            var book = _model.BookSelection.CurrentSelection;
            var shellForm = Shell.GetShellOrOtherOpenForm();
            if (
                shellForm != null
                && shellForm.IsHandleCreated
                && BookProcessor.NeedsPerPageFixup(book)
            )
            {
                shellForm.BeginInvoke(
                    (Action)(
                        () =>
                        {
                            BookProcessor.EnsurePerPageFixupIfNeeded(book, _webSocketServer);
                            // The modal blocks tab switching while it runs, but guard anyway: if we
                            // are somehow no longer on the Publish tab by the time it returns, don't
                            // activate now -- that would enable publishing and send switchToPublishTab
                            // under whatever tab is actually showing.
                            if (_tabSelection.ActiveTab == WorkspaceTab.publish)
                                ActivatePublishTab();
                        }
                    )
                );
                return;
            }

            ActivatePublishTab();
        }

        /// <summary>
        /// The rest of switching to the Publish tab, after any needed per-page fix-up has finished.
        /// Split out of Activate so it can be delayed until that fix-up completes (see Activate).
        /// </summary>
        private void ActivatePublishTab()
        {
            PublishHelper.InPublishTab = true;
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
