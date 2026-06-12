using System.IO;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Collection;
using Bloom.CollectionCreating;
using Bloom.MiscUI;
using Bloom.Utils;
using Bloom.Workspace;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Application-level API handler for the collection chooser dialog. Registered at startup
    /// (unlike most API handlers which are project-level), so the dialog works both at initial
    /// startup (before any project is loaded) and when switching collections mid-session.
    ///
    /// When a project is loaded, WorkspaceView.Current is non-null and actions are delegated to
    /// WorkspaceView's existing collection-switching methods. When no project is loaded (startup),
    /// the chosen path is stored in Program.CollectionChosenAtStartup and the ReactDialog is closed
    /// so Program.ChooseACollection can proceed to open the collection.
    /// </summary>
    public class CollectionChooserApi
    {
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "workspace/openCollection",
                HandleOpenCollection,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "workspace/browseForCollection",
                HandleBrowseForCollection,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "workspace/createNewCollection",
                HandleCreateNewCollection,
                true
            );

            apiHandler.RegisterEndpointHandler(
                "workspace/uiLanguages",
                request => request.ReplyWithJson(WorkspaceView.GetAvailableUiLanguageNames()),
                true
            );
            apiHandler.RegisterEndpointHandler(
                "workspace/uiLanguageLabel",
                request => request.ReplyWithJson(WorkspaceView.GetCurrentUiLanguageLabel()),
                true
            );
            apiHandler.RegisterEndpointHandler(
                "workspace/uiLanguageAction",
                request =>
                {
                    dynamic data = request.RequiredPostDynamic();
                    var action = (string)data.action;
                    WorkspaceView.HandleUiLanguageAction(action, (string)data.languageName);
                    // At startup (no project loaded), a language change can't trigger a project
                    // reload, so we close and reopen the dialog so all strings re-fetch in the
                    // new language.
                    if (action == "setLanguage" && WorkspaceView.Current == null)
                        Application.Idle += CloseForLanguageChange;
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "workspace/showUnapprovedTranslations",
                request => WorkspaceView.GetShowUnapprovedTranslations(),
                (request, value) => WorkspaceView.SetShowUnapprovedTranslations(value),
                true
            );
        }

        private void CloseForLanguageChange(object sender, System.EventArgs e)
        {
            Application.Idle -= CloseForLanguageChange;
            ReactDialog.CloseCurrentModal("languageChanged");
        }

        private string _collectionPathToOpen;

        /// <summary>
        /// Opens the collection at the path supplied in the POST body.
        /// </summary>
        private void HandleOpenCollection(ApiRequest request)
        {
            _collectionPathToOpen = request.RequiredPostString();
            Application.Idle += OpenCollectionOnIdle;
            request.PostSucceeded();
        }

        private void OpenCollectionOnIdle(object sender, System.EventArgs e)
        {
            Application.Idle -= OpenCollectionOnIdle;
            var workspaceView = WorkspaceView.Current;
            if (workspaceView != null)
                workspaceView.OpenSpecificCollection(_collectionPathToOpen);
            else
            {
                Program.CollectionChosenAtStartup = _collectionPathToOpen;
                ReactDialog.CloseCurrentModal("collection-chosen");
            }
        }

        /// <summary>
        /// Shows a file picker for .bloomCollection files and opens the selected one.
        /// </summary>
        private void HandleBrowseForCollection(ApiRequest request)
        {
            Application.Idle += BrowseForCollectionOnIdle;
            request.PostSucceeded();
        }

        private void BrowseForCollectionOnIdle(object sender, System.EventArgs e)
        {
            Application.Idle -= BrowseForCollectionOnIdle;
            var workspaceView = WorkspaceView.Current;
            if (workspaceView != null)
            {
                workspaceView.BrowseForAndOpenCollection();
            }
            else
            {
                var path = BrowseForCollectionPath();
                if (path != null)
                {
                    Program.CollectionChosenAtStartup = path;
                    ReactDialog.CloseCurrentModal("collection-chosen");
                }
                // If null, the user cancelled the file dialog; they remain in the chooser.
            }
        }

        /// <summary>
        /// Runs the New Collection Wizard and opens the new collection if the user completes it.
        /// </summary>
        private void HandleCreateNewCollection(ApiRequest request)
        {
            Application.Idle += CreateNewCollectionOnIdle;
            request.PostSucceeded();
        }

        private void CreateNewCollectionOnIdle(object sender, System.EventArgs e)
        {
            Application.Idle -= CreateNewCollectionOnIdle;
            var workspaceView = WorkspaceView.Current;
            if (workspaceView != null)
            {
                workspaceView.CreateNewCollection();
            }
            else
            {
                var path = NewCollectionWizard.CreateNewCollection(null, null);
                if (path != null)
                {
                    Program.CollectionChosenAtStartup = path;
                    ReactDialog.CloseCurrentModal("collection-chosen");
                }
                // If null, the user cancelled the wizard; they remain in the chooser.
            }
        }

        /// <summary>
        /// Shows a file open dialog for .bloomCollection files and returns the selected path,
        /// or null if the user cancelled.
        /// </summary>
        private static string BrowseForCollectionPath()
        {
            if (!Directory.Exists(NewCollectionWizard.DefaultParentDirectoryForCollections))
                Directory.CreateDirectory(NewCollectionWizard.DefaultParentDirectoryForCollections);
            using (var dlg = new BloomOpenFileDialog())
            {
                dlg.Title = LocalizationManager.GetString(
                    "CollectionTab.ChooseCollection",
                    "Choose Collection",
                    "Title of the file-open dialog for choosing a Bloom collection"
                );
                dlg.Filter = CollectionSettings.GetFileDialogFilterString();
                dlg.InitialDirectory = NewCollectionWizard.DefaultParentDirectoryForCollections;
                if (
                    dlg.ShowDialog() == DialogResult.Cancel
                    || MiscUtils.ReportIfInvalidCollection(dlg.FileName)
                )
                    return null;
                return dlg.FileName;
            }
        }
    }
}
