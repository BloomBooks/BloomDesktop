using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            WorkspaceView.OpenLegacySettingsDialog();

            // When the fully react dialog is ready, we'll do this instead:
            // _webSocketServer.LaunchDialog("CollectionSettingsDialog");

            request.PostSucceeded();
        }
    }
}
