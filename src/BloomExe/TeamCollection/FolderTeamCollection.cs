using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Bloom.CollectionCreating;
using Bloom.MiscUI;
using Bloom.Utils;
using ICSharpCode.SharpZipLib.Zip;
using SIL.IO;

namespace Bloom.TeamCollection
{
	/// <summary>
	/// Implementation of a team collection repository implemented as a shared (herein called repo) folder.
	/// As far as possible we are attempting to keep behavior that is specific to
	/// the folder implementation here, while behavior that is independent of how
	/// the shared repo is stored should be in TeamRepo.cs.
	/// </summary>
	public class FolderTeamCollection: TeamCollection
	{
		private string _repoFolderPath; // the (presumably somehow shared) folder storing the repo
		private FileSystemWatcher _watcher; // watches the _repoFolderPath for changes

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
		
		public FolderTeamCollection(ITeamCollectionManager manager, string localCollectionFolder, string repoFolderPath) : base(manager, localCollectionFolder)
		{
			_repoFolderPath = repoFolderPath;
		}

		public string RepoFolderPath => _repoFolderPath;

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
		protected override void PutBookInRepo(string sourceBookFolderPath, BookStatus status,
			bool inLostAndFound = false)
		{
			var bookFolderName = Path.GetFileName(sourceBookFolderPath);
			var bookPath = GetPathToBookFileInRepo(bookFolderName);

			if (inLostAndFound)
			{
				var lfPath = Path.Combine(_repoFolderPath, "Lost and Found");
				Directory.CreateDirectory(lfPath);
				int counter = 0;
				do
				{
					counter++;
					bookPath = Path.ChangeExtension(
						Path.Combine(lfPath, bookFolderName + (counter == 1 ? "" : counter.ToString())), ".bloom");
				} while (RobustFile.Exists(bookPath));
			}
			else
			{
				// Make sure the repo directory that holds books exists
				Directory.CreateDirectory(Path.GetDirectoryName(bookPath));
			}

			lock (_lockObject)
			{
				_lastWriteBookPath = bookPath;
				_writeBookInProgress = true;
			}

			var zipFile = new BloomZipFile(bookPath);
			zipFile.AddDirectory(sourceBookFolderPath, sourceBookFolderPath.Length + 1, null);
			zipFile.SetComment(status.ToJson());
			zipFile.Save();
			lock (_lockObject)
			{
				_lastWriteBookTime = DateTime.Now;
				_writeBookInProgress = false;
			}
		}

		internal string GetPathToBookFileInRepo(string bookFolderName)
		{
			return Path.ChangeExtension(Path.Combine(_repoFolderPath, "Books", bookFolderName), ".bloom");
		}

		public override void RemoveBook(string bookName)
		{
			var path = GetPathToBookFileInRepo(bookName);
			RobustFile.Delete(path);
		}

		/// <summary>
		/// Return a list of all the books currently in the repo. (It will not update as changes are made,
		/// either locally or remotely. Beware that conceivably a book in the list might be removed
		/// before you get around to processing it.)
		/// </summary>
		/// <returns></returns>
		public override string[] GetBookList()
		{
			return Directory.EnumerateFiles(Path.Combine(_repoFolderPath, "Books"), "*.bloom")
				.Select(path => Path.GetFileNameWithoutExtension(path)).ToArray();
		}

		/// <summary>
		/// The shared-folder-specific part of the public GetBook method in TeamRepo.
		/// </summary>
		/// <param name="destinationCollectionFolder">Where to put the retrieved book folder,
		/// typically the local collection folder.</param>
		/// <param name="bookName">The name of the book, with or without the .bloom suffix - either way is fine</param>
		protected override void FetchBookFromRepo(string destinationCollectionFolder, string bookName)
		{
			var bookPath = GetPathToBookFileInRepo(bookName);
			byte[] buffer = new byte[4096];     // 4K is optimum
			try
			{
				using (var zipFile = new ZipFile(bookPath))
				{
					var destFolder = Path.Combine(destinationCollectionFolder, bookName);
					foreach (ZipEntry entry in zipFile)
					{
						var fullOutputPath = Path.Combine(destFolder, entry.Name);
						if (entry.IsDirectory)
						{
							Directory.CreateDirectory(fullOutputPath);
							// In the SharpZipLib code, IsFile and IsDirectory are not defined exactly as inverse: a third
							// (or fourth) type of entry might be possible.  In practice in .bloom files, this should not be
							// an issue.
							continue;
						}

						var directoryName = Path.GetDirectoryName(fullOutputPath);
						if (!String.IsNullOrEmpty(directoryName))
							Directory.CreateDirectory(directoryName);
						using (var instream = zipFile.GetInputStream(entry))
						using (var writer = RobustFile.Create(fullOutputPath))
						{
							ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(instream, writer, buffer);
						}
					}
				}
			}
			catch (Exception e) when (e is ZipException || e is IOException)
			{
				NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom could not unpack a file in your Team Collection: " + bookPath, exception: e);
			}
		}

		public override void PutCollectionFiles(string[] names)
		{
			var destPath = GetRepoProjectFilesZipPath(_repoFolderPath);
			Directory.CreateDirectory(Path.GetDirectoryName(destPath));
			var zipFile = new BloomZipFile(destPath);
			foreach (var name in names)
			{
				var path = Path.Combine(_localCollectionFolder, name);
				if (!File.Exists(path))
					continue;
				zipFile.AddTopLevelFile(path, true);
			}

			zipFile.Save();
		}

		protected override DateTime LastRepoCollectionFileModifyTime
		{
			get
			{
				var repoProjectFilesZipPath = GetRepoProjectFilesZipPath(_repoFolderPath);
				if (!File.Exists(repoProjectFilesZipPath))
					return DateTime.MinValue; // brand new repo, want to copy TO it.
				var collectionFilesModTime = new FileInfo(repoProjectFilesZipPath).LastWriteTime;
				GetMaxModifyTime("Allowed Words", ref collectionFilesModTime);
				GetMaxModifyTime("Sample Texts", ref collectionFilesModTime);
				return collectionFilesModTime;
			}
		}

		private void GetMaxModifyTime(string folderName, ref DateTime max)
		{
			var zipPath =Path.Combine(_repoFolderPath, "Other", Path.ChangeExtension(folderName,"zip"));
			if (File.Exists(zipPath))
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
			Directory.CreateDirectory(Path.GetDirectoryName(destPath));
			var zipFile = new BloomZipFile(destPath);
			foreach (var path in Directory.EnumerateFiles(sourceDir))
			{
				zipFile.AddTopLevelFile(path);
			}

			zipFile.Save();
		}

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
		/// Copy collection level files from the repo to the local directory
		/// </summary>
		/// <param name="destFolder"></param>
		protected override void CopyRepoCollectionFilesToLocalImpl(string destFolder)
		{
			CopyRepoCollectionFilesTo(destFolder, _repoFolderPath);
			ExtractFolder(destFolder, _repoFolderPath, "Allowed Words");
			ExtractFolder(destFolder, _repoFolderPath, "Sample Texts");
		}

		private static void CopyRepoCollectionFilesTo(string destFolder, string repoFolder)
		{
			var collectionZipPath = GetRepoProjectFilesZipPath(repoFolder);
			if (!RobustFile.Exists(collectionZipPath))
				return;
			ExtractFolderFromZip(destFolder, collectionZipPath, () => new HashSet<string>(RootLevelCollectionFilesIn(destFolder,
				Path.GetFileNameWithoutExtension(GetLocalCollectionNameFromTcName(repoFolder)))));
		}

		// Extract files from the given zip to the given destination folder. Delete any files in the destFolder which
		// are identified by filesToDeleteIfNotInZip() and which were not found in the zip file.
		private static void ExtractFolderFromZip(string destFolder, string collectionZipPath, Func<HashSet<string>> filesToDeleteIfNotInZip)
		{
			byte[] buffer = new byte[4096]; // 4K is optimum
			try
			{
				var filesInZip = new HashSet<string>();
				using (var zipFile = new ZipFile(collectionZipPath))
				{
					foreach (ZipEntry entry in zipFile)
					{
						filesInZip.Add(entry.Name);
						var fullOutputPath = Path.Combine(destFolder, entry.Name);

						var directoryName = Path.GetDirectoryName(fullOutputPath);
						if (!String.IsNullOrEmpty(directoryName))
							Directory.CreateDirectory(directoryName);
						using (var instream = zipFile.GetInputStream(entry))
						using (var writer = RobustFile.Create(fullOutputPath))
						{
							ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(instream, writer, buffer);
						}
					}
				}

				// Remove any sharing-eligible files that are NOT in the zip
				var filesToDelete = filesToDeleteIfNotInZip();
				filesToDelete.ExceptWith(filesInZip);
				foreach (var discard in filesToDelete)
				{
					RobustFile.Delete(Path.Combine(destFolder, discard));
				}
			}
			catch (Exception e) when (e is ZipException || e is IOException)
			{
				NonFatalProblem.Report(ModalIf.All, PassiveIf.All,
					"Bloom could not unpack the collection files in your Team Collection");
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
			if (!File.Exists(sourceZip))
				return;
			var destFolder = Path.Combine(collectionFolder, folderName);
			ExtractFolderFromZip(destFolder, sourceZip,
				() => new HashSet<string>(Directory.EnumerateFiles(destFolder).Select(p => Path.GetFileName(p))));
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
			_watcher = new FileSystemWatcher();

			_watcher.Path = Path.Combine(_repoFolderPath, "Books");

			// Enhance: maybe one day we want to watch collection files too?

			// Watch for changes in LastWrite times, and
			// the renaming of files or directories.
			_watcher.NotifyFilter =  NotifyFilters.LastWrite
			                       | NotifyFilters.FileName
			                       | NotifyFilters.DirectoryName;

			const int debouncePeriodInMs = 100;
			_watcher.DebounceChanged(OnChanged, debouncePeriodInMs);
			_watcher.DebounceCreated(OnCreated, debouncePeriodInMs);
			_watcher.DebounceRenamed(OnRenamed, debouncePeriodInMs);

			// I think if the book was deleted we can afford to wait and let the next restart clean it up.
			// _watcher.DebounceDeleted(OnChanged, debouncePeriodInMs);

			// Begin watching.
			_watcher.EnableRaisingEvents = true;
		}

		private bool CheckOwnWriteNotification(string path)
		{
			lock (_lockObject)
			{
				// Not the book we most recently wrote, so not an 'own write'.
				// Note that our zip library sometimes creates a temp file by adding a suffix to the
				// path, so it's very likely that a recent write of a path starting with the name of the book we
				// wrote is a result of that.
				if (!string.IsNullOrWhiteSpace(_lastWriteBookPath) && !path.StartsWith(_lastWriteBookPath))
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

		protected virtual void OnChanged(object sender, FileSystemEventArgs e)
		{
			if (CheckOwnWriteNotification(e.FullPath))
				return;

			RaiseBookStateChange(Path.GetFileName(e.Name));
		}

		protected virtual void OnCreated(object sender, FileSystemEventArgs e)
		{
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

			// No renames in our PutBook, so we don't need to check for that here.
			RaiseBookStateChange(Path.GetFileName(e.Name));
			// Perhaps we should also do something about e.OldName? We don't want to
			// bother the user with two notifications. But it is (pathologically)
			// possible the user is editing the original file. I think it will still
			// get cleaned up on next restart, though, unless the user ignores the
			// warning and checks in before restarting.
		}

		protected internal override void StopMonitoring()
		{
			_watcher.EnableRaisingEvents = false;
			_watcher.Dispose();
			base.StopMonitoring();
		}

		/// <summary>
		/// Get the raw (JSON) string that stores the status information. Currently stored
		/// in the zip file comment.
		/// </summary>
		protected override string GetBookStatusJsonFromRepo(string bookFolderName)
		{
			var bookPath = GetPathToBookFileInRepo(bookFolderName);
			if (!RobustFile.Exists(bookPath))
			{
				return null;
			}
			using (var zipFile = new ZipFile(bookPath))
			{
				return zipFile.ZipFileComment;
			}
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
			lock (_lockObject)
			{
				_lastWriteBookPath = bookPath;
				_writeBookInProgress = true;
				_lastWriteBookTime = DateTime.Now;
			}
			using (var zipFile = new ZipFile(bookPath))
			{
				zipFile.BeginUpdate();
				zipFile.SetComment(status);
				zipFile.CommitUpdate();
			}
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
		/// Used when the user asks to create a team collection from the existing local collection.
		/// Assumes only that the folder we want to connect to exists (and, at least for now, expects
		/// it to have nothing else in it). We set it up with all the files it needs to have,
		/// including any books that already exist locally.
		/// </summary>
		/// <param name="repoFolder"></param>
		public void ConnectToTeamCollection(string repoFolder)
		{
			_repoFolderPath = repoFolder;
			CreateJoinCollectionFile();
			CreateTeamCollectionSettingsFile(_localCollectionFolder, repoFolder);
			CopyRepoCollectionFilesFromLocal(_localCollectionFolder);
			Directory.CreateDirectory(Path.Combine(repoFolder, "Books"));
			SynchronizeBooksFromLocalToRepo();
			StartMonitoring();
		}

		private static string _joinCollectionPath;
		private static string _newCollectionToJoin;

		// Create a new local collection from the team collection at the specified path.
		// Return the path to its settings (not team settings) file...the path we need to
		// open the new collection. This is the method that gets called when we open a
		// JoinTeamCollection file.
		public static string ShowJoinCollectionTeamDialog(string path)
		{
			_joinCollectionPath = path;
			_newCollectionToJoin = null; // set if JoinCollectionTeam called successfully
			var repoFolder = Path.GetDirectoryName(path);
			var collectionName = GetLocalCollectionNameFromTcName(Path.GetFileName(repoFolder));
			var localCollectionFolder =
				Path.Combine(NewCollectionWizard.DefaultParentDirectoryForCollections, collectionName);
			var url = BloomFileLocator.GetBrowserFile(false, "teamCollection", "NewTeamCollection.html").ToLocalhost()
			          + $"?name={collectionName}";
			if (Directory.Exists(localCollectionFolder))
			{
				url += "&existingCollection=true"; // any 'truthy' value in JS will do
			}

			using (var dlg = new BrowserDialog(url))
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

		/// <summary>
		/// Called when the user clicks the Join{ and Merge} button in the dialog.
		/// </summary>
		public static void JoinCollectionTeam()
		{
			var repoFolder = Path.GetDirectoryName(_joinCollectionPath);
			var collectionName = GetLocalCollectionNameFromTcName(Path.GetFileName(repoFolder));
			var localCollectionFolder =
				Path.Combine(NewCollectionWizard.DefaultParentDirectoryForCollections, collectionName);
			// Most of the collection settings files will be copied later when we create the repo
			// in TeamRepo.MakeInstance() and call CopyRepoCollectionFilesToLocal.
			// However, when we start up with a command line argument that causes JoinCollectionTeam,
			// the next thing we do is push the newly created project into our MRU list so it will
			// be the one that gets opened. The MRU list refuses to add a bloomCollection that doesn't
			// exist; so we have to make it exist.
			_newCollectionToJoin = SetupMinimumLocalCollectionFilesForRepo(repoFolder, localCollectionFolder);
			// Soon we will open the new collection, and do a SyncAtStartup. We want that to have some
			// special behavior.
			TeamCollectionManager.NextMergeIsJoinCollection = true;
		}

		/// <summary>
		/// Setup the bare minimum files in localCollectionFolder so that it can join the team collection
		/// in the specified repoFolder. (We could get away without unpacking more than the .bloomCollection
		/// file, but we'll want the others soon, and typically it's not a lot.)
		/// </summary>
		/// <returns></returns>
		public static string SetupMinimumLocalCollectionFilesForRepo(string repoFolder, string localCollectionFolder)
		{
			Directory.CreateDirectory(localCollectionFolder);
			CreateTeamCollectionSettingsFile(localCollectionFolder, repoFolder);
			CopyRepoCollectionFilesTo(localCollectionFolder, repoFolder);
			return CollectionPath(localCollectionFolder);
		}

		public void CreateJoinCollectionFile()
		{
			var joinCollectionPath = Path.Combine(_repoFolderPath, "Join this Team Collection.JoinBloomTC");
			// Don't think this needs to be localized. It's not really meant to be seen, just to provide some clue if anyone
			// is curious about this file.
			RobustFile.WriteAllText(joinCollectionPath,
				@"Double click this file (after installing Bloom 5.0 or later) to join this Team Collection. "
				+ @"You can rename this file but must keep the extension the same.");
		}

		public static void CreateTeamCollectionSettingsFile(string collectionFolder, string teamCollectionFolder) {
			var doc = new XDocument(
				new XElement("settings",
					new XElement("TeamCollectionFolder",
						new XText(teamCollectionFolder))));
			var teamSettingsPath = Path.Combine(collectionFolder, TeamCollectionManager.TeamCollectionSettingsFileName);
			using (var stream = new FileStream(teamSettingsPath, FileMode.Create))
			{
				using (var writer = new XmlTextWriter(stream, Encoding.UTF8))
				{
					doc.Save(writer);
				}
			}
		}
	}
}
