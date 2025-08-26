using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.CLI;
using Bloom.Collection;
using BloomTemp;
using NUnit.Framework;

namespace BloomTests.CLI
{
    [TestFixture]
    public class CreateArtifactsCommandTests
    {
        [TearDown]
        public void TearDown()
        {
            // Without this, subsequent tests will fail because they think the harvester is still running.
            Program.RunningHarvesterMode = false;
        }

        [Test]
        public void CreateArtifactsExitCode_GetErrorsFromExitCode_ExitCode0_ReturnsSuccess()
        {
            int exitCode = 0;
            var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

            Assert.That(errors.Count, Is.EqualTo(0));
        }

        [Test]
        public void CreateArtifactsExitCode_GetErrorsFromExitCode_UnhandledException_Returns1Error()
        {
            int exitCode = 1;
            var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

            CollectionAssert.AreEquivalent(new string[] { "UnhandledException" }, errors);
        }

        [Test]
        public void CreateArtifactsExitCode_GetErrorsFromExitCode_BookHtmlNotFound_Returns1Error()
        {
            int exitCode = 2;
            var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

            CollectionAssert.AreEquivalent(new string[] { "BookHtmlNotFound" }, errors);
        }

        [Test]
        public void CreateArtifactsExitCode_GetErrorsFromExitCode_LegacyBookCannotHarvest_Returns1Error()
        {
            int exitCode = 8;
            var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

            CollectionAssert.AreEquivalent(new string[] { "LegacyBookCannotHarvest" }, errors);
        }

        [Test]
        public void CreateArtifactsExitCode_GetErrorsFromExitCode_EpubError_Returns1Error()
        {
            int exitCode = 4;
            var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

            CollectionAssert.AreEquivalent(new string[] { "EpubException" }, errors);
        }

        [Test]
        public void CreateArtifactsExitCode_GetErrorsFromExitCode_MultipleFlags_ReturnsBoth()
        {
            int exitCode = 0;

            // bitwise arithmetic to set the first few flags
            int numFlags = 2;
            for (int i = 0; i < numFlags; ++i)
            {
                exitCode |= 1 << i;
            }

            var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

            CollectionAssert.AreEquivalent(
                new string[] { "UnhandledException", "BookHtmlNotFound" },
                errors
            );
        }

        [Test]
        public void CreateArtifactsExitCode_GetErrorsFromExitCode_UnknownFlag_ReturnsUnknown()
        {
            int exitCode = 1 << 20;
            var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

            CollectionAssert.AreEqual(new string[] { "Unknown" }, errors);
        }

        [Test]
        public void CreateArtifactsExitCode_GetErrorsFromExitCode_BigNumber_AddsUnknown()
        {
            int exitCode = -532462766;
            var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

            Assert.That(errors.Contains("Unknown"), Is.True);
        }

        // Validate that if an xmatter is not allowed to use legacy theme, we can still successfully convert
        // to default theme if that is allowed.
        [Test]
        public void CreateArtifacts_LegacyBookWithInvalidXmatter_HarvesterAllowedToConvert_ConvertsToDefaultTheme()
        {
            using (
                var testFolder = new TemporaryFolder(
                    "CreateArtifacts_LegacyBookWithInvalidXmatter_HarvesterAllowedToConvert_ConvertsToDefaultTheme"
                )
            )
            {
                var collectionFolderPath = testFolder.Combine("collection");

                var bookFolderPath = Path.Combine(collectionFolderPath, "book");
                System.IO.Directory.CreateDirectory(bookFolderPath);
                var collectionFilePath = Path.Combine(
                    collectionFolderPath,
                    "collection.bloomCollection"
                );
                var settings = new CollectionSettings(collectionFilePath);
                settings.XMatterPackName = "ABC-Reader";
                settings.Save();
                var metaData = new BookMetaData();
                metaData.WriteToFolder(bookFolderPath);
                var bookPath = System.IO.Path.Combine(bookFolderPath, "book.htm");
                System.IO.File.WriteAllText(
                    bookPath,
                    @"<html>
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
				</html>"
                );
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(bookFolderPath, "book.xmatter"),
                    "invalid"
                );
                var result = CreateArtifactsCommand.HandleInternal(
                    new CreateArtifactsParameters()
                    {
                        BookPath = bookFolderPath,
                        CollectionPath = collectionFilePath,
                        BloomDigitalOutputPath = Path.Combine(testFolder.FolderPath, "output"),
                    }
                );

                Assert.That(result, Is.EqualTo(CreateArtifactsExitCode.Success));

                var appearanceJson = System.IO.File.ReadAllText(
                    System.IO.Path.Combine(testFolder.FolderPath, "output", "appearance.json")
                );
                Assert.That(appearanceJson, Does.Contain("\"cssThemeName\": \"default\""));

                Assert.IsTrue(
                    SIL.IO.RobustFile.Exists(
                        Path.Combine(testFolder.FolderPath, "output", "basePage.css")
                    )
                );
                Assert.IsFalse(
                    SIL.IO.RobustFile.Exists(
                        Path.Combine(testFolder.FolderPath, "output", "basePage-legacy-5-6.css")
                    )
                );
            }
        }

        // The idea here is to make the simplest possible book that will get far enough into the
        // CreateArtifactsCommand code to attempt to migrate a book for publication which can't be
        // legacy, because the Xmatter does not support legacy (and also does not allow conversion to Default).
        // We want to validate the exception.
        //
        // Note, as of Apr 2024, there are no user xmatters which both don't support legacy and don't support conversion to default.
        //
        // Of course, it would also be nice to have some tests where artifact creation succeeds, but that's
        // too big a job for today.
        [Test]
        public void CreateArtifacts_LegacyBookWithInvalidXmatter_ReportsLegacyBookCannotHarvest()
        {
            using (
                var testFolder = new TemporaryFolder(
                    "CreateArtifacts_LegacyBookWithInvalidXmatter_ReportsLegacyBookCannotHarvest"
                )
            )
            {
                var collectionFolderPath = testFolder.Combine("collection");

                var bookFolderPath = Path.Combine(collectionFolderPath, "book");
                System.IO.Directory.CreateDirectory(bookFolderPath);
                var collectionFilePath = Path.Combine(
                    collectionFolderPath,
                    "collection.bloomCollection"
                );
                var settings = new CollectionSettings(collectionFilePath);
                settings.XMatterPackName = "unit-test-project-specific";
                settings.Save();
                var metaData = new BookMetaData();
                metaData.WriteToFolder(bookFolderPath);
                var bookPath = System.IO.Path.Combine(bookFolderPath, "book.htm");
                System.IO.File.WriteAllText(
                    bookPath,
                    @"<html>
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
				</html>"
                );
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(bookFolderPath, "book.xmatter"),
                    "invalid"
                );
                var result = CreateArtifactsCommand.HandleInternal(
                    new CreateArtifactsParameters()
                    {
                        BookPath = bookFolderPath,
                        CollectionPath = collectionFilePath,
                        BloomPubOutputPath = Path.Combine(testFolder.FolderPath, "output"),
                    }
                );

                Assert.That(result, Is.EqualTo(CreateArtifactsExitCode.LegacyBookCannotHarvest));
            }
        }

        [Test]
        public void CreateArtifacts_WithJsonOutput_CreatesJsonFile()
        {
            using (
                var testFolder = new TemporaryFolder(
                    "CreateArtifacts_WithJsonOutput_CreatesJsonFile"
                )
            )
            {
                var collectionFolderPath = testFolder.Combine("collection");

                var bookFolderPath = Path.Combine(collectionFolderPath, "book");
                System.IO.Directory.CreateDirectory(bookFolderPath);
                var collectionFilePath = Path.Combine(
                    collectionFolderPath,
                    "collection.bloomCollection"
                );
                var settings = new CollectionSettings(collectionFilePath);
                settings.Save();
                var metaData = new BookMetaData();
                metaData.WriteToFolder(bookFolderPath);
                var bookPath = System.IO.Path.Combine(bookFolderPath, "book.htm");
                System.IO.File.WriteAllText(
                    bookPath,
                    @"<html>
                    <body>
						<div class='bloom-page'>
							<div class='marginBox'>
								<div class='bloom-translationGroup'>
									<div class='bloom-editable' lang='en'>
                                        Hello
									</div>
                                    <div class='bloom-editable' lang='es'>
                                        Hola
									</div>
								</div>
							</div>
						</div>
					</body>
				</html>"
                );

                var jsonOutputPath = Path.Combine(testFolder.FolderPath, "texts.json");
                var result = CreateArtifactsCommand.HandleInternal(
                    new CreateArtifactsParameters()
                    {
                        BookPath = bookFolderPath,
                        CollectionPath = collectionFilePath,
                        JsonTextsOutputPath = jsonOutputPath,
                    }
                );

                Assert.That(result, Is.EqualTo(CreateArtifactsExitCode.Success));
                Assert.That(File.Exists(jsonOutputPath), Is.True);
                var jsonContent = File.ReadAllText(jsonOutputPath);
                // The json should have format {"pageGuid":"text content"}
                // Page guid is defined in bloom-page div's id attribute
                // Text content comes from bloom-content1 div
                Assert.That(jsonContent, Is.EqualTo(@"[{""en"":""Hello"",""es"":""Hola""}]"));
            }
        }
    }
}
