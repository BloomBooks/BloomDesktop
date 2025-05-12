using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Collection;
using Bloom.CollectionCreating;
using Bloom.MiscUI;
using Bloom.Utils;
using Bloom.web;
using L10NSharp;
using SIL.IO;

namespace Bloom.TeamCollection
{
    /// <summary>
    /// Implementation of a team collection repository implemented as a shared (herein called repo) folder.
    /// As far as possible we are attempting to keep behavior that is specific to
    /// the folder implementation here, while behavior that is independent of how
    /// the shared repo is stored should be in TeamRepo.cs.
    /// </summary>
    public class FolderTeamCollection : TeamCollection
    {
        private string _repoFolderPath; // the (presumably somehow shared) folder storing the repo
        private FileSystemWatcher _booksWatcher; // watches the _repoFolderPath/Books for changes
        private FileSystemWatcher _otherWatcher; // watches the _repoFolderPath/Other for changes

        // These four variables work together to track the last book we modified and whether we
        // are still doing so (and to lock access to the other two). They are manipulated
        // by the PutBookInRepo code, the SetBookStatusString code, and the change notification code to try to make sure
        // we don't raise notifications for changes to the repo that we made ourselves.
        // It's tricky because a change notification resulting from our own PutBookInRepo may
        // occur either during or shortly after the PutBookInRepo method finishes.
        // Currently we don't report notifications for the book being written while the put is in progress
        // or for 1s afterwards.
        private string _lastWriteBookPath;
        private bool _writeBookInProgress;
        private DateTime _lastWriteBookTime;
        private object _lockObject = new object(); // used to lock access to _lastPutBookPath and _putBookInProgress
        private CollectionLock _collectionLock;

        private const int kDebouncePeriodInMs = 100;
        private Dictionary<string, FileSystemEventRecord> _lastCreateEventByFile =
            new Dictionary<string, FileSystemEventRecord>();

        /// <summary>
        /// This empty constructor allows the class to be mocked.
        /// </summary>
        public FolderTeamCollection()
        {
            Debug.Assert(Program.RunningUnitTests);
        }

        public FolderTeamCollection(
            ITeamCollectionManager manager,
            string localCollectionFolder,
            string repoFolderPath,
            TeamCollectionMessageLog tcLog = null,
            BookCollectionHolder bookCollectionHolder = null,
            CollectionLock collectionLock = null
        )
            : base(manager, localCollectionFolder, tcLog, bookCollectionHolder)
        {
            _repoFolderPath = repoFolderPath;
            _collectionLock = collectionLock ?? new CollectionLock();
        }

        public string RepoFolderPath => _repoFolderPath;

        private string GetPathForTombstone(string bookFolderName)
        {
            string id = GetBookId(bookFolderName);
            if (id == null)
                return null;
            return Path.Combine(_repoFolderPath, id + ".tombstone");
        }

        /// <summary>
        /// The folder-implementation-specific part of PutBook, the public method in TeamRepo.
        /// Write the book as a .bloom by zipping the specified folder (and use its name).
        /// </summary>
        /// <param name="sourceBookFolderPath">The root folder for the book, typically ending in its title,
        ///     typically in the current collection folder.</param>
        /// <param name="newStatus"></param>
        /// <param name="inLostAndFound">If true, put the book into the Lost-and-found folder,
        ///     if necessary generating a unique name for it. If false, put it into the main repo
        ///     folder, overwriting any existing book.</param>
        /// <returns>The book's new status, with the new VersionCode</returns>
        protected override void PutBookInRepo(
            string sourceBookFolderPath,
            BookStatus status,
            bool inLostAndFound = false,
            Action<float> progressCallback = null
        )
        {
            var bookFolderName = Path.GetFileName(sourceBookFolderPath);
            var bookPath = GetPathToBookFileInRepo(bookFolderName);

            string pathToWrite = bookPath;

            if (inLostAndFound)
            {
                bookPath = AvailableLostAndFoundPath(bookFolderName);
                pathToWrite = bookPath;
            }
            else
            {
                // Make sure the repo directory that holds books exists
                var bookDirectoryPath = Path.GetDirectoryName(bookPath);
                Directory.CreateDirectory(bookDirectoryPath);
                if (RobustFile.Exists(bookPath))
                {
                    // We'll write the book initially to a new zip file. This may help with
                    // the problem of the main file being temporarily locked from a recent
                    // operation, such as checking out and then immediately in again (BL-9926).
                    // Also, if there is any sort of crash while writing the book, we won't
                    // leave a corrupt, incomplete zip file pretending to be a valid book.
                    // I'm not entirely happy with putting the tmp file in the shared
                    // directory. It's conceivable that Dropbox might try to replicate it.
                    // But RobustFile.Replace() won't handle things on different volumes,
                    // and we can't count on the system temp folder being on the same volume.
                    // In fact, in the LAN case, the shared directory may be the ONLY place
                    // this user is authorized to write on the destination volume.
                    // We could use delete and copy when they are on different volumes, but
                    // Dropbox sometimes throws up user warnings when Bloom deletes a Dropbox file;
                    // it doesn't seem to do so with Replace(). So this is the best option
                    // I can find.
                    pathToWrite = AvailablePath(bookFolderName, bookDirectoryPath, ".tmp");
                }
            }

            lock (_lockObject)
            {
                _lastWriteBookPath = bookPath;
                _writeBookInProgress = true;
            }

            try
            {
                var zipFile = new BloomZipFile(pathToWrite);
                zipFile.AddDirectory(
                    sourceBookFolderPath,
                    sourceBookFolderPath.Length + 1,
                    null,
                    progressCallback
                );
                zipFile.SetComment(status.WithCollectionId(CollectionId).ToJson());
                zipFile.Save();
                // If by any chance we've previously created a tombstone for this book, get rid of it.
                var pathForTombstone = GetPathForTombstone(bookFolderName);
                if (pathForTombstone != null)
                    RobustFile.Delete(pathForTombstone);
            }
            catch (Exception)
            {
                RobustFile.Delete(pathToWrite); // try to clean up
                throw;
            }

            if (pathToWrite != bookPath)
            {
                RobustFile.Replace(pathToWrite, bookPath, null);
            }

            lock (_lockObject)
            {
                _lastWriteBookTime = DateTime.Now;
                _writeBookInProgress = false;
            }
        }

        public override bool KnownToHaveBeenDeleted(string bookFolderPath)
        {
            var pathToBookFileInRepo = GetPathToBookFileInRepo(Path.GetFileName(bookFolderPath));
            var pathForTombstone = GetPathForTombstone(Path.GetFileName(bookFolderPath));
            if (pathForTombstone == null)
                return false; // if the book doesn't have meta.json, we have no way to know.
            return !RobustFile.Exists(pathToBookFileInRepo) && RobustFile.Exists(pathForTombstone);
        }

        /// <summary>
        /// Find a path in the Lost And Found folder for the specified book.
        /// It must not have an existing file, and the name should be a similar
        /// as possible to bookFolderName.bloom
        /// </summary>
        private string AvailableLostAndFoundPath(string bookFolderName)
        {
            var lfPath = Path.Combine(_repoFolderPath, "Lost and Found");
            return AvailablePath(bookFolderName, lfPath, ".bloom");
        }

        private static string AvailablePath(
            string bookFolderName,
            string folderName,
            string extension
        )
        {
            string bookPath;
            Directory.CreateDirectory(folderName);
            int counter = 0;
            do
            {
                counter++;
                // Don't use ChangeExtension here, bookFolderName may have arbitrary period
                bookPath =
                    Path.Combine(
                        folderName,
                        bookFolderName + (counter == 1 ? "" : counter.ToString())
                    ) + extension;
            } while (RobustFile.Exists(bookPath));

            return bookPath;
        }

        protected override void MoveRepoBookToLostAndFound(string bookName)
        {
            var source = GetPathToBookFileInRepo(bookName);
            var dest = AvailableLostAndFoundPath(bookName);
            RobustFile.Move(source, dest);
        }

        private static string GetPathToBookFolder(string repoFolderPath) =>
            Path.Combine(repoFolderPath, "Books");

        // public and virtual only to support mocking
        public virtual string GetPathToBookFileInRepo(string bookFolderName)
        {
            // Don't use ChangeExtension here, it will fail if the folderName contains
            // some arbitrary period.
            return Path.Combine(GetPathToBookFolder(_repoFolderPath), bookFolderName) + ".bloom";
        }

        public override string GetRepoBookFile(string bookName, string fileName)
        {
            var path = GetPathToBookFileInRepo(bookName);
            return RobustZip.GetZipEntryContent(path, fileName);
        }

        /// <summary>
        /// Return a list of all the books currently in the repo. (It will not update as changes are made,
        /// either locally or remotely. Beware that conceivably a book in the list might be removed
        /// before you get around to processing it.)
        /// </summary>
        /// <returns></returns>
        public override string[] GetBookList()
        {
            return Directory
                .EnumerateFiles(Path.Combine(_repoFolderPath, "Books"), "*.bloom")
                // We are usually wary of stripping extensions for fear of periods in book names,
                // but it's OK here because we KNOW path ends in .bloom, so exactly that will be removed.
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .ToArray();
        }

        /// <summary>
        /// The shared-folder-specific part of the public GetBook method in TeamRepo.
        /// </summary>
        /// <param name="destinationCollectionFolder">Where to put the retrieved book folder,
        /// typically the local collection folder.</param>
        /// <param name="bookName">The name of the book, with or without the .bloom suffix - either way is fine</param>
        /// <returns>error message if there was a problem, otherwise, null</returns>
        protected override string FetchBookFromRepo(
            string destinationCollectionFolder,
            string bookName
        )
        {
            var bookPath = GetPathToBookFileInRepo(bookName);
            var destFolder = Path.Combine(
                destinationCollectionFolder,
                GetBookNameWithoutSuffix(bookName)
            );
            Debug.Assert(
                !destFolder.EndsWith(".bloom"),
                $"Copying zipFile to folder \"{destFolder}\", which ends with .bloom. This is probably an error, unless the book title literally contains .bloom"
            );

            try
            {
                RobustZip.UnzipDirectory(destFolder, bookPath);
                return null;
            }
            catch (Exception e)
                when (e is ICSharpCode.SharpZipLib.Zip.ZipException || e is IOException)
            {
                return GetBadZipFileMessage(bookName);
            }
        }

        public string GetBadZipFileMessage(string zipName)
        {
            var zipPath = GetPathToBookFileInRepo(zipName);
            var part1 = GetSimpleBadZipFileMessage(zipName);
            var part2 = CommonMessages.GetPleaseClickHereForHelpMessage(zipPath);
            return part1 + " " + part2;
        }

        public virtual string GetCouldNotOpenCorruptZipMessage()
        {
            return LocalizationManager.GetString(
                "TeamCollection.BadZipFile",
                "Bloom was not able to open the zip file, which may be corrupted."
            );
        }

        public string GetSimpleBadZipFileMessage(string bookName)
        {
            return CommonMessages.GetProblemWithBookMessage(bookName)
                + " "
                + GetCouldNotOpenCorruptZipMessage();
        }

        public override void PutCollectionFiles(string[] names)
        {
            var destPath = GetRepoProjectFilesZipPath(_repoFolderPath);
            RobustZip.WriteFilesToZip(names, _localCollectionFolder, destPath);
        }

        protected override DateTime LastRepoCollectionFileModifyTime
        {
            get
            {
                var repoProjectFilesZipPath = GetRepoProjectFilesZipPath(_repoFolderPath);
                if (!RobustFile.Exists(repoProjectFilesZipPath))
                    return DateTime.MinValue; // brand new repo, want to copy TO it.
                var collectionFilesModTime = new FileInfo(repoProjectFilesZipPath).LastWriteTime;
                GetMaxModifyTime("Allowed Words", ref collectionFilesModTime);
                GetMaxModifyTime("Sample Texts", ref collectionFilesModTime);
                return collectionFilesModTime;
            }
        }

        private void GetMaxModifyTime(string folderName, ref DateTime max)
        {
            var zipPath = Path.Combine(
                _repoFolderPath,
                "Other",
                Path.ChangeExtension(folderName, "zip")
            );
            if (RobustFile.Exists(zipPath))
            {
                var thisModTime = new FileInfo(zipPath).LastWriteTime;
                if (thisModTime > max)
                    max = thisModTime;
            }
        }

        // Make a zip file (in our standard location in the Other directory) of the top-level
        // files in the specified folder. The zip file has the same name as the folder.
        // At this point, we are not handling child folders.
        protected override void CopyLocalFolderToRepo(string folderName)
        {
            var sourceDir = Path.Combine(_localCollectionFolder, folderName);
            if (!Directory.Exists(sourceDir))
                return;
            var destPath = GetZipFileForFolder(folderName, _repoFolderPath);
            RobustZip.WriteAllTopLevelFilesToZip(destPath, sourceDir);
        }

        /// <summary>
        /// Given a book name matching a book in the repo, if there is a corresponding local
        /// folder, make sure it has the same case as the repo. This helps keep things
        /// consistent after a remote rename.
        /// </summary>
        /// <param name="bookBaseName"></param>
        public override void EnsureConsistentCasingInLocalName(string bookBaseName)
        {
            var localFolderPath = Path.Combine(_localCollectionFolder, bookBaseName);
            if (DoLocalAndRemoteNamesDifferOnlyByCase(bookBaseName))
            {
                var tempName = Guid.NewGuid().ToString();
                var tempPath = Path.Combine(_localCollectionFolder, tempName);
                RobustIO.MoveDirectory(localFolderPath, tempPath);
                RobustIO.MoveDirectory(tempPath, localFolderPath);
                var htmFileName = Path.Combine(localFolderPath, bookBaseName + ".htm");
                if (RobustFile.Exists(htmFileName))
                {
                    // This looks like a no-op, but it actually forces the file system to
                    // rename the file to the correct case. The source file matches irrespective of case.
                    var tempBookPath = Path.Combine(localFolderPath, tempName);
                    RobustFile.Move(htmFileName, tempBookPath);
                    RobustFile.Move(tempBookPath, htmFileName);
                }
                // Possibly the remote name change involved changing whether the name is locked.
                // So when we need to update this, we will update meta.json, which stores that.
                // It's just possible that this will bring something to local that we don't want,
                // like which tool is open, but I think it's more important for the local repo
                // to be consistent about whether the name is locked.
                var metaJson = GetRepoBookFile(bookBaseName, "meta.json");
                if (!string.IsNullOrEmpty(metaJson))
                {
                    var metaDataPath = Path.Combine(localFolderPath, "meta.json");
                    RobustFile.WriteAllText(metaDataPath, metaJson);
                }
            }
        }

        /// <summary>
        /// Finds the file bookBaseName.bloom in the repo and the folder bookBaseName in the local
        /// directory. Returns true if both exist and have different case.
        /// (It's not possible that they differ in any way other than by case, since we won't find
        /// a file or folder by any other name in either place. Without any wild cards in the searchPattern,
        /// I don't think it's possible to find a file that is not an exact match, except for case.)
        /// </summary>
        /// <param name="bookBaseName"></param>
        /// <returns></returns>
        public override bool DoLocalAndRemoteNamesDifferOnlyByCase(string bookBaseName)
        {
            var repoPath = GetPathToBookFileInRepo(bookBaseName);
            var localFolderPath = Path.Combine(_localCollectionFolder, bookBaseName);
            // Get the actual file name on disk, which may have a different case.
            var realRepoName = Path.GetFileNameWithoutExtension(
                Directory
                    .EnumerateFiles(Path.GetDirectoryName(repoPath), bookBaseName + ".bloom")
                    .FirstOrDefault()
            );
            var realLocalFolderName = Path.GetFileName(
                Directory
                    .EnumerateDirectories(_localCollectionFolder, bookBaseName)
                    .FirstOrDefault()
            );
            return (
                !string.IsNullOrEmpty(realRepoName)
                && !string.IsNullOrEmpty(realLocalFolderName)
                && realRepoName != realLocalFolderName
            );
        }

        public override string RepoDescription => _repoFolderPath;

        // The standard place where we store zip files for a collection-level folder.
        private static string GetZipFileForFolder(string folderName, string repoFolderPath)
        {
            return Path.Combine(repoFolderPath, "Other", Path.ChangeExtension(folderName, "zip"));
        }

        // The standard place where we store the top level collection files.
        internal static string GetRepoProjectFilesZipPath(string repoFolderPath)
        {
            return Path.Combine(repoFolderPath, "Other", "Other Collection Files.zip");
        }

        /// <summary>
        /// Have any collection-level files in the repo been modified since we last
        /// synced? Note, this might need some refining if we start storing things like
        /// history in 'other' that are not collection level settings files.
        /// </summary>
        /// <returns></returns>
        private bool GetRepoCollectionFilesUpdatedSinceSync()
        {
            var savedModTime = LocalCollectionFilesRecordedSyncTime();
            try
            {
                // We don't have a robust version of this function and I don't think it's
                // important enough to try to synthesize one here. Worst that happens from
                // just returning false is the user doesn't see that there are new remote
                // changes until reloading for some other reason.
                return Directory
                    .EnumerateFiles(Path.Combine(_repoFolderPath, "Other"))
                    .Any(
                        p => // We don't care about colorPalettes.json changing because the
                            // local copy is always two-way merged with the repo copy.  See BL-14254.
                            Path.GetFileName(p) != "colorPalettes.json"
                            && new FileInfo(p).LastWriteTime > savedModTime
                    );
            }
            catch (Exception ex)
            {
                NonFatalProblem.ReportSentryOnly(ex);
                return false;
            }
        }

        /// <summary>
        /// Copy collection level files from the repo to the local directory.
        /// The local copy of the colorPalettes.json file will be merged with,
        /// rather than copied from, the repo copy.  The colorPalettes.json file
        /// in the repo may be updated as a result of this call although that is
        /// unlikely.
        /// </summary>
        /// <param name="destFolder"></param>
        protected override void CopyRepoCollectionFilesToLocalImpl(string destFolder)
        {
            // This task copies a new version of the main project file over the existing one.
            // We have this file locked to prevent certain problems, supposedly in a way that
            // allows writing it but not moving/deleting it. But it seems the way our zip
            // utility overwrites files involves something that is not allowed. We need to free
            // it up while we do this.
            _collectionLock.UnlockFor(() => CopyRepoCollectionFilesTo(destFolder, _repoFolderPath));
            ExtractFolder(destFolder, _repoFolderPath, "Allowed Words");
            ExtractFolder(destFolder, _repoFolderPath, "Sample Texts");
            SyncColorPaletteFileWithRepo(destFolder);
        }

        protected override void SyncColorPaletteFileWithRepo(string localFolder)
        {
            try
            {
                // No need to spend time checking on colorPalettes.json while we're syncing it.
                // And nothing else should be changing while we're syncing it.  See BL-14254.
                _updatingCollectionFiles = true;
                SyncColorPaletteFileWithRepo(
                    localFolder,
                    _repoFolderPath,
                    _tcManager.Settings,
                    LocalCollectionFolder
                );
            }
            finally
            {
                _updatingCollectionFiles = false;
            }
        }

        private static void CopyToRepoIfNeeded(
            string localColorPalettePath,
            string repoColorPalettePath
        )
        {
            var needToCopy = false;
            if (RobustFile.Exists(repoColorPalettePath))
            {
                var repoData = RobustFile.ReadAllText(repoColorPalettePath);
                var destData = RobustFile.ReadAllText(localColorPalettePath);
                needToCopy = repoData != destData;
            }
            else
            {
                needToCopy = true;
            }
            if (needToCopy)
            {
                try
                {
                    RobustFile.Copy(localColorPalettePath, repoColorPalettePath, true);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"Error copying {localColorPalettePath} to repo: {e.Message}");
                }
            }
        }

        protected override DateTime GetRepoColorPaletteTime()
        {
            var repoColorPalettePath = Path.Combine(_repoFolderPath, "Other", "colorPalettes.json");
            return RobustFile.Exists(repoColorPalettePath)
                ? new FileInfo(repoColorPalettePath).LastWriteTime
                : DateTime.MinValue;
        }

        private static string MergeColorPaletteValues(string repoValues, string localValues)
        {
            if (repoValues == localValues)
                return localValues;
            if (String.IsNullOrEmpty(repoValues))
                return localValues;
            if (String.IsNullOrEmpty(localValues))
                return repoValues;
            var repoArray = repoValues.Split(new char[] { ' ' });
            var localList = new List<string>(localValues.Split(new char[] { ' ' }));
            foreach (var value in repoArray)
            {
                if (!string.IsNullOrEmpty(value) && !localList.Contains(value))
                    localList.Add(value);
            }
            return string.Join(" ", localList);
        }

        private static void CopyRepoCollectionFilesTo(string destFolder, string repoFolder)
        {
            var collectionZipPath = GetRepoProjectFilesZipPath(repoFolder);
            if (!RobustFile.Exists(collectionZipPath))
                return;
            try
            {
                RobustZip.ExtractFolderFromZip(
                    destFolder,
                    collectionZipPath,
                    () => new HashSet<string>(RootLevelCollectionFilesIn(destFolder))
                );
                SyncColorPaletteFileWithRepo(destFolder, repoFolder);
            }
            catch (Exception e)
                when (e is ICSharpCode.SharpZipLib.Zip.ZipException || e is IOException)
            {
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.All,
                    "Bloom could not unpack the collection files in your Team Collection",
                    exception: e
                );
            }
        }

        private static void SyncColorPaletteFileWithRepo(
            string localFolder,
            string repoFolder,
            CollectionSettings collectionSettings = null,
            string localCollectionFolder = null
        )
        {
            var repoColorPalettePath = Path.Combine(repoFolder, "Other", "colorPalettes.json");
            var localColorPalettePath = Path.Combine(localFolder, "colorPalettes.json");
            if (RobustFile.Exists(repoColorPalettePath))
            {
                if (RobustFile.Exists(localColorPalettePath))
                {
                    // Merge the two files additively.
                    var repoColorPalettes = new Dictionary<string, string>();
                    CollectionSettings.LoadColorPalettesFromJsonFile(
                        repoColorPalettes,
                        repoColorPalettePath
                    );
                    var localColorPalettes = collectionSettings?.ColorPalettes;
                    if (localColorPalettes == null)
                    {
                        localColorPalettes = new Dictionary<string, string>();
                        CollectionSettings.LoadColorPalettesFromJsonFile(
                            localColorPalettes,
                            localColorPalettePath
                        );
                    }
                    var dirty = false;
                    foreach (var key in repoColorPalettes.Keys)
                    {
                        var mergedValues = MergeColorPaletteValues(
                            repoColorPalettes[key],
                            localColorPalettes[key]
                        );
                        if (mergedValues != localColorPalettes[key])
                        {
                            localColorPalettes[key] = mergedValues;
                            dirty = true;
                        }
                        if (mergedValues != repoColorPalettes[key])
                            dirty = true;
                    }
                    if (dirty)
                    {
                        if (collectionSettings == null)
                        {
                            CollectionSettings.SaveColorPalettesToJsonFile(
                                localColorPalettes,
                                localColorPalettePath
                            );
                            // The copy to repo may not happen when we're actually copying files to the
                            // local collection, but that's okay.
                            if (localFolder == localCollectionFolder)
                                CopyToRepoIfNeeded(localColorPalettePath, repoColorPalettePath);
                        }
                        else if (collectionSettings.FolderPath == localFolder)
                        {
                            collectionSettings.SaveColorPalettesToJsonFile();
                            CopyToRepoIfNeeded(localColorPalettePath, repoColorPalettePath);
                        }
                        // If the local folder is not the collection folder, we don't need to
                        // save the color palettes because we must be working with temp data.
                    }
                }
                else
                {
                    // Add the palette file to the local collection if it doesn't exist there.
                    RobustFile.Copy(repoColorPalettePath, localColorPalettePath);
                    if (collectionSettings != null && collectionSettings.FolderPath == localFolder)
                    {
                        CollectionSettings.LoadColorPalettesFromJsonFile(
                            collectionSettings.ColorPalettes,
                            localColorPalettePath
                        );
                    }
                }
            }
            else if (RobustFile.Exists(localColorPalettePath))
            {
                // Add the palette file to the repo if it doesn't exist there.
                CopyToRepoIfNeeded(localColorPalettePath, repoColorPalettePath);
            }
        }

        /// <summary>
        /// Extract files from the repo zip identified by the folderName to a folder of the same
        /// name in the collection folder, deleting any files already present that are not in the zip.
        /// </summary>
        /// <param name="collectionFolder"></param>
        /// <param name="repoFolder"></param>
        /// <param name="folderName"></param>
        static void ExtractFolder(string collectionFolder, string repoFolder, string folderName)
        {
            var sourceZip = GetZipFileForFolder(folderName, repoFolder);
            if (!RobustFile.Exists(sourceZip))
                return;
            var destFolder = Path.Combine(collectionFolder, folderName);
            try
            {
                RobustZip.ExtractFolderFromZip(
                    destFolder,
                    sourceZip,
                    () =>
                        Directory.Exists(destFolder)
                            ? new HashSet<string>(
                                Directory
                                    .EnumerateFiles(destFolder)
                                    .Select(p => Path.GetFileName(p))
                            )
                            : new HashSet<string>()
                );
            }
            catch (Exception e)
                when (e is ICSharpCode.SharpZipLib.Zip.ZipException || e is IOException)
            {
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.All,
                    $"Bloom could not unpack the file {sourceZip} in your Team Collection"
                );
            }
        }

        // All the people who have something checked out in the repo.
        // Not yet used.
        //public override string[] GetPeople()
        //{
        //	var users = new HashSet<string>();
        //	foreach (var path in Directory.EnumerateFiles(_repoFolderPath, "*.bloom"))
        //	{
        //		var whoHasBookLocked = WhoHasBookLocked(Path.GetFileNameWithoutExtension(path));
        //		if (whoHasBookLocked != null)
        //			users.Add(whoHasBookLocked);
        //	}

        //	var results = users.ToList();
        //	results.Sort();
        //	return results.ToArray();
        //}

        // After calling this, NewBook and BookStatusChanged notifications will occur when
        // books are added or modified.
        protected internal override void StartMonitoring()
        {
            base.StartMonitoring();
            _booksWatcher = new FileSystemWatcher();

            var booksPath = Path.Combine(_repoFolderPath, "Books");
            if (!Directory.Exists(booksPath))
                return; // probably joining a TC and didn't get it synced properly.
            _booksWatcher.Path = booksPath;

            // Enhance: maybe one day we want to watch collection files too?

            // Watch for changes in LastWrite times, and
            // the renaming of files or directories.
            _booksWatcher.NotifyFilter =
                NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;

            _booksWatcher.DebounceChanged(OnChanged, kDebouncePeriodInMs);
            _booksWatcher.DebounceCreated(OnCreated, kDebouncePeriodInMs);
            _booksWatcher.DebounceRenamed(OnRenamed, kDebouncePeriodInMs);
            _booksWatcher.DebounceDeleted(OnDeleted, kDebouncePeriodInMs);

            // Begin watching.
            _booksWatcher.EnableRaisingEvents = true;

            _otherWatcher = new FileSystemWatcher(Path.Combine(_repoFolderPath, "Other"));
            _otherWatcher.NotifyFilter = NotifyFilters.LastWrite;
            _otherWatcher.DebounceChanged(OnCollectionFilesChanged, kDebouncePeriodInMs);
            _otherWatcher.EnableRaisingEvents = true;
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            if (CheckOwnWriteNotification(e.FullPath))
                return;

            if (CheckRecentCreateEvent(e.FullPath, new TimeSpan(0, 0, 1)))
                return;

            RaiseDeleteRepoBookFile(Path.GetFileName(e.Name));
        }

        private bool CheckOwnWriteNotification(string path)
        {
            lock (_lockObject)
            {
                // not interested in changes to tmp files.
                // (If by any chance a .tmp file gets propagated to another system, we're
                // still not interested in it, so harmless to respond 'true'.)
                if (Path.GetExtension(path) == ".tmp")
                    return true;
                // Not the book we most recently wrote, so not an 'own write'.
                // Note that our zip library sometimes creates a temp file by adding a suffix to the
                // path, so it's very likely that a recent write of a path starting with the name of the book we
                // wrote is a result of that.
                if (
                    !string.IsNullOrWhiteSpace(_lastWriteBookPath)
                    && !path.StartsWith(_lastWriteBookPath)
                )
                    return false;
                // We're still writing it...definitely an 'own write'
                if (_writeBookInProgress)
                    return true;

                // We were writing it within the last two seconds. It MIGHT be someone else's write, but
                // very unlikely.
                if (DateTime.Now - _lastWriteBookTime < new TimeSpan(0, 0, 2))
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Returns true if a FileSystemWatcher Create event was recently raised.
        /// </summary>
        /// <param name="fullPath">The path of the relevant file/directory</param>
        /// <param name="timeThreshold">Defines what "recently" means</param>
        /// <returns>Returns true if a Create event for the same {fullPath} was raised within {timeThreshold} amount of time.
        /// Otherwise, returns false</returns>
        private bool CheckRecentCreateEvent(string fullPath, TimeSpan timeThreshold)
        {
            if (
                _lastCreateEventByFile.TryGetValue(
                    fullPath,
                    out FileSystemEventRecord lastCreateEvent
                )
                && lastCreateEvent != null
            )
            {
                // Note: The timestamps are going to be too far apart if it got stopped in the debugger, but...
                // I don't know how to get the timestamps onto this earlier.
                DateTime now = DateTime.Now;
                if (now - lastCreateEvent.Timestamp <= timeThreshold)
                {
                    lastCreateEvent.Timestamp = now; // Update the time while the file is still being modified
                    return true;
                }
            }

            return false;
        }

        protected virtual void OnCollectionFilesChanged(object sender, FileSystemEventArgs e)
        {
            // This prevents most notifications while we are doing updates ourselves.
            if (_updatingCollectionFiles)
                return;
            // To prevent any notifications of our own updates that might arrive after we
            // turn off _updatingCollectionFiles, we take advantage of the fact that before
            // we turn it off we write a modify time record. If the files haven't actually
            // been modified since then, we can ignore the change. This seems simpler and
            // more reliable than trying to track what files we actually wrote how recently.
            if (GetRepoCollectionFilesUpdatedSinceSync())
                RaiseRepoCollectionFilesChanged();
        }

        protected virtual void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (CheckOwnWriteNotification(e.FullPath))
                return;

            if (CheckRecentCreateEvent(e.FullPath, new TimeSpan(0, 0, 1)))
                return;

            RaiseBookStateChange(Path.GetFileName(e.Name));
        }

        /// <summary>
        /// Delete the indicated book from the repo (if it's there...not a problem if it's
        /// only local).
        /// </summary>
        /// <param name="bookFolderPath"></param>
        /// <param name="makeTombstone">May be set false if we don't want to remember that
        /// a book was deleted here. This was previously desirable in the case of a rename, but
        /// those are now handled using RenameBookInRepo, so currently we never pass false.</param>
        public override void DeleteBookFromRepo(string bookFolderPath, bool makeTombstone = true)
        {
            var pathToBookFileInRepo = GetPathToBookFileInRepo(Path.GetFileName(bookFolderPath));
            // The test here is mostly unnecessary, since Delete won't throw if the file doesn't exist
            // (as indeed it might not, even after the test, in a rare race condition with someone else
            // deleting it, or if this is called as part of MoveBookToCollection). It does serve to make sure at least the containing folder exists, which
            // WOULD cause an exception if by any chance it did not.
            if (RobustFile.Exists(pathToBookFileInRepo))
                RobustFile.Delete(pathToBookFileInRepo);
            if (makeTombstone)
            {
                var pathForTombstone = GetPathForTombstone(Path.GetFileName(bookFolderPath));
                if (pathForTombstone != null)
                {
                    RobustFile.WriteAllText(
                        pathForTombstone,
                        "This file marks the deletion of a book previously in the collection"
                    );
                }
            }
        }

        public override void RenameBookInRepo(string newBookFolderPath, string oldName)
        {
            var oldLocalPath = Path.Combine(Path.GetDirectoryName(newBookFolderPath), oldName);
            var pathToOldBookFileInRepo = GetPathToBookFileInRepo(oldLocalPath);
            var pathToNewBookFileInRepo = GetPathToBookFileInRepo(newBookFolderPath);
            // There is probably some pathological case where pathToNewBookFileInRepo already exists,
            // but I can't think of a decent way to handle it, so just let it fail.
            RobustFile.Move(pathToOldBookFileInRepo, pathToNewBookFileInRepo);
        }

        protected virtual void OnCreated(object sender, FileSystemEventArgs e)
        {
            var createEvent = new FileSystemEventRecord(e);
            _lastCreateEventByFile[e.FullPath] = createEvent;

            if (CheckOwnWriteNotification(e.FullPath))
                return;

            RaiseNewBook(Path.GetFileName(e.Name));
        }

        // I'm not sure this can even happen with DropBox and remote users. But team collection could just
        // involve a local shared folder, or something local might do a rename...?
        private void OnRenamed(object sender, FileSystemEventArgs e)
        {
            // Note: if needed, e should be able to be successfully cast to RenamedEventArgs
            // But this type is listed as FileSystemEventArgs due to make life easier for FileSystemWatcherExtensions DebounceRenamed.

            // No obvious renames in our PutBook, but in fact SharpZipLib makes a temp file
            // by appending to our path, and then renames it, so we can get spurious ones.
            if (CheckOwnWriteNotification(e.FullPath))
                return;

            RaiseBookStateChange(Path.GetFileName(e.Name));
            // Perhaps we should also do something about e.OldName? We don't want to
            // bother the user with two notifications. But it is (pathologically)
            // possible the user is editing the original file. I think it will still
            // get cleaned up on next restart, though, unless the user ignores the
            // warning and checks in before restarting.
        }

        protected internal override void StopMonitoring()
        {
            if (_booksWatcher != null)
            {
                _booksWatcher.EnableRaisingEvents = false;
                _booksWatcher.Dispose();
                _booksWatcher = null;
            }

            if (_otherWatcher != null)
            {
                _otherWatcher.EnableRaisingEvents = false;
                _otherWatcher.Dispose();
                _otherWatcher = null;
            }

            base.StopMonitoring();
        }

        /// <summary>
        /// Get the raw (JSON) string that stores the status information. Currently stored
        /// in the zip file comment.
        /// </summary>
        /// <remarks>Needs to be thread-safe</remarks>
        protected override string GetBookStatusJsonFromRepo(string bookFolderName)
        {
            var bookPath = GetPathToBookFileInRepo(bookFolderName);
            if (!RobustFile.Exists(bookPath))
            {
                return null;
            }

            return RobustZip.GetComment(bookPath);
        }

        /// <summary>
        /// needs to be thread-safe
        /// </summary>
        protected override bool TryGetBookStatusJsonFromRepo(
            string bookFolderName,
            out string status,
            bool reportFailure = true
        )
        {
            try
            {
                status = GetBookStatusJsonFromRepo(bookFolderName);
                return true;
            }
            catch (Exception e)
                when (e is ICSharpCode.SharpZipLib.Zip.ZipException || e is IOException)
            {
                if (reportFailure)
                    MessageLog.WriteMessage(
                        MessageAndMilestoneType.ErrorNoReload,
                        "",
                        GetBadZipFileMessage(bookFolderName)
                    );
                status = null;
                return false;
            }
        }

        /// <summary>
        /// Return true if the book exists in the repo.
        /// </summary>
        public override bool IsBookPresentInRepo(string bookFolderName)
        {
            var bookPath = GetPathToBookFileInRepo(bookFolderName);
            return RobustFile.Exists(bookPath);
        }

        public class CannotLockException : Exception
        {
            public CannotLockException(string msg)
                : base(msg) { }

            public string SyncAgent;
        }

        /// <summary>
        /// Write the raw (JSON) string that stores the status information. Currently stored
        /// in the zip file comment.
        /// </summary>
        protected override void WriteBookStatusJsonToRepo(string bookName, string status)
        {
            var bookPath = GetPathToBookFileInRepo(bookName);
            if (!RobustFile.Exists(bookPath))
            {
                throw new ArgumentException("trying to write status on a book not in the repo");
            }

            // This is a low-level check, mainly to handle the case where the book is locked in Dropbox.
            // Locking is supposed to prevent writes and creation of conflict files. However, something
            // about how our zip library updates comments instead results in creating a conflict...
            // every time we try to check it out. This check is enough to prevent that (unless the lock
            // happens at exactly the wrong instant between when we check and when we write the status).
            // Enhance: if we want to support this case, it would be much nicer to check at a higher level
            // and have a new state of the BookStatusPanel indicating that the TC version of the book is
            // locked in a non-standard way (i.e., not checked out, but still not writeable). Possibly
            // a new color for the state circle, too. But we want to DIScourage people from using file
            // locking to achieve something that our Checkout mechanism is designed to handle. Throwing
            // this argument exception puts the TC in the "problems encountered" state with a rather
            // cryptic message in the dialog box, and no change in the book status panel. But at least
            // we are not cluttering the TC with conflicts.
            if (RobustIO.IsFileLocked(bookPath))
            {
                var isDropbox = DropboxUtils.IsPathInDropboxFolder(bookPath);
                var msg =
                    $"Bloom was not able to modify {bookName} because some other program is busy with it. "
                    + (isDropbox ? "This may just be Dropbox synchronizing the file. " : "")
                    + "Please try again later. If the problem continues, restart your computer.";
                throw new CannotLockException(msg)
                {
                    SyncAgent = isDropbox ? @"Dropbox" : "Unknown"
                };
            }
            lock (_lockObject)
            {
                _lastWriteBookPath = bookPath;
                _writeBookInProgress = true;
                _lastWriteBookTime = DateTime.Now;
            }

            // We've had some failures on very fast clicking of Checkin/Checkout.
            // Not clear how they come to overlap, but it's worth just trying again
            // as a recovery strategy.
            RobustZip.WriteZipComment(status, bookPath);
            lock (_lockObject)
            {
                _writeBookInProgress = false;
            }
        }

        /// <summary>
        /// Used at program startup to decide whether the command line arguments represent
        /// opening a file that triggers joining a team collection.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static bool IsJoinTeamCollectionFile(string[] args)
        {
            return args.Length == 1 && args[0].EndsWith(".JoinBloomTC");
        }

        /// <summary>
        /// Set up a team collection created from the existing local collection in the specified
        /// (typically newly created) folder (and links the local collection to it so it becomes a TC).
        /// We set it up with all the files it needs to have, including any books that already exist locally.
        /// </summary>
        /// <param name="repoFolder"></param>
        public void SetupTeamCollection(string repoFolder, IWebSocketProgress progress)
        {
            _repoFolderPath = repoFolder;
            progress.Message("SettingUpCore", "Setting up the core team collection files");
            CreateJoinCollectionFile();
            CreateTeamCollectionLinkFile(_localCollectionFolder, repoFolder);
            CopyRepoCollectionFilesFromLocal(_localCollectionFolder);
            // The new TC now has the current collection-level files. But a couple of things might try
            // to copy them again: we do a sync when closing down the collection, as we will shortly
            // do in order to re-open it as a TC. And then the idle loop sync might fire, too.
            // In at least one case (BL-9902), we had a crash when rewriting the collection settings zip
            // file failed due to a file lock, and at best it wastes time. So this collection,
            // which is about to go away, can just stop doing this sort of sync.
            _stopSyncingCollectionFiles = true;
            Directory.CreateDirectory(Path.Combine(repoFolder, "Books"));
            SynchronizeBooksFromLocalToRepo(progress);
            StartMonitoring();
        }

        /// <summary>
        /// Wraps SetupTeamCollection() with a dialog showing progress.
        /// </summary>
        /// <param name="repoFolder"></param>
        public void SetupTeamCollectionWithProgressDialog(string repoFolder)
        {
            var title = "Setting Up Team Collection"; // todo l10n
            ShowProgressDialog(
                title,
                (progress, worker) =>
                {
                    progress.Message(
                        "StartingCopy",
                        "",
                        "Starting to set up the Team Collection",
                        ProgressKind.Progress
                    );

                    SetupTeamCollection(repoFolder, progress);

                    progress.Message("Done", "Done");
                    return false; // always close dialog when done

                    // Review: I (JH) notice that the TeamCollection.SynchronizeRepoAndLocal() version of this
                    // returns true if an error was found. Why doesn't this one?
                }
            );
        }

        private static string _joinCollectionPath; // when joining a TC, the path to the repo we're joining
        private static string _joinCollectionName; // when joining a TC, the collection name derived from the temporary Settings object.
        private static string _newCollectionToJoin;
        private static bool _joiningSameCollection; // when joining a TC, and a corresponding local directory already exists, is it the same collection as we're joining?

        // Create a new local collection from the team collection at the specified path.
        // Return the path to its settings (not team settings) file...the path we need to
        // open the new collection. This is the method that gets called when we open a
        // JoinTeamCollection file. The tcManager passed is a temporary one created by
        // syncing the repo and its settings to a temporary folder.
        public static string ShowJoinCollectionTeamDialog(
            string path,
            TeamCollectionManager tcManager
        )
        {
            if (!PromptForSufficientRegistrationIfNeeded())
                return null;

            _joinCollectionPath = path;
            _newCollectionToJoin = null; // set if JoinCollectionTeam called successfully
            var repoFolder = Path.GetDirectoryName(path);
            _joinCollectionName = tcManager.Settings.CollectionName;
            if (_joinCollectionName == "projectName")
            {
                // This is what comes up when the TC has no zipped settings file.
                // We MIGHT get a useful name from the parent folder.
                // (It doesn't matter much because in any case we don't have enough
                // of a collection to join.)
                _joinCollectionName = Path.GetFileName(repoFolder);
            }
            var localCollectionFolder = Path.Combine(
                NewCollectionWizard.DefaultParentDirectoryForCollections,
                _joinCollectionName
            );
            var isExistingCollection = Directory.Exists(localCollectionFolder);
            var tcLinkPath = TeamCollectionManager.GetTcLinkPathFromLcPath(localCollectionFolder);
            var isAlreadyTcCollection = isExistingCollection && RobustFile.Exists(tcLinkPath);
            var repoFolderPathFromLinkPath = isAlreadyTcCollection
                ? TeamCollectionManager.RepoFolderPathFromLinkPath(tcLinkPath)
                : "";
            var isCurrentCollection =
                isAlreadyTcCollection && repoFolderPathFromLinkPath == repoFolder;
            var joiningGuid = CollectionSettings.CollectionIdFromCollectionFolder(
                tcManager.CurrentCollection.LocalCollectionFolder
            );
            var localGuid = CollectionSettings.CollectionIdFromCollectionFolder(
                localCollectionFolder
            );
            var isSameCollection = _joiningSameCollection = joiningGuid == localGuid;
            // If it's a different collection and associated with a TC that exists, we're going to
            // not allow the user to join. But if the TC doesn't exist...we'll let them just go
            // ahead and merge, as if it was never linked.
            if (!isSameCollection && !Directory.Exists(repoFolderPathFromLinkPath))
                isAlreadyTcCollection = false;

            using (
                var dlg = new ReactDialog(
                    "joinTeamCollectionDialogBundle",
                    new
                    {
                        missingTcPieces = MissingTcPieces(path),
                        collectionName = _joinCollectionName,
                        existingCollection = isExistingCollection,
                        isAlreadyTcCollection,
                        isCurrentCollection,
                        isSameCollection,
                        existingCollectionFolder = localCollectionFolder,
                        conflictingCollection = repoFolderPathFromLinkPath,
                        joiningRepo = repoFolder,
                        joiningGuid,
                        localGuid
                    },
                    "Join Team Collection"
                )
            )
            {
                dlg.Width = 560;
                dlg.Height = 400;
                // This dialog is neater without a task bar. We don't need to be able to
                // drag it around. There's nothing left to give it one if we don't set a title
                // and remove the control box.
                dlg.ControlBox = false;
                dlg.ShowDialog();
            }

            // Unless the user canceled, this will have been set in JoinCollectionTeam()
            // before the dialog closes.
            return _newCollectionToJoin;
        }

        public static string MissingTcPieces(string joinCollectionPath)
        {
            var repoFolder = Path.GetDirectoryName(joinCollectionPath);
            var result = "";
            if (!Directory.Exists(GetPathToBookFolder(repoFolder)))
            {
                result += "book folder at " + repoFolder;
            }

            var repoProjectFilesZipPath = GetRepoProjectFilesZipPath(repoFolder);
            if (!RobustFile.Exists(repoProjectFilesZipPath))
            {
                if (result.Length > 0)
                    result += " and ";
                result += "project files zip at " + repoProjectFilesZipPath;
            }

            return result;
        }

        /// <summary>
        /// Called when the user clicks the Join{ and Merge} button in the dialog.
        /// </summary>
        /// <returns>an indication of the type of join for analytics</returns>
        public static string JoinCollectionTeam()
        {
            var repoFolder = Path.GetDirectoryName(_joinCollectionPath);
            var localCollectionFolder = Path.Combine(
                NewCollectionWizard.DefaultParentDirectoryForCollections,
                _joinCollectionName
            );
            var firstTimeJoin = true; // default assumption
            var result = "create";
            if (Directory.Exists(localCollectionFolder)) // if not, no merging, so value of firstTimeJoin doesn't matter.
            {
                result = "merge";
                var tcLinkPath = TeamCollectionManager.GetTcLinkPathFromLcPath(
                    localCollectionFolder
                );
                if (RobustFile.Exists(tcLinkPath))
                {
                    // it thinks it's already part of a TC. (If it doesn't, even though it is the same collection
                    // ID, we want a first time join; maybe the local copy has existed independently for some
                    // time and contains extra books we want to merge.)
                    if (_joiningSameCollection)
                    {
                        // It's basically the same collection...the user joined a second time, either
                        // by mistake, or to fix things up after it moved. So we want to sync normally,
                        // not as if we're merging collections.
                        firstTimeJoin = false;
                        result = "open";
                    }
                }
            }
            // Most of the collection settings files will be copied later when we create the repo
            // in TeamRepo.MakeInstance() and call CopyRepoCollectionFilesToLocal.
            // However, when we start up with a command line argument that causes JoinCollectionTeam,
            // the next thing we do is push the newly created project into our MRU list so it will
            // be the one that gets opened. The MRU list refuses to add a bloomCollection that doesn't
            // exist; so we have to make it exist.
            _newCollectionToJoin = SetupMinimumLocalCollectionFilesForRepo(
                repoFolder,
                localCollectionFolder
            );
            // Soon we will open the new collection, and do a SyncAtStartup. We want that to have some
            // special behavior, but only if joining for the first time.
            if (firstTimeJoin)
                TeamCollectionManager.NextMergeIsFirstTimeJoinCollection = true;
            return result;
        }

        /// <summary>
        /// Setup the bare minimum files in localCollectionFolder so that it can join the team collection
        /// in the specified repoFolder. (We could get away without unpacking more than the .bloomCollection
        /// file, but we'll want the others soon, and typically it's not a lot.)
        /// </summary>
        public static string SetupMinimumLocalCollectionFilesForRepo(
            string repoFolder,
            string localCollectionFolder
        )
        {
            Directory.CreateDirectory(localCollectionFolder);
            CreateTeamCollectionLinkFile(localCollectionFolder, repoFolder);
            CopyRepoCollectionFilesTo(localCollectionFolder, repoFolder);
            return CollectionPath(localCollectionFolder);
        }

        public void CreateJoinCollectionFile()
        {
            var joinCollectionPath = Path.Combine(
                _repoFolderPath,
                "Join this Team Collection.JoinBloomTC"
            );
            // Don't think this needs to be localized. It's not really meant to be seen, just to provide some clue if anyone
            // is curious about this file.
            RobustFile.WriteAllText(
                joinCollectionPath,
                @"Double click this file (after installing Bloom 5.0 or later) to join this Team Collection. "
                    + @"You can rename this file but must keep the extension the same."
            );
        }

        public static void CreateTeamCollectionLinkFile(
            string collectionFolder,
            string teamCollectionFolder
        )
        {
            var teamCollectionLinkPath = Path.Combine(
                collectionFolder,
                TeamCollectionManager.TeamCollectionLinkFileName
            );
            RobustFile.WriteAllText(teamCollectionLinkPath, teamCollectionFolder);
        }

        /// <summary>
        /// Returns null if connection is fine, otherwise, a message describing the problem.
        /// </summary>
        public override TeamCollectionMessage CheckConnection()
        {
            if (!Directory.Exists(_repoFolderPath))
            {
                return new TeamCollectionMessage(
                    MessageAndMilestoneType.Error,
                    "TeamCollection.MissingRepo",
                    "Bloom could not find the Team Collection folder at '{0}'. If that drive or network is disconnected, re-connect it. If you have moved where that folder is located, 1) quit Bloom 2) go to the Team Collection folder and double-click \"Join this Team Collection\".",
                    _repoFolderPath
                );
            }

            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {
                return new TeamCollectionMessage(
                    MessageAndMilestoneType.Error,
                    "TeamCollection.NoNetwork",
                    "No network is available on this computer."
                );
            }

            var isOnLocalNetwork = IsFolderOnLocalNetwork(_repoFolderPath);
            if (DropboxUtils.IsPathInDropboxFolder(_repoFolderPath))
            {
                if (!DropboxUtils.IsDropboxProcessRunning())
                {
                    if (isOnLocalNetwork)
                        _tcManager.MessageLog.WriteMessage(
                            MessageAndMilestoneType.History,
                            "TeamCollection.NeedDropboxRunningButLANOK",
                            "Dropbox does not appear to be running, but the folder has also been shared locally which appears to be okay."
                        );
                    else
                        return new TeamCollectionMessage(
                            MessageAndMilestoneType.Error,
                            "TeamCollection.NeedDropboxRunning",
                            "Dropbox does not appear to be running. Please see [Troubleshooting Dropbox](https://docs.bloomlibrary.org/dropbox-trouble)."
                        );
                }

                if (!DropboxUtils.CanAccessDropbox())
                {
                    if (isOnLocalNetwork)
                        _tcManager.MessageLog.WriteMessage(
                            MessageAndMilestoneType.History,
                            "TeamCollection.NeedDropboxAccessButLANOK",
                            "Bloom cannot reach Dropbox.com, but the folder has also been shared locally which appears to be okay."
                        );
                    else
                        return new TeamCollectionMessage(
                            MessageAndMilestoneType.Error,
                            "TeamCollection.NeedDropboxAccess",
                            "Bloom cannot reach Dropbox.com."
                        );
                }
            }

            return null;
        }

        private bool IsFolderOnLocalNetwork(string repoFolderPath)
        {
            try
            {
#if __MonoCS__
                // This will require more research and work...
                // Not sure how to tell if a folder has been shared to the network.  May have to be samba / NFS / ... specific.
                // Possibly call "/bin/df -T" and parse output for file system type for folders shared from network?
#else
                // Check whether the repo folder exists inside a *local* folder we've shared to the local network.
                // 8 Dec 2021:  (BL-10704) We are ignoring the machine from which the folder is shared for
                // now, since we believe that the standard use case for local network sharing is to have a
                // separate machine hosting the repo that never runs Bloom.
                //var repoFolderLower = repoFolderPath.ToLowerInvariant();
                //var searcher = new System.Management.ManagementObjectSearcher("select * from win32_share");
                //foreach (var share in searcher.Get())
                //{
                //	string type = share["Type"].ToString();
                //	if (type == "0") // 0 = DiskDrive (1 = Print Queue, 2 = Device, 3 = IPH)
                //	{
                //		var name = share["Name"].ToString(); //getting share name
                //		var path = share["Path"].ToString(); //getting share path
                //		if (name.EndsWith("$"))
                //			continue;	// skip system shares like print$ or C$
                //		if (Directory.Exists(path))
                //		{
                //			The problem was here where "C:/Users" was shared on most machines, so this
                //			always returned true.
                //			if (repoFolderLower.StartsWith(path.ToLowerInvariant()+"\\"))
                //				return true;
                //		}
                //	}
                //}
                // Check whether the repo folder is actually one that has been shared *from elsewhere* on the local network.
                // The RE matches paths like "\\this\is\a\test of a\network path" or "\\computer\C$"
                // The alternation is needed to allow for single-character elements in the path as
                // well as path elements that can contain, but not begin or end with, spaces. (The original RE was from
                // https://social.msdn.microsoft.com/Forums/vstudio/en-US/31d2bc84-c948-4914-8a9d-97b9e788b341/validate-a-network-folder-path?forum=csharpgeneral.)
                if (
                    Regex
                        .Match(
                            repoFolderPath,
                            @"^\\{2}[\w-]+(\\{1}(([\w-][\w\-\s]*[\w-]+[$$]?)|([\w-][$$]?)))+"
                        )
                        .Success
                )
                    return true;
                // Network folders can also be mapped to a drive letter.  Check for this situation.
                if (Regex.Match(repoFolderPath, "^[A-Za-z]:").Success)
                {
                    var di = new DriveInfo(repoFolderPath);
                    if (di.DriveType == DriveType.Network)
                        return true;
                }
#endif
            }
            catch (Exception e)
            {
                Debug.WriteLine(
                    $"Caught exception in IsFolderOnLocalNetwork(\"{repoFolderPath}\"): {e}"
                );
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"Caught exception in IsFolderOnLocalNetwork(\"{repoFolderPath}\")"
                );
            }
            return false;
        }

        public override string GetBackendType()
        {
            if (!string.IsNullOrEmpty(RepoFolderPath))
            {
                if (DropboxUtils.IsPathInDropboxFolder(RepoFolderPath))
                    return "DropBox";
                if (
                    RepoFolderPath.StartsWith("\\\\", StringComparison.InvariantCulture)
                    || RepoFolderPath.StartsWith("//", StringComparison.InvariantCulture)
                    || new DriveInfo(Path.GetPathRoot(RepoFolderPath)).DriveType
                        == DriveType.Network
                )
                    return "LAN"; // probably works only on Windows
            }
            return "Other";
        }
    }
}
