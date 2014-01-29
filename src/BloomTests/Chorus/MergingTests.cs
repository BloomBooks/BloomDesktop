using System;
using System.IO;
using Chorus.VcsDrivers.Mercurial;
using Chorus.merge;
using LibChorus.TestUtilities;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Progress;
using Palaso.TestUtilities;

namespace BloomTests.Chorus
{
	public class BookMergingTests
	{
		[Test, Ignore("not yet")]
		public void CreateOrLocate_FolderHasAccentedLetter_FindsIt()
		{
			using (var setup = new RepositorySetup("Abé Books"))
			{
				Assert.NotNull(HgRepository.CreateOrUseExisting(setup.Repository.PathToRepo, new ConsoleProgress()));
			}
		}

		[Test, Ignore("not yet")]
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
														 "html/body/div[@class='bloom-page']", 3);
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
							   "html/body/div[@class='bloom-page']", 2);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath("html/body/div[@id='pageA']/div/div[text()='changed by us']",1);
						   AssertThatXmlIn.HtmlFile(file).HasSpecifiedNumberOfMatchesForXpath("html/body/div[@id='pageB']/div/div[text()='changed by them']", 1);
					   },
					   testsOnEventListener: (listener) => { listener.AssertExpectedConflictCount(0); });

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
				var mergeOrder = new MergeOrder(oursTemp.Path, ancestorTemp.Path, theirsTemp.Path, situation) { EventListener = listener };
				new Bloom_ChorusPlugin.BloomHtmlFileTypeHandler().Do3WayMerge(mergeOrder);

				testsOnResultingFile(mergeOrder.pathToOurs);
				testsOnEventListener(listener);
			}
		}
	  }
}
