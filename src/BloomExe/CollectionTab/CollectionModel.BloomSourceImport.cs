using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.History;
using Bloom.MiscUI;
using Bloom.Utils;
using Bloom.web;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.CollectionTab
{
    // The .bloomSource import feature lives in this partial-class file so it doesn't bloat
    // CollectionModel.cs. It covers choosing the file(s), the pre-flight duplicate check the
    // collection screen uses to pick which dialog to show, and the import itself (edit-as-is,
    // make-derivative, replace-existing, or add-as-copy). Saving/exporting a book AS a .bloomSource
    // is a separate concern and stays in CollectionModel.cs (SaveAsBloomSourceFile).
    //
    // These methods are stateless commands: the front-end owns the flow, so the chosen file paths
    // are returned to it (by ChooseBloomSourceFilesToImport) and passed back in for the duplicate
    // check and the import, rather than being remembered here between the several API calls.
    public partial class CollectionModel
    {
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
        /// Prompts the user to pick one or more .bloomSource files and returns the chosen paths (an
        /// empty array if the user cancelled). The caller (the collection screen) holds onto these and
        /// passes them back to the duplicate check and to <see cref="ImportBloomSourceFiles"/>. This
        /// is separate from the import itself so the collection screen can, once the files are chosen,
        /// show the single import dialog appropriate to the batch (edit-vs-derivative when none are
        /// already present, replace-vs-add-copy when any are).
        /// </summary>
        public string[] ChooseBloomSourceFilesToImport()
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
                    return new string[0];
                return dlg.FileNames;
            }
        }

        /// <summary>
        /// Runs <see cref="ImportBloomSourceFiles"/> behind the embedded progress dialog on a
        /// background thread, so a large batch neither freezes the collection screen nor lets the
        /// awaiting browser request time out. The front-end already hosts the "collectionTab"
        /// EmbeddedProgressDialog, so it needs no extra wiring.
        /// </summary>
        public async Task ImportBloomSourceFilesWithProgressAsync(
            string[] paths,
            bool makeDerivatives,
            bool replaceExistingDuplicates
        )
        {
            await BrowserProgressDialog.DoWorkWithProgressDialogAsync(
                _webSocketServer,
                (progress, worker) =>
                {
                    ImportBloomSourceFiles(
                        paths,
                        makeDerivatives,
                        replaceExistingDuplicates,
                        progress
                    );
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
        /// Imports the .bloomSource files at <paramref name="paths"/> (the ones the front-end got
        /// from <see cref="ChooseBloomSourceFilesToImport"/>) into the current editable collection.
        /// The user has already made the choice (in the collection screen) that applies to the whole
        /// batch: when <paramref name="makeDerivatives"/> is true, each imported book is used to make
        /// a new derivative book (otherwise each is imported as an editable book); and, for the edit
        /// case, when <paramref name="replaceExistingDuplicates"/> is true any book already in the
        /// collection is replaced (otherwise it is added as a numbered copy). The last successfully
        /// imported book is selected when done.
        /// </summary>
        public void ImportBloomSourceFiles(
            string[] paths,
            bool makeDerivatives,
            bool replaceExistingDuplicates,
            IWebSocketProgress progress = null
        )
        {
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
                            // duplicate is not (see AllBloomSourceDuplicatesAreReplaceable), but
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
        /// Returns true only if every chosen .bloomSource book that is already in the collection may
        /// currently be replaced. Replacing recycles the existing book, so in a Team Collection each
        /// duplicate must be checked out here (BookInfo.IsSaveable); outside a Team Collection books
        /// are always saveable. The collection screen uses this to disable the "Replace" duplicate
        /// choice (and explain why) when any duplicate is not checked out, so the user can't delete a
        /// book they haven't checked out. When there are no duplicates the question is moot, so this
        /// returns true. Duplicates are detected purely by bookInstanceId, exactly as in
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
        /// library, so we must never end up with two books sharing one). The collection screen uses
        /// this (on the paths it got from <see cref="ChooseBloomSourceFilesToImport"/>) to decide
        /// whether to ask the user how duplicates should be handled before importing.
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
    }
}
