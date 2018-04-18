using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Gecko.WebIDL;
using NUnit.Framework;
namespace BloomTests.Book
{
	[TestFixture]
	public class BookReaderFileMakerTests
	{
		// Got this pasting from notepad and word with the extra line in between the Qs already, into Bloom 4.2.
		[TestCase("<p>first<br>one<br>*two<br><br>second<br>*aa<br>bb<br></p>")]
		// Got this by typing instead of pasting in Bloom
		[TestCase(@"<p>first<br></p><p>one</p><p>*two</p><p><br></p><p>second</p><p>*aa</p><p>bb</p>")]
		// got this from pasting from notepad into Bloom 4.1
		[TestCase(("<p>first<br>one<br>*two<br></p><p><br></p><p>second<br>*aa<br>bb</p>"))]
		// got this from pasting from notepad into Firefox 59
		[TestCase("<p>first<br>one<br>*two<br></p><p><br></p><p>second<br>*aa<br>bb<br></p>")]
		public void ExtractQuestionGroups_ParsesCorrectly(string contents)
		{
			contents = contents.Replace("<br>", "<br/>"); // convert from html to xml
			var page = new XmlDocument();
			page.LoadXml(@"<div><div class='bloom-editable' lang='abc'>"+contents+"</div></div>");
			var questionGroups = new List<QuestionGroup>();
			BloomReaderFileMaker.ExtractQuestionGroups(page.DocumentElement, questionGroups);
			Assert.AreEqual(1, questionGroups.Count);
			Assert.AreEqual(2, questionGroups[0].questions.Length);
			Assert.AreEqual("first", questionGroups[0].questions[0].question);
			Assert.AreEqual(2, questionGroups[0].questions[0].answers.Length);
			Assert.AreEqual("one", questionGroups[0].questions[0].answers[0].text);
			Assert.AreEqual("two", questionGroups[0].questions[0].answers[1].text);
			Assert.IsFalse(questionGroups[0].questions[0].answers[0].correct);
			Assert.IsTrue(questionGroups[0].questions[0].answers[1].correct);

			Assert.AreEqual("second", questionGroups[0].questions[1].question);
			Assert.AreEqual(2, questionGroups[0].questions[1].answers.Length);
			Assert.AreEqual("aa", questionGroups[0].questions[1].answers[0].text);
			Assert.AreEqual("bb", questionGroups[0].questions[1].answers[1].text);
			Assert.IsTrue(questionGroups[0].questions[1].answers[0].correct);
			Assert.IsFalse(questionGroups[0].questions[1].answers[1].correct);
		}
	}
}
