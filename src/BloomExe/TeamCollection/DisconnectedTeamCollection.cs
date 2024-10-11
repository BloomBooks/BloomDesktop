using System;
using System.IO;
using Bloom.Book;
using SIL.IO;

namespace Bloom.TeamCollection
{
    /// <summary>
    /// Implements a Team Collection that is disconnected. Possibly the folder named in the
    /// TeamCollectionSettings file is not found. Possibly we can determine that it is
    /// a Dropbox folder and either (a) Dropbox is not running, or (b) the Dropbox server
    /// is not accessible. In any case, it's useful to have a TeamCollection object,
    /// but most functions are not actually needed, and will throw if called.
    /// It is also used for Disabled TCs (e.g., Enterprise status expired).
    /// </summary>
    public class DisconnectedTeamCollection : TeamCollection
    {
        public DisconnectedTeamCollection(
            ITeamCollectionManager manager,
            string localCollectionFolder,
            string description,
            TeamCollectionMessageLog tcLog = null
        )
            : base(manager, localCollectionFolder, tcLog)
        {
            RepoDescription = description;
        }

        public bool DisconnectedBecauseNoEnterprise = false;

        // For Moq
        // Alternatively,  you could make it implement an ITeamCollection interface instead.
        public DisconnectedTeamCollection()
            : base(null, "")
        {
            if (!Program.RunningUnitTests)
            {
                throw new ApplicationException(
                    "Parameterless constructor is only for mocking purposes"
                );
            }
        }

        public override bool CanChangeBookInstanceId(BookInfo info)
        {
            // No way to know for sure, so play safe.
            return false;
        }

        public override bool IsDisconnected => true;

        public override TeamCollectionStatus CollectionStatus => TeamCollectionStatus.Disconnected;

        // Currently this is the path to the place where we expected to find the repo folder.
        // Later it might be something else, perhaps a git URL.
        public override string RepoDescription { get; }

        protected override void PutBookInRepo(
            string sourceBookFolderPath,
            BookStatus newStatus,
            bool inLostAndFound = false,
            Action<float> progressCallback = null
        )
        {
            throw new NotImplementedException();
        }

        public override bool KnownToHaveBeenDeleted(string oldName)
        {
            throw new NotImplementedException();
        }

        protected override void MoveRepoBookToLostAndFound(string bookName)
        {
            throw new NotImplementedException();
        }

        public override string GetRepoBookFile(string bookName, string fileName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This might not work for everything, but it works for the one current client,
        /// since SyncAtStartup only uses it to consider whether repo books are missing
        /// or different from what we have locally, and when disconnected, it's fine to
        /// do neither.
        /// </summary>
        /// <returns></returns>
        public override string[] GetBookList()
        {
            return new string[0];
        }

        protected override string FetchBookFromRepo(
            string destinationCollectionFolder,
            string bookName
        )
        {
            throw new NotImplementedException();
        }

        public override void PutCollectionFiles(string[] names)
        {
            throw new NotImplementedException();
        }

        public override void DeleteBookFromRepo(string bookFolderPath, bool makeTombstone = true)
        {
            throw new NotImplementedException();
        }

        public override void RenameBookInRepo(string newBookFolderPath, string oldName)
        {
            throw new NotImplementedException();
        }

        protected override string GetBookStatusJsonFromRepo(string bookFolderName)
        {
            // We're disconnected, so can't really get the status from the repo.
            // But returning a carefully crafted status causes various clients to behave
            // the way we want.
            var localStatusPath = GetStatusFilePath(bookFolderName, _localCollectionFolder);
            // If it doesn't have local status, treat it as not being in the repo, that is, newly
            // created locally.
            if (!RobustFile.Exists(localStatusPath))
                return null;
            var localStatus = GetLocalStatus(bookFolderName);
            // If the local status it has belongs to another collection, that's exactly like not
            // having local status at all...it implies a book that's not present in the repo.
            if (localStatus.collectionId != CollectionId)
                return null;
            // Otherwise, it mostly works to return the local status as the repo status.
            // One exception is that oldName is only ever stored in local status, so it
            // would be pathological to find it in what is supposed to be a repo status.
            // (Of course, if the book has really been renamed, then we should have found no
            // repo status by looking under the new name. But that would lead to looking
            // for it using the old name in GetStatus(), and we typically won't find
            // ANYTHING under that in a disconnected TC. The closest approximation we
            // can get to the unavailable repo status is the local status without the oldName.)
            return localStatus.WithOldName(null).ToJson();
        }

        protected override bool TryGetBookStatusJsonFromRepo(
            string bookFolderName,
            out string status,
            bool reportFailure = true
        )
        {
            status = GetBookStatusJsonFromRepo(bookFolderName);
            return true;
        }

        public override bool IsBookPresentInRepo(string bookFolderName)
        {
            throw new NotImplementedException();
        }

        protected override void WriteBookStatusJsonToRepo(string bookName, string status)
        {
            throw new NotImplementedException();
        }

        protected override void CopyRepoCollectionFilesToLocalImpl(string destFolder)
        {
            throw new NotImplementedException();
        }

        protected override DateTime LastRepoCollectionFileModifyTime { get; }

        protected override void CopyLocalFolderToRepo(string folderName)
        {
            throw new NotImplementedException();
        }

        public override void EnsureConsistentCasingInLocalName(string bookBaseName)
        {
            throw new NotImplementedException();
        }

        public override bool DoLocalAndRemoteNamesDifferOnlyByCase(string bookBaseName)
        {
            throw new NotImplementedException();
        }

        public override bool CannotDeleteBecauseDisconnected(string bookFolderPath)
        {
            // The only books we allow to be deleted while disconnected are newly created ones
            // that only exist locally. We might one day allow deleting books while offline
            // if they are checked out here, but it's not obvious how to arrange to delete the
            // book from the repo eventually, and if we never get reconnected it may not happen.
            return GetStatus(Path.GetFileName(bookFolderPath)).lockedBy
                != TeamCollection.FakeUserIndicatingNewBook;
        }

        protected internal override void StartMonitoring()
        {
            // do nothing. No need to monitor local collection while we can't save changes
            // to the repo, and of course we can't monitor the repo.
        }

        protected internal override void StopMonitoring()
        {
            // do nothing. We didn't start anything so there's nothing to stop.
        }

        public override string GetBackendType()
        {
            return "Disconnected";
        }
    }
}
