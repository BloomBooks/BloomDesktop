using System.Threading;
using System.Threading.Tasks;
using Bloom.Edit;
using NUnit.Framework;

namespace BloomTests.Edit
{
    /// <summary>
    /// Tests of PageSnapshot, which holds the copy of the page the BROWSER volunteered and decides
    /// which such copies to believe.
    ///
    /// The interesting cases are all about ordering. The endpoint that receives a snapshot is
    /// deliberately not synchronised (a keystroke has no business queueing behind a save) and is
    /// not ordered against the notification that a page has finished loading, so snapshots can and
    /// do arrive early, late, and from pages we have already left.
    /// </summary>
    [TestFixture]
    public class PageSnapshotTests
    {
        private PageSnapshot _snapshot;

        [SetUp]
        public void Setup()
        {
            _snapshot = new PageSnapshot();
        }

        /// The normal sequence: we navigate, the page reports itself loaded, then it sends content.
        private void ArriveAtPage(string loadId)
        {
            _snapshot.Clear(); // navigation starts
            _snapshot.AcceptSnapshotsFromLoad(loadId); // the page reports ready
        }

        [Test]
        public void Set_FromTheLoadWeAreShowing_IsKept()
        {
            ArriveAtPage("load-1");

            Assert.That(_snapshot.Set("page-A", "load-1", "typed"), Is.True);
            Assert.That(_snapshot.GetFor("page-A"), Is.EqualTo("typed"));
        }

        [Test]
        public void Set_BeforeThePageReportsReady_IsRefusedRatherThanKept()
        {
            // The browser can genuinely be first: its two calls are not ordered against each other.
            // Refusing tells it to offer the content again; keeping it would file content under a
            // load we know nothing about.
            _snapshot.Clear();

            Assert.That(_snapshot.Set("page-A", "load-1", "typed"), Is.False);
            Assert.That(_snapshot.GetFor("page-A"), Is.Null);
        }

        [Test]
        public void Set_FromALoadWeHaveLeft_IsRefused()
        {
            ArriveAtPage("load-1");
            Assert.That(_snapshot.Set("page-A", "load-1", "first"), Is.True, "test setup");

            ArriveAtPage("load-2"); // moved to another page

            Assert.That(_snapshot.Set("page-A", "load-1", "late"), Is.False);
            Assert.That(
                _snapshot.GetFor("page-A"),
                Is.Null,
                "the stale post must not refill what the navigation cleared"
            );
        }

        [Test]
        public void Set_AfterReloadingTheSamePage_RefusesThePreReloadContent()
        {
            // The case the load id exists for. Change Layout, importing a video and changing the
            // topic all rebuild the page under its OWN id, so matching on the page id alone would
            // let content from before the reload be merged over what the reload built.
            ArriveAtPage("load-1");
            Assert.That(_snapshot.Set("page-A", "load-1", "before"), Is.True, "test setup");

            ArriveAtPage("load-2"); // same page, rebuilt

            Assert.That(_snapshot.Set("page-A", "load-1", "before"), Is.False);
            Assert.That(_snapshot.GetFor("page-A"), Is.Null);

            Assert.That(_snapshot.Set("page-A", "load-2", "after"), Is.True);
            Assert.That(_snapshot.GetFor("page-A"), Is.EqualTo("after"));
        }

        [Test]
        public void AcceptSnapshotsFromLoad_LateNotificationDoesNotDisableTheCurrentPage()
        {
            // This is the ordering that mattered most, and the one that was wrong: a "page is
            // ready" notification from a page we had already left arriving after the current page's
            // own. If we adopt its id, every snapshot the user's actual page sends is refused --
            // and since a refusal makes the browser retry rather than give up, it stays refused. We
            // would hold nothing, and quitting would write nothing: not the last keystroke lost,
            // but everything since the page loaded.
            //
            // EditingModel is what enforces this, by only calling us for a notification it
            // accepted; this test pins the consequence so the rule cannot be quietly dropped.
            ArriveAtPage("load-2"); // we are on the second page
            Assert.That(_snapshot.Set("page-B", "load-2", "typing"), Is.True, "test setup");

            // The stale notification for the page we left must NOT be handed to us. If it were:
            _snapshot.AcceptSnapshotsFromLoad("load-1");
            Assert.That(
                _snapshot.Set("page-B", "load-2", "more typing"),
                Is.False,
                "this is what going wrong looks like -- the live page can no longer be saved"
            );
        }

        [Test]
        public void GetFor_AnotherPage_IsNull()
        {
            ArriveAtPage("load-1");
            _snapshot.Set("page-A", "load-1", "typed");

            Assert.That(_snapshot.GetFor("page-B"), Is.Null);
        }

        [Test]
        public void Clear_ForgetsBothTheContentAndTheLoadWeBelieve()
        {
            ArriveAtPage("load-1");
            _snapshot.Set("page-A", "load-1", "typed");

            _snapshot.Clear();

            Assert.That(_snapshot.GetFor("page-A"), Is.Null);
            Assert.That(
                _snapshot.Set("page-A", "load-1", "typed again"),
                Is.False,
                "after a navigation we believe nothing until the next page reports ready"
            );
        }

        // The browser tells us when asynchronous work whose result belongs in the saved page has
        // begun and ended (see pageContentDelays.ts). A save that uses the snapshot waits for the
        // end, for a bounded time, because the snapshot it holds predates the work.

        [Test]
        public void WaitUntilIdle_WhileBusy_GivesUpAfterTheLimitAndNamesTheWork()
        {
            ArriveAtPage("load-1");
            Assert.That(_snapshot.SetBusy("load-1", 1, "sizing an image"), Is.True, "test setup");

            var idle = _snapshot.WaitUntilIdle(60, out var busyWith);

            Assert.That(idle, Is.False, "the browser never said it had finished");
            Assert.That(
                busyWith,
                Is.EqualTo("sizing an image"),
                "so the log can say what held us up"
            );
        }

        [Test]
        public void WaitUntilIdle_WhenTheBrowserSaysIdle_Returns()
        {
            ArriveAtPage("load-1");
            Assert.That(_snapshot.SetBusy("load-1", 1, "sizing an image"), Is.True, "test setup");
            // The idle notice arrives on a server thread while the UI thread is asleep in the wait.
            Task.Run(() =>
            {
                Thread.Sleep(40);
                _snapshot.SetIdle("load-1", 2);
            });

            var idle = _snapshot.WaitUntilIdle(5000, out var busyWith);

            Assert.That(idle, Is.True);
            Assert.That(busyWith, Is.Null);
        }

        [Test]
        public void WaitUntilIdle_WhenNothingIsBusy_ReturnsAtOnce()
        {
            ArriveAtPage("load-1");

            Assert.That(_snapshot.WaitUntilIdle(5000, out var busyWith), Is.True);
            Assert.That(busyWith, Is.Null);
        }

        [Test]
        public void SetBusy_FromALoadWeAreNotShowing_IsRefusedAndDoesNotMakeUsWait()
        {
            ArriveAtPage("load-1");

            Assert.That(_snapshot.SetBusy("load-0", 1, "sizing an image"), Is.False);
            Assert.That(
                _snapshot.WaitUntilIdle(5000, out _),
                Is.True,
                "a stale notice must not hold up a save"
            );
        }

        [Test]
        public void Clear_ForgetsThatThePageWasBusy()
        {
            // Navigating away: whatever the page we left was busy with is no longer our concern,
            // and must not delay the next save on the page we are going to.
            ArriveAtPage("load-1");
            Assert.That(_snapshot.SetBusy("load-1", 1, "sizing an image"), Is.True, "test setup");

            _snapshot.Clear();

            Assert.That(_snapshot.WaitUntilIdle(5000, out _), Is.True);
        }

        [Test]
        public void SetIdle_FromBeforeTheBusyNoticeWeHold_IsIgnored()
        {
            // The two notices are separate requests and can be processed out of order. An idle
            // notice for work that finished BEFORE the work we now know about began must not clear
            // the busy state, or a save would go ahead in the middle of that work.
            ArriveAtPage("load-1");
            Assert.That(_snapshot.SetBusy("load-1", 3, "settling a paste"), Is.True, "test setup");

            Assert.That(
                _snapshot.SetIdle("load-1", 2),
                Is.True,
                "answered as taken, so the browser does not offer it again"
            );

            Assert.That(
                _snapshot.WaitUntilIdle(60, out var busyWith),
                Is.False,
                "still waiting on the newer work"
            );
            Assert.That(busyWith, Is.EqualTo("settling a paste"));
        }

        [Test]
        public void SetBusy_FromBeforeTheIdleNoticeWeHold_IsIgnored()
        {
            ArriveAtPage("load-1");
            Assert.That(_snapshot.SetBusy("load-1", 1, "sizing an image"), Is.True, "test setup");
            Assert.That(_snapshot.SetIdle("load-1", 2), Is.True, "test setup");

            // The busy notice from before the idle arrives late.
            Assert.That(_snapshot.SetBusy("load-1", 1, "sizing an image"), Is.True);

            Assert.That(
                _snapshot.WaitUntilIdle(5000, out _),
                Is.True,
                "the late notice changes nothing"
            );
        }
    }
}
