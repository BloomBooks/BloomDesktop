#if Chorus
using System;
using System.IO;
using Chorus.VcsDrivers.Mercurial;
using Chorus.merge;
using Chorus.merge.xml.generic;
using LibChorus.TestUtilities;
using NUnit.Framework;
using SIL.IO;
using SIL.Progress;
using SIL.TestUtilities;

namespace BloomTests.Chorus
{
	[TestFixture]
	public class BookMergingTests
	{
		[Test]
		public void CreateOrLocate_FolderHasAccentedLetter_FindsIt()
		{
			using (var setup = new RepositorySetup("Abé Books"))
			{
				Assert.NotNull(HgRepository.CreateOrUseExisting(setup.Repository.PathToRepo, new ConsoleProgress()));
			}
		}

		[Test]
		public void CreateOrLocate_FolderHasAccentedLetter2_FindsIt()
		{
			using (var testRoot = new TemporaryFolder("bloom sr test"))
			{
				string path = Path.Combine(testRoot.Path, "Abé Books");
				Directory.CreateDirectory(path);

				Assert.NotNull(HgRepository.CreateOrUseExisting(path, new ConsoleProgress()));
				Assert.NotNull(HgRepository.CreateOrUseExisting(path, new ConsoleProgress()));
			}
		}

		[Test]
		public void Merge_EachAddedItemToDatDiv()
		{
			TestBodyMerge(
				@"  <div id='bloomDataDiv'>
										<div data-book='coverImage'>theCover.png</div>
									</div>",
				@"<div id='bloomDataDiv'>
										 <div data-book='ourNew'>our new thing</div>
										<div data-book='coverImage'>theCover.png</div>
									</div>",
				@"<div id='bloomDataDiv'>
										<div data-book='coverImage'>theCover.png</div>
										 <div data-book='theirNew'>their new thing</div>
									</div>",
				(path) =>
					{
						AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("html/body/div[@id='bloomDataDiv']", 1);
						AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("html/body/div/div", 3);
						AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("html/body/div/div[@data-book='coverImage']", 1);
						AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("html/body/div/div[@data-book='theirNew']", 1);
						AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("html/body/div/div[@data-book='ourNew']", 1);
					},
				(listener) => {  Assert.AreEqual(0, listener.Conflicts.Count);});
		}


		[Test]
		public void Merge_EachHasNewPage_BothAdded()
		{
			TestBodyMerge(ancestorBody: "<div class='bloom-page' id='pageB'></div>",
						ourBody: @"<div class='bloom-page' id='pageB'></div>
									<div class='bloom-page' id='pageC'></div>",
						theirBody:@"<div class='bloom-page' id='pageA'></div>
								   <div class='bloom-page' id='pageB'></div>",
						testsOnResultingFile: (file) =>
												 {
													 AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
														 "//div[@class='bloom-page']", 3);
												 },
						testsOnEventListener: (listener)=>{listener.AssertExpectedConflictCount(0);});

		}

		[Test]
		public void Merge_EachEditedADifferentPage_GoodMergeNoConflicts()
		{
			TestBodyMerge(ancestorBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original a</div>
											</div>
										</div>
								   <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original b</div>
											</div></div>",
						  ourBody:@"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>changed by us</div>
											</div>
										</div>
								   <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original b</div>
											</div></div>",
						 theirBody:  @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original a</div>
											</div>
										</div>
								   <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>changed by them</div>
											</div></div>",
					   testsOnResultingFile: (file) =>
					   {
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@class='bloom-page']", 2);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath("//div[@id='pageA']//div[text()='changed by us']",1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath("//div[@id='pageB']//div[text()='changed by them']", 1);
					   },
					   testsOnEventListener: (listener) => { listener.AssertExpectedConflictCount(0); });

		}

		[Test]
		public void Merge_EachEditedTheSamePage_OneUserAddsSeparateBloomEditableDivs()
		{
			TestBodyMerge(ancestorBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original a</div>
											</div>
										</div>
								   <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original b</div>
											</div></div>",
						  ourBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>changed by us</div>
											</div>
										</div>
								   <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original b</div>
											</div></div>",
						 theirBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original a</div>
												<div class='bloom-editable bloom-content2' contenteditable='true' lang='sss'>changed by them</div>
											</div>
										</div>
								   <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original b</div>
												<div class='bloom-editable bloom-content2' contenteditable='true' lang='sss'>changed by them</div>
											</div></div>",
					   testsOnResultingFile: (file) =>
					   {
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@class='bloom-page']", 2);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@id='pageA']//div[text()='changed by us']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@id='pageB']//div[text()='changed by them']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[contains(@class, 'bloom-content2')]", 2);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@class='bloom-translationGroup']", 2);
					   },
					   testsOnEventListener: (listener) => { listener.AssertExpectedConflictCount(0); });

		}
		[Test]
		public void Merge_EachEditedTheSamePage_MergeMustBeBasedOnLanguage()
		{
			TestBodyMerge(ancestorBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original sse-lang text</div>
											</div>
										  </div>",
						  ourBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original sse-lang text</div>
												<div class='bloom-editable bloom-content2' contenteditable='true' lang='third'>original third-lang text</div>
											</div>
										  </div>",
						 theirBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='other'>original other-lang text</div>
												<div class='bloom-editable bloom-content2' contenteditable='true' lang='sse'>changed by them</div>
											</div>
										  </div>",
					   testsOnResultingFile: (file) =>
					   {
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@class='bloom-page']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[contains(@class, 'bloom-editable')]", 3);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[text()='changed by them']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[text()='original third-lang text']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[text()='original other-lang text']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@class='bloom-translationGroup']", 1);
					   },
					   testsOnEventListener: (listener) => { listener.AssertExpectedConflictCount(0); });

		}
		[Test]
		public void Merge_EachEditedTheSamePage_ConflictOnSecondPage()
		{
			TestBodyMerge(ancestorBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>first page sse-lang text</div>
											</div>
										  </div>
										  <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original sse-lang text</div>
											</div>
										  </div>",
						  ourBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>first page sse-lang text</div>
											</div>
										  </div>
									<div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>changed by us</div>
												<div class='bloom-editable bloom-content2' contenteditable='true' lang='third'>original third-lang text</div>
											</div>
										  </div>",
						 theirBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>first page sse-lang text</div>
											</div>
										  </div>
									<div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='other'>original other-lang text</div>
												<div class='bloom-editable bloom-content2' contenteditable='true' lang='sse'>changed by them</div>
											</div>
										  </div>",
					   testsOnResultingFile: (file) =>
					   {
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@class='bloom-page']", 2);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[contains(@class, 'bloom-editable')]", 4);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[text()='changed by them']", 0);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[text()='changed by us']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[text()='original third-lang text']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[text()='original other-lang text']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@class='bloom-translationGroup']", 2);
					   },
					   testsOnEventListener: (listener) =>
					   {
						   listener.AssertExpectedConflictCount(1);
						   listener.AssertFirstConflictType<BothEditedTheSameAtomicElement>();
						   var conflict = listener.Conflicts[0];
						   conflict.HtmlDetails.Contains("Page number: 2");
						   Assert.AreEqual("BloomBook group language=sse", conflict.Context.DataLabel);
					   });

		}

		[Test]
		public void Merge_EachEditedTheSamePage_OneConflict()
		{
			TestBodyMerge(ancestorBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original a</div>
											</div>
										</div>
								   <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original b</div>
											</div></div>",
						  ourBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>changed by us</div>
											</div>
										</div>
								   <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original b</div>
											</div></div>",
						 theirBody: @"<div class='bloom-page' id='pageA'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>changed by them</div>
											</div>
										</div>
								   <div class='bloom-page' id='pageB'>
											<div class='bloom-translationGroup'>
												<div class='bloom-editable bloom-content1' contenteditable='true' lang='sse'>original b</div>
											</div></div>",
					   testsOnResultingFile: (file) =>
					   {
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@class='bloom-page']", 2);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@id='pageA']//div[text()='changed by us']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@id='pageB']//div[text()='original b']", 1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath(
							   "//div[@class='bloom-translationGroup']", 2);
					   },
					   testsOnEventListener: (listener) =>
					   {
						   listener.AssertExpectedConflictCount(1);
						   listener.AssertFirstConflictType<BothEditedTheSameAtomicElement>();
						   var conflict = listener.Conflicts[0];
						   conflict.HtmlDetails.Contains("Page number: 1");
						   Assert.AreEqual("BloomBook group language=sse", conflict.Context.DataLabel);
					   });

		}

		private void TestBodyMerge(string ancestorBody, string ourBody, string theirBody, Action<string> testsOnResultingFile, Action<ListenerForUnitTests> testsOnEventListener )
		{
			string ancestor = @"<?xml version='1.0' encoding='utf-8'?><html><body>"+ancestorBody+"</body></html>";
			string theirs = @"<?xml version='1.0' encoding='utf-8'?><html><body>" + theirBody + "</body></html>";
			string ours = @"<?xml version='1.0' encoding='utf-8'?><html><body>" + ourBody + "</body></html>";

			using (var oursTemp = new TempFile(ours))
			using (var theirsTemp = new TempFile(theirs))
			using (var ancestorTemp = new TempFile(ancestor))
			{
				var listener = new ListenerForUnitTests();
				var situation = new NullMergeSituation();
				situation.AlphaUserId = "us";
				situation.BetaUserId = "them";
				var mergeOrder = new MergeOrder(oursTemp.Path, ancestorTemp.Path, theirsTemp.Path, situation) { EventListener = listener };
				new Bloom_ChorusPlugin.BloomHtmlFileTypeHandler().Do3WayMerge(mergeOrder);

				testsOnResultingFile(mergeOrder.pathToOurs);
				testsOnEventListener(listener);
			}
		}
	  }
}
#endif