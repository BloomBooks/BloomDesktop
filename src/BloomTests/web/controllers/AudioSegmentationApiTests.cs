using System;
using System.Collections.Generic;
using System.Linq;
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
	}
}
