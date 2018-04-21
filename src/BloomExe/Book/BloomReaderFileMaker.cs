using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using BloomTemp;
using SIL.Progress;
using SIL.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// This class is the beginnings of a separate place to put code for creating .bloomd files.
	/// Much of the logic is still in BookCompressor. Eventually we might move more of it here,
	/// so that making a bloomd actually starts here and calls BookCompressor.
	/// </summary>
	public class BloomReaderFileMaker
	{
		public const string QuestionFileName = "questions.json";

		public static Book PrepareBookForBloomReader(Book book, BookServer bookServer, TemporaryFolder temp, Color backColor)
		{
			var modifiedBook = BookCompressor.MakeDeviceXmatterTempBook(book, bookServer, temp.FolderPath);

			var jsonPath = Path.Combine(temp.FolderPath, QuestionFileName);
			var questionPages = modifiedBook.RawDom.SafeSelectNodes(
				"//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'questions')]");
			var questions = new List<QuestionGroup>();
			foreach (var page in questionPages.Cast<XmlElement>().ToArray())
			{
				ExtractQuestionGroups(page, questions);
				page.ParentNode.RemoveChild(page);
			}
			var builder = new StringBuilder("[");
			foreach (var question in questions)
			{
				if (builder.Length > 1)
					builder.Append(",\n");
				builder.Append(question.GetJson());

			}
			builder.Append("]");
			File.WriteAllText(jsonPath, builder.ToString());

			// Do this after making questions, as they satisfy the criteria for being 'blank'
			modifiedBook.RemoveBlankPages();

			modifiedBook.Save();

			return modifiedBook;
		}

		/// <summary>
		/// Start with a page, which should appear to the user to contain blocks like this,
		/// separated by blank lines:
		/// Question A
		/// answer1
		/// *correct answer2
		/// answer3
		///
		/// Question B
		/// *correct answer1
		/// answer2
		/// answer3
		///
		/// The actual html encoding will vary. Each line may be wrapped as a paragraph, or there might be br-type line breaks.
		/// We want to make json like this for each question:
		/// {"question":"Question", "answers": [{"text":"answer1"}, {"text":"correct answer", "correct":true}, {"text":"answer2"}]},
		/// </summary>
		public static void ExtractQuestionGroups(XmlElement page, List<QuestionGroup> questionGroups)
		{
			foreach (XmlElement source in page.SafeSelectNodes(".//div[contains(@class, 'bloom-editable')]"))
			{
				var lang = source.Attributes["lang"]?.Value??"";
				if (string.IsNullOrEmpty(lang) || lang == "z")
					continue;
				var group = new QuestionGroup() {lang = lang};
				// this looks weird, but it's just driven by the test cases which are in turn collected
				// from various ways of getting the questions on the page (typing, pasting).
				// See BookReaderFileMakerTests.ExtractQuestionGroups_ParsesCorrectly()
				var separators = new[]
				{
					"<br />", "</p>",
					// now add those may not actually show up in firefox, but are in the pre-existing
					// unit tests, presumably with written-by-hand html?
					"</br>", "<p />"
				};
				var lines = source.InnerXml.Split(separators, StringSplitOptions.None);
				var questions = new List<Question>();
				Question question = null;
				var answers = new List<Answer>();
				foreach (var line in lines)
				{
					var cleanLine = line.Replace("<p>", ""); // our split above just looks at the ends of paragraphs, ignores the starts.
					// Similarly, our split above just looks at the ends of brs, ignores the starts
					//(separate start vs. end br elements might not occur in real FF tests, see note above).
					cleanLine = cleanLine.Replace("<br>", "");
					cleanLine = cleanLine.Replace("\u200c", "");
					if (string.IsNullOrWhiteSpace(cleanLine))
					{
						// If we've accumulated an actual question and answers, put it in the output.
						// otherwise, we're probably just dealing with leading white space before the first question.
						if (answers.Any())
						{
							question.answers = answers.ToArray();
							answers.Clear();
							questions.Add(question);
							question = null;
						}
					} else
					{
						var trimLine = cleanLine.Trim();
						if (question == null)
						{
							// If we don't already have a question being built, this first line is the question.
							question = new Question() { question=trimLine};
						}
						else
						{
							// We already got the question, and haven't seen a blank line since,
							// so this is one of its answers.
							var correct = trimLine.StartsWith("*");
							if (correct)
							{
								trimLine = trimLine.Substring(1).Trim();
							}
							answers.Add(new Answer() {text=trimLine, correct = correct});
						}
					}
				}
				if (answers.Any())
				{
					// Save the final question.
					question.answers = answers.ToArray();
					questions.Add(question);
				}
				if (questions.Any())
				{
					// There may well be editable divs, especially automatically generated for active langauges,
					// which don't have any questions. Skip them. But if we got at least one, save it.
					group.questions = questions.ToArray();
					questionGroups.Add(group);
				}
			}
		}
	}
}
