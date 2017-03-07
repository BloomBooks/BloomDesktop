using NUnit.Framework;

namespace Bloom.web.controllers
{
	[TestFixture]
	public class PageTemplatesApiTests
	{
		[Test]
		public void GetBookTemplatePaths_NoOtherTemplates_ReturnsJustSourceTemplate()
		{
			var pathToCurrentTemplateHtml ="c:\\some\\templates\\here\\\\basic book.htm";
			var sourceBookPaths = new []{ "c:\\some\\templates\\here\\\\basic book.htm" };
			var result = PageTemplatesApi.GetBookTemplatePaths(pathToCurrentTemplateHtml, sourceBookPaths);
			Assert.AreEqual(0,result.IndexOf(pathToCurrentTemplateHtml));
			Assert.AreEqual(1, result.Count);
		}

		[Test]
		public void GetBookTemplatePaths_NonBasicBookOriginal_BasicBookOfferedSecond()
		{
			var pathToCurrentTemplateHtml = "c:\\some\\templates\\here\\originalTemplate.html";
			var pathToBasicBook = "c:\\installation dir\\templates\\basic book.html";
			var pathToAlphabet = "c:\\some\\templates\\here\\alphabet template.html";
			var pathToZebra = "c:\\some\\templates\\here\\zebras template.html";
			var sourceBookPaths = new[]
			{
				pathToAlphabet,
				"c:\\installation dir\\templates\\some book that is not a template at all.html",
				pathToBasicBook,
				pathToCurrentTemplateHtml,
				pathToZebra
			};
			var result = PageTemplatesApi.GetBookTemplatePaths(pathToCurrentTemplateHtml, sourceBookPaths);
			Assert.AreEqual(4, result.Count);
			Assert.AreEqual(0, result.IndexOf(pathToCurrentTemplateHtml),
				"Template used to make the book should be first in the list.");
			Assert.AreEqual(1, result.IndexOf(pathToBasicBook),
				"Basic Book should move ahead of Arithmetic to be second in list when it is not first.");
			Assert.AreEqual(2, result.IndexOf(pathToAlphabet), "Arithmetic should be third.");
			Assert.AreEqual(3, result.IndexOf(pathToZebra), "Zebra should be last.");
		}

		[Test]
		public void GetBookTemplatePaths_TemplateInThisCollection_ReturnsTemplate()
		{
			var pathToCurrentTemplateHtml = "c:\\some\\templates\\here\\\\basic book.htm";
			var sourceBookPaths = new[] {"c:\\some\\templates\\here\\\\my template.htm"};
			var result = PageTemplatesApi.GetBookTemplatePaths(pathToCurrentTemplateHtml, sourceBookPaths);
			Assert.AreEqual(1, result.IndexOf(sourceBookPaths[0]));
		}

		[Test]
		public void GetBookTemplatePaths_TemplateInThisCollectionAndSourceBooks_ReturnsItOnlyOnce()
		{
			var pathToCurrentTemplateHtml = "c:\\some\\templates\\here\\\\basic book.htm";
			var sourceBookPaths = new[]
				{"c:\\some\\templates\\here\\\\my template.htm", "c:\\some\\templates\\here\\\\my template.htm"};
			var result = PageTemplatesApi.GetBookTemplatePaths(pathToCurrentTemplateHtml, sourceBookPaths);
			Assert.AreEqual(2, result.Count, "Should only list my template once.");
		}

		[Test]
		public void GetBookTemplatePaths_TwoTemplatesWithSameName_ListsBoth()
		{
			var pathToCurrentTemplateHtml = "c:\\some\\templates\\here\\\\basic book.htm";
			var sourceBookPaths = new[]
				{"c:\\some\\templates\\here\\\\my template.htm", "c:\\other\\templates\\overhere\\\\my template.htm"};
			var result = PageTemplatesApi.GetBookTemplatePaths(pathToCurrentTemplateHtml, sourceBookPaths);
			Assert.AreEqual(3, result.Count, "Should list each unique path, not name.");
		}

		/// <summary>
		/// This is what was actually happening: the drive letter was upper case in one instance, and lower in the other
		/// </summary>
		[Test]
		[Platform(Exclude = "Linux", Reason = "On Linux, case is significant")]
		public void GetBookTemplatePaths_TwoTemplatesWithSameNameButDifferentCase_ReturnsItOnlyOnce()
		{
				var pathToCurrentTemplateHtml = "c:\\some\\templates\\here\\\\basic book.htm";
				var sourceBookPaths = new[]
					{"C:\\Some\\Templates\\Here\\\\My template.htm", "c:\\some\\templates\\here\\\\my template.htm"};
				var result = PageTemplatesApi.GetBookTemplatePaths(pathToCurrentTemplateHtml, sourceBookPaths);
				Assert.AreEqual(2, result.Count, "Should only list my template once.");
		}
	}
}
