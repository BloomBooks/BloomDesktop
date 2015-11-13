using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom;
using Chorus.FileTypeHanders.html;
using Chorus.merge;
using Chorus.merge.xml.generic;
using LibChorus.TestUtilities;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Progress.LogBox;
using Palaso.TestUtilities;

namespace Bloom_ChorusTests.cs
{
	[TestFixture]
	public class HtmlHandlerTests
	{
		[Test]
		public void CanMergeFile_HTM_True()
		{
			var h = new BloomHtmlFileTypeHandler();

			using (var f = TempFile.WithExtension(".htm"))
			{
				Assert.IsTrue(h.CanMergeFile(f.Path));
			}
		}

		[Test]
		public void CanMergeFile_xyz_False()
		{
			var h = new BloomHtmlFileTypeHandler();

			using (var f = TempFile.WithExtension(".xyz"))
			{
				Assert.IsFalse(h.CanMergeFile(f.Path));
			}
		}

		[Test]
		public void DescribeInitialContentsShouldHaveAddedForLabel()
		{
			var initialContents = new BloomHtmlFileTypeHandler().DescribeInitialContents(null, null);
			Assert.AreEqual(1, initialContents.Count());
			var onlyOne = initialContents.First();
			Assert.AreEqual("Added", onlyOne.ActionLabel);
		}

		[Test]
		public void GetExtensionsOfKnownTextFileTypesIsLiftRanges()
		{
			var extensions = new BloomHtmlFileTypeHandler().GetExtensionsOfKnownTextFileTypes().ToArray();
			Assert.AreEqual(1, extensions.Count(), "Wrong number of extensions.");
			Assert.AreEqual("htm", extensions[0]);
		}

		[Test]
		public void CannotDiffAFile()
		{
			Assert.IsFalse(new BloomHtmlFileTypeHandler().CanDiffFile(null));
		}

		[Test]
		public void CannotValidateAFile()
		{
			Assert.IsFalse(new BloomHtmlFileTypeHandler().CanValidateFile(null));
		}

		[Test]
		public void CanMergeFile_html_True()
		{
			using (var tempFile = TempFile.WithExtension(".htm"))
			{
				File.WriteAllText(tempFile.Path, "<html><head></head><body></body></html>");
				Assert.IsTrue(new BloomHtmlFileTypeHandler().CanMergeFile(tempFile.Path));
			}
		}

		[Test]
		public void CannotPresentANullFile()
		{
			Assert.IsFalse(new BloomHtmlFileTypeHandler().CanPresentFile(null));
		}

		[Test]
		public void CannotPresentAnEmptyFileName()
		{
			Assert.IsFalse(new BloomHtmlFileTypeHandler().CanPresentFile(""));
		}

		[Test]
		public void CannotPresentAFileWithOtherExtension()
		{
			using (var tempFile = TempFile.WithExtension(".ClassData"))
			{
				Assert.IsFalse(new BloomHtmlFileTypeHandler().CanPresentFile(tempFile.Path));
			}
		}

		[Test]
		public void CanPresentAGoodFile()
		{
			using (var tempFile = TempFile.WithExtension(".ClassData"))
			{
				Assert.IsFalse(new BloomHtmlFileTypeHandler().CanPresentFile(tempFile.Path));
			}
		}

		[Test]
		public void Find2WayDifferences_Throws()
		{
			Assert.Throws<NotImplementedException>(() => new BloomHtmlFileTypeHandler().Find2WayDifferences(null, null, null));
		}

		[Test]
		public void ValidateFile_HasNoResultsForValidFile()
		{
			const string data =
				@"<html><head></head><body></body></html>";
			using (var tempFile = new TempFile(data))
			{
				Assert.IsNull(new BloomHtmlFileTypeHandler().ValidateFile(tempFile.Path, new NullProgress()));
			}
		}

		[Test]
		public void Merge_NobodyDidAnything_NoChange()
		{
			const string common = @"<!DOCTYPE html><html><head></head><body><div class='bloom-page' id='1'></div></body></html>";
			const string ours = common;
			const string theirs = common;

			var result = DoMerge(common, ours, theirs);
			result.listener.AssertExpectedConflictCount(0);
			result.listener.AssertExpectedChangesCount(0);
		}

		[Test]
		public void Merge_WeEditTheyDoNothing_GetOurs()
		{
			const string common = @"<html><head></head><body><div class='bloom-page' id='1'></div></body></html>";
			const string ours = @"<html><head></head><body><div class='bloom-page' id='1'>hello</div></body></html>";
			const string theirs = common;
			var result = DoMerge(common, ours, theirs);
			Assert.IsTrue(result.resultString.Contains("hello"));
			result.listener.AssertExpectedConflictCount(0);
			result.listener.AssertExpectedChangesCount(1);
		}

		[Test]
		public void Merge_TheyEditWeDoNothing_GetTheirs()
		{
			const string common = @"<html><head></head><body><div class='bloom-page' id='1'></div></body></html>";
			const string theirs = @"<html><head></head><body><div class='bloom-page' id='1'>hello</div></body></html>";
			const string ours = common;
			var result = DoMerge(common, ours, theirs);
			Assert.IsTrue(result.resultString.Contains("hello"));
			result.listener.AssertExpectedConflictCount(0);
			result.listener.AssertExpectedChangesCount(1);
		}

		[Test]
		public void Merge_BothAddPagesWithUniqueIdsToEmptyDoc_AmbiguousConflict()
		{
			const string common = @"<html><head></head><body></body></html>";
			const string theirs = @"<html><head></head><body><div class='bloom-page' id='1'>one</div></body></html>";
			const string ours = @"<html><head></head><body><div class='bloom-page' id='2'>two</div></body></html>";
			var result = DoMerge(common, ours, theirs);
			Assert.IsTrue(result.resultString.Contains("one"));
			Assert.IsTrue(result.resultString.Contains("two"));
			//nb: the conflict is that we don't have a way of knowing which of these pages should come first
			result.listener.AssertExpectedConflictCount(1);
			result.listener.AssertFirstConflictType<AmbiguousInsertConflict>();
			result.listener.AssertExpectedChangesCount(2);
		}


		[Test]
		public void BothDoSameEdit()
		{
			const string common = @"<html><head></head><body><div class='bloom-page' id='1'>one</div></body></html>";
			const string theirs = @"<html><head></head><body><div class='bloom-page' id='1'>two</div></body></html>";
			const string ours = @"<html><head></head><body><div class='bloom-page' id='1'>two</div></body></html>";

			var result = DoMerge(common, ours, theirs);
			result.listener.AssertExpectedConflictCount(0);
			result.listener.AssertExpectedChangesCount(1);
		}

		[Test]
		public void BothEditWithConflictAndWeWin()
		{
			const string common = @"<html><head></head><body><div class='bloom-page' id='1'>one</div></body></html>";
			const string theirs = @"<html><head></head><body><div class='bloom-page' id='1'>theirs</div></body></html>";
			const string ours = @"<html><head></head><body><div class='bloom-page' id='1'>ours</div></body></html>";

			var result = DoMerge(common, ours, theirs);
			result.listener.AssertExpectedConflictCount(1);
			result.listener.AssertExpectedChangesCount(0); //REVIEW: this is how chorus works, but it seems wrong. Just because there's a conflict, why is it not counted as a change?
		}

		[Test]
		public void BothEditDifferentDataDivElements_GetBoth()
		{
			const string common = @"<html><head></head><body><div id='bloomDataDiv'></div></body></html>";
			const string ours = @"<html><head></head><body><div id='bloomDataDiv'>
										<div data-book='bookTitle' lang='fr'>French Title</div>
									</div></body></html>";
			const string theirs = @"<html><head></head><body><div id='bloomDataDiv'>
										<div data-book='bookTitle' lang='en'>English Title</div>
									</div></body></html>";

			var result = DoMerge(common, ours, theirs);
			result.listener.AssertExpectedConflictCount(0);
			result.listener.AssertExpectedChangesCount(2);
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(result.resultString);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book]",2);
		}

		[Test]
		public void BothAddedSameDataDivElement_GetOurs()
		{
			const string common = @"<html><head></head><body><div id='bloomDataDiv'></div></body></html>";
			const string ours = @"<html><head></head><body><div id='bloomDataDiv'>
										<div data-book='bookTitle' lang='en'>our version</div>
									</div></body></html>";
			const string theirs = @"<html><head></head><body><div id='bloomDataDiv'>
										<div data-book='bookTitle' lang='en'>their version</div>
									</div></body></html>";

			var result = DoMerge(common, ours, theirs);
			result.listener.AssertExpectedConflictCount(1);
			result.listener.AssertExpectedChangesCount(0);
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(result.resultString);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book and text()='our version']", 1);
		}

		private MergeResult DoMerge(string commonAncestor, string ourContent, string theirContent)
		{
			var result = new MergeResult();
			using (var ours = new TempFile(ourContent))
			using (var theirs = new TempFile(theirContent))
			using (var ancestor = new TempFile(commonAncestor))
			{
				var situation = new NullMergeSituation();
				var mergeOrder = new MergeOrder(ours.Path, ancestor.Path, theirs.Path, situation);
				result.listener = new ListenerForUnitTests();
				mergeOrder.EventListener = result.listener;

				new BloomHtmlFileTypeHandler().Do3WayMerge(mergeOrder);
				result.resultString = File.ReadAllText(ours.Path);
			}
			return result;
		}
	}

	internal class MergeResult
	{
		public ListenerForUnitTests listener;
		public string resultString;
	}
}
