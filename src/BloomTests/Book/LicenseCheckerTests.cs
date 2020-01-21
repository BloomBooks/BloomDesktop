using System;
using System.Collections.Generic;
using System.IO;
using Bloom.Api;
using Bloom.Book;
using BloomTemp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace BloomTests.Book
{
	public class LicenseCheckerTests
	{
		[Test]
		public void ProblemLanguages_KeepsExactMatchNeedingTrim_RemovesNotMatched_WritesOfflineCache()
		{
			using (var folder = new TemporaryFolder("ProblemLanguages_KeepsExactMatchNeedingTrim_RemovesNotMatched_WritesOfflineCache"))
			{
				LicenseChecker.SetOfflineFolder(folder.FolderPath);
				var checker = SimpleTest(true);
				Assert.That(File.Exists(checker.getCacheFile()));
				var json = LicenseChecker.ReadObfuscatedFile(checker.getCacheFile());
				Assert.DoesNotThrow(() => DynamicJson.Parse(json));
				LicenseChecker.SetOfflineFolder(null);
			}
		}

		private static LicenseChecker SimpleTest(bool expectCheck)
		{
			var checker = new LicenseChecker();
			// bjn, at the time of writing, has a spurious newline, and comes back as bjn\n.
			// That might change in the online version but should stay true in the offline data below.
			var inputLangs = new String[] {"en", "bjn"};
			var key = "kingstone.superbible.ruth";
			bool didCheck;
			IEnumerable<string> result = checker.GetProblemLanguages(inputLangs, key, out didCheck);
			Assert.That(didCheck, Is.EqualTo(expectCheck));
			if (didCheck)
			{
				Assert.That(result, Does.Contain("en"));
				Assert.That(result, Does.Not.Contain("bjn"));
			}

			return checker;
		}


		[Test]
		public void ProblemLanguages_KeepsAsteriskMatch()
		{
			var checker = new LicenseChecker();
			var inputLangs = new String[] {"en", "fr", "ru", "zh-CN"};
			var key = "kingstone.superbible.ruth";
			bool didCheck;
			IEnumerable<string> result = checker.GetProblemLanguages(inputLangs, key, out didCheck);
			Assert.That(didCheck, Is.True);
			Assert.That(result, Does.Contain("en"));
			Assert.That(result, Does.Contain("fr"));
			Assert.That(result, Does.Not.Contain("ru"));
			Assert.That(result, Does.Not.Contain("zh-CN"));
		}

		private TemporaryFolder SetupDefaultOfflineLicenseInfo()
		{
			var folder = new TemporaryFolder("DefaultOfflineLicenseTest");
			LicenseChecker.SetOfflineFolder(folder.FolderPath);
			LicenseChecker.SetAllowInternetAccess(false);
			LicenseChecker.WriteObfuscatedFile(folder.FolderPath + "/license.cache", @"{
  ""range"": ""Sheet1!A1:B1001"",
  ""majorDimension"": ""ROWS"",
  ""values"": [
    [
      ""content-id\n"",
      ""language-code""
    ],
    [
      ""kingstone.superbible.*"",
      ""ar\n""
    ],
    [
      ""kingstone.superbible.*"",
      ""tr""
    ],
    [
      ""kingstone.superbible.*"",
      ""fa""
    ],
    [
      ""kingstone.superbible.*"",
      ""zh-CN""
    ],
    [
      ""kingstone.superbible.*"",
      ""in""
    ],
    [
      ""kingstone.superbible.*"",
      ""ne""
    ],
    [
      ""kingstone.superbible.*"",
      ""hi""
    ],
    [
      ""kingstone.superbible.*"",
      ""ru""
    ],
    [
      ""kingstone.superbible.*"",
      ""bn""
    ],
    [
      ""kingstone.superbible.ruth"",
      ""bjn""
    ]
  ]
}");
			return folder;
		}

		[Test]
		public void ProblemLanguages_UsesOfflineCacheIfAvailable()
		{
			using (var folder = SetupDefaultOfflineLicenseInfo())
			{
				var checker = SimpleTest(true);
				File.Delete(checker.getCacheFile());
				SimpleTest(false);
				LicenseChecker.SetOfflineFolder(null);
				SimpleTest(false);
				LicenseChecker.SetAllowInternetAccess(true);
			}
		}

		[Test]
		public void CheckBook_NoMetaLicenseId_ReturnsNull()
		{
			var dom = BookTestsBase.MakeDom(@"<div class='bloom-page numberedPage customPage A5Portrait'>
						<div id='testDiv' class='marginBox'>
							<div class='bloom-translationGroup bloom-trailingElement normal-style'>
								<div class='bloom-editable normal-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
										some text
								</div>
							</div>
						</div>
					</div>");
			var checker = new LicenseChecker();
			LicenseChecker.SetAllowInternetAccess(false);
			LicenseChecker.SetOfflineFolder(null);
			Assert.That(checker.CheckBook(dom, new[] {"en", "fr"}), Is.Null);
			LicenseChecker.SetAllowInternetAccess(true);
			Assert.That(checker.CheckBook(dom, new[] {"en", "fr"}), Is.Null);
		}

		[Test]
		public void CheckBook_MetaLicenseId_ReturnsCantUse_IfLangsNotAllowed()
		{
			var dom = MakeDomWithLicenseMeta();
			using (var folder = SetupDefaultOfflineLicenseInfo())
			{
				var checker = new LicenseChecker();
				var expectedTemplate = LicenseChecker.kUnlicenseLanguageMessage;
				var expectedEn = string.Format(expectedTemplate, "English");
				Assert.That(checker.CheckBook(dom, new[] {"en"}), Is.EqualTo(expectedEn));
				var expectedFr = string.Format(expectedTemplate, "français");
				Assert.That(checker.CheckBook(dom, new[] {"fr"}), Is.EqualTo(expectedFr));
				var expectedEnFr = string.Format(expectedTemplate, "English, français");
				Assert.That(checker.CheckBook(dom, new[] {"en", "fr"}), Is.EqualTo(expectedEnFr));
				// Russian and bjn ARE permitted.
				Assert.That(checker.CheckBook(dom, new[] {"ru", "bjn"}), Is.Null);
				Assert.That(checker.CheckBook(dom, new[] {"en", "fr", "ru", "bjn"}), Is.EqualTo(expectedEnFr));
				// It might make sense to test (and implement) not complaining about languages which occur in the list
				// but do not occur in the book. But in practice we don't give users any opportunity to request languages
				// that don't occur in the book, so there's no point.
			}
		}

		private static HtmlDom MakeDomWithLicenseMeta()
		{
			// The really critical thing about this DOM is that the head (second argument) contains the bloom-licensed-content-id metadata.
			// The LicenseChecker doesn't pay any attention to the book content. However, we don't usually expect to ask about languages
			// that don't occur, so in the interests of making a slightly more realistic test, I put in some book content.
			return BookTestsBase.MakeDom(@"<div class='bloom-page numberedPage customPage A5Portrait'>
						<div id='testDiv' class='marginBox'>
							<div class='bloom-translationGroup bloom-trailingElement normal-style'>
								<div class='bloom-editable normal-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
										some text
								</div>
								<div class='bloom-editable normal-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='fr'>
										french text
								</div>
								<div class='bloom-editable normal-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='ru'>
										french text
								</div>
							</div>
						</div>
					</div>", "<meta name='bloom-licensed-content-id' content='kingstone.superbible.ruth'></meta>");
		}

		[Test]
		public void CheckBook_HasMeta_NoAccessToSpreadsheet_ReportsCantTest()
		{
			var dom = MakeDomWithLicenseMeta();
			LicenseChecker.SetAllowInternetAccess(false);
			LicenseChecker.SetOfflineFolder(null);
			var checker = new LicenseChecker();
			Assert.That(checker.CheckBook(dom, new[] {"en"}), Is.EqualTo(LicenseChecker.kCannotReachLicenseServerMessage));
		}

		[Test]
		public void GetAllowedLanguageCodes_JsonMissingContentId_SkipsOverLine()
		{
			string badJson = GetBadJsonMissingContentId();
			var invoker = new PrivateType(typeof(LicenseChecker));
			var observedAllowedLangs = invoker.InvokeStatic("GetAllowedLanguageCodes", badJson, "") as HashSet<string>;
			Assert.That(observedAllowedLangs.Contains(""), Is.False, "Empty string should be skipped over");
			Assert.That(observedAllowedLangs.Count, Is.EqualTo(0));

			// Now look it up with a more "realisitc" key
			observedAllowedLangs = invoker.InvokeStatic("GetAllowedLanguageCodes", badJson, "kingstone.superbible.*") as HashSet<string>;
			Assert.That(observedAllowedLangs.Contains(""), Is.False);
			Assert.That(observedAllowedLangs.Contains("tr"), Is.True);
			Assert.That(observedAllowedLangs.Count, Is.GreaterThan(0), "Search for Kingstone should return the other valid values");
		}

		[Test]
		public void GetAllowedLanguageCodes_JsonMissingLangCode_SkipsOverLine()
		{
			string badJson = GetBadJsonMissingLangCode();
			var invoker = new PrivateType(typeof(LicenseChecker));
			var observedAllowedLangs = invoker.InvokeStatic("GetAllowedLanguageCodes", badJson, "kingstone.superbible.*") as HashSet<string>;
			Assert.That(observedAllowedLangs.Contains("tr"), Is.True);
			Assert.That(observedAllowedLangs.Count, Is.GreaterThan(0));
		}

		private static string GetBadJsonMissingContentId()
		{
			return
@"{
  ""range"": ""Sheet1!A1:B1001"",
  ""majorDimension"": ""ROWS"",
  ""values"": [
	[
      ""content-id\n"",
      ""language-code""
    ],
    [
      ""kingstone.superbible.*"",
      ""ar\n""
    ],
    [
	  """",
      ""fa""
    ],
    [
      ""kingstone.superbible.*"",
      ""tr\n""
    ],
  ]
}";
		}

		private static string GetBadJsonMissingLangCode()
		{
			return
@"{
  ""range"": ""Sheet1!A1:B1001"",
  ""majorDimension"": ""ROWS"",
  ""values"": [
	[
      ""content-id\n"",
      ""language-code""
    ],
    [
      ""kingstone.superbible.*"",
      ""ar\n""
    ],
    [
      ""kingstone.superbible.*""
    ],
    [
      ""kingstone.superbible.*"",
      ""tr\n""
    ],
  ]
}";
		}
	}
}
