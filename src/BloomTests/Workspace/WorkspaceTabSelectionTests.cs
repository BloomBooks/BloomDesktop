using Bloom.Publish;
using Bloom.Workspace;
using NUnit.Framework;

namespace BloomTests.Workspace
{
    /// <summary>
    /// PublishHelper.InPublishTab is a static mirror of ActiveTab, needed because the book-staging
    /// code that consults it is static. These tests pin down that the mirror is updated by the
    /// ActiveTab setter itself, so nothing can observe the two disagreeing. When they could
    /// disagree, switching to the Publish tab and asking for a BloomPUB preview sometimes died with
    /// "Should not be creating bloom book while not in publish tab" (BL-16174).
    /// </summary>
    [TestFixture]
    // These tests read and write PublishHelper.InPublishTab, which is process-wide. Setup/TearDown
    // put it back, which is enough while fixtures run one at a time, but say so explicitly so that
    // turning parallel test execution on later cannot quietly let this fixture and the publish
    // tests perturb each other.
    [NonParallelizable]
    public class WorkspaceTabSelectionTests
    {
        private bool _originalInPublishTab;

        [SetUp]
        public void Setup()
        {
            // It's a static, shared with the rest of the test run, so put it back afterwards.
            _originalInPublishTab = PublishHelper.InPublishTab;
            PublishHelper.InPublishTab = false;
        }

        [TearDown]
        public void TearDown()
        {
            PublishHelper.InPublishTab = _originalInPublishTab;
        }

        [Test]
        public void ActiveTab_SetToPublish_SetsInPublishTab()
        {
            var selection = new WorkspaceTabSelection();
            Assert.That(
                PublishHelper.InPublishTab,
                Is.False,
                "Sanity check: this test is meaningless unless the flag starts out false."
            );

            selection.ActiveTab = WorkspaceTab.publish;

            Assert.That(selection.ActiveTab, Is.EqualTo(WorkspaceTab.publish));
            Assert.That(PublishHelper.InPublishTab, Is.True);
        }

        [TestCase(WorkspaceTab.collection)]
        [TestCase(WorkspaceTab.edit)]
        public void ActiveTab_SetToOtherTab_ClearsInPublishTab(WorkspaceTab tab)
        {
            var selection = new WorkspaceTabSelection();
            selection.ActiveTab = WorkspaceTab.publish;
            Assert.That(
                PublishHelper.InPublishTab,
                Is.True,
                "Sanity check: we should be starting from the publish tab."
            );

            selection.ActiveTab = tab;

            Assert.That(selection.ActiveTab, Is.EqualTo(tab));
            Assert.That(PublishHelper.InPublishTab, Is.False);
        }

        /// <summary>
        /// Opening a different collection builds a whole new ProjectContext, and so a new
        /// WorkspaceTabSelection, but InPublishTab is static and survives. Note that merely
        /// constructing the new WorkspaceTabSelection does not clear it -- what clears it is the
        /// new WorkspaceView constructor's `_tabSelection.ActiveTab = WorkspaceTab.collection`
        /// (WorkspaceView.cs), which this test stands in for. So the invariant depends on that
        /// line continuing to exist; without it we would carry "we are in the publish tab" over
        /// into a collection that is sitting on its Collection tab.
        /// </summary>
        [Test]
        public void ActiveTab_NewSelectionInitializedToCollection_ClearsStaleInPublishTab()
        {
            new WorkspaceTabSelection().ActiveTab = WorkspaceTab.publish;
            Assert.That(
                PublishHelper.InPublishTab,
                Is.True,
                "Sanity check: we should be starting from the publish tab."
            );

            // What WorkspaceView's constructor does for the new collection.
            var newSelection = new WorkspaceTabSelection();
            newSelection.ActiveTab = WorkspaceTab.collection;

            Assert.That(PublishHelper.InPublishTab, Is.False);
        }
    }
}
