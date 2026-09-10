using System;
using Bloom.Book;
using Bloom.Publish.Rab;
using BloomTests.Book;
using NUnit.Framework;

namespace BloomTests.Publish.Rab
{
    /// <summary>
    /// Publish > Apps must respect the same content-license restrictions as the other publish paths (BL-16833):
    /// a book carrying a bloom-licensed-content-id may only go into the app in the languages its copyright
    /// holder has permitted.
    /// </summary>
    [TestFixture]
    public class RabLicenseCheckTests : BookTestsBase
    {
        private const string kLicensedBookHead =
            "<meta name='bloom-licensed-content-id' content='kingstone.superbible.ruth'></meta>";
        private const string kBookBody =
            @"<div class='bloom-page numberedPage customPage A5Portrait'>
				<div class='marginBox'>
					<div class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>some text</div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='ru'>Russian text</div>
					</div>
				</div>
			</div>";

        private BloomTemp.TemporaryFolder _licenseCacheFolder;

        protected override string GetTestFolderName() => "RabLicenseCheckTests";

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            _licenseCacheFolder = LicenseCheckerTests.SetupDefaultOfflineLicenseInfo(
                "RabLicenseCheckTests-licenses"
            );
        }

        [TearDown]
        public override void TearDown()
        {
            LicenseChecker.SetOfflineFolder(null);
            LicenseChecker.SetAllowInternetAccess(true);
            _licenseCacheFolder?.Dispose();
            base.TearDown();
        }

        [Test]
        public void EnsureBooksAreLicensedForPublishing_UnlicensedLanguage_ThrowsWithLicenseMessage()
        {
            var book = CreateBookWithPhysicalFile(kBookBody, kLicensedBookHead);
            // Sanity check that the fixture really does forbid English for this book.
            Assert.That(new LicenseChecker().CheckBook(book, new[] { "en" }), Is.Not.Null);

            var exception = Assert.Throws<ApplicationException>(() =>
                RabProjectService.EnsureBooksAreLicensedForPublishing(
                    new[] { (book, "Ruth", new[] { "en" }) }
                )
            );

            Assert.That(
                exception.Message,
                Is.EqualTo(
                    string.Format(
                        LicenseChecker.kUnlicenseLanguageMessage,
                        book.PrettyPrintLanguage("en")
                    )
                )
            );
        }

        [Test]
        public void EnsureBooksAreLicensedForPublishing_LicensedLanguage_DoesNotThrow()
        {
            var book = CreateBookWithPhysicalFile(kBookBody, kLicensedBookHead);

            Assert.DoesNotThrow(() =>
                RabProjectService.EnsureBooksAreLicensedForPublishing(
                    new[] { (book, "Ruth", new[] { "ru" }) }
                )
            );
        }

        [Test]
        public void EnsureBooksAreLicensedForPublishing_BookWithoutLicenseId_DoesNotThrow()
        {
            var book = CreateBookWithPhysicalFile(kBookBody, headContent: null);
            // Sanity check: this book has no license restriction at all.
            Assert.That(
                book.OurHtmlDom.SelectSingleNode("//meta[@name='bloom-licensed-content-id']"),
                Is.Null
            );

            Assert.DoesNotThrow(() =>
                RabProjectService.EnsureBooksAreLicensedForPublishing(
                    new[] { (book, "Free Book", new[] { "en", "fr" }) }
                )
            );
        }

        [Test]
        public void EnsureBooksAreLicensedForPublishing_SeveralProblemBooks_NamesEachBook()
        {
            var book = CreateBookWithPhysicalFile(kBookBody, kLicensedBookHead);

            var exception = Assert.Throws<ApplicationException>(() =>
                RabProjectService.EnsureBooksAreLicensedForPublishing(
                    new[]
                    {
                        (book, "Ruth", new[] { "en" }),
                        (book, "Licensed OK", new[] { "ru" }),
                        (book, "Esther", new[] { "en" }),
                    }
                )
            );

            var lines = exception.Message.Split(
                new[] { Environment.NewLine },
                StringSplitOptions.None
            );
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Does.StartWith("Ruth: "));
            Assert.That(lines[1], Does.StartWith("Esther: "));
            Assert.That(exception.Message, Does.Not.Contain("Licensed OK"));
        }
    }
}
