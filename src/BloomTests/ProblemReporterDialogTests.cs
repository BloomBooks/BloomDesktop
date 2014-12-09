using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom;
using Bloom.MiscUI;
using NUnit.Framework;
#if __MonoCS__
	using Gecko;
#endif

namespace BloomTests
{
	[TestFixture]
#if __MonoCS__
	[RequiresSTA]
#endif
	public class ProblemReporterDialogTests
	{
		[TestFixtureSetUp]
		public void FixtureSetup()
		{
			Browser.SetUpXulRunner();
		}

		[TestFixtureTearDown]
		public void FixtureTearDown()
		{
#if __MonoCS__
	// Doing this in Windows works on dev machines but somehow freezes the TC test runner
			Xpcom.Shutdown();
#endif
		}

		/// <summary>
		/// This is just a smoke-test that will notify us if the SIL JIRA stops working with the API we're relying on.
		/// It sends reports to https://jira.sil.org/browse/AUT
		/// </summary>
		[Test, Ignore("Jira is down")]
		public void CanSubmitToSILJiraAutomatedTestProject()
		{
			using (var dlg = new ProblemReporterDialog(null, null))
			{
				dlg.SetupForUnitTest("AUT");
				dlg.ShowDialog();
			}
		}
	}
}