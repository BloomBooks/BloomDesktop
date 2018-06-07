using System.Linq;
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
			Assert.AreEqual(0, results.Count(),"No problems were expected");
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
			Assert.AreEqual(1, results.Count(), "Should point out missing language description");
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
					{GetHtmlForPageWithImage(divWithDescription)}
					{GetHtmlForPageWithImage(divWithoutCorrectLangDescription)}
					{GetHtmlForPageWithImage(divWithDescription)}
					{GetHtmlForPageWithImage(divWithoutCorrectLangDescription)}
				</body> </html>";
			var testBook = CreateBookWithPhysicalFile(html);
			var results = AccessibilityCheckers.CheckDescriptionsForAllImages(testBook);
			Assert.AreEqual(2, results.Count(), "Should point out missing language description");
		}

		private Bloom.Book.Book GetBookWithImage(string translationGroupText, string pageNumber = "1", string pageLabel = "Some page label")
		{
			var html = $@"<html> <body>
							{GetHtmlForPageWithImage(translationGroupText, pageNumber, pageLabel )}
						</body> </html>";
			return CreateBookWithPhysicalFile(html);
		}

		private string GetHtmlForPageWithImage(string translationGroupText, string pageNumber = "1", string pageLabel = "Some page label")
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
	}
}
