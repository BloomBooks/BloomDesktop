using Bloom.Book;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Book
{
    [TestFixture]
    public class UserPrefsTests
    {
        [Test]
        public void LoadOrMakeNew_EmptyFile_GivesNewPrefs()
        {
            using (var t = new TempFile(""))
            {
                var up = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(up.MostRecentPage == 0);
            }
        }

        [Test]
        public void LoadOrMakeNew_CorrupFile_GivesNewPrefs()
        {
            using (var t = new TempFile("hellow world"))
            {
                var up = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(up.MostRecentPage == 0);
            }
        }

        [Test]
        public void LoadOrMakeNew_MostRecentPage_ReadsCorrectly()
        {
            using (var t = new TempFile("{\"mostRecentPage\":3}"))
            {
                var up = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(up.MostRecentPage == 3);
            }
        }

        [Test]
        public void LoadOrMakeNew_IncludeBackgroundColors_ReadsAndSavesCorrectly()
        {
            using (var t = new TempFile("{\"includeBackgroundColors\":true}"))
            {
                var up = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(up.IncludeBackgroundColors, Is.True);

                up.IncludeBackgroundColors = false;

                var reloaded = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(reloaded.IncludeBackgroundColors, Is.False);
            }
        }

        [Test]
        public void FlowTextReflowOnPageChange_NewPrefs_IsOn()
        {
            using (var t = new TempFile(""))
            {
                var up = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(
                    up.FlowTextReflowOnPageChange,
                    Is.True,
                    "Refitting on a page change is what a book does unless it says otherwise."
                );
            }
        }

        [Test]
        public void FlowTextReflowOnPageChange_TurnedOff_IsSavedAndReadBack()
        {
            using (var t = new TempFile(""))
            {
                var up = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(up.FlowTextReflowOnPageChange, Is.True, "Sanity check.");

                up.FlowTextReflowOnPageChange = false;

                Assert.That(
                    RobustFile.ReadAllText(t.Path),
                    Does.Contain("flowTextReflowOnPageChange"),
                    "The setting is not the default any more, so the file has to carry it."
                );
                var reloaded = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(reloaded.FlowTextReflowOnPageChange, Is.False);
            }
        }

        [Test]
        public void FlowTextReflowOnPageChange_TurnedBackOn_LeavesNoEntry()
        {
            using (var t = new TempFile("{\"flowTextReflowOnPageChange\":false}"))
            {
                var up = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(up.FlowTextReflowOnPageChange, Is.False, "Sanity check.");

                up.FlowTextReflowOnPageChange = true;

                Assert.That(
                    RobustFile.ReadAllText(t.Path),
                    Does.Not.Contain("flowTextReflowOnPageChange"),
                    "A book that holds the default writes no entry for it."
                );
                var reloaded = UserPrefs.LoadOrMakeNew(t.Path);
                Assert.That(reloaded.FlowTextReflowOnPageChange, Is.True);
            }
        }
    }
}
