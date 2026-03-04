using System;
using System.Drawing;
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

namespace Bloom.Publish
{
    public class PublishView : UserControl, IBloomTabArea
    {
        public readonly PublishModel _model;
        private BookUpload _bookTransferrer;
        private PublishToBloomPubApi _publishToBloomPubApi;
        private PublishAudioVideoAPI _publishToVideoApi;
        private PublishEpubApi _publishEpubApi;
        private BloomWebSocketServer _webSocketServer;

        private web.ReactControl _reactControl;

        public delegate PublishView Factory(); //autofac uses this

        public PublishView(
            PublishModel model,
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

            _reactControl = new ReactControl();
            _reactControl.JavascriptBundleName = "publishTabPaneBundle";
            _reactControl.BackColor = Palette.GeneralBackground;
            _reactControl.Dock = System.Windows.Forms.DockStyle.Fill;
            _reactControl.Location = new System.Drawing.Point(0, 0);
            _reactControl.Name = "_reactControl";
            _reactControl.Size = new System.Drawing.Size(773, 518);
            _reactControl.TabIndex = 16;
            Controls.Add(_reactControl);
            _reactControl.SetLocalizationChangedEvent(localizationChangedEvent);

            //NB: just triggering off "VisibilityChanged" was unreliable. So now we trigger
            //off the tab itself changing, either to us or away from us.
            selectedTabChangedEvent.Subscribe(c =>
            {
                if (c.To == this)
                {
                    Activate();
                }
                else if (c.To != this)
                {
                    Deactivate();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reactControl?.Dispose();
                _publishEpubApi?.EpubMaker?.Dispose();
                _publishToBloomPubApi?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void Activate()
        {
            PublishHelper.InPublishTab = true;
            BloomPubMaker.ControlForInvoke = ParentForm;
            PublishEpubApi.ControlForInvoke = ParentForm;
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
            add { _reactControl.OnBrowserClick += value; }
            remove { _reactControl.OnBrowserClick -= value; }
        }
    }
}
