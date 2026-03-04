using System;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Workspace;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Handles the one command that is specific to the WorkspaceView
    /// </summary>
    public class WorkspaceApi
    {
        // We'd prefer to have this a private value set from a WorkspaceView automatically
        // passed to a constructor by autofac. However, there's something special about the
        // way we create WorkspaceView that prevents one just being created when a WorkspaceApi
        // is wanted. So instead, we let he WorkspaceView get one of these as a constructor
        // argument, and set this in that constructor.
        public WorkspaceView WorkspaceView;

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "workspace/openOrCreateCollection/",
                HandleOpenOrCreateCollection,
                true
            );

            apiHandler.RegisterEndpointHandler(
                "workspace/showLegacySettingsDialog",
                HandleShowLegacySettingsDialog,
                true
            );

            apiHandler.RegisterEndpointHandler(
                "workspace/topRight/openLanguageMenu",
                HandleOpenLanguageMenu,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "workspace/topRight/openHelpMenu",
                HandleOpenHelpMenu,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "workspace/topRight/uiLanguageLabel",
                HandleGetUiLanguageLabel,
                true
            );
            apiHandler.RegisterEndpointHandler("workspace/topRight/zoom", HandleZoom, true);

            apiHandler.RegisterEndpointHandler("workspace/selectTab", HandleSelectTab, true);
            apiHandler.RegisterEndpointHandler("workspace/tabs", HandleTabs, true);
        }

        private void HandleOpenOrCreateCollection(ApiRequest request)
        {
            // This shuts everything down, so it needs to happen after all the request processing
            // is complete.
            Application.Idle += OpenCreateCollection;
            request.PostSucceeded();
        }

        private void OpenCreateCollection(object sender, EventArgs e)
        {
            Application.Idle -= OpenCreateCollection;
            WorkspaceView.OpenCreateCollection();
        }

        private void HandleShowLegacySettingsDialog(ApiRequest request)
        {
            // We used to launch the dialog directly from here, but that caused problems because
            // request was synchronous and wouldn't complete until the dialog was closed.  So now
            // we return success immediately, and then launch the dialog when the application is idle.
            // This avoids tying up a thread waiting for the dialog to close, and also avoids having
            // that thread lock the API processing.  (BL-15858)
            Application.Idle += ShowLegacySettingsDialog;

            // When the fully react dialog is ready, we'll do this instead:
            // _webSocketServer.LaunchDialog("CollectionSettingsDialog");
            request.PostSucceeded();
        }

        private void ShowLegacySettingsDialog(object sender, EventArgs e)
        {
            Application.Idle -= ShowLegacySettingsDialog;
            WorkspaceView.OpenLegacySettingsDialog();
        }

        private void HandleGetUiLanguageLabel(ApiRequest request)
        {
            request.ReplyWithJson(WorkspaceView.GetCurrentUiLanguageLabel());
        }

        private void HandleOpenLanguageMenu(ApiRequest request)
        {
            WorkspaceView.ShowUiLanguageMenu();
            request.PostSucceeded();
        }

        private void HandleOpenHelpMenu(ApiRequest request)
        {
            WorkspaceView.ShowHelpMenu();
            request.PostSucceeded();
        }

        private void HandleZoom(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Get)
            {
                request.ReplyWithJson(WorkspaceView.GetZoomInfo());
                return;
            }

            var data = request.RequiredPostDynamic();
            int zoom = Convert.ToInt32(data.zoom);
            WorkspaceView.SetZoom(zoom);
            request.PostSucceeded();
        }

        private void HandleSelectTab(ApiRequest request)
        {
            var data = request.RequiredPostDynamic();
            var tab = (string)data.tab;
            switch (tab)
            {
                case "collection":
                    WorkspaceView.ChangeTab(WorkspaceTab.collection);
                    break;
                case "edit":
                    WorkspaceView.ChangeTab(WorkspaceTab.edit);
                    break;
                case "publish":
                    WorkspaceView.ChangeTab(WorkspaceTab.publish);
                    break;
                default:
                    throw new ArgumentException($"Unknown tab '{tab}'");
            }

            request.PostSucceeded();
        }

        private void HandleTabs(ApiRequest request)
        {
            if (request.HttpMethod != HttpMethods.Get)
                throw new ArgumentException("workspace/tabs only supports GET");

            request.ReplyWithJson(WorkspaceView.GetTabInfoForClient());
        }
    }
}
