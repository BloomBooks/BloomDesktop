using System;
using System.Net;
using Bloom;
using NUnit.Framework;

namespace BloomTests
{
	[TestFixture]
	public class UpdateVersionTableTests
	{
		[Test]
		public void ThisVersionTooLarge_ReturnsEmptyString()
		{
			var t = new UpdateVersionTable();
			t.RunningVersion = Version.Parse("99.99.99");
			t.TextContentsOfTable = @"# the format is min,max,url
														0.0.0,1.1.999, http://example.com/appcast.xml";
			Assert.IsEmpty(t.LookupURLOfUpdate().URL);
		}

		[Test]
		public void LookupURLOfUpdate_NoLineForDesiredVersion_ReportsError()
		{
			var t = new UpdateVersionTable();
			t.TextContentsOfTable = @"0.0.0,3.1.99999, http://first.com/first";
			t.RunningVersion = Version.Parse("3.2.0");
			var lookupResult = t.LookupURLOfUpdate();
			Assert.That(lookupResult.URL, Is.Null.Or.Empty);
			Assert.That(lookupResult.Error.Message, Is.EqualTo("http://bloomlibrary.org/channels/UpgradeTableTestChannel.txt contains no record for this version of Bloom"));
		}

		[Test]
		public void LookupURLOfUpdate_AllWell_ReportsNoErrorAndReturnsUrl()
		{
			var t = new UpdateVersionTable();
			t.TextContentsOfTable = @"0.0.0,3.2.99999, http://first.com/first";
			t.RunningVersion = Version.Parse("3.2.0");
			var lookupResult = t.LookupURLOfUpdate();
			Assert.IsFalse(lookupResult.IsConnectivityError);
			Assert.IsNull(lookupResult.Error);
			Assert.That(lookupResult.URL, Is.EqualTo("http://first.com/first"));
		}

		[Test]
		public void LookupURLOfUpdate_TooManyCommas_LogsErrorGivesNoURL()
		{
			var t = new UpdateVersionTable();
			t.TextContentsOfTable = @"0.0.0,3,1,99999, http://first.com/first"; // too many commas
			t.RunningVersion = Version.Parse("3.2.0");
			var lookupResult = t.LookupURLOfUpdate();
			Assert.That(lookupResult.URL, Is.Null.Or.Empty);
			Assert.IsTrue(lookupResult.Error.Message.StartsWith("Could not parse a line of the UpdateVersionTable"));
		}

		[Test]
		public void LookupURLOfUpdate_TooFewCommas_LogsErrorGivesNoURL()
		{
			var t = new UpdateVersionTable();
			t.TextContentsOfTable = @"0.0.0, http://first.com/first"; // too few commas
			t.RunningVersion = Version.Parse("3.2.0");
			var lookupResult = t.LookupURLOfUpdate();
			Assert.That(lookupResult.URL, Is.Null.Or.Empty);
			Assert.IsTrue(lookupResult.Error.Message.StartsWith("Could not parse a line of the UpdateVersionTable"));
		}

		[Test]
		public void LookupURLOfUpdate_BadVersionNumber_LogsErrorGivesNoURL()
		{
			var t = new UpdateVersionTable();
			t.TextContentsOfTable = @"random,3.1.99999, http://first.com/first"; // bad version number
			t.RunningVersion = Version.Parse("3.2.0");
			var lookupResult = t.LookupURLOfUpdate();
			Assert.That(lookupResult.URL, Is.Null.Or.Empty);
			Assert.IsTrue(lookupResult.Error.Message.StartsWith("Could not parse a version number in the UpdateVersionTable"));
		}

		[Test]
		public void ServerAddressIsBogus_ErrorIsCorrect()
		{
			var t = new UpdateVersionTable {URLOfTable = "http://notthere7blah/foo.txt"};
			//the jenkins server gets a ProtocolError, while my desktop gets a NameResolutionFailure
			var e = t.LookupURLOfUpdate().Error.Status;
			Assert.IsTrue(e  == WebExceptionStatus.NameResolutionFailure || e == WebExceptionStatus.ProtocolError );
		}
		[Test]
		public void FileForThisChannelIsMissing_ErrorIsCorrect()
		{
			var t = new UpdateVersionTable { URLOfTable = "http://bloomlibrary.org/channels/UpgradeTableSomethingBogus.txt"};
			Assert.AreEqual(WebExceptionStatus.ProtocolError, t.LookupURLOfUpdate().Error.Status);
		}

		[Test]
		public void ValueOnLowerBound_ReturnsCorrectUrl()
		{
			var t = new UpdateVersionTable();
			t.TextContentsOfTable = @"# the format is min,max,url
														0.0.0,1.1.999, http://first.com/first
														2.1.1,2.9.999, http://second.com/second
														3.2.2,3.9.999, http://third.com/third";

			t.RunningVersion = Version.Parse("0.0.0");
			Assert.AreEqual("http://first.com/first", t.LookupURLOfUpdate().URL);
			t.RunningVersion = Version.Parse("2.1.1");
			Assert.AreEqual("http://second.com/second", t.LookupURLOfUpdate().URL);
			t.RunningVersion = Version.Parse("3.2.2");
			Assert.AreEqual("http://third.com/third", t.LookupURLOfUpdate().URL);
		}

		[Test]
		public void ValueOnUpperBound_ReturnsCorrectUrl()
		{
			var t = new UpdateVersionTable();
			t.TextContentsOfTable = @"# the format is min,max,url
														0.0.0,1.1.999, http://first.com/first
														2.1.1,2.9.999, http://second.com/second
														3.2.2,3.9.999, http://third.com/third";

			t.RunningVersion = Version.Parse("1.1.999");
			Assert.AreEqual("http://first.com/first", t.LookupURLOfUpdate().URL);
			t.RunningVersion = Version.Parse("2.9.999");
			Assert.AreEqual("http://second.com/second", t.LookupURLOfUpdate().URL);
			t.RunningVersion = Version.Parse("3.9.999");
			Assert.AreEqual("http://third.com/third", t.LookupURLOfUpdate().URL);
		}

		[Test]
		public void ValueOnInMiddle_ReturnsCorrectUrl()
		{
			var t = new UpdateVersionTable();
			t.TextContentsOfTable = @"# the format is min,max,url
														1.0.0,1.0.50, http://first.com/first
														1.0.50,2.1.99, http://second.com/second
														3.0.0,3.9.999, http://third.com/third";

			t.RunningVersion = Version.Parse("1.0.40");
			Assert.AreEqual("http://first.com/first", t.LookupURLOfUpdate().URL);
			t.RunningVersion = Version.Parse("1.1.0");
			Assert.AreEqual("http://second.com/second", t.LookupURLOfUpdate().URL);
			t.RunningVersion = Version.Parse("3.0.1");
			Assert.AreEqual("http://third.com/third", t.LookupURLOfUpdate().URL);
		}

		[Test]
		public void LookupURLOfUpdate_CanReadTableForAlphaFromServer()
		{
			var t = new UpdateVersionTable();
			t.URLOfTable = "http://bloomlibrary.org/channels/UpgradeTableAlpha.txt";
			t.RunningVersion = Version.Parse("2.0.2000");
			//the full result will be something like
			//"https://s3.amazonaws.com/bloomlibrary.org/deltasAlpha"
			//this just checks the part that is less likely to break (independent of channel)
			Assert.That(t.LookupURLOfUpdate().URL.StartsWith("https://s3.amazonaws.com/bloomlibrary.org/deltas"));
		}
	}
}
