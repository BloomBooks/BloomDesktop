using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Bloom;
using BloomTemp;
using Gecko;
using NUnit.Framework;

namespace BloomTests
{
	[TestFixture]
	public class NavigationIsolatorTests
	{
		[Test]
		public void SimpleNavigation_JustHappens()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));
			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed after navigation completes");
		}

		[Test]
		public void SecondNavigation_OnSameBrowser_HappensWhenFirstRaisesNavigated()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));
			string target2 = "some other web address";
			isolator.Navigate(browser, target2);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "Second navigation should not have proceeded at once");

			browser.NormalTermination();
			Assert.That(browser.NavigateTarget, Is.EqualTo(target2), "Second navigation should have proceeded when first completed");

			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");
		}

		[Test]
		public void TwoPendingNavigations_WithNavigatedEvents_AreHandledCorrectly()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));
			string target2 = "some other web address";
			isolator.Navigate(browser, target2);
			string target3 = "yet another other web address";
			isolator.Navigate(browser, target3);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "Second and third navigation should not have proceeded at once");

			browser.NormalTermination();
			Assert.That(browser.NavigateTarget, Is.EqualTo(target2), "Second navigation should have proceeded when first completed (but third should not)");

			browser.NormalTermination();
			Assert.That(browser.NavigateTarget, Is.EqualTo(target3), "Third navigation should have proceeded when second completed");

			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");
		}

		[Test]
		public void TwoPendingNavigationsOnDifferentBrowsers_WithNavigatedEvents_AreHandledCorrectly()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));
			string target2 = "some other web address";
			var browser2 = new BrowserStub();
			isolator.Navigate(browser2, target2);
			string target3 = "yet another other web address";
			var browser3 = new BrowserStub();
			isolator.Navigate(browser3, target3);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "Second and third navigation should not have proceeded at once");
			Assert.That(browser2.NavigateTarget, Is.Null, "browser 2 should not have navigated anywhere yet");
			Assert.That(browser3.NavigateTarget, Is.Null, "browser 3 should not have navigated anywhere yet");

			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");
			Assert.That(browser2.NavigateTarget, Is.EqualTo(target2), "Second navigation should have proceeded (on second browser) first completed");
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "First browser should not have navigated again");
			Assert.That(browser3.NavigateTarget, Is.Null, "browser 3 should not have navigated anywhere when first completed");

			browser2.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "nothing new should have happened to browser 1");
			Assert.That(browser2.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");
			Assert.That(browser3.NavigateTarget, Is.EqualTo(target3), "Third navigation should have proceeded when second completed");
			Assert.That(browser2.NavigateTarget, Is.EqualTo(target2), "Second browser should not have navigated again");
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "First browser should not have navigated again");

			browser3.NormalTermination();
			Assert.That(browser3.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "nothing new should have happened to browser 1");
			Assert.That(browser2.EventHandlerCount, Is.EqualTo(0), "nothing new should have happened to browser 2");
		}

		[Test]
		public void SpuriousNavigatedEvents_AreIgnored()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));
			string target2 = "some other web address";
			isolator.Navigate(browser, target2);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "Second navigation should not have proceeded at once");

			browser.RaiseNavigated(this, new EventArgs()); // got the event notification, but still busy.
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "Second navigation should not have proceeded even on Navigated while browser still busy");

			browser.NormalTermination();
			Assert.That(browser.NavigateTarget, Is.EqualTo(target2), "Second navigation should have proceeded when first completed (and browser no longer busy)");

			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");
		}

		[Test]
		public void SecondRequest_WhenFirstNoLongerBusy_ProceedsAtOnce()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));
			browser.IsBusy = false; // clear state without raising event
			string target2 = "some other web address";
			isolator.Navigate(browser, target2);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target2), "Second navigation should have proceeded since browser is already not busy");

			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");

		}


		[Test]
		public void NoLongerBusy_EvenWithoutEvent_IsNoticed()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));
			string target2 = "some other web address";
			isolator.Navigate(browser, target2);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "Second navigation should not have proceeded at once");
			browser.IsBusy = false; // finished but did not raise event.
			var start = DateTime.Now;
			while (DateTime.Now - start < new TimeSpan(0, 0,0, 0, 150))
				Application.DoEvents(); // allow timer to tick.
			Assert.That(() => browser.NavigateTarget, Is.EqualTo(target2), "Second navigation should have proceeded soon after first no longer busy");

			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");
		}

		[Test]
		public void IdleNavigation_WhenNothingHappening_ProceedsAtOnce()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			Assert.That(isolator.NavigateIfIdle(browser, target), Is.True);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));
			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed after navigation completes");
		}

		[Test]
		public void IdleNavigation_NavigationInProgress_ReturnsFalse_NeverProceeds()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));
			string target2 = "some other web address";
			Assert.That(isolator.NavigateIfIdle(browser, target2), Is.False);
			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed after navigation completes");
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "failed idle navigation should not happen");
		}

		[Test]
		public void RegularNavigation_DelayedProperlyByIdleNavigation()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			Assert.That(isolator.NavigateIfIdle(browser, target), Is.True);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));

			string target2 = "some other web address";
			isolator.Navigate(browser, target2);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "Second navigation should not have proceeded at once");

			browser.NormalTermination();
			Assert.That(browser.NavigateTarget, Is.EqualTo(target2), "Second navigation should have proceeded when first completed");

			browser.NormalTermination();
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");
		}

		[Test]
		public void Isolation_AfterLongDelay_GivesUpAndMovesOn()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));

			string target2 = "some other web address";
			isolator.Navigate(browser, target2);
			string target3 = "yet another web address";
			isolator.Navigate(browser, target3);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target), "Second navigation should not have proceeded at once");
			var start = DateTime.Now;
			while (DateTime.Now - start < new TimeSpan(0, 0, 0, 2, 300))
				Application.DoEvents(); // allow timer to tick.
			Assert.That(() => browser.NavigateTarget, Is.EqualTo(target2), "Second navigation should have proceeded eventually");

			browser.NormalTermination(); // possibly the long-delayed notification of the first nav, but more likely the second.
			Assert.That(() => browser.NavigateTarget, Is.EqualTo(target3), "Third navigation should have proceeded when second finished");

			browser.NormalTermination(); // hopefully from the third.
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");			
		}

		[Test]
		public void SingleTask_AfterLongDelay_AllowsIdleNavigation()
		{
			var browser = new BrowserStub();
			string target = "any old web address";
			var isolator = new NavigationIsolator();
			isolator.Navigate(browser, target);
			Assert.That(browser.NavigateTarget, Is.EqualTo(target));

			string target2 = "some other web address";
			var start = DateTime.Now;
			var success = false;
			while (!success && DateTime.Now - start < new TimeSpan(0, 0, 0, 2, 300))
			{
				success = isolator.NavigateIfIdle(browser, target2);
				Application.DoEvents(); // allow timer to tick.
			}
			Assert.That(() => browser.NavigateTarget, Is.EqualTo(target2), "Idle navigation should have proceeded eventually");
			Assert.That(success, "NavigateIfIdle should eventually succeed");

			browser.NormalTermination(); // possibly the long-delayed notification of the first nav, but more likely the idle navigation.
			Assert.That(browser.EventHandlerCount, Is.EqualTo(0), "event handlers should be removed once last navigation completed");
		}
	}

	class BrowserStub : IIsolatedBrowser
	{
		public String NavigateTarget;
		public void Navigate(string url)
		{
			IsBusy = true;
			NavigateTarget = url;
		}

		public event EventHandler Navigated;

		public void NormalTermination()
		{
			IsBusy = false;
			RaiseNavigated(this, new EventArgs());
		}
		public void RaiseNavigated(object sender, EventArgs args)
		{
			if (Navigated != null)
				Navigated(sender, args);
		}
		public bool IsBusy { get; set; }

		public int EventHandlerCount;
		public void InstallEventHandlers()
		{
			EventHandlerCount++;
		}

		public void RemoveEventHandlers()
		{
			EventHandlerCount--;
		}
	}
}
