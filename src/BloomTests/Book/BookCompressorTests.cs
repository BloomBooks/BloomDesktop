using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Publish.Epub;
using Bloom.web;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using Color = System.Drawing.Color;

namespace BloomTests.Book
{
	class BookCompressorTests : BookTestsBase
	{
		private BookServer _bookServer;
		private TemporaryFolder _projectFolder;
		private BookStarter _starter;

		private string kMinimumValidBookHtml =
			@"<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css'></link></head><body>
					<div class='bloom-page' id='guid1'></div>
			</body></html>";

		[SetUp]
		public void SetupFixture()
		{
			_bookServer = CreateBookServer();
		}

		[Test]
		public void CompressBookForDevice_FileNameIsCorrect()
		{
			var testBook = CreateBookWithPhysicalFile(kMinimumValidBookHtml, bringBookUpToDate: true);

			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, testBook, _bookServer, Color.Azure, new NullWebSocketProgress());
				Assert.AreEqual(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook,
					Path.GetFileName(bloomdTempFile.Path));
			}
		}

		[Test]
		public void CompressBookForDevice_IncludesWantedFiles()
		{
			var wantedFiles = new List<string>()
			{
				"thumbnail.png", // should be left alone
				"previewMode.css",
				"meta.json", // should be left alone
				"readerStyles.css", // gets added
				"Device-XMatter.css", // added when we apply this xmatter
				"customCollectionStyles.css", // should be moved from parent directory
				"settingsCollectionStyles.css" // should be moved from parent directory
			};

			TestHtmlAfterCompression(kMinimumValidBookHtml,
				actionsOnFolderBeforeCompressing: folderPath =>
				{
					// These png files have to be real; just putting some text in it leads to out-of-memory failures when Bloom
					// tries to make its background transparent.
					File.Copy(SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"),
						Path.Combine(folderPath, "thumbnail.png"));
					File.WriteAllText(Path.Combine(folderPath, "previewMode.css"), @"This is wanted");
					File.WriteAllText(Path.Combine(Path.GetDirectoryName(folderPath), "customCollectionStyles.css"), @"This is wanted");
					File.WriteAllText(Path.Combine(Path.GetDirectoryName(folderPath), "settingsCollectionStyles.css"), @"This is wanted");
				},
				assertionsOnResultingHtmlString: html =>
				{
					// These two files get moved into the book folder, the links must get fixed
					Assert.That(html, Does.Contain("href=\"customCollectionStyles.css\""));
					Assert.That(html, Does.Contain("href=\"settingsCollectionStyles.css\""));
					// The parent folder doesn't go with the book, so we shouldn't be referencing anything there
					Assert.That(html, Does.Not.Contain("href=\"../"));
				},
				assertionsOnZipArchive: zip =>
				{
					foreach (var name in wantedFiles)
					{
						Assert.AreNotEqual(-1, zip.FindEntry(Path.GetFileName(name), true), "expected " + name + " to be part of .bloomd zip");
					}
				});
		}

		[Test]
		public void CompressBookForDevice_OmitsUnwantedFiles()
		{
			// some files we don't want copied into the .bloomd
			var unwantedFiles = new List<string> {
				"book.BloomBookOrder", "book.pdf", "thumbnail-256.png", "thumbnail-70.png", // these are artifacts of uploading book to BloomLibrary.org
				"Traditional-XMatter.css" // since we're adding Device-XMatter.css, this is no longer needed
			};

			TestHtmlAfterCompression(kMinimumValidBookHtml,
				actionsOnFolderBeforeCompressing: folderPath =>
				{
					// The png files have to be real; just putting some text in them leads to out-of-memory failures when Bloom
					// tries to make their background transparent.
					File.Copy(SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"), Path.Combine(folderPath, "thumbnail.png"));
					File.WriteAllText(Path.Combine(folderPath, "previewMode.css"), @"This is wanted");

					// now some files we expect to be omitted from the .bloomd archive
					File.WriteAllText(Path.Combine(folderPath, "book.BloomBookOrder"), @"This is unwanted");
					File.WriteAllText(Path.Combine(folderPath, "book.pdf"), @"This is unwanted");
					File.Copy(SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"), Path.Combine(folderPath, "thumbnail-256.png"));
					File.Copy(SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"), Path.Combine(folderPath, "thumbnail-70.png"));
				},
				assertionsOnZipArchive: zip =>
				{
					foreach(var name in unwantedFiles)
					{
						Assert.AreEqual(-1, zip.FindEntry(Path.GetFileName(name), true),
							"expected " + name + " to not be part of .bloomd zip");
					}
				});
		}

		// Also verifies that images that DO exist are NOT removed (even if src attr includes params like ?optional=true)
		// Since this is one of the few tests that makes a real HTML file we use it also to check
		// the the HTML file is at the root of the zip.
		[Test]
		public void CompressBookForDevice_RemovesImgElementsWithMissingSrc_AndContentEditable()
		{
			const string imgsToRemove = "<img src='nonsence.svg'/><img src=\"rubbish\"/>";
			var htmlTemplate = @"<html>
									<body>
										<div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
											<div class='pageLabel' lang='en'>
												Outside Back Cover
											</div>
											<div class='pageDescription' lang='en'></div>

											<div class='marginBox'>
											<div class='bloom-translationGroup' data-default-languages='N1'>
												<div class='bloom-editable Outside-Back-Cover-style bloom-copyFromOtherLanguageIfNecessary bloom-contentNational1 bloom-visibility-code-on' lang='fr' contenteditable='false' data-book='outsideBackCover'>
													<label class='bubble'>If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.</label>
												</div>

												<div class='bloom-editable Outside-Back-Cover-style bloom-copyFromOtherLanguageIfNecessary bloom-contentNational2' lang='de'contenteditable='true' data-book='outsideBackCover'></div>

												<div class='bloom-editable Outside-Back-Cover-style bloom-copyFromOtherLanguageIfNecessary bloom-content1' lang='ksf' contenteditable='true' data-book='outsideBackCover'></div>
											</div>
											{0}
											</div>
										</div>
									</body>
									</html>";
			var htmlOriginal = string.Format(htmlTemplate, imgsToRemove);
			var testBook = CreateBookWithPhysicalFile(htmlOriginal, bringBookUpToDate: true);

			TestHtmlAfterCompression(htmlOriginal,
				actionsOnFolderBeforeCompressing:
				bookFolderPath => // Simulate the typical situation where we have the regular but not the wide svg
						File.WriteAllText(Path.Combine(bookFolderPath, "somelogo.svg"), @"this is a fake for testing"),

				assertionsOnResultingHtmlString:
				html =>
				{
					var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
					AssertThatXmlIn.Dom(htmlDom).HasNoMatchForXpath("//div[@contenteditable]");
					AssertThatXmlIn.Dom(htmlDom).HasSpecifiedNumberOfMatchesForXpath("//img[@src='nonsence.svg']", 0);
					AssertThatXmlIn.Dom(htmlDom).HasSpecifiedNumberOfMatchesForXpath("//img[@src='rubbish']", 0);
					AssertThatXmlIn.Dom(htmlDom).HasSpecifiedNumberOfMatchesForXpath("//img[@src='license.png']", 1);
				});
		}

		[Test]
		public void CompressBookForDevice_ImgInImageContainer_ConvertedToBackground()
		{
			// The odd file names here are an important part of the test; they need to get converted to proper URL syntax.
			const string bookHtml = @"<html>
										<body>
											<div class='bloom-page' id='blah'>
												<div class='marginBox'>
													<div class='bloom-imageContainer bloom-leadingElement'>"
													+"	<img src=\"HL00'14 1.svg\"/>"
													+@"</div>
													<div class='bloom-imageContainer bloom-leadingElement'>"
														+ "<img src=\"HL00'14 1.svg\"/>"
													+@"</div>
											</div>
										</body>
									</html>";

			TestHtmlAfterCompression(bookHtml,
				actionsOnFolderBeforeCompressing:
					bookFolderPath => File.WriteAllText(Path.Combine(bookFolderPath, "HL00'14 1.svg"), @"this is a fake for testing"),
				assertionsOnResultingHtmlString:
					changedHtml =>
					{
						// The imgs should be replaced with something like this:
						//		"<div class='bloom-imageContainer bloom-leadingElement bloom-backgroundImage' style='background-image:url('HL00%2714%201.svg.svg')'</div>
						//	Note that normally there would also be data-creator, data-license, etc. If we put those in the html, they will be stripped because
						// the code will actually look at our fake image and, finding now metadata will remove these. This is not a problem for our
						// testing here, because we're not trying to test the functioning of that function here. The bit we can test, that the image became a
						// background image, is sufficient to know the function was run.

						// Oct 2017 jh: I added this bloom-imageContainer/ because the code that does the conversion is limited to these,
						// presumably because that is the only img's that were giving us problems (ones that need to be sized at display time).
						// But Xmatter has other img's, for license & branding.
						var changedDom = XmlHtmlConverter.GetXmlDomFromHtml(changedHtml);
						AssertThatXmlIn.Dom(changedDom).HasNoMatchForXpath("//bloom-imageContainer/img"); // should be merged into parent

						//Note: things like  @data-creator='Anis', @data-license='cc-by' and @data-copyright='1996 SIL PNG' are not going to be there by now,
						//because they aren't actually supported by the image file, so they get stripped.
						AssertThatXmlIn.Dom(changedDom).HasSpecifiedNumberOfMatchesForXpath("//div[@class='bloom-imageContainer bloom-leadingElement bloom-backgroundImage' and @style=\"background-image:url('HL00%2714%201.svg')\"]", 2);
					});
		}

		[Test]
		public void CompressBookForDevice_IncludesVersionFileAndStyleSheet()
		{
			// This requires a real book file (which a mocked book usually doesn't have).
			// It's also important that the book contains something like contenteditable that will be removed when
			// sending the book. The sha is based on the actual file contents of the book, not the
			// content actually embedded in the bloomd.
			var bookHtml = @"<html>
								<head>
									<meta charset='UTF-8'></meta>
									<link rel='stylesheet' href='../settingsCollectionStyles.css' type='text/css'></link>
									<link rel='stylesheet' href='../customCollectionStyles.css' type='text/css'></link>
								</head>
								<body>
									<div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
										<div  contenteditable='true'>something</div>
									</div>
								</body>
							</html>";

			string entryContents = null;

			TestHtmlAfterCompression(bookHtml,
				actionsOnFolderBeforeCompressing:
				bookFolderPath => // Simulate the typical situation where we have the regular but not the wide svg
					File.WriteAllText(Path.Combine(bookFolderPath, "back-cover-outside.svg"), @"this is a fake for testing"),

				assertionsOnResultingHtmlString:
				html =>
				{
					AssertThatXmlIn.Dom(XmlHtmlConverter.GetXmlDomFromHtml(html)).HasSpecifiedNumberOfMatchesForXpath("//html/head/link[@rel='stylesheet' and @href='readerStyles.css' and @type='text/css']", 1);
				},

				assertionsOnZipArchive: zip =>
					// This test worked when we didn't have to modify the book before making the .bloomd.
					// Now that we do I haven't figured out a reasonable way to rewrite it to test this value again...
					// Assert.That(GetEntryContents(zip, "version.txt"), Is.EqualTo(Bloom.Book.Book.MakeVersionCode(html, bookPath)));
					// ... so for now we just make sure that it was added and looks like a hash code
				{
					entryContents = GetEntryContents(zip, "version.txt");
					Assert.AreEqual(44, entryContents.Length);
				},
				assertionsOnRepeat: zip =>
				{
					Assert.That(GetEntryContents(zip, "version.txt"), Is.EqualTo(entryContents));
				});
		}
		[Test]
		public void CompressBookForDevice_QuestionsPages_ConvertsToJson()
		{
			// This requires a real book file (which a mocked book usually doesn't have).
			// Test data reflects a number of important conditions, including presence or absence of
			// white space before and after asterisk, paragraphs broken up with br.
			// As yet does not cover questions with no answers (currently will be excluded),
			// questions with no right answer (currently will be included)
			// questions with more than one right answer (currently will be included)
			// questions with only one answer (currently will be included),
			// since I'm not sure what the desired behavior is.
			// If we want to test corner cases it might be easier to test BloomReaderFileMaker.ExtractQuestionGroups directly.
			var bookHtml = @"<html>
<head>
	<meta charset='UTF-8'></meta>
	<link rel='stylesheet' href='../settingsCollectionStyles.css' type='text/css'></link>
	<link rel='stylesheet' href='../customCollectionStyles.css' type='text/css'></link>
</head>
<body>
	<div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
		<div  contenteditable='true'>This page should make it into the book</div>
	</div>
    <div class='bloom-page customPage enterprise questions bloom-nonprinting Device16x9Portrait layout-style-Default side-left bloom-monolingual' id='86574a93-a50f-42da-b78f-574ef790c481' data-page='' data-pagelineage='4140d100-e4c3-49c4-af05-dda5789e019b' data-page-number='1' lang=''>
        <div class='pageLabel' lang='en'>
            Comprehension Questions
        </div>
        <div class='pageDescription' lang='en'></div>

        <div class='marginBox'>
            <div style='min-width: 0px;' class='split-pane vertical-percent'>
                <div class='split-pane-component position-left'>
                    <div class='split-pane-component-inner'>
                        <div class='ccLabel'>
                            <p>Enter your comprehension questions for this book..., see <a href='https://docs.google.com/document/d/1LV0_OtjH1BTJl7wqdth0bZXQxduTqD7WenX4AsksVGs/edit#heading=h.lxe9k6qcvzwb'>Bloom Enterprise Service</a></p>

							...

                            <p>*Appeared to wear the cap</p>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider vertical-divider'></div>

                <div class='split-pane-component position-right'>
                    <div class='split-pane-component-inner adding'>
                        <div class='bloom-translationGroup bloom-trailingElement cc-style' data-default-languages='auto'>
                            <div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr cc-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
                                <p>Where do questions belong?</p>

                                <p>* At the end</p>

                                <p>At the start</p>

                                <p>In the middle</p>

                                <p></p>
                            </div>
                            <div class='bloom-editable' contenteditable='true' lang='fr'>
                                <p>Where do French questions belong?</p>

                                <p> *At the end of the French</p>

                                <p>At the start of the French</p>

                                <p>In the middle of the French</p>

                            </div>
                            <div class='bloom-editable' contenteditable='true' lang='z'></div>

                            <div aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-contentNational1' contenteditable='true' lang='es'>
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class='bloom-page customPage enterprise questions bloom-nonprinting Device16x9Portrait layout-style-Default side-left bloom-monolingual' id='299c0b20-56f7-4a0f-a6d4-08f1ec01f1e6' data-page='' data-pagelineage='4140d100-e4c3-49c4-af05-dda5789e019b' data-page-number='2' lang=''>
        <div class='pageLabel' lang='en'>
            Comprehension Questions
        </div>

        <div class='pageDescription' lang='en'></div>

        <div class='marginBox'>
            <div style='min-width: 0px;' class='split-pane vertical-percent'>
                <div class='split-pane-component position-left'>
                    <div class='split-pane-component-inner'>
                        <div class='ccLabel'>
                            <p>Enter your ..., see <a href='https://docs.google.com/document/d/1LV0_OtjH1BTJl7wqdth0bZXQxduTqD7WenX4AsksVGs/edit#heading=h.lxe9k6qcvzwb'>Bloom Enterprise Service</a></p>

                            <p></p>
								...

                            <p>*Appeared to wear the cap</p>
                        </div>
                    </div>
                </div>

                <div class='split-pane-divider vertical-divider'></div>

                <div class='split-pane-component position-right'>
                    <div class='split-pane-component-inner adding'>
                        <div class='bloom-translationGroup bloom-trailingElement cc-style' data-default-languages='auto'>
                            <div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr cc-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
                                <p>Where is the USA?<br></br>
                                South America<br></br>
                                *North America<br></br>
                                Europe<br></br>
                                Asia</p>

                                <p></p>

                                <p>Where does the platypus come from?<br></br>
                                *Australia<br></br>
                                Papua New Guinea<br></br>
                                Africa<br></br>
                                Peru</p>

                                <p></p>

                                <p>What is an Emu?<br></br>
                                A fish<br></br>
                                An insect<br></br>
                                A spider<br></br>
                                * A bird</p>

                                <p></p>

                                <p>Where do emus live?<br></br>
                                New Zealand<br></br>
                                * Farms in the USA<br></br>
                                England<br></br>
                                Wherever</p>

                                <p></p>
                            </div>

                            <div class='bloom-editable' contenteditable='true' lang='z'></div>

                            <div aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-contentNational1' contenteditable='true' lang='es'>
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";

			TestHtmlAfterCompression(bookHtml,
				assertionsOnResultingHtmlString:
				html =>
				{
					// The questions pages should be removed.
					var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
					AssertThatXmlIn.Dom(htmlDom).HasNoMatchForXpath("//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'questions')]");
				},

				assertionsOnZipArchive: zip =>
				{
					var json = GetEntryContents(zip, BloomReaderFileMaker.QuestionFileName);
					var groups = QuestionGroup.FromJson(json);
					// Two (non-z-language) groups in first question page, one in second.
					Assert.That(groups, Has.Length.EqualTo(3));
					Assert.That(groups[0].questions, Has.Length.EqualTo(1));
					Assert.That(groups[1].questions, Has.Length.EqualTo(1));
					Assert.That(groups[2].questions, Has.Length.EqualTo(4));

					Assert.That(groups[0].lang, Is.EqualTo("en"));
					Assert.That(groups[1].lang, Is.EqualTo("fr"));

					Assert.That(groups[0].questions[0].question, Is.EqualTo("Where do questions belong?"));
					Assert.That(groups[0].questions[0].answers, Has.Length.EqualTo(3));
					Assert.That(groups[0].questions[0].answers[0].text, Is.EqualTo("At the end"));
					Assert.That(groups[0].questions[0].answers[0].correct, Is.True);
					Assert.That(groups[0].questions[0].answers[1].text, Is.EqualTo("At the start"));
					Assert.That(groups[0].questions[0].answers[1].correct, Is.False);


					Assert.That(groups[1].questions[0].question, Is.EqualTo("Where do French questions belong?"));
					Assert.That(groups[1].questions[0].answers, Has.Length.EqualTo(3));
					Assert.That(groups[1].questions[0].answers[0].text, Is.EqualTo("At the end of the French"));
					Assert.That(groups[1].questions[0].answers[0].correct, Is.True);
					Assert.That(groups[1].questions[0].answers[1].text, Is.EqualTo("At the start of the French"));
					Assert.That(groups[1].questions[0].answers[1].correct, Is.False);

					Assert.That(groups[2].questions[0].question, Is.EqualTo("Where is the USA?"));
					Assert.That(groups[2].questions[0].answers, Has.Length.EqualTo(4));
					Assert.That(groups[2].questions[0].answers[3].text, Is.EqualTo("Asia"));
					Assert.That(groups[2].questions[0].answers[3].correct, Is.False);
					Assert.That(groups[2].questions[0].answers[1].text, Is.EqualTo("North America"));
					Assert.That(groups[2].questions[0].answers[1].correct, Is.True);

					Assert.That(groups[2].questions[2].question, Is.EqualTo("What is an Emu?"));
					Assert.That(groups[2].questions[2].answers, Has.Length.EqualTo(4));
					Assert.That(groups[2].questions[2].answers[0].text, Is.EqualTo("A fish"));
					Assert.That(groups[2].questions[2].answers[0].correct, Is.False);
					Assert.That(groups[2].questions[2].answers[3].text, Is.EqualTo("A bird"));
					Assert.That(groups[2].questions[2].answers[3].correct, Is.True);

					// Make sure we don't miss the last answer of the last question.
					Assert.That(groups[2].questions[3].answers[3].text, Is.EqualTo("Wherever"));
				}
			);
		}


		[Test]
		public void CompressBookForDevice_MakesThumbnailFromCoverPicture()
		{
			// This requires a real book file (which a mocked book usually doesn't have).
			var bookHtml = @"<html>
								<head>
									<meta charset='UTF-8'></meta>
									<link rel='stylesheet' href='../settingsCollectionStyles.css' type='text/css'></link>
									<link rel='stylesheet' href='../customCollectionStyles.css' type='text/css'></link>
								</head>
								<body>
									<div id='bloomDataDiv'>
										<div data-book='coverImage' lang='*'>
											Listen to My Body_Cover.png
										</div>
									</div>
									<div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
										<div class='marginBox'>"
											+ "<div class=\"bloom-imageContainer bloom-backgroundImage\" data-book=\"coverImage\" style=\"background-image:url('Listen%20to%20My%20Body_Cover.png')\"></div>"
										+ @"</div>
									</div>
								</body>
							</html>";

			TestHtmlAfterCompression(bookHtml,
				actionsOnFolderBeforeCompressing:
				bookFolderPath =>
				{
					File.Copy(SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"),
						Path.Combine(bookFolderPath, "Listen to My Body_Cover.png"));
				},

				assertionsOnZipArchive: zip =>
				{
					using (var thumbStream = GetEntryContentsStream(zip, "thumbnail.png"))
					{
						using (var thumbImage = Image.FromStream(thumbStream))
						{
							// I don't know how to verify that it's made from shirt.png, but this at least verifies
							// that some shrinking was done and that it considers height as well as width, since
							// the shirt.png image happens to be higher than it is wide.
							// It would make sense to test that it works for jpg images, too, but it's rather a slow
							// test and jpg doesn't involve a different path through the new code.
							Assert.That(thumbImage.Width, Is.LessThanOrEqualTo(256));
							Assert.That(thumbImage.Height, Is.LessThanOrEqualTo(256));
						}
					}
				}
			);
		}

		private Stream GetEntryContentsStream(ZipFile zip, string name, bool exact = false)
		{
			Func<ZipEntry, bool> predicate;
			if (exact)
				predicate = n => n.Name.Equals(name);
			else
				predicate = n => n.Name.EndsWith(name);

			var ze = (from ZipEntry entry in zip select entry).FirstOrDefault(predicate);
			Assert.That(ze, Is.Not.Null);

			return zip.GetInputStream(ze);
		}

		private string GetEntryContents(ZipFile zip, string name, bool exact = false)
		{
			var buffer = new byte[4096];

			using (var instream = GetEntryContentsStream(zip, name, exact))
			using (var writer = new MemoryStream())
			{
				ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(instream, writer, buffer);
				writer.Position = 0;
				using (var reader = new StreamReader(writer))
				{
					return reader.ReadToEnd();
				}
			}
		}

		// re-use the images from another test (added LakePendOreille.jpg for these tests)
		private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

		[Test]
		public void GetBytesOfReducedImage_SmallPngImageMadeTransparent()
		{
			// bird.png:                   PNG image data, 274 x 300, 8-bit/color RGBA, non-interlaced

			var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "bird.png");
			byte[] originalBytes = File.ReadAllBytes(path);
			byte[] reducedBytes = BookCompressor.GetImageBytesForElectronicPub(path,true);
			Assert.That(reducedBytes, Is.Not.EqualTo(originalBytes)); // no easy way to check it was made transparent, but should be changed.
			// Size should not change much.
			Assert.That(reducedBytes.Length, Is.LessThan(originalBytes.Length * 11/10));
			Assert.That(reducedBytes.Length, Is.GreaterThan(originalBytes.Length * 9 / 10));
			using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
			{
				var oldMetadata = Metadata.FromFile(path);
				RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
				var newMetadata = Metadata.FromFile(tempFile.Path);
				if (oldMetadata.IsEmpty)
				{
					Assert.IsTrue(newMetadata.IsEmpty);
				}
				else
				{
					Assert.IsFalse(newMetadata.IsEmpty);
					Assert.AreEqual(oldMetadata.CopyrightNotice, newMetadata.CopyrightNotice, "copyright preserved for bird.png");
					Assert.AreEqual(oldMetadata.Creator, newMetadata.Creator, "creator preserved for bird.png");
					Assert.AreEqual(oldMetadata.License.ToString(), newMetadata.License.ToString(), "license preserved for bird.png");
				}
			}
		}

		[Test]
		public void GetBytesOfReducedImage_SmallJpgImageStaysSame()
		{
			// man.jpg:                    JPEG image data, JFIF standard 1.01, ..., precision 8, 118x154, frames 3

			var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "man.jpg");
			var originalBytes = File.ReadAllBytes(path);
			var reducedBytes = BookCompressor.GetImageBytesForElectronicPub(path, true);
			Assert.AreEqual(originalBytes, reducedBytes, "man.jpg is already small enough (118x154)");
			using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
			{
				var oldMetadata = Metadata.FromFile(path);
				RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
				var newMetadata = Metadata.FromFile(tempFile.Path);
				if (oldMetadata.IsEmpty)
				{
					Assert.IsTrue(newMetadata.IsEmpty);
				}
				else
				{
					Assert.IsFalse(newMetadata.IsEmpty);
					Assert.AreEqual(oldMetadata.CopyrightNotice, newMetadata.CopyrightNotice, "copyright preserved for man.jpg");
					Assert.AreEqual(oldMetadata.Creator, newMetadata.Creator, "creator preserved for man.jpg");
					Assert.AreEqual(oldMetadata.License.ToString(), newMetadata.License.ToString(), "license preserved for man.jpg");
				}
			}
		}

		[Test]
		public void GetBytesOfReducedImage_LargePngImageReduced()
		{
			// shirtWithTransparentBg.png: PNG image data, 2208 x 2400, 8-bit/color RGBA, non-interlaced

			var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png");
			var originalBytes = File.ReadAllBytes(path);
			var reducedBytes = BookCompressor.GetImageBytesForElectronicPub(path, true);
			Assert.Greater(originalBytes.Length, reducedBytes.Length, "shirt.png is reduced from 2208x2400");
			using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
			{
				var oldMetadata = Metadata.FromFile(path);
				RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
				var newMetadata = Metadata.FromFile(tempFile.Path);
				if (oldMetadata.IsEmpty)
				{
					Assert.IsTrue(newMetadata.IsEmpty);
				}
				else
				{
					Assert.IsFalse(newMetadata.IsEmpty);
					Assert.AreEqual(oldMetadata.CopyrightNotice, newMetadata.CopyrightNotice, "copyright preserved for shirt.png");
					Assert.AreEqual(oldMetadata.Creator, newMetadata.Creator, "creator preserved for shirt.png");
					Assert.AreEqual(oldMetadata.License.ToString(), newMetadata.License.ToString(), "license preserved for shirt.png");
				}
			}
		}

		[Test]
		public void GetBytesOfReducedImage_LargeJpgImageReduced()
		{
			// LakePendOreille.jpg:        JPEG image data, JFIF standard 1.01, ... precision 8, 3264x2448, frames 3

			var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "LakePendOreille.jpg");
			var originalBytes = File.ReadAllBytes(path);
			var reducedBytes = BookCompressor.GetImageBytesForElectronicPub(path, true);
			Assert.Greater(originalBytes.Length, reducedBytes.Length, "LakePendOreille.jpg is reduced from 3264x2448");
			using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
			{
				var oldMetadata = Metadata.FromFile(path);
				RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
				var newMetadata = Metadata.FromFile(tempFile.Path);
				if (oldMetadata.IsEmpty)
				{
					Assert.IsTrue(newMetadata.IsEmpty);
				}
				else
				{
					Assert.IsFalse(newMetadata.IsEmpty);
					Assert.AreEqual(oldMetadata.CopyrightNotice, newMetadata.CopyrightNotice, "copyright preserved for LakePendOreille.jpg");
					Assert.AreEqual(oldMetadata.Creator, newMetadata.Creator, "creator preserved for LakePendOreille.jpg");
					Assert.AreEqual(oldMetadata.License.ToString(), newMetadata.License.ToString(), "license preserved for LakePendOreille.jpg");
				}
			}
		}

		[Test]
		public void GetBytesOfReducedImage_LargePng24bImageReduced()
		{
			// lady24b.png:        PNG image data, 24bit depth, 3632w x 3872h

			var path = FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "lady24b.png");
			var originalBytes = File.ReadAllBytes(path);
			var reducedBytes = BookCompressor.GetImageBytesForElectronicPub(path, true);
			// Is it reduced, even tho' we switched from 24bit depth to 32bit depth?
			Assert.Greater(originalBytes.Length, reducedBytes.Length, "lady24b.png is reduced from 3632x3872");
			using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
			{
				RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
				using (var newImage = PalasoImage.FromFileRobustly(tempFile.Path))
					Assert.AreEqual(PixelFormat.Format32bppArgb, newImage.Image.PixelFormat, "should have switched to 32bit depth");
			}
		}

		[Test]
		public void CompressBookForDevice_PointsAtDeviceXMatter()
		{
			var bookHtml = @"<html><head>
						<link rel='stylesheet' href='Basic Book.css' type='text/css'></link>
						<link rel='stylesheet' href='Traditional-XMatter.css' type='text/css'></link>
					</head><body>
					<div class='bloom-page' id='guid1'></div>
			</body></html>";
			TestHtmlAfterCompression(bookHtml,
				assertionsOnResultingHtmlString: html =>
				{
					var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
					AssertThatXmlIn.Dom(htmlDom)
						.HasSpecifiedNumberOfMatchesForXpath(
							"//html/head/link[@rel='stylesheet' and @href='Device-XMatter.css' and @type='text/css']", 1);
					AssertThatXmlIn.Dom(htmlDom)
						.HasNoMatchForXpath(
							"//html/head/link[@rel='stylesheet' and @href='Traditional-XMatter.css' and @type='text/css']");

				});
		}

		class StubProgress : IWebSocketProgress
		{
			public List<string> MessagesNotLocalized = new List<string>();
			public void MessageWithoutLocalizing(string message)
			{
				MessagesNotLocalized.Add(message);
			}
			public List<string> ErrorsNotLocalized = new List<string>();
			public void ErrorWithoutLocalizing(string message)
			{
				ErrorsNotLocalized.Add(message);
			}

			public void Message(string idSuffix, string comment, string message, bool useL10nIdPrefix = true)
			{
				MessagesNotLocalized.Add(string.Format(message));
			}

			public void MessageWithParams(string id, string comment, string message, params object[] parameters)
			{
				MessagesNotLocalized.Add(string.Format(message, parameters));
			}

			public void ErrorWithParams(string id, string comment, string message, params object[] parameters)
			{
				ErrorsNotLocalized.Add(string.Format(message, parameters));
			}

			public void MessageWithColorAndParams(string id, string comment, string color, string message, params object[] parameters)
			{
				MessagesNotLocalized.Add("<span style='color:" + color + "'>" + string.Format(message, parameters) + "</span>");
			}
		}

		class StubFontFinder : IFontFinder {
			public StubFontFinder()
			{
				FontsWeCantInstall = new HashSet<string>();
			}
			public Dictionary<string, string[]> FilesForFont = new Dictionary<string, string[]>();
			public IEnumerable<string> GetFilesForFont(string fontName)
			{
				string[] result;
				FilesForFont.TryGetValue(fontName, out result);
				if (result == null)
					result = new string[0];
				return result;
			}

			public bool NoteFontsWeCantInstall { get; set; }
			public HashSet<string> FontsWeCantInstall { get; }
			public Dictionary<string, FontGroup> FontGroups = new Dictionary<string, FontGroup>();
			public FontGroup GetGroupForFont(string fontName)
			{
				FontGroup result;
				FontGroups.TryGetValue(fontName, out result);
				return result;
			}
		}

		[Test]
		public void EmbedFonts_EmbedsExpectedFontsAndReportsOthers()
		{
			var bookHtml = @"<html><head>
						<link rel='stylesheet' href='Basic Book.css' type='text/css'></link>
						<link rel='stylesheet' href='Traditional-XMatter.css' type='text/css'></link>
						<link rel='stylesheet' href='CustomBookStyles.css' type='text/css'></link>
						<style type='text/css' title='userModifiedStyles'>
							/*<![CDATA[*/
							.Times-style[lang='tpi'] { font-family: Times New Roman ! important; font-size: 12pt  }
							/*]]>*/
						</style>
					</head><body>
					<div class='bloom-page' id='guid1'></div>
			</body></html>";
			var testBook = CreateBookWithPhysicalFile(bookHtml, bringBookUpToDate: false);
			var fontFileFinder = new StubFontFinder();
			using (var tempFontFolder = new TemporaryFolder("EmbedFonts_EmbedsExpectedFontsAndReportsOthers"))
			{
				fontFileFinder.NoteFontsWeCantInstall = true;

				// Font called for in HTML
				var timesNewRomanFileName = "Times New Roman R.ttf";
				var tnrPath = Path.Combine(tempFontFolder.Path, timesNewRomanFileName);
				File.WriteAllText(tnrPath, "This is phony TNR");

				// Font called for in custom styles CSS
				var calibreFileName = "Calibre R.ttf";
				var calibrePath = Path.Combine(tempFontFolder.Path, calibreFileName);
				File.WriteAllBytes(calibrePath, new byte[200008]); // we want something with a size greater than zero in megs

				fontFileFinder.FilesForFont["Times New Roman"] = new[] { tnrPath };
				fontFileFinder.FilesForFont["Calibre"] = new [] { calibrePath };
				fontFileFinder.FontsWeCantInstall.Add("NotAllowed");
				// And "NotFound" just doesn't get a mention anywhere.

				var stubProgress = new StubProgress();

				var customStylesPath = Path.Combine(testBook.FolderPath, "CustomBookStyles.css");
				File.WriteAllText(customStylesPath, ".someStyle {font-family:Calibre} .otherStyle {font-family: NotFound} .yetAnother {font-family:NotAllowed}");

				var tnrGroup = new FontGroup();
				tnrGroup.Normal = tnrPath;
				fontFileFinder.FontGroups["Times New Roman"] = tnrGroup;
				var calibreGroup = new FontGroup();
				calibreGroup.Normal = calibrePath;
				fontFileFinder.FontGroups["Calibre"] = calibreGroup;

				BloomReaderFileMaker.EmbedFonts(testBook, stubProgress, fontFileFinder);

				Assert.That(File.Exists(Path.Combine(testBook.FolderPath, timesNewRomanFileName)));
				Assert.That(File.Exists(Path.Combine(testBook.FolderPath, calibreFileName)));
				Assert.That(stubProgress.MessagesNotLocalized, Has.Member("Checking Times New Roman font: License OK for embedding."));
				Assert.That(stubProgress.MessagesNotLocalized, Has.Member("<span style='color:blue'>Embedding font Times New Roman at a cost of 0.0 megs</span>"));
				Assert.That(stubProgress.MessagesNotLocalized, Has.Member("Checking Calibre font: License OK for embedding."));
				Assert.That(stubProgress.MessagesNotLocalized, Has.Member("<span style='color:blue'>Embedding font Calibre at a cost of 0.2 megs</span>"));

				Assert.That(stubProgress.ErrorsNotLocalized, Has.Member("Checking NotAllowed font: License does not permit embedding."));
				Assert.That(stubProgress.ErrorsNotLocalized, Has.Member("Substituting \"Andika New Basic\" for \"NotAllowed\""));
				Assert.That(stubProgress.ErrorsNotLocalized, Has.Member("Checking NotFound font: No font found to embed."));
				Assert.That(stubProgress.ErrorsNotLocalized, Has.Member("Substituting \"Andika New Basic\" for \"NotFound\""));

				var fontSourceRulesPath = Path.Combine(testBook.FolderPath, "fonts.css");
				var fontSource = RobustFile.ReadAllText(fontSourceRulesPath);
				// We're OK with these in either order.
				string lineTimes = "@font-face {font-family:'Times New Roman'; font-weight:normal; font-style:normal; src:url(Times New Roman R.ttf) format('opentype');}"
					+ Environment.NewLine;
				string lineCalibre = "@font-face {font-family:'Calibre'; font-weight:normal; font-style:normal; src:url(Calibre R.ttf) format('opentype');}"
					+ Environment.NewLine;
				Assert.That(fontSource, Is.EqualTo(lineTimes + lineCalibre).Or.EqualTo(lineCalibre + lineTimes));
				AssertThatXmlIn.Dom(testBook.RawDom).HasSpecifiedNumberOfMatchesForXpath("//link[@href='fonts.css']", 1);
			}

		}

		private void TestHtmlAfterCompression(string originalBookHtml, Action<string> actionsOnFolderBeforeCompressing = null,
			Action<string> assertionsOnResultingHtmlString = null,
			Action<ZipFile> assertionsOnZipArchive = null,
			Action<ZipFile> assertionsOnRepeat = null)
		{
			var testBook = CreateBookWithPhysicalFile(originalBookHtml, bringBookUpToDate: true);
			var bookFileName = Path.GetFileName(testBook.GetPathHtmlFile());

			actionsOnFolderBeforeCompressing?.Invoke(testBook.FolderPath);

			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, testBook, _bookServer, Color.Azure, new NullWebSocketProgress());
				var zip = new ZipFile(bloomdTempFile.Path);
				assertionsOnZipArchive?.Invoke(zip);
				var newHtml = GetEntryContents(zip, bookFileName);
				assertionsOnResultingHtmlString?.Invoke(newHtml);
				if (assertionsOnRepeat != null)
				{
					// compress it again! Used for checking important repeatable results
					using (var extraTempFile =
						TempFile.WithFilenameInTempFolder(testBook.Title + "2" + BookCompressor.ExtensionForDeviceBloomBook))
					{
						BookCompressor.CompressBookForDevice(extraTempFile.Path, testBook, _bookServer, Color.Azure, new NullWebSocketProgress());
						zip = new ZipFile(extraTempFile.Path);
						assertionsOnRepeat(zip);
					}
				}
			}
		}
	}
}
