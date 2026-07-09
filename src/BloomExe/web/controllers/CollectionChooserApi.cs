using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionCreating;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;

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
        /// <summary>
        /// Set to true by HandleOpenCollectionFolderInExplorer when the caller passes
        /// updateAfter=true. Read and cleared by CollectionApi.CheckForCollectionUpdates /
        /// ResetUpdatingList when Bloom regains focus after Explorer closes.
        /// </summary>
        internal static bool UpdateAfterExplorerOpened;

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
                (request, value) =>
                {
                    WorkspaceView.SetShowUnapprovedTranslations(value);
                    // At startup (no project loaded), Bloom can't restart to apply the
                    // change, so reopen the dialog to refresh the language list.
                    if (WorkspaceView.Current == null)
                        Application.Idle += CloseForLanguageChange;
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                "collections/getMostRecentlyUsedCollections",
                HandleGetMostRecentlyUsedCollections,
                false,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "collections/openCollectionFolderInExplorer",
                HandleOpenCollectionFolderInExplorer,
                true
            );
            // Allow background thread; this makes a network call to BloomLibrary.
            apiHandler.RegisterEndpointHandler(
                "collections/getUnpublishedCount",
                HandleGetUnpublishedCount,
                false,
                false
            );
            // Allow background thread; this makes a network call to the cloud server (when
            // signed in -- see SharingApi.GetMyCollectionsForJoinCards).
            apiHandler.RegisterEndpointHandler(
                "collections/getJoinCards",
                HandleGetJoinCards,
                false,
                false
            );
        }

        /// <summary>
        /// Returns a list of recently used and locally available collections for display
        /// in the collection chooser dialog.
        /// </summary>
        private static void HandleGetMostRecentlyUsedCollections(ApiRequest request)
        {
            var collections = new List<dynamic>();

            const int maxMruItems = 10;
            var collectionsToShow = Settings.Default.MruProjects.Paths.Take(maxMruItems).ToList();

            // Always include the MRU items first.
            collections.AddRange(collectionsToShow.Select(path => MakeCollectionInfoObject(path)));

            // If there are fewer MRU items than the max, fill remaining slots with collections
            // discovered in the default directory, ordered most-recently-modified first
            // (matching the Reverse().Take() pattern from the old OpenCreateCloneControl logic).
            if (
                collectionsToShow.Count < maxMruItems
                && Directory.Exists(NewCollectionWizard.DefaultParentDirectoryForCollections)
            )
            {
                collections.AddRange(
                    Directory
                        .GetDirectories(NewCollectionWizard.DefaultParentDirectoryForCollections)
                        .Select(d =>
                            //Avoiding use of Path.ChangeExtension as it's just possible the collectionName could have a period.
                            CollectionSettings.GetSettingsFilePath(d)
                        )
                        .Where(path => RobustFile.Exists(path) && !collectionsToShow.Contains(path))
                        .OrderByDescending(path =>
                            Directory.GetLastWriteTime(Path.GetDirectoryName(path))
                        )
                        .Take(maxMruItems - collectionsToShow.Count)
                        .Select(MakeCollectionInfoObject)
                );
            }

            request.ReplyWithJson(collections);
        }

        /// <summary>
        /// Builds the JSON object returned per collection by getMostRecentlyUsedCollections.
        /// Add new fields here as the collection chooser needs more information.
        /// </summary>
        private static dynamic MakeCollectionInfoObject(string collectionFilePath)
        {
            var folderPath = Path.GetDirectoryName(collectionFilePath);
            var isTeamCollection = IsTeamCollectionFolder(folderPath);
            var checkedOutCount = isTeamCollection
                ? CountCheckedOutToCurrentUser(folderPath)
                : (int?)null;
            return new
            {
                path = collectionFilePath,
                title = Path.GetFileNameWithoutExtension(collectionFilePath),
                bookCount = CountBooksInCollection(folderPath),
                isTeamCollection,
                checkedOutCount,
            };
        }

        /// <summary>
        /// Returns true if the collection folder is a team collection, indicated by
        /// the presence of a TeamCollectionLink.txt file.
        /// </summary>
        private static bool IsTeamCollectionFolder(string collectionFolderPath) =>
            RobustFile.Exists(TeamCollectionManager.GetTcLinkPathFromLcPath(collectionFolderPath));

        /// <summary>
        /// Counts the books in a TC collection folder that are checked out to the current user.
        /// This includes books whose TeamCollection.status file has lockedBy == currentUser,
        /// and also books with no status file at all (local-only books that have never been
        /// synced to the shared repo).
        /// </summary>
        private static int CountCheckedOutToCurrentUser(string collectionFolderPath)
        {
            var currentUser = TeamCollectionManager.CurrentUser;
            if (string.IsNullOrEmpty(currentUser))
                return 0;
            return GetBookFolders(collectionFolderPath)
                .Count(bookFolder =>
                {
                    var statusPath = Path.Combine(bookFolder, "TeamCollection.status");
                    if (!RobustFile.Exists(statusPath))
                        return true; // local-only book; belongs to the current user
                    try
                    {
                        var status = BookStatus.FromJson(RobustFile.ReadAllText(statusPath));
                        return status?.lockedBy == currentUser;
                    }
                    catch
                    {
                        return false;
                    }
                });
        }

        /// <summary>
        /// Returns the paths of all book folders in a collection folder (excluding xmatter,
        /// hidden, and BloomIgnore'd folders).
        /// </summary>
        private static IEnumerable<string> GetBookFolders(string collectionFolderPath)
        {
            if (!Directory.Exists(collectionFolderPath))
                return Array.Empty<string>();
            return Directory
                .GetDirectories(collectionFolderPath)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .Where(d => !d.ToLowerInvariant().Contains("xmatter"))
                .Where(d => !RobustFile.Exists(Path.Combine(d, "BloomIgnore.txt")))
                .Where(d => Directory.GetFiles(d, "*.htm").Length > 0);
        }

        /// <summary>
        /// Counts all books in a collection folder.
        /// </summary>
        private static int CountBooksInCollection(string collectionFolderPath) =>
            GetBookFolders(collectionFolderPath).Count();

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
        /// Opens the folder containing a collection in the system file explorer.
        /// Accepts either a .bloomCollection file path or a directory path.
        /// Pass updateAfter=true as a query parameter to trigger a collection list refresh
        /// after the explorer window is opened (used by CollectionsTabPane when the user
        /// may delete the folder).
        /// </summary>
        private static void HandleOpenCollectionFolderInExplorer(ApiRequest request)
        {
            // path might be a .bloomCollection file or a directory
            var path = request.RequiredPostString();
            string collectionFolderPath;
            if (RobustFile.Exists(path))
            {
                collectionFolderPath = Path.GetDirectoryName(path);
            }
            else if (Directory.Exists(path))
            {
                collectionFolderPath = path;
            }
            else
            {
                request.Failed();
                return;
            }
            request.PostSucceeded();
            if (request.Parameters["updateAfter"] == "true")
                UpdateAfterExplorerOpened = true;
            ProcessExtra.SafeStartInFront(collectionFolderPath);
        }

        /// <summary>
        /// Returns the number of books in the given collection that have not been published to
        /// BloomLibrary.org (i.e. not found on the server or found only as a draft). Requires a
        /// network call to BloomLibrary, so this endpoint is called lazily per-card.
        /// </summary>
        private static void HandleGetUnpublishedCount(ApiRequest request)
        {
            var collectionFilePath = request.RequiredParam("collectionPath");
            var folderPath = Path.GetDirectoryName(collectionFilePath);

            var bookFolders = GetBookFolders(folderPath).ToList();
            if (bookFolders.Count == 0)
            {
                request.ReplyWithJson(new { count = 0 });
                return;
            }

            var bookInfos = bookFolders
                .Select(f => new BookInfo(f, false, NoEditSaveContext.Singleton))
                .ToList();

            try
            {
                var apiClient = new BloomLibraryBookApiClient();
                var statuses = apiClient.GetLibraryStatusForBooks(bookInfos);

                // A book is "unpublished" if it has no entry on the server, or if its entry is
                // marked as a draft (not yet publicly visible).
                var unpublishedCount = bookInfos.Count(bi =>
                    !statuses.TryGetValue(bi.Id, out var status) || status.Draft
                );

                request.ReplyWithJson(new { count = unpublishedCount });
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    "Error getting unpublished count for collection",
                    exception: e
                );
                request.Failed("Could not reach BloomLibrary");
            }
        }

        /// <summary>
        /// Dogfood batch 1, item 6: returns one "join card" entry per cloud Team Collection the
        /// signed-in user belongs to but has no local copy of yet, for the collection chooser to
        /// append after its normal MRU card list. Degrades silently (empty list, no cloud call)
        /// when signed out -- see SharingApi.GetMyCollectionsForJoinCards -- so this endpoint is
        /// safe to call unconditionally from a folder-only user's chooser.
        /// </summary>
        private static void HandleGetJoinCards(ApiRequest request)
        {
            var myCloudCollections = SharingApi.GetMyCollectionsForJoinCards();
            var joinCards =
                myCloudCollections.Count == 0
                    ? new List<dynamic>()
                    : ComputeJoinCards(myCloudCollections, GetLocalCloudCollectionIds());
            request.ReplyWithJson(joinCards);
        }

        /// <summary>
        /// Pure matching logic (unit-tested by CollectionChooserApiTests, no filesystem/network):
        /// a cloud collection gets a join card iff none of the given local cloud-collection ids
        /// (gathered by <see cref="GetLocalCloudCollectionIds"/>, which reads TeamCollectionLink.txt
        /// files) matches its id. Per the batch's decision, matching is by cloud id ONLY -- a local
        /// folder with the same name that is NOT itself a cloud TC (e.g. a plain or folder-TC
        /// collection) still gets a join card; CloudJoinFlow's own scenario matching handles the
        /// merge-or-conflict decision once the user actually tries to join.
        /// </summary>
        internal static List<dynamic> ComputeJoinCards(
            IEnumerable<CloudCollectionSummary> myCloudCollections,
            ISet<string> localCloudCollectionIds
        )
        {
            return myCloudCollections
                .Where(c => !localCloudCollectionIds.Contains(c.Id))
                .Select(c => (dynamic)new { collectionId = c.Id, title = c.Name })
                .ToList();
        }

        /// <summary>
        /// Scans the same candidate collection folders <see cref="HandleGetMostRecentlyUsedCollections"/>
        /// considers (MRU list + collections discovered in the default parent directory), reading
        /// each one's TeamCollectionLink.txt (if any) to find which cloud collection ids already
        /// have a local copy on this machine. Uncapped (unlike the MRU display list's maxMruItems)
        /// since a join card should be suppressed even if the local copy has scrolled out of the
        /// visible MRU list.
        /// </summary>
        private static HashSet<string> GetLocalCloudCollectionIds()
        {
            var ids = new HashSet<string>();
            foreach (var folderPath in GetCandidateCollectionFolders())
            {
                var linkPath = TeamCollectionManager.GetTcLinkPathFromLcPath(folderPath);
                if (!RobustFile.Exists(linkPath))
                    continue;
                TeamCollectionLink link;
                try
                {
                    link = TeamCollectionLink.FromFile(linkPath);
                }
                catch (InvalidTeamCollectionLinkException)
                {
                    // Unparseable link content; treat as "not a recognizable cloud TC" rather than
                    // crashing the whole join-card computation over one bad folder.
                    continue;
                }
                if (link != null && link.IsCloud)
                    ids.Add(link.CloudCollectionId);
            }
            return ids;
        }

        /// <summary>
        /// All local collection FOLDER paths worth checking for a TeamCollectionLink.txt: every
        /// MRU entry plus every collection discovered in the default parent directory (the same two
        /// sources <see cref="HandleGetMostRecentlyUsedCollections"/> draws from, but not capped at
        /// maxMruItems -- see that method's own cap comment).
        /// </summary>
        private static IEnumerable<string> GetCandidateCollectionFolders()
        {
            var mruFolders = Settings.Default.MruProjects.Paths.Select(Path.GetDirectoryName);
            var discovered = Directory.Exists(
                NewCollectionWizard.DefaultParentDirectoryForCollections
            )
                ? Directory.GetDirectories(NewCollectionWizard.DefaultParentDirectoryForCollections)
                : Array.Empty<string>();
            return mruFolders.Concat(discovered).Distinct();
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
