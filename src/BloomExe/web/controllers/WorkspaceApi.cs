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
                "workspace/topRight/state",
                HandleGetTopRightState,
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
            apiHandler.RegisterEndpointHandler("workspace/topRight/zoom", HandleZoom, true);
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

        private void HandleGetTopRightState(ApiRequest request)
        {
            WorkspaceView.Invoke(
                new Action(() => request.ReplyWithJson(WorkspaceView.BuildTopRightState()))
            );
        }

        private void HandleOpenLanguageMenu(ApiRequest request)
        {
            WorkspaceView.Invoke(new Action(WorkspaceView.ShowUiLanguageMenuAtCursor));
            request.PostSucceeded();
        }

        private void HandleOpenHelpMenu(ApiRequest request)
        {
            WorkspaceView.Invoke(new Action(WorkspaceView.ShowHelpMenuAtCursor));
            request.PostSucceeded();
        }

        private void HandleZoom(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Get)
            {
                request.ReplyWithJson(WorkspaceView.BuildTopRightState());
                return;
            }

            var data = request.RequiredPostDynamic();
            int zoom = Convert.ToInt32(data.zoom);
            WorkspaceView.Invoke(new Action(() => WorkspaceView.SetZoomFromApi(zoom)));
            request.PostSucceeded();
        }
    }
}
