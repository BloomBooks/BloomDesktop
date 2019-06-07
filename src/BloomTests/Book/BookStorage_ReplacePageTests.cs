using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace BloomTests.Book
{
	/// <summary>
	/// This class tests a particular method of BookStorage that does not require the Setup()
	/// used for the bulk of BookStorage tests.
	/// </summary>
	[TestFixture()]
	public class BookStorage_ReplacePageTests
	{
		private void TryReplace(string input, string pageId, string replacement, string expectedOutput)
		{
			using (var inputStream = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(input))))
			using (var output = new MemoryStream())
			using (var outputStream = new StreamWriter(output))
			{
				BookStorage.ReplacePage(pageId, inputStream, outputStream, replacement);
				outputStream.Flush();

				output.Position = 0;
				string result = new StreamReader(output).ReadToEnd();
				Assert.That(result, Is.EqualTo(expectedOutput));
			}
		}

		[Test]
		public void NoChangeIfDivNotFound()
		{
			TryReplace("rubbish", "ignored", "unused", "rubbish");
		}

		// This one makes sure it can skip a previous page; find the following page;
		// skip various divs within the page to be replaced;
		// not confuse the following page with one that has a class similar to bloom-page;
		// match the following page when bloom-page is in the middle of the class attr;
		[TestCase("<div id=\"prePage\">some other stuff</div>",
			"<div id=\"234-abc\">the <div>old</div> <div class=\"not bloom page\">stuff</div></div>",
			"<div class=\"rubbish bloom-page more rubbish>more stuff</div>")]
		// Here the id attr of the previous page isn't its first attribute, and there are two previous pages.
		// the page to replace has another level of nested divs, and a more complex set of non-id attributes.
		// bloom-page is the first class in the following div
		[TestCase("<div class=\"bloom-page\" id=\"prePage\">some other stuff</div> <div class=\"bloom-page\" id=\"anotherpage\">some other stuff</div>",
			"<div class=\"bloom-page\" data-book=\"something\" id=\"234-abc\">the <div>old</div> <div class=\"not bloom page\">stuff<div> another nested one</div></div></div>",
			"<div class=\"bloom-page more rubbish>more stuff</div>")]
		// Here we're replacing the very first page. The target page has a trick attribute whose name starts with id.
		// bloom-page is the only thing in the class attr of the following page.
		[TestCase("",
			"<div id5=\"nonsense\" id=\"234-abc\">the <div>old</div> <div class=\"not bloom page\">stuff</div></div>",
			"<div id=\"nextPage\" class=\"bloom-page\">more stuff</div>")]
		public void ReplacesNonFinalPage(string previous, string pageToReplace, string following)
		{
			var frame = "<html><body>{0}{1}{2}</body></html>";
			var input = string.Format(frame, previous, pageToReplace, following);
			string newPage = "<div id=\"234-abc\">some new stuff</div>";
			var expected = string.Format(frame, previous, newPage, following );
			TryReplace(input, "234-abc", newPage, expected);
		}

		[Test]
		public void ReplacesFinalPage()
		{
			var frame = "<html><body><div id=\"prePage\">some other stuff</div>{0}</body></html>";
			var input = string.Format(frame, "<div id=\"234-abc\">the <div>old</div> <div class=\"not bloom page\">stuff</div></div>");
			string newPage = "<div id=\"234-abc\">some new stuff</div>";
			var expected = string.Format(frame, newPage);
			TryReplace(input, "234-abc", newPage, expected);
		}
	}
}
