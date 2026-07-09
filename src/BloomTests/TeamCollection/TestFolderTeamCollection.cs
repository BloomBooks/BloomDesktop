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

        /// <summary>
        /// Test-only toggle so a FolderTeamCollection-based test can exercise the auto-apply code
        /// path in TeamCollection.HandleModifiedFile (normally only CloudTeamCollection sets this
        /// true) without needing the whole cloud stack.
        /// </summary>
        public bool AutoApplyRemoteChangesForTests { get; set; }

        protected override bool CanAutoApplyRemoteChanges => AutoApplyRemoteChangesForTests;
    }
}
