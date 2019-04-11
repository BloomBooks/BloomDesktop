using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Bloom.web.controllers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace BloomTests.web.controllers
{
	public class AudioSegmentationApiTests
	{
		[Test]
		public void ParseJson_Valid_ParsesAllParams()
		{
			string inputJson = "{\"audioFilenameBase\":\"id0\",\"audioTextFragments\":[{\"fragmentText\":\"Sentence 1.\",\"id\":\"id1\"},{\"fragmentText\":\"Sentence 2.\",\"id\":\"id2\"},{\"fragmentText\":\"Sentence 3.\",\"id\":\"id3\"}],\"lang\":\"es\"}";

			var request = AudioSegmentationApi.ParseJson(inputJson);
			
			Assert.That(request.audioFilenameBase, Is.EqualTo("id0"));

			Assert.That(request.audioTextFragments, Is.Not.Null);
			Assert.That(request.audioTextFragments.Length, Is.EqualTo(3));
			for (int i = 0; i < request.audioTextFragments.Length; ++i)
			{
				Assert.That(request.audioTextFragments[i].fragmentText, Is.EqualTo($"Sentence {i + 1}."));
				Assert.That(request.audioTextFragments[i].id, Is.EqualTo($"id{i+1}"));
			}

			Assert.That(request.lang, Is.EqualTo("es"));
		}

		[TestCase("1.000\t4.980\tf000001")]
		public void ParseTimingFileTSV_ValidSingleLineInput_ParsedSuccessfully(string inputLine)
		{
			var timingRanges = AudioSegmentationApi.ParseTimingFileTSV(new string[] { inputLine });

			Assert.That(timingRanges.Count, Is.EqualTo(1));
			Assert.That(timingRanges[0].Item1, Is.EqualTo("1.000"), "Start timing");
			Assert.That(timingRanges[0].Item2, Is.EqualTo("4.980"), "End timing");
		}

		[Test]
		public void ParseTimingFileTSV_MultiLineInputWithMissingStart_ParsedSuccessfully()
		{
			string[] inputs = new string[] { "1.000\t4.980\tf000001", "\t10.000\tf000002" };
			var timingRanges = AudioSegmentationApi.ParseTimingFileTSV(inputs);

			Assert.That(timingRanges.Count, Is.EqualTo(2));
			Assert.That(timingRanges[0].Item1, Is.EqualTo("1.000"), "Start timing 1");
			Assert.That(timingRanges[0].Item2, Is.EqualTo("4.980"), "End timing 1");
			Assert.That(timingRanges[1].Item1, Is.EqualTo("4.980"), "Start timing 2");
			Assert.That(timingRanges[1].Item2, Is.EqualTo("10.000"), "End timing 2");
		}

		[Test]
		public void ParseTimingFileTSV_MultiLineInputWithMissingEnd_ParsedSuccessfully()
		{
			string[] inputs = new string[] { "1.000\t\tf000001", "4.980\t10.000\tf000002" };
			var timingRanges = AudioSegmentationApi.ParseTimingFileTSV(inputs);

			Assert.That(timingRanges.Count, Is.EqualTo(2));
			Assert.That(timingRanges[0].Item1, Is.EqualTo("1.000"), "Start timing 1");
			Assert.That(timingRanges[0].Item2, Is.EqualTo("4.980"), "End timing 1");
			Assert.That(timingRanges[1].Item1, Is.EqualTo("4.980"), "Start timing 2");
			Assert.That(timingRanges[1].Item2, Is.EqualTo("10.000"), "End timing 2");
		}

		[Test]
		public void SanitizeTextForESpeakPreview_Quotes_Stripped()
		{
			string unsafeText = "One\" && espeak -v en \"Two.";
			string safeText = AudioSegmentationApi.SanitizeTextForESpeakPreview(unsafeText);

			Assert.That(safeText, Is.Not.EqualTo(unsafeText));	// Definitely must pass
			Assert.That(safeText, Is.EqualTo("One  && espeak -v en  Two."));	// There are other acceptable variations that it could equal
		}


		[Test]
		public void SanitizeTextForESpeakPreview_Newlines_ReplacedWithSpace()
		{
			string unsafeText = "One\nTwo";
			string safeText = AudioSegmentationApi.SanitizeTextForESpeakPreview(unsafeText);

			Assert.That(safeText, Is.Not.EqualTo(unsafeText));
			Assert.That(safeText, Is.EqualTo("One Two"));   // There are other acceptable variations that it could equal
		}

		[Test]
		public void ApplyOrthographyConversion_Ascii_MapJToY()
		{
			// Setup
			string tempFileName = Path.GetTempFileName();
			File.WriteAllText(tempFileName, "j\ty");

			var fragments = new List<string>();
			fragments.Add("jojo");

			// System Under Test
			List<string> mappedFragments = AudioSegmentationApi.ApplyOrthographyConversion(fragments, tempFileName).ToList();

			// Cleanup
			File.Delete(tempFileName);

			// Verification
			Assert.That(mappedFragments[0], Is.EqualTo("yoyo"));
		}

		// Try a few encodings because the user could pick one of several options in their favorite text editor when creating their settings file.
		[TestCase("utf8")]
		[TestCase("Unicode")]
		[TestCase("BigEndianUnicode")]
		public void ApplyOrthographyConversion_NonAscii_SpecialCharsRecognizedAndConverted(string encodingStr)
		{
			// Parse test case parameter
			Encoding encoding = Encoding.Default;
			if (encodingStr == "utf8")
			{
				encoding = Encoding.UTF8;
			}
			else if (encodingStr.Equals("Unicode", StringComparison.OrdinalIgnoreCase) || encodingStr == "utf16" || encodingStr == "utf16le")
			{
				encoding = Encoding.Unicode;
			}
			else if (encodingStr.Equals("BigEndianUnicode", StringComparison.OrdinalIgnoreCase) || encodingStr == "utf16be")
			{
				encoding = Encoding.BigEndianUnicode;
			}

			// Setup the settings file that the System Under Test will read
			// Converts "άλφα" into "alpha"
			string[] lines = new string[]
			{
				"ά\ta",
				"λ\tl",
				"φ\tph",
				"α\ta"
			};

			string tempFileName = Path.GetTempFileName();
			File.WriteAllLines(tempFileName, lines, encoding);

			// Other Setup
			var fragments = new List<string>();
			fragments.Add("άλφα");

			// System Under Test
			List<string> mappedFragments = AudioSegmentationApi.ApplyOrthographyConversion(fragments, tempFileName).ToList();

			// Cleanup
			File.Delete(tempFileName);

			// Verification
			Assert.That(mappedFragments[0], Is.EqualTo("alpha"));
		}
	}
}
