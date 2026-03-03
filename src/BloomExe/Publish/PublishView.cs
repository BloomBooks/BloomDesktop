using System;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.CollectionTab;
using Bloom.Publish.BloomLibrary;
using Bloom.Publish.BloomPub;
using Bloom.Publish.Epub;
using Bloom.Publish.Video;
using Bloom.web;
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
        private readonly IframeReactControl _iframeReactControl;
        private bool _loadedIntoIframe;
        private bool _isActive;
        private EventHandler _mainBrowserClickBridge;

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
            _iframeReactControl = new IframeReactControl();
            localizationChangedEvent.Subscribe(unused =>
            {
                if (_loadedIntoIframe)
                {
                    ReloadIntoIframe();
                }
            });

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
            if (WorkspaceView?.MainBrowser != null && _mainBrowserClickBridge != null)
                WorkspaceView.MainBrowser.OnBrowserClick -= _mainBrowserClickBridge;

            _iframeReactControl?.Dispose();
            _publishEpubApi?.EpubMaker?.Dispose();
            _publishToBloomPubApi?.Dispose();
        }

        internal void EnsureLoadedInMainBrowser()
        {
            ReloadIntoIframe();
        }

        internal Control GetHostControlForInvoke()
        {
            var hostForm = WorkspaceView?.FindForm();
            if (hostForm != null)
                return hostForm;

            return WorkspaceView;
        }

        private void ReloadIntoIframe()
        {
            if (WorkspaceView?.MainBrowser == null)
                return;

            WorkspaceView.EnsureMainBrowserHasWorkspaceRootLoaded();
            _ = _iframeReactControl.Load(
                WorkspaceView.MainBrowser,
                "publishTabPaneBundle",
                null,
                "publishTab"
            );
            _loadedIntoIframe = true;
        }

        private void Activate()
        {
            PublishHelper.InPublishTab = true;
            var hostForm = GetHostControlForInvoke() as Form;
            BloomPubMaker.ControlForInvoke = hostForm;
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

        // Temporary bridge while workspace menus are still WinForms menus.
        // Remove when menus and tabs run in one browser UI.
        internal event EventHandler BrowserClick
        {
            add
            {
                if (WorkspaceView?.MainBrowser == null)
                    return;

                _mainBrowserClickBridge += value;
                WorkspaceView.MainBrowser.OnBrowserClick += value;
            }
            remove
            {
                if (WorkspaceView?.MainBrowser == null)
                    return;

                _mainBrowserClickBridge -= value;
                WorkspaceView.MainBrowser.OnBrowserClick -= value;
            }
        }
    }
}
