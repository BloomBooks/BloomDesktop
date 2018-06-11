using System.IO;
using System.Linq;
using Bloom.Publish;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.Book
{
	public class AccessibilityCheckersTests : BookTestsBase
	{
		[SetUp]
		public void SetupFixture()
		{
			_collectionSettings = CreateDefaultCollectionsSettings();
		}

		[Test]
		public void CheckDescriptionsForAllImages_No_Images_NoProblems()
		{
			var html = @"<html>
					<body>
						<div class='bloom-page'>
							<div class='marginBox'>
								<div class='bloom-translationGroup normal-style'>
									<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='en'>
									</div>
								</div>
							</div>
						</div>
					</body>
				</html>";
			var testBook = CreateBookWithPhysicalFile(html);
			var results = AccessibilityCheckers.CheckDescriptionsForAllImages(testBook);
			Assert.AreEqual(0, results.Count(), "No problems were expected.");
		}

		[Test]
		public void CheckDescriptionsForAllImages_DescriptionInWrongLang()
		{
			var testBook = GetBookWithImage(@"<div class='bloom-translationGroup bloom-imageDescription'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='a2b'>
											<p>A flower.</p>
										</div>
									</div>
								</div>");
			var results = AccessibilityCheckers.CheckDescriptionsForAllImages(testBook);
			Assert.AreEqual(1, results.Count(), "Should point out missing language description");
		}

		[Test]
		public void CheckDescriptionsForAllImages_DescriptionInCorrectLang()
		{
			var testBook = GetBookWithImage(
				$@"<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable' lang='{this._collectionSettings.Language1Iso639Code}'>
						<p>A flower.</p>
					</div>
				</div>");
			var results = AccessibilityCheckers.CheckDescriptionsForAllImages(testBook);
			Assert.AreEqual(0, results.Count(), "No problems were expected");
		}

		[Test]
		[TestCase("3", "unused")]
		[TestCase(null, "Cover Page")]
		public void CheckDescriptionsForAllImages_DescriptionEmpty(string pageNumber, string pageLabel)
		{
			var testBook = GetBookWithImage(
				$@"<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable' lang='{this._collectionSettings.Language1Iso639Code}'>
						<p>  </p>
					</div>
				</div>",
				pageNumber, pageLabel);
			var results = AccessibilityCheckers.CheckDescriptionsForAllImages(testBook);
			Assert.AreEqual(1, results.Count(), "Should point out missing image description");
			var expected = pageNumber ?? pageLabel;

			Assert.AreEqual($"Missing image description on page {expected}", results.First());
		}

		[Test]
		public void CheckDescriptionsForAllImages_MutliplePages()
		{
			var divWithoutCorrectLangDescription =
				$@"<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable' lang='{this._collectionSettings.Language1Iso639Code}'>
						<p>  </p>
					</div>
				</div>";

			var divWithDescription =
				$@"<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable' lang='{this._collectionSettings.Language1Iso639Code}'>
						<p>A nice flower</p>
					</div>
				</div>";

			var html = $@"<html> <body>
					{MakeHtmlForPageWithImage(divWithDescription)}
					{MakeHtmlForPageWithImage(divWithoutCorrectLangDescription)}
					{MakeHtmlForPageWithImage(divWithDescription)}
					{MakeHtmlForPageWithImage(divWithoutCorrectLangDescription)}
				</body> </html>";
			var testBook = CreateBookWithPhysicalFile(html);
			var results = AccessibilityCheckers.CheckDescriptionsForAllImages(testBook);
			Assert.AreEqual(2, results.Count(), "Should point out missing image description");
		}

		/* -----------------------------------------------------------------------------------*/
		/* ----------------------CheckAudioForAllText-----------------------------------------*/
		/* -----------------------------------------------------------------------------------*/

		[TestCase("<p><span id='iExist' class='audio-sentence'>A flower.</span></p>")]
		[TestCase(@"<p><span id='iExist' class='audio-sentence'>A flower.</span>
					<span id='iExist' class='audio-sentence'>A dog.</span></p>")]
		public void CheckAudioForAllText_NoErrors(string content)
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_collectionSettings.Language1Iso639Code
				}'>
											{content}
										</div>
									</div>
								</div>");

			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.AreEqual(0, results.Count(), "No errors were expected");
		}

		[TestCase("<p>A flower.</p>")]
		[TestCase(@"<p><span id='iExist' class='audio-sentence'>A flower.</span>
					A dog.</p>")]
		[TestCase(@"<p><span id='iExist' class='audio-sentence'>A flower.</span></p>
					<p>A dog.</p>")]
		public void CheckAudioForAllText_TextWithoutSpans(string content)
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_collectionSettings.Language1Iso639Code
				}'>
											{content}
										</div>
									</div>
								</div>");
			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.Greater(results.Count(), 0, "Error should have been reported");
		}

		[TestCase("<p><span id='bogus123' class='audio-sentence'>A flower.</span></p>")]
		[TestCase(@"<p><span id='iExist' class='audio-sentence'>A flower.</span></p>
					<p><span id='bogus456' class='audio-sentence'>A dog.</span</p>")]
		public void CheckAudioForAllText_SpansAudioMissing(string content)
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_collectionSettings.Language1Iso639Code
				}'>
											{content}
										</div>
									</div>
								</div>");
			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.Greater(results.Count(), 0, "Error should have been reported");
		}

		[Test]
		public void CheckAudioForAllText_AudioMissingInImageDescriptionOnly_DoesNotReport()
		{
			var testBook = GetBookWithImage($@"<div class='bloom-translationGroup bloom-imageDescription'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_collectionSettings.Language1Iso639Code
				}'>
											<p>record me!</p>
										</div>
									</div>
								</div>");
			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.AreEqual(0, results.Count(), "No error should have been reported");
		}

		/* -----------------------------------------------------------------------------------*/
		/* ----------------------CheckAudioForAllImageDescriptions----------------------------*/
		/* -----------------------------------------------------------------------------------*/
		// Note, we do not retest all the corner cases that were tested for
		// CheckAudioForAllText(), because the code is shared beteween them

		[TestCase(0, "<p><span id='iExist' class='audio-sentence'>A flower.</span></p>")]
		[TestCase(1, "<p><span id='bogus123' class='audio-sentence'>A flower.</span></p>")]
		public void CheckAudioForAllImageDescriptions_AudioMissing(int numberOfErrorsExpected, string content)
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup bloom-imageDescription'>
								<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_collectionSettings.Language1Iso639Code
				}'>
									{content}
								</div>
							</div>
						</div>");
			var results = AccessibilityCheckers.CheckAudioForAllImageDescriptions(testBook);
			Assert.AreEqual(numberOfErrorsExpected, results.Count(), "Number of errors does not match expected");
		}

		private Bloom.Book.Book GetBookWithImage(string translationGroupText, string pageNumber = "1",
			string pageLabel = "Some page label")
		{
			var html = $@"<html> <body>
					{MakeHtmlForPageWithImage(translationGroupText, pageNumber, pageLabel)}
				</body> </html>";
			return CreateBookWithPhysicalFile(html);
		}

		private string MakeHtmlForPageWithImage(string translationGroupText, string pageNumber = "1",
			string pageLabel = "Some page label")
		{
			return $@"<div class='bloom-page' data-page-number='{pageNumber ?? ""}'>
		<div class='pageLabel'>{pageLabel}</div>
		<div class='marginBox'>
				<div class='bloom-imageContainer'>
					<img src='flower.png'></img>
					{translationGroupText}
				</div>
			</div>
		</div>";
		}

		private Bloom.Book.Book MakeBookWithOneAudioFile(string translationGroupText, string pageNumber = "1",
			string pageLabel = "Some page label")
		{
			var html = $@"<html> <body>
					{GetHtmlForPage(translationGroupText, pageNumber, pageLabel)}
				</body> </html>";
			var book = CreateBookWithPhysicalFile(html);

			var audioDir = AudioProcessor.GetAudioFolderPath(book.FolderPath);
			Directory.CreateDirectory(audioDir);
			File.WriteAllText(Path.Combine(audioDir, "iExist.mp3"), "hello");

			return book;
		}

		private string GetHtmlForPage(string translationGroupText, string pageNumber = "1",
			string pageLabel = "Some page label")
		{
			return $@"<div class='bloom-page' data-page-number='{pageNumber ?? ""}'>
		<div class='pageLabel'>{pageLabel}</div>
		<div class='marginBox'>
					{translationGroupText}
		</div>";
		}
	}
}
