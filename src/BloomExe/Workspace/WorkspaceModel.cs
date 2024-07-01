using System;
using System.Collections.Generic;
using System.IO;
using Bloom.Book;
using Bloom.Collection;
using Bloom.TeamCollection;

namespace Bloom.Workspace
{
    public class WorkspaceModel
    {
        private readonly BookSelection _bookSelection;
        private readonly string _directoryPath;
        private TeamCollectionManager _tcManager;
        private CollectionSettings _collectionSettings;
        private SourceCollectionsList _sourceCollectionsList;

        public delegate WorkspaceModel Factory(string directoryPath); //autofac uses this
        public event EventHandler UpdateDisplay;

        public WorkspaceModel(
            BookSelection bookSelection,
            string directoryPath,
            TeamCollectionManager tcManager,
            CollectionSettings collectionSettings,
            SourceCollectionsList sourceCollectionsList
        )
        {
            _bookSelection = bookSelection;
            _directoryPath = directoryPath;
            _tcManager = tcManager;
            _collectionSettings = collectionSettings;
            _bookSelection.SelectionChanged += OnSelectionChanged;
            _sourceCollectionsList = sourceCollectionsList;
        }

        public bool ShowEditTab
        {
            get
            {
                return _bookSelection.CurrentSelection != null
                    && _bookSelection.CurrentSelection.IsInEditableCollection
                    && !_bookSelection.CurrentSelection.HasFatalError;
            }
        }

        /// <summary>
        /// True if we can't currently edit, either because nothing is selected
        /// or because the current book is not one we can save.
        /// </summary>
        public bool EditTabLocked =>
            _bookSelection.CurrentSelection == null || !_bookSelection.CurrentSelection.IsSaveable;

        public bool ShowPublishTab
        {
            get
            {
                return _bookSelection.CurrentSelection != null
                    && _bookSelection.CurrentSelection.IsInEditableCollection;
            }
        }

        public string ProjectName
        {
            get { return Path.GetFileName(_directoryPath); }
        }

        void OnSelectionChanged(
            object sender,
            BookSelectionChangedEventArgs bookSelectionChangedEventArgs
        )
        {
            InvokeUpdateDisplay();
        }

        private void InvokeUpdateDisplay()
        {
            EventHandler handler = UpdateDisplay;
            if (handler != null)
            {
                handler(this, null);
            }
        }

        public Book.Book FindTemplate(string key)
        {
            return null;
        }

        // Must be called before we call GetBookCollections() (or GetBookCollectionsOnce) in CollectionModel.
        internal void HandleTeamStuffBeforeGetBookCollections(Action whenDone)
        {
            // It would be nice if this was just in the  TCManager constructor. But TCManager has important
            // work to do before we can create a CollectionSettings object, and that's the object that
            // knows whether we have enterprise enabled, so there is something of a circularity.
            // This means that even with enterprise disabled, we will still pick up the latest
            // shared collection-level files each startup. So if the shared collection is updated with
            // new enterprise credentials, things will self-heal. We decided it's OK for that much
            // TC functionality to go on working even with enterprise disabled.
            _tcManager.CheckDisablingTeamCollections(_collectionSettings);
            // Before loading up the collection, update with anything new from any TeamCollection we are linked to.
            // To do this the TC if any needs to know the CollectionId. (We're not having autofac give it the
            // CollectionSettings because circular dependencies would result.)
            // This may not be the final place to do this.  But it's the latest we can do it without needing to reconcile
            // the changes synchronization makes with collection data we've loaded.
            _tcManager.SetCollectionId(_collectionSettings.CollectionId);

            // Don't put anything after this line. This method is called within an idle event handler
            // and displays a dialog. If we are still in the time frame for showing the splash
            // screen, the dialog will not close, and SynchronizeRepoAndLocal() will not return,
            // until the expiration of the splash screen time. And other startup idle tasks will
            // be allowed to run once the sync is complete. Anything we want to happen after
            // this sync should be part of a distinct startup idle task.
            // This won't do much if disabled, but it can clean out the status files for
            // books copied from another collection, and update checkout status for
            // an offline TC.
            _tcManager.CurrentCollectionEvenIfDisconnected?.SynchronizeRepoAndLocal(whenDone);
        }

        // Alternative for GetBookCollections() that returns folder paths of all source collections.
        // It is safe to call before HandleTeamStuffBeforeGetBookCollections().
        internal IEnumerable<string> GetSourceCollectionFolders()
        {
            return _sourceCollectionsList.GetCollectionFolders();
        }
    }
}
