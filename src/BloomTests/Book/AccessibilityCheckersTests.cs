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
			_bookData = CreateDefaultBookData();
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
					<div class='bloom-editable' lang='{_bookData.Language1.Iso639Code}'>
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
					<div class='bloom-editable' lang='{_bookData.Language1.Iso639Code}'>
						<p>  </p>
					</div>
				</div>",
				pageNumber, pageLabel);
			var results = AccessibilityCheckers.CheckDescriptionsForAllImages(testBook);
			Assert.AreEqual(1, results.Count(), "Should point out missing image description");
			var expected = pageNumber ?? pageLabel;

			Assert.AreEqual($"Missing image description on page {expected}", results.First().message);
		}

		[Test]
		public void CheckDescriptionsForAllImages_MutliplePages()
		{
			var divWithoutCorrectLangDescription =
				$@"<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable' lang='{_bookData.Language1.Iso639Code}'>
						<p>  </p>
					</div>
				</div>";

			var divWithDescription =
				$@"<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable' lang='{_bookData.Language1.Iso639Code}'>
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

		[Test]
		public void CheckDescriptionsForAllImages_AriaHidden()
		{
			var divWithoutCorrectLangDescription =
				$@"<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable' lang='{_bookData.Language1.Iso639Code}'>
						<p>  </p>
					</div>
				</div>";

			var divWithDescription =
				$@"<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable' lang='{_bookData.Language1.Iso639Code}'>
						<p>A nice flower</p>
					</div>
				</div>";

			var html = $@"<html> <body>
					{MakeHtmlForPageWithImage(divWithDescription)}
					{MakeHtmlForPageWithImage(divWithoutCorrectLangDescription)}
					{MakeHtmlForPageWithImageAriaHidden(divWithDescription)}
					{MakeHtmlForPageWithImageAriaHidden(divWithoutCorrectLangDescription)}
				</body> </html>";
			var testBook = CreateBookWithPhysicalFile(html);
			var results = AccessibilityCheckers.CheckDescriptionsForAllImages(testBook);
			Assert.AreEqual(1, results.Count(), "Should point out missing image description");
		}

		/* -----------------------------------------------------------------------------------*/
		/* ----------------------CheckAudioForAllText-----------------------------------------*/
		/* -----------------------------------------------------------------------------------*/

		[TestCase("<p><span id='iExist' class='audio-sentence'>A flower.</span></p>")]
		[TestCase("<p><div id='iExist' class='audio-sentence'>A flower.</div></p>")]
		[TestCase(@"<p><span id='iExist' class='audio-sentence'>A flower.</span>
					<span id='iExist' class='audio-sentence'>A dog.</span></p>")]
		[TestCase(@"<p><div id='iExist' class='audio-sentence'>A flower.</div>
					<div id='iExist' class='audio-sentence'>A dog.</div></p>")]
		[TestCase(@"<label>This is bubble text</label>")]
		public void CheckAudioForAllText_NoErrors(string content)
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_bookData.Language1.Iso639Code
				}'>
											{content}
										</div>
									</div>
								</div>");

			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.AreEqual(0, results.Count(), "No errors were expected");
		}

		[Test]
		public void CheckAudioForAllText_RecordingOnBloomEditable_NoErrors()
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on audio-sentence' id='iExist' lang='{
					_bookData.Language1.Iso639Code
				}'>
											<p>This is the text</p>
										</div>
									</div>
								</div>");

			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.AreEqual(0, results.Count(), "No errors were expected");
		}

		[TestCase("<p>A flower.</p>")]
		[TestCase(@"<p><span id='iExist' class='audio-sentence'>A flower.</span>
					A dog.</p>")]
		[TestCase(@"<p><div id='iExist' class='audio-sentence'>A flower.</div>
					A dog.</p>")]
		[TestCase(@"<p><span id='iExist' class='audio-sentence'>A flower.</span></p>
					<p>A dog.</p>")]
		public void CheckAudioForAllText_TextWithoutSpans(string content)
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_bookData.Language1.Iso639Code
				}'>
											{content}
										</div>
									</div>
								</div>");
			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.Greater(results.Count(), 0, "Error should have been reported");
		}

		[TestCase("<p><span id='bogus123' class='audio-sentence'>A flower.</span></p>")]
		[TestCase("<p><div id='bogus123' class='audio-sentence'>A flower.</div></p>")]
		[TestCase(@"<p><span id='iExist' class='audio-sentence'>A flower.</span></p>
					<p><span id='bogus456' class='audio-sentence'>A dog.</span</p>")]
		[TestCase(@"<p><div id='iExist' class='audio-sentence'>A flower.</div></p>
					<p><div id='bogus456' class='audio-sentence'>A dog.</div</p>")]
		public void CheckAudioForAllText_SpansAudioMissing(string content)
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_bookData.Language1.Iso639Code
				}'>
											{content}
										</div>
									</div>
								</div>");
			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.Greater(results.Count(), 0, "Error should have been reported");
		}

		[Test]
		public void CheckAudioForAllText_TextInRandomLangButVisibleAndNotRecorded_GivesError()
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup'>
										<div class='bloom-editable bloom-visibility-code-on' lang=''>
											<p>hello</p>
										</div>
									</div>
								</div>");
			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.AreEqual(1,results.Count(), "The text has to be recorded because it is visible");
		}

		[Test]
		public void CheckAudioForAllText_TextInNationalLanguageNotVisible_NotRecorded()
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup'>
										<div class='bloom-editable bloom-visibility-code-off' lang='{
					_bookData.Language2.Iso639Code
				}'>
											<p>hello</p>
										</div>
									</div>
								</div>");
			var results = AccessibilityCheckers.CheckAudioForAllText(testBook);
			Assert.AreEqual(0, results.Count(), "Since the text is not visible, should not give error if not recorded");
		}

		[Test]
		public void CheckAudioForAllText_AudioMissingInImageDescriptionOnly_DoesNotReport()
		{
			var testBook = GetBookWithImage($@"<div class='bloom-translationGroup bloom-imageDescription'>
										<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_bookData.Language1.Iso639Code
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
		[TestCase(0, "<p><div id='iExist' class='audio-sentence'>A flower.</div></p>")]
		[TestCase(1, "<p><span id='bogus123' class='audio-sentence'>A flower.</span></p>")]
		[TestCase(1, "<p><div id='bogus123' class='audio-sentence'>A flower.</div></p>")]
		public void CheckAudioForAllImageDescriptions_AudioMissing(int numberOfErrorsExpected, string content)
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup bloom-imageDescription'>
								<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_bookData.Language1.Iso639Code
				}'>
									{content}
								</div>
							</div>
						</div>");
			var results = AccessibilityCheckers.CheckAudioForAllImageDescriptions(testBook);
			Assert.AreEqual(numberOfErrorsExpected, results.Count(), "Number of errors does not match expected");
		}

		[Test]
		public void CheckAudioForAllImageDescriptions_AudioFolderMissing_JustReturnsNormalMissingAudioError()
		{
			var testBook = MakeBookWithOneAudioFile($@"<div class='bloom-translationGroup bloom-imageDescription'>
								<div class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='{
					_bookData.Language1.Iso639Code
				}'>
									{"<p><span id='bogus123' class='audio-sentence'>A flower.</span></p>"}
								</div>
							</div>
						</div>");
			SIL.IO.RobustIO.DeleteDirectoryAndContents(AudioProcessor.GetAudioFolderPath(testBook.FolderPath));
			var results = AccessibilityCheckers.CheckAudioForAllImageDescriptions(testBook);
			Assert.AreEqual(1, results.Count(), "Number of errors does not match expected");
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

		private string MakeHtmlForPageWithImageAriaHidden(string translationGroupText, string pageNumber = "1",
			string pageLabel = "Some page label")
		{
			return $@"<div class='bloom-page' data-page-number='{pageNumber ?? ""}'>
		<div class='pageLabel'>{pageLabel}</div>
		<div class='marginBox'>
				<div class='bloom-imageContainer' aria-hidden='true'>
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
