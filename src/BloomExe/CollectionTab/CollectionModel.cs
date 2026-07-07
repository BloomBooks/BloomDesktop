using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.History;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.SafeXml;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using Bloom.ToPalaso.Experimental;
using Bloom.Utils;
using Bloom.web;
using Bloom.web.controllers;
using DesktopAnalytics;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms;
using SIL.Windows.Forms.FileSystem;
using SIL.Xml;

namespace Bloom.CollectionTab
{
    public partial class CollectionModel
    {
        private readonly BookSelection _bookSelection;
        private readonly string _pathToCollection;
        private readonly CollectionSettings _collectionSettings;
        private readonly SourceCollectionsList _sourceCollectionsList;
        private readonly BookCollection.Factory _bookCollectionFactory;
        private readonly EditBookCommand _editBookCommand;
        private readonly BookServer _bookServer;
        private readonly CurrentEditableCollectionSelection _currentEditableCollectionSelection;
        private List<BookCollection> _bookCollections;
        private readonly BookThumbNailer _thumbNailer;
        private TeamCollectionManager _tcManager;
        private readonly BloomWebSocketServer _webSocketServer;
        private LocalizationChangedEvent _localizationChangedEvent;
        private BookCollectionHolder _bookCollectionHolder;

        // The .bloomSource files the user chose to import, remembered between the file-picker step
        // (ChooseBloomSourceFilesToImport) and the actual import (ImportBloomSourceFiles), so the
        // collection screen can show the appropriate import dialog in between.
        private string[] _bloomSourceFilesToImport;

        public CollectionModel(
            string pathToCollection,
            CollectionSettings collectionSettings,
            BookSelection bookSelection,
            SourceCollectionsList sourceCollectionsList,
            BookCollection.Factory bookCollectionFactory,
            EditBookCommand editBookCommand,
            CreateFromSourceBookCommand createFromSourceBookCommand,
            BookServer bookServer,
            CurrentEditableCollectionSelection currentEditableCollectionSelection,
            BookThumbNailer thumbNailer,
            TeamCollectionManager tcManager,
            BloomWebSocketServer webSocketServer,
            BookCollectionHolder bookCollectionHolder,
            LocalizationChangedEvent localizationChangedEvent
        )
        {
            _bookSelection = bookSelection;
            _pathToCollection = pathToCollection;
            _collectionSettings = collectionSettings;
            _sourceCollectionsList = sourceCollectionsList;
            _bookCollectionFactory = bookCollectionFactory;
            _editBookCommand = editBookCommand;
            _bookServer = bookServer;
            _currentEditableCollectionSelection = currentEditableCollectionSelection;
            _thumbNailer = thumbNailer;
            _tcManager = tcManager;
            _webSocketServer = webSocketServer;
            _bookCollectionHolder = bookCollectionHolder;
            _localizationChangedEvent = localizationChangedEvent;

            createFromSourceBookCommand.Subscribe(CreateFromSourceBook);
        }

        public BookCollection CurrentEditableCollection =>
            _currentEditableCollectionSelection.CurrentSelection;

        /// <summary>
        /// The constructor of BookCommandsApi calls this to work around an Autofac circularity problem.
        /// </summary>
        public BookCommandsApi BookCommands { get; set; }

        public bool CanDeleteSelection
        {
            get
            {
                return _bookSelection.CurrentSelection != null
                    && _collectionSettings.AllowDeleteBooks
                    && _bookSelection.CurrentSelection.CanDelete;
            }
        }

        internal CollectionSettings CollectionSettings
        {
            get { return _collectionSettings; }
        }

        public string LanguageName
        {
            get { return _collectionSettings.Language1.Name; } // collection tab still uses collection language settings
        }

        private object _bookCollectionLock = new object(); // Locks creation of _bookCollections

        // List out all the collections we have loaded
        // 0) the editable collection of this ".bloomCollection" folder.
        // 1) "Templates"
        // etc.
        public IReadOnlyList<BookCollection> GetBookCollections(bool disposing = false)
        {
            lock (_bookCollectionLock)
            {
                if (_bookCollections == null)
                {
                    if (disposing)
                    {
                        // We don't want to create new collections when we're disposing of the model.
                        return new List<BookCollection>();
                    }
                    _bookCollections = new List<BookCollection>(GetBookCollectionsOnce());

                    //we want the templates to be second (after the editable collection) regardless of alphabetical sorting
                    var templates = _bookCollections.FirstOrDefault(c => c.Name == "Templates");
                    if (templates != null)
                    {
                        _bookCollections.Remove(templates);
                        _bookCollections.Insert(1, templates);
                    }
                }
                return _bookCollections;
            }
        }

        public BookInfo BookInfoFromCollectionAndId(string collectionPath, string bookId)
        {
            var collection = GetBookCollections()
                .FirstOrDefault(c => c.PathToDirectory == collectionPath);
            if (collection == null)
                return null;
            return collection.GetBookInfos().FirstOrDefault(bi => bi.Id == bookId);
        }

        public void ReloadCollections()
        {
            lock (_bookCollectionLock)
            {
                _bookCollections = null;
                GetBookCollections();
            }

            _webSocketServer.SendEvent(
                "editableCollectionList",
                "reload:" + _bookCollections[0].PathToDirectory
            );
        }

        public void DuplicateBook(Book.Book book)
        {
            var newBookDir = book.Storage.Duplicate();

            // Get rid of any TC status we copied from the original, so Bloom treats it correctly as a new book.
            BookStorage.RemoveLocalOnlyFiles(newBookDir);

            ReloadEditableCollection();

            var dupInfo = TheOneEditableCollection
                .GetBookInfos()
                .FirstOrDefault(info => info.FolderPath == newBookDir);
            if (dupInfo != null)
            {
                var newBook = GetBookFromBookInfo(dupInfo);
                SelectBook(newBook);
                BookHistory.AddEvent(
                    newBook,
                    BookHistoryEventType.Created,
                    $"Duplicated from existing book \"{book.Title}\""
                );
                newBook.UserPrefs.UploadAgreementsAccepted = false;
            }
        }

        /// <summary>
        /// What the user chose to do when an imported .bloomSource book is already present
        /// (same bookInstanceId) in the editable collection.
        /// </summary>
        internal enum ImportDuplicateChoice
        {
            Replace,
            AddCopy,
            Cancel,
        }

        /// <summary>
        /// A problem with an imported .bloomSource file that we can explain to the user
        /// (e.g. corrupt file, wrong kind of file, no book inside). The Message is shown
        /// directly to the user, so it should be friendly and not require a bug report.
        /// </summary>
        internal class BloomSourceImportException : Exception
        {
            public BloomSourceImportException(string userMessage)
                : base(userMessage) { }
        }

        /// <summary>
        /// Prompts the user to pick one or more .bloomSource files, remembering them for a following
        /// <see cref="ImportBloomSourceFiles"/> call. Returns true if at least one file was chosen.
        /// This is separate from the import itself so the collection screen can, once the files are
        /// chosen, show the single import dialog appropriate to the batch (edit-vs-derivative when
        /// none are already present, replace-vs-add-copy when any are).
        /// </summary>
        public bool ChooseBloomSourceFilesToImport()
        {
            using (var dlg = new BloomOpenFileDialog())
            {
                dlg.Multiselect = true;
                dlg.CheckFileExists = true;
                dlg.Title = LocalizationManager.GetString(
                    "CollectionTab.ImportBloomSource",
                    "Import .bloomSource File(s)"
                );
                dlg.Filter =
                    "Bloom Source files (*.bloomSource)|*.bloomSource|Team Collection books (*.bloom)|*.bloom|All files (*.*)|*.*";
                if (dlg.ShowDialog() != DialogResult.OK)
                {
                    _bloomSourceFilesToImport = null;
                    return false;
                }
                _bloomSourceFilesToImport = dlg.FileNames;
                return _bloomSourceFilesToImport.Length > 0;
            }
        }

        /// <summary>
        /// Runs <see cref="ImportBloomSourceFiles"/> behind the embedded progress dialog on a
        /// background thread, so a large batch neither freezes the collection screen nor lets the
        /// awaiting browser request time out. The front-end already hosts the "collectionTab"
        /// EmbeddedProgressDialog, so it needs no extra wiring.
        /// </summary>
        public async Task ImportBloomSourceFilesWithProgressAsync(
            bool makeDerivatives,
            bool replaceExistingDuplicates
        )
        {
            await BrowserProgressDialog.DoWorkWithProgressDialogAsync(
                _webSocketServer,
                (progress, worker) =>
                {
                    ImportBloomSourceFiles(makeDerivatives, replaceExistingDuplicates, progress);
                    return Task.FromResult(false); // false => close the dialog when we finish
                },
                "collectionTab",
                LocalizationManager.GetString(
                    "CollectionTab.ImportBloomSource.Importing",
                    "Importing Books"
                ),
                showCancelButton: false
            );
        }

        /// <summary>
        /// Imports the .bloomSource files previously chosen via
        /// <see cref="ChooseBloomSourceFilesToImport"/> into the current editable collection. The
        /// user has already made the choice (in the collection screen) that applies to the whole
        /// batch: when <paramref name="makeDerivatives"/> is true, each imported book is used to make
        /// a new derivative book (otherwise each is imported as an editable book); and, for the edit
        /// case, when <paramref name="replaceExistingDuplicates"/> is true any book already in the
        /// collection is replaced (otherwise it is added as a numbered copy). The last successfully
        /// imported book is selected when done.
        /// </summary>
        public void ImportBloomSourceFiles(
            bool makeDerivatives,
            bool replaceExistingDuplicates,
            IWebSocketProgress progress = null
        )
        {
            var paths = _bloomSourceFilesToImport;
            _bloomSourceFilesToImport = null;
            if (paths == null || paths.Length == 0)
                return;

            // The user has already chosen, once for the whole batch, what to do when an imported
            // book is already in the collection (only relevant when editing, not making derivatives).
            var duplicateChoice = replaceExistingDuplicates
                ? ImportDuplicateChoice.Replace
                : ImportDuplicateChoice.AddCopy;

            Book.Book lastImported = null;
            // For the edit path we move every book into place first and reload the collection just
            // once at the end, instead of reloading (which rescans the whole collection from disk)
            // once per file. Each entry pairs the destination folder with its source file name so we
            // can record an accurate "Imported from" history event after that single reload.
            var importedEditFolders = new List<(string destFolder, string sourceName)>();
            // Because the collection isn't reloaded between files, the per-file duplicate check can't
            // see books imported earlier in this same batch. Track their ids here so two files that
            // share a bookInstanceId don't both land with that id (which would defeat the whole point
            // of duplicate handling).
            var idsImportedThisBatch = new HashSet<string>();
            foreach (var path in paths)
            {
                try
                {
                    progress?.MessageWithoutLocalizing($"Importing {Path.GetFileName(path)}...");
                    if (makeDerivatives)
                    {
                        // The derivative path builds its Book in memory and adds it to the collection
                        // itself, so it needs no reload.
                        var book = MakeDerivativeFromBloomSourceFile(path);
                        if (book != null)
                            lastImported = book;
                    }
                    else
                    {
                        var destFolder = ImportBloomSourceFileToCollectionFolder(
                            path,
                            _ => duplicateChoice,
                            idsImportedThisBatch
                        );
                        if (destFolder != null)
                            importedEditFolders.Add((destFolder, Path.GetFileName(path)));
                    }
                }
                catch (BloomSourceImportException e)
                {
                    // A problem we can explain to the user; no bug report needed.
                    ErrorReport.NotifyUserOfProblem(
                        "{0}\r\n\r\n{1}",
                        Path.GetFileName(path),
                        e.Message
                    );
                }
                catch (Exception e)
                {
                    // Something unexpected; let the user report it.
                    NonFatalProblem.Report(
                        ModalIf.All,
                        PassiveIf.None,
                        shortUserLevelMessage: string.Format(
                            "Bloom was not able to import \"{0}\".",
                            Path.GetFileName(path)
                        ),
                        moreDetails: null,
                        exception: e
                    );
                }
            }

            // Now that every edit-import's folder is in place, reload the collection a single time
            // and turn each imported folder into a Book with its Created history event.
            if (importedEditFolders.Count > 0)
            {
                ReloadEditableCollection();
                foreach (var (destFolder, sourceName) in importedEditFolders)
                {
                    var newInfo = TheOneEditableCollection
                        .GetBookInfos()
                        .FirstOrDefault(i => i.FolderPath == destFolder);
                    if (newInfo == null)
                    {
                        // Should not happen after a successful move; surface it but don't abort the
                        // rest of the batch.
                        NonFatalProblem.Report(
                            ModalIf.All,
                            PassiveIf.None,
                            shortUserLevelMessage: string.Format(
                                "Bloom imported \"{0}\" but could not find it in the collection afterward.",
                                sourceName
                            )
                        );
                        continue;
                    }
                    var newBook = GetBookFromBookInfo(newInfo);
                    BookHistory.AddEvent(
                        newBook,
                        BookHistoryEventType.Created,
                        $"Imported from \"{sourceName}\""
                    );
                    lastImported = newBook;
                }
            }

            if (lastImported != null)
                SelectBookOnUiThread(lastImported);
        }

        /// <summary>
        /// Selects a book, marshaling to the UI thread when necessary. Import can run on a background
        /// thread (behind the progress dialog), but SelectBook raises SelectionChanged synchronously
        /// to WinForms views, so it must happen on the UI thread. When no window is open (e.g. unit
        /// tests) this just runs inline.
        /// </summary>
        private void SelectBookOnUiThread(Book.Book book)
        {
            var form = Shell.GetShellOrOtherOpenForm();
            if (form != null && form.InvokeRequired)
                form.Invoke((Action)(() => _bookSelection.SelectBook(book)));
            else
                _bookSelection.SelectBook(book);
        }

        /// <summary>
        /// Does the file-system part of importing a .bloomSource: validates it, extracts it, removes
        /// the stray .bloomCollection and any Team-Collection status files, handles the
        /// already-in-collection case (Replace/Add-a-copy/Cancel), and moves the book into the
        /// editable collection folder. Returns the destination folder, or null if the user cancelled.
        /// This is separated from the Book-level work so it can be unit tested without loading a Book.
        /// </summary>
        /// <param name="idsAlreadyImportedThisBatch">Ids of books already imported earlier in this
        /// same batch. Because the collection is reloaded only once, after the whole batch, a book
        /// whose id is in this set isn't yet visible to the in-collection check above; we treat it as
        /// a within-batch duplicate and give the incoming book a fresh id so the two don't collide.
        /// When null (e.g. single-file tests), within-batch tracking is skipped.</param>
        internal string ImportBloomSourceFileToCollectionFolder(
            string sourcePath,
            Func<string, ImportDuplicateChoice> resolveDuplicate,
            ISet<string> idsAlreadyImportedThisBatch = null
        )
        {
            var editable = TheOneEditableCollection;
            var tempFolder = ExtractAndPrepareBloomSourceToTemp(
                sourcePath,
                out var htmlPath,
                out var instanceId
            );
            // The id this book arrived with. We record it (once the import succeeds, below) so that a
            // later file in the same batch carrying the same id is recognized as a duplicate even
            // though the collection is only reloaded once, after the whole batch.
            var originalInstanceId = instanceId;
            string destFolder = null;
            string replaceTargetFolder = null;
            // Once the book has been moved into the collection the import has succeeded; a failure
            // after that point (e.g. recycling the replaced duplicate) must not delete it.
            var importSucceeded = false;
            // "Add a copy" produces an independent copy exactly like the Duplicate Book command:
            // a new id and the "<name> - Copy-<id>" folder convention (the caption is left alone,
            // just as Duplicate leaves it).
            var folderSeparator = "-";
            try
            {
                var baseName = Path.GetFileNameWithoutExtension(htmlPath);

                var existing = string.IsNullOrEmpty(instanceId)
                    ? null
                    : editable.GetBookInfos().FirstOrDefault(b => b.Id == instanceId);
                // A book with this id isn't in the collection, but an earlier file in this same batch
                // already brought it in (the collection is only reloaded once, after the batch, so it
                // isn't visible above yet). Replacing a book from the same batch is meaningless, so we
                // always make this one an independent copy so the two don't end up sharing an id.
                var isDuplicateWithinBatch =
                    existing == null
                    && !string.IsNullOrEmpty(instanceId)
                    && idsAlreadyImportedThisBatch != null
                    && idsAlreadyImportedThisBatch.Contains(instanceId);

                // Whether to turn the incoming book into an independent copy: a new id (so it can't
                // collide) plus the " - Copy-" folder convention the Duplicate command uses.
                var makeIndependentCopy = false;
                if (existing != null)
                {
                    switch (resolveDuplicate(existing.Title))
                    {
                        case ImportDuplicateChoice.Cancel:
                            return null;
                        case ImportDuplicateChoice.Replace:
                            // Replacing overwrites the existing book in place, so we must be allowed
                            // to modify it: in a Team Collection that means it has to be checked out
                            // here. The collection screen already disables "Replace" when any
                            // duplicate is not (see AllChosenBloomSourceDuplicatesAreReplaceable), but
                            // guard here too so we can never overwrite a book the user hasn't checked
                            // out.
                            if (!existing.IsSaveable)
                                throw new ApplicationException(
                                    $"Cannot replace \"{existing.Title}\" because it is not checked out of the Team Collection."
                                );
                            // Overwrite this book's folder in place (below), keeping its folder name
                            // and — in a Team Collection — its checkout status, so it stays the same
                            // checked-out repo book (the new content is pushed to the shared repo on
                            // the next check-in) rather than becoming a new local book while the old
                            // one resurrects from the repo. The imported book keeps the same id.
                            replaceTargetFolder = existing.FolderPath;
                            break;
                        case ImportDuplicateChoice.AddCopy:
                            makeIndependentCopy = true;
                            break;
                    }
                }
                else if (isDuplicateWithinBatch)
                {
                    makeIndependentCopy = true;
                }

                if (makeIndependentCopy)
                {
                    folderSeparator = " - Copy-";
                    instanceId = Guid.NewGuid().ToString();
                    var meta = BookMetaData.FromFolder(tempFolder);
                    if (meta != null)
                    {
                        meta.Id = instanceId;
                        meta.WriteToFolder(tempFolder);
                    }
                }

                if (replaceTargetFolder != null)
                {
                    // "Replace" overwrites the existing book's folder in place rather than making a
                    // new folder and deleting the old one. This keeps the folder name and (in a Team
                    // Collection) the checkout status, so it stays the same checked-out repo book; see
                    // the Replace case above.
                    //
                    // If the book we're about to overwrite is the current selection, deselect it first
                    // so its files aren't locked (e.g. open in a preview) while we swap the folder.
                    if (_bookSelection.CurrentSelection?.FolderPath == replaceTargetFolder)
                        SelectBookOnUiThread(null);
                    destFolder = ReplaceBookInPlaceKeepingTeamStatus(
                        tempFolder,
                        htmlPath,
                        replaceTargetFolder
                    );
                    importSucceeded = true;
                    // Remember the id this book arrived with so a later file in the same batch carrying
                    // the same id is caught as a within-batch duplicate above.
                    if (!string.IsNullOrEmpty(originalInstanceId))
                        idsAlreadyImportedThisBatch?.Add(originalInstanceId);
                    return destFolder;
                }

                // Pick a folder name that doesn't collide with an existing book on disk, and make
                // the main htm file match it (Bloom's convention: folder name == book htm name).
                var destName = BookStorage.GetUniqueBookFolderName(
                    editable.PathToDirectory,
                    BookStorage.SanitizeNameForFileSystem(baseName),
                    instanceId,
                    folderSeparator
                );
                destFolder = Path.Combine(editable.PathToDirectory, destName);
                var renamedHtmlPath = Path.Combine(tempFolder, destName + ".htm");
                if (!string.Equals(htmlPath, renamedHtmlPath, StringComparison.OrdinalIgnoreCase))
                    RobustFile.Move(htmlPath, renamedHtmlPath);
                SIL.IO.RobustIO.MoveDirectory(tempFolder, destFolder);
                importSucceeded = true;

                // Remember the id this book arrived with so a later file in the same batch carrying
                // the same id is caught as a within-batch duplicate above.
                if (!string.IsNullOrEmpty(originalInstanceId))
                    idsAlreadyImportedThisBatch?.Add(originalInstanceId);

                return destFolder;
            }
            catch
            {
                // If we failed partway through creating the destination folder, don't leave a
                // half-imported book behind in the collection. (The Replace/overwrite path handles
                // its own rollback in ReplaceBookInPlaceKeepingTeamStatus and only sets destFolder
                // once it has fully succeeded, so this never deletes a restored original.)
                if (!importSucceeded && destFolder != null && Directory.Exists(destFolder))
                    SIL.IO.RobustIO.DeleteDirectoryAndContents(destFolder);
                throw;
            }
            finally
            {
                if (Directory.Exists(tempFolder))
                    SIL.IO.RobustIO.DeleteDirectoryAndContents(tempFolder);
            }
        }

        /// <summary>
        /// Replaces the book in <paramref name="targetFolder"/> with the freshly-extracted imported
        /// book in <paramref name="tempFolder"/>, overwriting its content in place. The target folder
        /// keeps its name and its Team Collection files (checkout status etc.), so in a Team
        /// Collection it stays the same checked-out book — the new content is pushed to the shared
        /// repo on the next check-in — rather than becoming a new local book while the old one
        /// resurrects from the repo. The imported book already carries the same bookInstanceId
        /// (Replace keeps the id). The swap goes through a set-aside backup so a mid-swap failure
        /// restores the original rather than losing the book.
        /// </summary>
        /// <returns>the target folder path, now holding the imported book</returns>
        private string ReplaceBookInPlaceKeepingTeamStatus(
            string tempFolder,
            string htmlPath,
            string targetFolder
        )
        {
            // Bloom's convention is folder name == main htm name; keep the target's existing folder
            // name (which is what maps it to its Team Collection repo entry), so rename the incoming
            // htm to match it.
            var targetName = Path.GetFileName(targetFolder);
            var renamedHtmlPath = Path.Combine(tempFolder, targetName + ".htm");
            if (!string.Equals(htmlPath, renamedHtmlPath, StringComparison.OrdinalIgnoreCase))
                RobustFile.Move(htmlPath, renamedHtmlPath);

            // Swap on the same volume: move the existing book aside, move the imported content into
            // its place, then carry the Team Collection files over from the original so the book stays
            // checked out. If anything fails mid-swap, restore the original and rethrow so we never
            // lose the book. (Outside a Team Collection there are no TC files, so that step does
            // nothing.)
            // The set-aside name is dot-prefixed so that, if final cleanup ever fails and it lingers,
            // the collection scan ignores it (it only skips dot-prefixed folders) rather than picking
            // it up as a book — which, since it holds the old content with the same bookInstanceId,
            // would be a duplicate-id book.
            var backupFolder = Path.Combine(
                Path.GetDirectoryName(targetFolder),
                "." + Path.GetFileName(targetFolder) + ".replacing-" + Guid.NewGuid()
            );
            SIL.IO.RobustIO.MoveDirectory(targetFolder, backupFolder);
            try
            {
                SIL.IO.RobustIO.MoveDirectory(tempFolder, targetFolder);
                var teamFiles = new List<string>();
                Bloom.TeamCollection.TeamCollection.AddTCSpecificFiles(backupFolder, teamFiles);
                foreach (var teamFile in teamFiles)
                    RobustFile.Copy(
                        teamFile,
                        Path.Combine(targetFolder, Path.GetFileName(teamFile)),
                        true
                    );
            }
            catch
            {
                if (Directory.Exists(targetFolder))
                    SIL.IO.RobustIO.DeleteDirectoryAndContents(targetFolder);
                SIL.IO.RobustIO.MoveDirectory(backupFolder, targetFolder);
                throw;
            }
            // The replace has fully succeeded; deleting the set-aside old content is just cleanup. If
            // it fails (locked files, flaky network/removable drive, AV), don't let that turn a
            // successful replace into an "import failed" error and keep the replaced book from being
            // reloaded into view — report it as a minor problem instead. (The backup is dot-prefixed,
            // so even if it lingers the collection scan ignores it.)
            try
            {
                SIL.IO.RobustIO.DeleteDirectoryAndContents(backupFolder);
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.None,
                    shortUserLevelMessage: string.Format(
                        "The book \"{0}\" was replaced, but Bloom could not remove a temporary copy of the old version. You may delete the folder \"{1}\" manually.",
                        Path.GetFileName(targetFolder),
                        Path.GetFileName(backupFolder)
                    ),
                    moreDetails: null,
                    exception: e
                );
            }
            return targetFolder;
        }

        /// <summary>
        /// Validates a .bloomSource file, extracts it to a fresh temp folder, removes the stray
        /// .bloomCollection settings file and any Team-Collection status files, and confirms the
        /// folder contains a book. Returns the temp folder path (the caller owns deleting it), the
        /// book's main htm path, and its bookInstanceId (may be null). On any failure the temp
        /// folder is cleaned up and the exception is rethrown.
        /// </summary>
        private string ExtractAndPrepareBloomSourceToTemp(
            string sourcePath,
            out string htmlPath,
            out string instanceId
        )
        {
            // A .bloomSource is a zip of a single book folder's *contents* (flat at the root),
            // plus the collection's .bloomCollection settings file. Validate that shape and guard
            // against malicious entries before we extract anything.
            ValidateBloomSourceZip(sourcePath);

            // Extract onto the same volume as the collection. The book is later moved into the
            // collection folder with a directory move, which cannot cross volumes; the system
            // temp folder is frequently on a different drive than the user's collection (e.g. %TEMP%
            // on C: but the collection on D: or a removable/network drive), which would make every
            // edit-mode import fail. Staging in the collection's parent guarantees a same-volume move.
            // If the collection is at a filesystem root (e.g. "D:\"), it has no parent directory, so
            // stage inside the collection folder itself instead — still same-volume, and never %TEMP%
            // (which could be on another drive and reintroduce the cross-volume failure).
            // The name is dot-prefixed so it is ignored by the collection's book scan if it ever ends
            // up inside a scanned folder.
            var collectionDir = TheOneEditableCollection.PathToDirectory;
            var stagingParent = Directory.GetParent(collectionDir)?.FullName ?? collectionDir;
            var tempFolder = Path.Combine(stagingParent, ".BloomImport-" + Guid.NewGuid());
            try
            {
                ZipUtils.ExpandZip(sourcePath, tempFolder);

                // The .bloomCollection file (there should be exactly one, but delete any) is
                // collection-level and must not end up inside the book folder.
                foreach (var settingsFile in Directory.GetFiles(tempFolder, "*.bloomCollection"))
                    RobustFile.Delete(settingsFile);

                htmlPath = BookStorage.FindBookHtmlInFolder(tempFolder);
                if (string.IsNullOrEmpty(htmlPath))
                    throw new BloomSourceImportException("No book was found in this file.");

                // An imported book should not carry over Team Collection status from wherever it came from.
                BookStorage.RemoveLocalOnlyFiles(tempFolder);

                instanceId = BookMetaData.FromFolder(tempFolder)?.Id;
                return tempFolder;
            }
            catch
            {
                if (Directory.Exists(tempFolder))
                    SIL.IO.RobustIO.DeleteDirectoryAndContents(tempFolder);
                throw;
            }
        }

        /// <summary>
        /// Imports one .bloomSource file by making a NEW derivative book (new id and lineage) from
        /// the book it contains, placed in the editable collection. Returns the new Book, or null if
        /// creation was cancelled (e.g. a template configuration dialog). Because a derivative always
        /// gets a fresh id, there is never a duplicate to resolve.
        /// </summary>
        internal Book.Book MakeDerivativeFromBloomSourceFile(string sourcePath)
        {
            var tempFolder = ExtractAndPrepareBloomSourceToTemp(sourcePath, out _, out _);
            try
            {
                var newBook = _bookServer.CreateFromSourceBook(
                    tempFolder,
                    TheOneEditableCollection.PathToDirectory
                );
                if (newBook == null)
                    return null; // e.g. the source had a configuration dialog and the user cancelled

                // Add the new book to the collection in memory rather than reloading and looking it
                // up again (which was the source of a silent not-found fallback).
                //
                // The normal "make a book from this source" flow (CreateFromSourceBook) defers the
                // "Created" history event via a pending marker and flushes it when the book is later
                // deselected, so it can capture a title the user types afterward. That does not work
                // for a batch import: only the last-imported book is ever selected, so every earlier
                // book's pending event would be lost once its Book object is discarded (a freshly
                // rebuilt Book has no pending marker). An imported book already has its title, so we
                // set the marker and flush it immediately, giving every imported derivative its own
                // "Created" history entry.
                newBook.PendingCreationSource = Path.GetFileName(sourcePath);
                newBook.PendingCreationSourceTitle = newBook.BookInfo.GetTitleForLanguage(
                    newBook.BookData.Language1Tag
                );

                newBook.BringBookUpToDate(new NullProgress(), false);

                TheOneEditableCollection.AddBookInfo(newBook.BookInfo);
                newBook.RecordPendingCreatedHistoryEvent();
                return newBook;
            }
            finally
            {
                if (Directory.Exists(tempFolder))
                    SIL.IO.RobustIO.DeleteDirectoryAndContents(tempFolder);
            }
        }

        /// <summary>
        /// Opens the file as a zip and confirms it looks like a single-book .bloomSource: it must
        /// open as a zip, must not contain path-traversal/absolute entries (zip-slip), and must not
        /// be a Bloom Pack (a zip whose sole top-level entry is a collection folder).
        /// Throws <see cref="BloomSourceImportException"/> otherwise.
        /// </summary>
        private void ValidateBloomSourceZip(string sourcePath)
        {
            ICSharpCode.SharpZipLib.Zip.ZipFile zip;
            try
            {
                zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(sourcePath);
            }
            catch (Exception)
            {
                throw new BloomSourceImportException("This is not a valid Bloom source file.");
            }
            using (zip)
            {
                var topLevelDirs = new HashSet<string>();
                var hasTopLevelFile = false;
                foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry entry in zip)
                {
                    var name = entry.Name.Replace('\\', '/');
                    var parts = name.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    // Zip-slip / absolute path guard: reject anything that could escape the target folder.
                    if (Path.IsPathRooted(entry.Name) || parts.Contains(".."))
                        throw new BloomSourceImportException(
                            "This file contains invalid entries and cannot be imported."
                        );
                    if (parts.Length == 0)
                        continue;
                    if (parts.Length == 1 && !entry.IsDirectory)
                        hasTopLevelFile = true;
                    else
                        topLevelDirs.Add(parts[0]);
                }

                // A single book always has files at the root (meta.json, the book htm). A Bloom Pack
                // instead wraps everything in one collection folder, so it has no top-level files.
                if (!hasTopLevelFile && topLevelDirs.Count == 1)
                    throw new BloomSourceImportException(
                        "This looks like a Bloom Pack (a whole collection), not a single book. "
                            + "To install a Bloom Pack, double-click it or use \"Open or Create Another Collection\"."
                    );
            }
        }

        /// <summary>
        /// Returns true if any of the .bloomSource files the user chose (via
        /// <see cref="ChooseBloomSourceFilesToImport"/>) contains a book whose bookInstanceId is
        /// already present in the editable collection. The collection screen uses this to decide
        /// whether to ask the user how duplicates should be handled before importing.
        /// </summary>
        public bool AnyChosenBloomSourceIsAlreadyInCollection()
        {
            return AnyBloomSourceIsAlreadyInCollection(_bloomSourceFilesToImport);
        }

        /// <summary>
        /// Returns true only if every chosen .bloomSource book that is already in the collection may
        /// currently be replaced. Replacing recycles the existing book, so in a Team Collection each
        /// duplicate must be checked out here (BookInfo.IsSaveable); outside a Team Collection books
        /// are always saveable. The collection screen uses this to disable the "Replace" duplicate
        /// choice (and explain why) when any duplicate is not checked out, so the user can't delete a
        /// book they haven't checked out. When there are no duplicates the question is moot, so this
        /// returns true.
        /// </summary>
        public bool AllChosenBloomSourceDuplicatesAreReplaceable()
        {
            return AllBloomSourceDuplicatesAreReplaceable(_bloomSourceFilesToImport);
        }

        /// <summary>
        /// The testable core of <see cref="AllChosenBloomSourceDuplicatesAreReplaceable"/>. Duplicates
        /// are detected purely by bookInstanceId, exactly as in
        /// <see cref="AnyBloomSourceIsAlreadyInCollection"/>.
        /// </summary>
        internal bool AllBloomSourceDuplicatesAreReplaceable(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return true;

            var existingById = TheOneEditableCollection
                .GetBookInfos()
                .Where(b => !string.IsNullOrEmpty(b.Id))
                .GroupBy(b => b.Id)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var path in paths)
            {
                var id = ReadBookInstanceIdFromBloomSource(path);
                if (
                    !string.IsNullOrEmpty(id)
                    && existingById.TryGetValue(id, out var existing)
                    && !existing.IsSaveable
                )
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if any of the given .bloomSource files contains a book whose bookInstanceId
        /// is already present in the editable collection. Detection is purely by bookInstanceId, not
        /// by title or folder name: two books with the same name but different ids are not duplicates,
        /// while two books with the same id are (that id also controls re-uploading to the Bloom
        /// library, so we must never end up with two books sharing one). Split out from
        /// <see cref="AnyChosenBloomSourceIsAlreadyInCollection"/> so it can be unit tested without a
        /// file-picker dialog.
        /// </summary>
        internal bool AnyBloomSourceIsAlreadyInCollection(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return false;

            var existingIds = new HashSet<string>(
                TheOneEditableCollection
                    .GetBookInfos()
                    .Select(b => b.Id)
                    .Where(id => !string.IsNullOrEmpty(id))
            );
            foreach (var path in paths)
            {
                var id = ReadBookInstanceIdFromBloomSource(path);
                if (!string.IsNullOrEmpty(id) && existingIds.Contains(id))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Reads the bookInstanceId from a .bloomSource file's meta.json (which is at the zip root)
        /// without extracting the whole book. Returns null if it can't be read (e.g. the file is
        /// not a valid single-book source); such files simply aren't treated as duplicates here.
        /// Parses the metadata the same way the import itself does (BookMetaData) so the pre-flight
        /// duplicate check can't disagree with the actual import about a book's id.
        /// </summary>
        private static string ReadBookInstanceIdFromBloomSource(string sourcePath)
        {
            try
            {
                using (var zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(sourcePath))
                {
                    var index = zip.FindEntry("meta.json", true);
                    if (index < 0)
                        return null;
                    using (var stream = zip.GetInputStream(index))
                    using (var reader = new StreamReader(stream))
                    {
                        return BookMetaData.FromString(reader.ReadToEnd())?.Id;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void moveBookIntoThisCollection(Book.Book origBook, BookCollection origCollection)
        {
            var possibleTCFilePath = TeamCollectionManager.GetTcLinkPathFromLcPath(
                origCollection.PathToDirectory
            );
            var origCollectionIsTC = RobustFile.Exists(possibleTCFilePath);

            TeamCollectionManager origTcManager = null;
            if (origCollectionIsTC)
            {
                // Original collection is a TC. We should only allow the move if the book is already checked out in its
                // original tc, and the original tc is connected so we can properly delete it there

                var origSettingsFilePath = TeamCollection.TeamCollection.CollectionPath(
                    origCollection.PathToDirectory
                );

                // This team collection manager is only used to check and delete the book from the original tc, so it
                // doesn't need the normal objects, and it's probably safer not to give it objects which might have
                // connections to the current collection.
                origTcManager = new TeamCollectionManager(
                    origSettingsFilePath,
                    null,
                    null,
                    null,
                    null,
                    null
                );
                if (origTcManager.CannotDeleteBecauseDisconnected(origBook))
                {
                    throw new ApplicationException(
                        $"{origCollection.Name} is a Team Collection which is currently disconnected. Please connect the Team Collection before moving books that are part of it."
                    );
                }
                var tc = origTcManager.CurrentCollection;
                if (tc == null || !tc.CanSaveChanges(origBook.BookInfo))
                {
                    throw new ApplicationException(
                        $"{origCollection.Name} is a Team Collection. Please open {origCollection.Name} and check out {origBook.BookInfo.Title} before moving it."
                    );
                }
            }

            var origBookFolderPath = origBook.FolderPath;
            var bookFolderName = Path.GetFileName(origBookFolderPath);
            var newCollectionDir = TheOneEditableCollection.PathToDirectory;
            var newBookFolderPath = Path.Combine(newCollectionDir, bookFolderName);
            if (origCollectionIsTC)
            {
                origTcManager.CurrentCollection.DeleteBookFromRepo(origBookFolderPath);

                // Get rid of any TC status, etc,. that we copied over
                BookStorage.RemoveLocalOnlyFiles(origBookFolderPath);
            }
            SIL.IO.RobustIO.MoveDirectory(origBookFolderPath, newBookFolderPath);
            Logger.WriteEvent("After BookStorage.DeleteBook({0})", origBook.BookInfo.FolderPath);
            origCollection.HandleBookDeletedFromCollection(origBookFolderPath);
            ReloadEditableCollection();
            var movedBookInfo = TheOneEditableCollection
                .GetBookInfos()
                .FirstOrDefault(info => info.FolderPath == newBookFolderPath);
            var movedBook = GetBookFromBookInfo(movedBookInfo);
            SelectBook(movedBook);
            BookHistory.AddEvent(
                movedBook,
                BookHistoryEventType.Moved,
                $"Moved book from collection \"{origCollection.Name}\" to collection \"{TheOneEditableCollection.Name}\""
            );
        }

        /// <summary>
        /// Eventually this might entirely replace ReloadCollections, since we probably never need to reload anything ut the first.
        /// For now it actually reloads them all, but at least allows clients that definitely only need the first reloaded to do so.
        /// </summary>
        /// <param name="collection"></param>
        public void ReloadEditableCollection()
        {
            // I hope we can get rid of this when we retire the old LibraryListView, but for now we need to keep both views up to date.
            // optimize: we only need to reload the first (editable) collection; better yet, we only need to add the one new book to it.
            ReloadCollections();
        }

        public void UpdateLabelOfBookInEditableCollection(Book.Book book)
        {
            // We can find a more efficient way to do this if necessary.
            //ReloadEditableCollection();
            // This allows it to get resorted. Is that good? It may move dramatically, even disappear from the screen.
            TheOneEditableCollection.UpdateBookInfo(book.BookInfo);
            // This actually changes the label. (One would think that re-rendering the collection would do this,
            // but somehow the way we are caching the book title as state in anticipation of the following
            // message was preventing this. It might be redundant now.)
            BookCommandsApi.UpdateButtonTitle(
                _webSocketServer,
                book.BookInfo,
                book.NameBestForUserDisplay
            );

            // happens as a side effect
            //_webSocketServer.SendEvent("editableCollectionList", "reload:" + _bookCollections[0].PathToDirectory);
        }

        /// <summary>
        /// Titles of all the books in the vernacular collection.
        /// </summary>
        internal IEnumerable<string> BookTitles
        {
            get { return TheOneEditableCollection.GetBookInfos().Select(book => book.Title); }
        }

        public BookCollection TheOneEditableCollection
        {
            get
            {
                return GetBookCollections()
                    .First(c => c.Type == BookCollection.CollectionType.TheOneEditableCollection);
            }
        }

        public string VernacularCollectionNamePhrase
        {
            get { return _collectionSettings.VernacularCollectionNamePhrase; }
        }

        public bool ShowSourceCollections
        {
            get { return _collectionSettings.AllowNewBooks; }
        }

        private void SetupChangeNotifications(BookCollection collection)
        {
            collection.CollectionChanged += (sender, args) =>
            {
                _webSocketServer.SendEvent(
                    "editableCollectionList",
                    "reload:" + collection.PathToDirectory
                );
            };

            _localizationChangedEvent?.Subscribe(unused =>
            {
                if (collection.IsFactoryInstalled)
                {
                    _webSocketServer.SendEvent(
                        "editableCollectionList",
                        "reload:" + collection.PathToDirectory
                    );
                }
                else
                {
                    // This is tricky. Reloading the collection won't do it, because nothing has changed that would cause
                    // the buttons to re-render. But some of them may be showing a string like "Missing title" that
                    // is localizable. This is not very efficient, as we may process updates for many books that
                    // don't need it or don't even have buttons due to laziness. But changing UI language is really rare.
                    foreach (var info in collection.GetBookInfos())
                        BookCommands.RequestButtonLabelUpdate(collection.PathToDirectory, info.Id);
                }
            });
        }

        /// <summary>
        /// This may be called on any thread. Please leave things where the only call is in GetBookCollections,
        /// having claimed the appropriate lock.
        /// </summary>
        /// <returns></returns>
        private IEnumerable<BookCollection> GetBookCollectionsOnce()
        {
            BookCollection editableCollection;
            using (PerformanceMeasurement.Global?.Measure("Creating Primary Collection"))
            {
                editableCollection = _bookCollectionFactory(
                    _pathToCollection,
                    BookCollection.CollectionType.TheOneEditableCollection,
                    _collectionSettings
                );
                if (_bookCollectionHolder != null)
                    _bookCollectionHolder.TheOneEditableCollection = editableCollection;
                SetupChangeNotifications(editableCollection);
            }

            _currentEditableCollectionSelection.SelectCollection(editableCollection);
            yield return editableCollection;
            // If we're locked to one downloaded book, we don't need to show the source collections, or even to load them.
            if (!_collectionSettings.EditingABlorgBook)
            {
                foreach (var bookCollection in _sourceCollectionsList.GetSourceCollectionsFolders())
                {
                    var collection = _bookCollectionFactory(
                        bookCollection,
                        BookCollection.CollectionType.SourceCollection
                    );
                    // Apart from the editable collection, I think only the downloaded books needs this (because books
                    // can be deleted from it and possibly added by new downloads); but it seems safest to set up for all.
                    SetupChangeNotifications(collection);
                    yield return collection;
                }
            }
        }

        private Form MainShell =>
            Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);

        // Not entirely happy that this method which launches a dialog is in the Model.
        // but with the Collection UI moving to JS I don't see a good alternative
        public void MakeBloomPack(bool forReaderTools)
        {
            var initialPath = FilePathMemory.GetOutputFilePath(
                TheOneEditableCollection,
                ".BloomPack"
            );
            var destFileName = MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                initialPath,
                "BloomPack files|*.BloomPack"
            );
            if (!string.IsNullOrEmpty(destFileName))
            {
                FilePathMemory.RememberOutputFilePath(
                    TheOneEditableCollection,
                    ".BloomPack",
                    destFileName
                );
                MakeBloomPack(destFileName, forReaderTools);
            }
        }

        public void RescueMissingImages()
        {
            var initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var selectedPath = BloomFolderChooser.ChooseFolder(
                initialPath,
                "Select the folder where replacement images can be found"
            );
            if (!string.IsNullOrEmpty(selectedPath))
                AttemptMissingImageReplacements(selectedPath);
        }

        public void SelectBook(Book.Book book)
        {
            _bookSelection.SelectBook(book);
        }

        public Book.Book GetSelectedBookOrNull()
        {
            return _bookSelection.CurrentSelection;
        }

        public bool DeleteBook(Book.Book book, BookCollection collection = null)
        {
            if (collection == null)
                collection = TheOneEditableCollection;
            Debug.Assert(book.FolderPath == _bookSelection.CurrentSelection?.FolderPath);

            if (
                _bookSelection.CurrentSelection != null
                && _bookSelection.CurrentSelection.CanDelete
            )
            {
                if (IsCurrentBookInCollection())
                {
                    if (!_bookSelection.CurrentSelection.IsDeletable)
                    {
                        var msg = LocalizationManager.GetString(
                            "TeamCollection.CheckOutForDelete",
                            "Please check out the book before deleting it."
                        );
                        BloomMessageBox.ShowInfo(msg);
                        return false;
                    }
                    if (_tcManager.CannotDeleteBecauseDisconnected(_bookSelection.CurrentSelection))
                    {
                        var msg = LocalizationManager.GetString(
                            "TeamCollection.ConnectForDelete",
                            "Please connect to the Team Collection before deleting books that are part of it."
                        );
                        BloomMessageBox.ShowInfo(msg);
                        return false;
                    }
                }
                var bookName = _bookSelection.CurrentSelection.NameBestForUserDisplay;
                var bookId = _bookSelection.CurrentSelection.ID;
                var confirmRecycleDescription = L10NSharp.LocalizationManager.GetString(
                    "CollectionTab.ConfirmRecycleDescription",
                    "The book '{0}'"
                );
                if (
                    ConfirmRecycleDialog.JustConfirm(
                        string.Format(confirmRecycleDescription, bookName),
                        false,
                        "Palaso"
                    )
                )
                {
                    // The sequence of these is a bit arbitrary. We'd like to delete the book in both places.
                    // Either could conceivably fail. If something goes wrong with removing the selection
                    // from it (very unlikely), we may as well leave nothing changed. If we delete it from
                    // the local collection but fail to delete it from the repo, it will come back at the
                    // next startup. If we delete it from the repo but fail to delete it locally,
                    // it will just stick around, and at least the desired team collection result has
                    // been achieved and the local result won't be a surprise later. So it seems marginally
                    // better to do them in this order.
                    _bookSelection.SelectBook(null);
                    if (collection == TheOneEditableCollection)
                        _tcManager.CurrentCollection?.DeleteBookFromRepo(book.FolderPath);
                    collection.DeleteBook(book.BookInfo);
                    // We only want history in the main collection. In particular, it causes problems
                    // with our change watcher looking for new books in the downloads collection if
                    // we mess with history there.
                    if (collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
                        CollectionHistory.AddBookEvent(
                            collection.PathToDirectory,
                            bookName,
                            bookId,
                            BookHistoryEventType.Deleted
                        );
                    return true;
                }
            }
            return false;
        }

        private bool IsCurrentBookInCollection()
        {
            var currentFolder = Path.GetDirectoryName(_bookSelection.CurrentSelection.FolderPath);
            return (currentFolder == _collectionSettings.FolderPath);
        }

        public void DoubleClickedBook()
        {
            // If we need the book to be checked out for editing, make sure it is. Do not allow double click
            // to check it out.
            if (_bookSelection.CurrentSelection?.IsSaveable ?? false)
            {
                _editBookCommand.Raise(_bookSelection.CurrentSelection);
            }
        }

        public void OpenFolderOnDisk()
        {
            try
            {
                ProcessExtra.ShowFileInExplorerInFront(_bookSelection.CurrentSelection.FolderPath);
            }
            catch (System.Runtime.InteropServices.COMException e)
            {
                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                    e,
                    "Bloom had a problem asking your operating system to show that folder. Sorry!"
                );
            }
        }

        public void BringBookUpToDate()
        {
            var b = _bookSelection.CurrentSelection;
            _bookSelection.SelectBook(null);

            using (var dlg = new ProgressDialogForeground()) //REVIEW: this foreground dialog has known problems in other contexts... it was used here because of its ability to handle exceptions well. TODO: make the background one handle exceptions well
            {
                // Since the user explicitly told us to do this again, we will, even if we think
                // it's already been done.
                dlg.ShowAndDoWork(progress => b.BringBookUpToDate(progress));
            }

            _bookSelection.SelectBook(b);
        }

        /// <summary>
        /// All we do at this point is make a file with a ".doc" extension and open it.
        /// </summary>
        /// <remarks>
        /// The .doc extension allows the operating system to recognize which program
        /// should open the file, and the program (whether Microsoft Word or LibreOffice
        /// or OpenOffice) seems to handle HTML content just fine.
        /// </remarks>
        public void ExportDocFormat(string destDocPath)
        {
            var outputFolder = Path.GetDirectoryName(destDocPath);
            if (Directory.Exists(outputFolder))
            {
                // Clean out everything from the destination folder unless it appears to be a bloom
                // book folder (which shouldn't happen).
                var appearsToBeBloomBookFolder = Directory
                    .EnumerateFiles(outputFolder, "*.htm")
                    .Any();
                if (appearsToBeBloomBookFolder)
                {
                    var msgTemplate = LocalizationManager.GetString(
                        "Spreadsheet.OverwriteBook",
                        "The folder named {0} already exists and looks like it might be a Bloom book folder!"
                    );
                    var msg = string.Format(msgTemplate, outputFolder);
                    var messageBoxButtons = new[]
                    {
                        new MessageBoxButton()
                        {
                            Text = "Cancel",
                            Id = "cancel",
                            Default = true,
                        },
                    };
                    var formToInvokeOn = Application
                        .OpenForms.Cast<Form>()
                        .FirstOrDefault(f => f is Shell);
                    formToInvokeOn.Invoke(
                        (Action)(
                            () =>
                            {
                                BloomMessageBox.Show(
                                    formToInvokeOn,
                                    msg,
                                    messageBoxButtons,
                                    MessageBoxIcon.Warning
                                );
                            }
                        )
                    );
                    return;
                }
                if (Directory.EnumerateFileSystemEntries(outputFolder).Any())
                {
                    var msgTemplate = LocalizationManager.GetString(
                        "Spreadsheet.Overwrite",
                        "You are about to replace the existing folder named {0}"
                    );
                    var msg = string.Format(msgTemplate, outputFolder);
                    var messageBoxButtons = new[]
                    {
                        new MessageBoxButton() { Text = "Overwrite", Id = "overwrite" },
                        new MessageBoxButton()
                        {
                            Text = "Cancel",
                            Id = "cancel",
                            Default = true,
                        },
                    };
                    string result = null;
                    var formToInvokeOn = Application
                        .OpenForms.Cast<Form>()
                        .FirstOrDefault(f => f is Shell);
                    formToInvokeOn.Invoke(
                        (Action)(
                            () =>
                            {
                                result = BloomMessageBox.Show(
                                    formToInvokeOn,
                                    msg,
                                    messageBoxButtons,
                                    MessageBoxIcon.Warning
                                );
                            }
                        )
                    );
                    if (result != "overwrite")
                        return;
                }
                // In case there's a previous export, get rid of it.
                SIL.IO.RobustIO.DeleteDirectoryAndContents(outputFolder);
            }
            Directory.CreateDirectory(outputFolder);

            var sourcePath = _bookSelection.CurrentSelection.GetPathHtmlFile();
            // Linux (Trusty) LibreOffice requires slightly different metadata at the beginning
            // of the file in order to recognize it as HTML.  Otherwise it opens the file as raw
            // HTML (See https://silbloom.myjetbrains.com/youtrack/issue/BL-2276 if you don't
            // believe me.)  I don't know any perfect way to add this information to the file,
            // but a simple string replace should be safe.  This change works okay for both
            // Windows and Linux and for all three programs (Word, OpenOffice and Libre Office).
            var content = RobustFile.ReadAllText(sourcePath);
            var fixedContent = content.Replace(
                "<meta charset=\"UTF-8\">",
                "<meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\">"
            );
            var xmlDoc = RepairWordVisibility(fixedContent);
            XmlHtmlConverter.SaveDOMAsHtml5(xmlDoc, destDocPath); // writes file and returns path
            // We need to copy the CSS and image files from the book's folder to the destination folder.
            // We don't need other files from there for this export. (audio, video, etc)
            var sourceFolder = Path.GetDirectoryName(sourcePath);
            foreach (var sourceFilePath in Directory.EnumerateFiles(sourceFolder, "*.*"))
            {
                var filename = Path.GetFileName(sourceFilePath);
                var extension = Path.GetExtension(filename).ToLowerInvariant();
                if (
                    BookCompressor.CompressableImageFileExtensions.Contains(extension)
                    || extension == ".svg"
                    || extension == ".css"
                )
                {
                    var destFilePath = Path.Combine(outputFolder, filename);
                    RobustFile.Copy(sourceFilePath, destFilePath, true);
                }
            }
        }

        // BL-5998 Apparently Word doesn't read our CSS rules for bloom-visibility correctly.
        // So we're forced to control visibility more directly with inline styles.
        private static SafeXmlDocument RepairWordVisibility(string content)
        {
            var xmlDoc = XmlHtmlConverter.GetXmlDomFromHtml(content);
            var dom = new HtmlDom(xmlDoc);
            var bloomEditableDivs = dom.RawDom.SafeSelectNodes(
                "//div[contains(@class, 'bloom-editable')]"
            );
            foreach (SafeXmlElement editableDiv in bloomEditableDivs)
            {
                HtmlDom.AppendInlineStyle(
                    editableDiv,
                    editableDiv.HasClass("bloom-visibility-code-on")
                        ? "display: block;"
                        : "display: none;"
                );
            }

            return dom.RawDom;
        }

        public void UpdateThumbnailAsync(
            Book.Book book,
            HtmlThumbNailer.ThumbnailOptions thumbnailOptions,
            Action<Book.BookInfo, Image> callback,
            Action<Book.BookInfo, Exception> errorCallback
        )
        {
            if (!(book is ErrorBook))
            {
                _thumbNailer.RebuildThumbNailAsync(book, thumbnailOptions, callback, errorCallback);
            }
        }

        public void UpdateThumbnailAsync(Book.Book book)
        {
            UpdateThumbnailAsync(
                book,
                BookThumbNailer.GetCoverThumbnailOptions(-1, Guid.Empty),
                (bookInfo, image) =>
                {
                    try
                    {
                        RefreshOneThumbnail(bookInfo, image);
                    }
                    finally
                    {
                        image?.Dispose();
                    }
                },
                HandleThumbnailerErrror
            );
        }

        // This is currently only actually needed and useful if the thumbnail update was
        // triggered by the "Update Thumbnail" menu item;
        // If we are entering the Collection tab then the listener is probably not mounted yet
        // and will rely on other cache busting instead (BL-16199)
        private void RefreshOneThumbnail(Book.BookInfo bookInfo, Image image)
        {
            _webSocketServer.SendString("bookImage", "reload", bookInfo.Id);
        }

        private void HandleThumbnailerErrror(Book.BookInfo bookInfo, Exception error)
        {
            string path = Path.Combine(bookInfo.FolderPath, "thumbnail.png");
            try
            {
                Resources.Error70x70.Save(@path, ImageFormat.Png);
            }
            catch (Exception e)
            {
                Logger.WriteError("Could not save error icon for book", e);
            }

            RefreshOneThumbnail(bookInfo, Resources.Error70x70);
        }

        internal (string dirName, string dirPrefix) GetDirNameAndPrefixForCollectionBloomPack()
        {
            var dir = TheOneEditableCollection.PathToDirectory;
            return (dir, "");
        }

        public void MakeBloomPack(string outputPath, bool forReaderTools = false)
        {
            var (dirName, dirPrefix) = GetDirNameAndPrefixForCollectionBloomPack();
            var rootName = Path.GetFileName(dirName);
            if (rootName == null)
                return;
            Logger.WriteEvent($"Making BloomPack at {outputPath} forReaderTools={forReaderTools}");
            MakeBloomPackWithUI(outputPath, dirName, dirPrefix, forReaderTools, isCollection: true);
        }

        internal (string dirName, string dirPrefix) GetDirNameAndPrefixForSingleBookBloomPack(
            string inputBookFolder
        )
        {
            var rootName = Path.GetFileName(inputBookFolder);
            if (rootName != null)
                rootName += Path.DirectorySeparatorChar;

            return (inputBookFolder, rootName);
        }

        public void MakeSingleBookBloomPack(string outputPath, string inputBookFolder)
        {
            var (dirName, dirPrefix) = GetDirNameAndPrefixForSingleBookBloomPack(inputBookFolder);
            if (dirPrefix == null)
                return;
            Logger.WriteEvent(
                $"Making single book BloomPack at {outputPath} bookFolderPath={inputBookFolder}"
            );
            MakeBloomPackWithUI(outputPath, dirName, dirPrefix, false, isCollection: false);
        }

        private void MakeBloomPackWithUI(
            string outputPath,
            string sourceDirectory,
            string dirNamePrefix,
            bool forReaderTools,
            bool isCollection
        )
        {
            try
            {
                if (RobustFile.Exists(outputPath))
                {
                    // UI already got permission for this
                    RobustFile.Delete(outputPath);
                }
                using (var pleaseWait = new SimpleMessageDialog("Creating BloomPack...", "Bloom"))
                {
                    try
                    {
                        pleaseWait.Show();
                        pleaseWait.BringToFront();
                        Application.DoEvents(); // actually show it
                        Cursor.Current = Cursors.WaitCursor;

                        Logger.WriteEvent(
                            "BloomPack outputPath will be "
                                + outputPath
                                + ", made from "
                                + sourceDirectory
                                + " with rootName "
                                + Path.GetFileName(sourceDirectory)
                        );
                        MakeBloomPackInternal(
                            outputPath,
                            sourceDirectory,
                            dirNamePrefix,
                            forReaderTools,
                            isCollection
                        );

                        // show it
                        Logger.WriteEvent("Showing BloomPack on disk");
                        ProcessExtra.ShowFileInExplorerInFront(outputPath);
                        Analytics.Track("Create BloomPack");
                    }
                    finally
                    {
                        Cursor.Current = Cursors.Default;
                        pleaseWait.Close();
                    }
                }
            }
            catch (Exception e)
            {
                ErrorReport.NotifyUserOfProblem(e, "Could not make the BloomPack at " + outputPath);
            }
        }

        /// <summary>
        /// Makes a BloomPack of the specified sourceDirectory.
        /// </summary>
        /// <param name="outputPath">The outputPath to write to. Precondition: Must not exist.</param>
        internal void MakeBloomPackInternal(
            string outputPath,
            string sourceDirectory,
            string dirNamePrefix,
            bool forReaderTools,
            bool isCollection
        )
        {
            if (isCollection)
            {
                BookCompressor.CompressCollectionDirectory(
                    outputPath,
                    sourceDirectory,
                    dirNamePrefix,
                    forReaderTools
                );
            }
            else
            {
                BookCompressor.CompressBookDirectory(
                    outputPath,
                    sourceDirectory,
                    MakeBloomPackBookFileFilter(sourceDirectory),
                    dirNamePrefix,
                    forReaderTools
                );
            }
        }

        public static BookFileFilter MakeBloomPackBookFileFilter(string bookFolderPath)
        {
            var filter = new BookFileFilter(bookFolderPath)
            {
                IncludeFilesForContinuedEditing = true,
                // want audio in bloompack: see https://issues.bloomlibrary.org/youtrack/issue/BL-11741.
                NarrationLanguages = null, // all audio
                WantMusic = true,
                WantVideo = true,
            };
            // these are artifacts of uploading book to BloomLibrary.org and not useful in BloomPubs
            filter.AlwaysReject(new Regex("^thumbnail-"));
            return filter;
        }

        public string GetSuggestedBloomPackPath()
        {
            return TheOneEditableCollection.Name + ".BloomPack";
        }

        public void DoUpdatesOfAllBooks()
        {
            using (var dlg = new ProgressDialogBackground())
            {
                dlg.ShowAndDoWork((progress, args) => DoUpdatesOfAllBooks(progress));
            }
        }

        public void DoUpdatesOfAllBooks(IProgress progress)
        {
            int i = 0;
            foreach (var bookInfo in TheOneEditableCollection.GetBookInfos())
            {
                i++;
                var book = _bookServer.GetBookFromBookInfo(bookInfo);
                //gets overwritten: progress.WriteStatus(book.NameBestForUserDisplay);
                progress.WriteMessage(
                    "Processing "
                        + book.NameBestForUserDisplay
                        + " "
                        + i
                        + "/"
                        + TheOneEditableCollection.GetBookInfos().Count()
                );
                // Since the user told us to do it, we'll do it even to books that we think are already
                // up to date. (EnsureUpToDate would do so anyway, since these are newly created Book objects, even if they are
                // for books we already have in memory.) But not to ones where saving is disabled (in a TC).
                if (book.IsSaveable)
                    book.BringBookUpToDate(progress);
            }
        }

        public void DoChecksOfAllBooks()
        {
            using (var dlg = new ProgressDialogBackground())
            {
                dlg.ShowAndDoWork((progress, args) => DoChecksOfAllBooksBackgroundWork(dlg, null));
                if (dlg.Progress.ErrorEncountered || dlg.Progress.WarningsEncountered)
                {
                    MessageBox.Show("Bloom will now open a list of problems it found.");
                    var path = Path.GetTempFileName() + ".txt";
                    RobustFile.WriteAllText(path, dlg.ProgressString.Text);
                    ProcessExtra.SafeStartInFront(path);
                }
                else
                {
                    MessageBox.Show("Bloom didn't find any problems.");
                }
            }
        }

        public void AttemptMissingImageReplacements(string pathToFolderOfReplacementImages = null)
        {
            using (var dlg = new ProgressDialogBackground())
            {
                dlg.ShowAndDoWork(
                    (progress, args) =>
                        DoChecksOfAllBooksBackgroundWork(dlg, pathToFolderOfReplacementImages)
                );
                if (dlg.Progress.ErrorEncountered || dlg.Progress.WarningsEncountered)
                {
                    MessageBox.Show(
                        "There were some problems. Bloom will now open a log of the attempt to replace missing images."
                    );
                }
                else
                {
                    MessageBox.Show(
                        "There are no more missing images. Bloom will now open a log of what it did."
                    );
                }

                var path = Path.GetTempFileName() + ".txt";
                RobustFile.WriteAllText(path, dlg.ProgressString.Text);
                try
                {
                    ProcessExtra.SafeStartInFront(path);
                }
                catch (System.OutOfMemoryException)
                {
                    // This has happened at least once.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3431.
                    MessageBox.Show(
                        "Bloom ran out of memory trying to open the log.  You should quit and restart the program.  (Your books should all be okay.)"
                    );
                }
            }
        }

        public void DoChecksOfAllBooksBackgroundWork(
            ProgressDialogBackground dialog,
            string pathToFolderOfReplacementImages
        )
        {
            var bookInfos = TheOneEditableCollection.GetBookInfos();
            var count = bookInfos.Count();
            if (count == 0)
                return;

            foreach (var bookInfo in bookInfos)
            {
                //not allowed in this thread: dialog.ProgressBar.Value++;
                dialog.Progress.ProgressIndicator.PercentCompleted += 100 / count;

                var book = _bookServer.GetBookFromBookInfo(bookInfo);

                dialog.Progress.WriteMessage("Checking " + book.NameBestForUserDisplay);
                book.CheckBook(dialog.Progress, pathToFolderOfReplacementImages);
                dialog.ProgressString.WriteMessage("");
            }
            dialog.Progress.ProgressIndicator.PercentCompleted = 100;
        }

        private void CreateFromSourceBook(Book.Book sourceBook)
        {
            try
            {
                var newBook = _bookServer.CreateFromSourceBook(
                    sourceBook,
                    TheOneEditableCollection.PathToDirectory
                );
                if (newBook == null)
                    return; //This can happen if there is a configuration dialog and the user clicks Cancel

                // We want to eventually make a book-created entry in history for this book,
                // which includes information about the book we made it from. We want to wait
                // to make that record until the new author gives it a name in L1. Possible
                // sources for the original name may be lost in the course of bringing it up to
                // date, so we capture the original title now. Also, we capture the initial
                // L1 title, so we can reliably tell whether the user has actually provided one.
                newBook.PendingCreationSource = Path.GetFileName(sourceBook.FolderPath);
                var l1Lang = newBook?.BookData?.Language1Tag;
                var sourceL1Title = sourceBook.BookInfo.GetTitleForLanguage(l1Lang);
                newBook.PendingCreationSourceTitle = sourceL1Title;
                newBook.BringBookUpToDate(new NullProgress(), false);

                TheOneEditableCollection.AddBookInfo(newBook.BookInfo);

                if (_bookSelection != null)
                {
                    _bookSelection.SelectBook(newBook, aboutToEdit: true);
                }
                //enhance: would be nice to know if this is a new shell
                if (!sourceBook.IsInEditableCollection)
                {
                    Analytics.Track(
                        "Create Book",
                        new Dictionary<string, string>()
                        {
                            { "Category", sourceBook.CategoryForUsageReporting },
                            { "BookId", newBook.ID },
                            { "Country", _collectionSettings.Country },
                        }
                    );
                }
                _editBookCommand.Raise(newBook);
            }
            catch (Exception e)
            {
                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                    e,
                    "Bloom ran into an error while creating that book. (Sorry!)"
                );
            }
        }

        public BookInfo GetBookInfoByFolderPath(string path)
        {
            var collectionPath = Path.GetDirectoryName(path);
            // This might need adjustment if we ever get Linux/Mac versions working. But I think not. See the
            // comment in BookCollection.GetBookInfoByFolderPath.
            var collection = GetBookCollections()
                .FirstOrDefault(c =>
                    c.PathToDirectory.ToLowerInvariant() == collectionPath.ToLowerInvariant()
                );
            if (collection == null)
                return null;
            return collection.GetBookInfoByFolderPath(path);
        }

        public Book.Book GetBookFromBookInfo(BookInfo bookInfo)
        {
            // If we're looking for the current book it's important to return the actual book object,
            // because it could end up modified in ways that make our one out-of-date if we modify another
            // instance based on the same folder. For example, Rename could make our FolderPath wrong.
            if (bookInfo.FolderPath == _bookSelection.CurrentSelection?.FolderPath)
                return _bookSelection.CurrentSelection;
            return _bookServer.GetBookFromBookInfo(bookInfo);
        }

        /// <summary>
        /// Zip up the book folder, excluding .pdf, .bloombookorder, .map, .bloompack, .db files.
        /// The resulting file should have a .bloomSource extension, but this depends on properly
        /// setting the value of destFileName before calling this method.  TeamCollection still
        /// uses the .bloom extension until we can figure out how to safely migrate teams as a
        /// whole to using .bloomSource.
        /// </summary>
        /// <param name="exception">any exception which occurs when trying to save the file</param>
        /// <returns>true if file was saved successfully; false otherwise</returns>
        /// <remarks>if return value is false, exception is non-null and vice versa</remarks>
        public static bool SaveAsBloomSourceFile(
            string srcFolderName,
            string destFileName,
            out Exception exception,
            string[] extraFilesToInclude = null
        )
        {
            exception = null;
            try
            {
                // Note, .bloombookorder files are no longer created; they are obsolete.
                var excludedExtensions = new[]
                {
                    ".pdf",
                    ".bloombookorder",
                    ".map",
                    ".bloompack",
                    ".db",
                };

                Logger.WriteEvent("Zipping up {0} ...", destFileName);
                var zipFile = new BloomZipFile(destFileName);
                zipFile.AddDirectoryContents(srcFolderName, excludedExtensions);
                foreach (var path in extraFilesToInclude ?? new string[0])
                {
                    zipFile.AddTopLevelFile(path);
                }

                Logger.WriteEvent("Saving {0} ...", destFileName);
                zipFile.Save();

                if (destFileName.EndsWith(".bloom"))
                    Logger.WriteEvent("Finished writing .bloom file.");
                else
                    Logger.WriteEvent("Finished writing .bloomSource file.");
            }
            catch (Exception e)
            {
                exception = e;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Get a requested book by id or fall back to the current selection when no id is provided.
        /// Returns null if the requested id cannot be resolved.
        /// </summary>
        public Book.Book GetRequestedBookOrDefaultOrNull(string requestedBookId)
        {
            if (!string.IsNullOrWhiteSpace(requestedBookId))
            {
                try
                {
                    return GetBookFromId(requestedBookId);
                }
                catch
                {
                    return null;
                }
            }

            return _bookSelection?.CurrentSelection;
        }
    }
}
