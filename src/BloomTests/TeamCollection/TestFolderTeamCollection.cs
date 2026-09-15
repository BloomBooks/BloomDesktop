using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.TeamCollection;

namespace BloomTests.TeamCollection
{
    public class TestFolderTeamCollection : FolderTeamCollection
    {
        public TestFolderTeamCollection(
            ITeamCollectionManager tcManager,
            string localCollectionFolder,
            string repoFolderPath,
            TeamCollectionMessageLog tcLog = null
        )
            : base(tcManager, localCollectionFolder, repoFolderPath, tcLog) { }

        public Action OnCreatedCalled;
        public Action OnChangedCalled;
        public Action OnCollectionChangedCalled;

        /// <summary>
        /// Lets a test pretend this is (or is not) the collection the manager is using, without
        /// standing up a whole live TeamCollectionManager. Null means "use the real answer".
        /// </summary>
        public bool? PretendIsLiveCollection;

        protected internal override bool IsLiveCollection =>
            PretendIsLiveCollection ?? base.IsLiveCollection;

        /// <summary>
        /// Lets a test pretend a repo write is in progress.
        /// </summary>
        public bool PretendIsWritingToRepo;

        protected internal override bool IsWritingToRepo =>
            PretendIsWritingToRepo || base.IsWritingToRepo;

        /// <summary>
        /// Set InterceptCheckConnection to have CheckConnection report PretendConnectionProblem
        /// instead of really looking, so a test can drive ConnectionHeartbeat.Tick with no
        /// network and no real repo. Left off, the real implementation runs.
        /// </summary>
        public bool InterceptCheckConnection;
        public TeamCollectionMessage PretendConnectionProblem;
        public int CheckConnectionCallCount;

        /// <summary>
        /// Set to have the intercepted CheckConnection throw instead of answering, so a test can
        /// cover what the periodic check does when the probe itself fails.
        /// </summary>
        public bool PretendCheckConnectionThrows;

        public override TeamCollectionMessage CheckConnection(bool writeHistoryMessages)
        {
            CheckConnectionCallCount++;
            if (!InterceptCheckConnection)
                return base.CheckConnection(writeHistoryMessages);
            if (PretendCheckConnectionThrows)
                throw new IOException("pretend the probe itself blew up");
            return PretendConnectionProblem;
        }

        protected override void OnCreated(object sender, FileSystemEventArgs e)
        {
            base.OnCreated(sender, e);
            OnCreatedCalled?.Invoke();
        }

        protected override void OnChanged(object sender, FileSystemEventArgs e)
        {
            base.OnChanged(sender, e);
            OnChangedCalled?.Invoke();
        }

        protected override void OnCollectionFilesChanged(object sender, FileSystemEventArgs e)
        {
            base.OnCollectionFilesChanged(sender, e);
            OnCollectionChangedCalled?.Invoke();
        }
    }
}
