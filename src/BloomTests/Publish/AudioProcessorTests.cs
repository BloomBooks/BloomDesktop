using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Bloom.Book;
using Bloom.Publish;

namespace BloomTests.Publish
{
	public class AudioProcessorTests
	{
		[Test]
		public void AudioProcessorGetTrueForAllAudioElements_MixedSpansWithIds_OnlyAudioSpansAreProcessed()
		{
			// Test Setup //
			// Note: This HTML is purely hypothetical for exercising the unit under test and not derived from any real use case.
			// In particular, this tests ensures that the code is not just looking for spans with an ID as the necessary and sufficient condition to be an audio-snetnece.
			var dom = new HtmlDom(
				@"<html>
						<head></head>
						<body>
							<div id='page1'><div class='bloom-editable'><p><span id='audioRecordingGuid1' class='audio-sentence'>Page 1 Sentence 1</span></p></div></div>
							<div id='page2'><div class='bloom-editable'><p><span id='videoRecordingGuid1' class='video-sentence'>Page 2 Sentence 1</span></p></div></div>
							<div id='page3'><div class='bloom-editable'><p><span id='audioRecordingGuid3' class='audio-sentence'>Page 3 Sentence 1</span></p></div></div>
							<div id='page4'><div class='bloom-editable video-sentence' id='videoRecordingGuid2'><p>Page 4, Paragraph 1, Sentence 1</p><p>Page 4, Paragraph 2, Sentence 1</p></div></div>
						</body>
				</html>");


			Func<string, string, bool> failOnInvalidInputPredicate = ReturnTrueOnlyIfAudioSentence;
			var runner = new Microsoft.VisualStudio.TestTools.UnitTesting.PrivateType(typeof(AudioProcessor));
			bool result = (bool)runner.InvokeStatic("GetTrueForAllAudioSentenceElements", "bookFolderPath", dom.RawDom, failOnInvalidInputPredicate);

			Assert.IsTrue(result, "An invalid input was passed to the predicate. Make sure the unit under test removes all invalid inputs.");
		}

		// This helper will return false if it receives a non-audio span, which allows us to test that the underlying function is properly processing only audio sentences.
		private static bool ReturnTrueOnlyIfAudioSentence(string wavPath, string mp3Path)
		{
			// Note: Don't just search audio, because it is automatically in a folder called audio.
			string audioSentenceFilenameWithoutExtension = "audioRecordingGuid";	
			return wavPath.Contains(audioSentenceFilenameWithoutExtension) && mp3Path.Contains(audioSentenceFilenameWithoutExtension);
		}


		[Test]
		public void AudioProcessorGetTrueForAllAudioElements_AudioSpansAndDivs_CheckAudioDivsAreProcessedToo()
		{
			// Test Setup //
			// This one is designed to return false is div's are processed and return true if div's are ignored.  This lets us test whether divs are actually processed.
			// Note: This HTML is purely hypothetical for exercising the unit under test and not derived from any real use case.
			var dom = new HtmlDom(
				@"<html>
						<head></head>
						<body>
							<div id='page1'><p><span id='audioRecordingGuid1' class='audio-sentence'>Page 2 Sentence 1</span></p></div>
							<div id='page3'><p><span id='audioRecordingGuid2' class='audio-sentence'>Page 2 Sentence 1</span></p></div>
							<div id='page3'><div id='specialAudioRecordingGuid' class='audio-sentence'><p>Page 3, Paragraph 1, Sentence 1</p><p>Page 3, Paragraph 2, Sentence 1</p></div></div>
						</body>
				</html>");


			Func<string, string, bool> failIfSearchTargetFound = ReturnFalseIfTargetFound;
			var runner = new Microsoft.VisualStudio.TestTools.UnitTesting.PrivateType(typeof(AudioProcessor));
			bool result = (bool)runner.InvokeStatic("GetTrueForAllAudioSentenceElements", "bookFolderPath", dom.RawDom, failIfSearchTargetFound);

			Assert.IsFalse(result, "The method should've processed specialAudioRecordingGuid but it actually didn't");
		}

		// This helper will return false if it receives a non-audio span, which allows us to test that the underlying function is properly processing only audio sentences.
		private static bool ReturnFalseIfTargetFound(string wavPath, string mp3Path)
		{
			// Note: Don't just search audio, because it is automatically in a folder called audio.
			string target = "specialAudioRecordingGuid";
			return !wavPath.Contains(target);
		}

	}
}
