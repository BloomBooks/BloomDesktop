using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Collection;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Collection
{
    internal class CollectionFileFilterTests
    {
        private TemporaryFolder _collectionFolder;
        private string _someBookFolderPath;
        private string _someBookPath;
        private BookFileFilter _someBookFilter;
        private string _otherBookFolderPath;
        private string _otherBookPath;
        private BookFileFilter _otherBookFilter;
        private CollectionFileFilter _filter;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _collectionFolder = new TemporaryFolder("Collection File Filter");
            _someBookFolderPath = Path.Combine(_collectionFolder.FolderPath, "SomeBook");
            Directory.CreateDirectory(_someBookFolderPath);
            _someBookPath = Path.Combine(
                _someBookFolderPath,
                Path.GetFileName(_someBookFolderPath) + ".htm"
            );
            var normalHtmlContent =
                $@"<html><head></head><body>
					<div class='bloom-page numberedPage' id='page1' data-page-number='1' data-backgroundaudio='Abcdefg.mp3'>
						<div class='marginBox'>
							<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
	                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 44px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" data-audiorecordingmode=""Sentence"">
	                                <p><span id=""i1083a390-c1ef-41d2-a55d-815eacb5c08c"" class=""audio-sentence"" recordingmd5=""5b5efdab7f705554614a6383ae6d9469"" data-duration=""3.004082"">This is some akl data</span></p>
	                            </div>
								<div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 44px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""es"" contenteditable=""true"" data-languagetipcontent=""Spanish"" data-audiorecordingmode=""Sentence"">
	                                <p><span id=""i1083a390-c1ef-41d2-a55d-815eacb5c08d"" class=""audio-sentence"" recordingmd5=""5b5efdab7f705554614a6383ae6d9469"" data-duration=""3.004082"">This is Spanish</span></p>
	                            </div>
								<div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 44px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""de"" contenteditable=""true"" data-languagetipcontent=""German"" data-audiorecordingmode=""Sentence"">
	                                <p><span id=""i1083a390-c1ef-41d2-a55d-815eacb5c08e"" class=""audio-sentence"" recordingmd5=""5b5efdab7f705554614a6383ae6d9469"" data-duration=""3.004082"">This is Spanish</span></p>
	                            </div>

	                            <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
	                                <p></p>
	                            </div>

	                            <div class=""bloom-editable normal-style bloom-contentNational1"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""en"" contenteditable=""true"" data-languagetipcontent=""English"">
	                                <p></p>
	                            </div>
	                        </div>
							<div class=""bloom-videoContainer bloom-noVideoSelected bloom-leadingElement bloom-selected"">
	                            <video>
	                            <source src=""video/847700f2-cfa3-41a4-8f5b-29198b8eca28.mp4""></source></video>
	                        </div>
						</div>
					</div>
				</body></html>";
            RobustFile.WriteAllText(_someBookPath, normalHtmlContent);
            _someBookFilter = new BookFileFilter(_someBookFolderPath);

            _otherBookFolderPath = Path.Combine(_collectionFolder.FolderPath, "otherBook");
            Directory.CreateDirectory(_otherBookFolderPath);
            _otherBookPath = Path.Combine(
                _otherBookFolderPath,
                Path.GetFileName(_otherBookFolderPath) + ".htm"
            );
            RobustFile.WriteAllText(_otherBookPath, normalHtmlContent);
            _otherBookFilter = new BookFileFilter(_otherBookFolderPath);

            _filter = new CollectionFileFilter();
            _filter.AddBookFilter(_someBookFilter);
            _filter.AddBookFilter(_otherBookFilter);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _collectionFolder.Dispose();
        }

        [Test]
        public void Filter_AcceptsBothHtmlFiles()
        {
            Assert.That(_filter.ShouldAllow(_someBookPath), Is.True);
            Assert.That(_filter.ShouldAllow(_otherBookPath), Is.True);
        }

        [Test]
        public void Filter_DoesNotAcceptUnwantedFiles()
        {
            Assert.That(
                _filter.ShouldAllow(Path.Combine(_someBookFolderPath, "rubbish.bad")),
                Is.False
            );
        }

        [Test]
        public void Filter_AcceptsRootCssFiles()
        {
            Assert.That(
                _filter.ShouldAllow(
                    Path.Combine(_collectionFolder.FolderPath, "CustomCollectionStyles.css")
                ),
                Is.True
            );
            Assert.That(
                _filter.ShouldAllow(
                    Path.Combine(_collectionFolder.FolderPath, "settingsCollectionStyles.css")
                ),
                Is.True
            );
            // Not sure we want this
            Assert.That(
                _filter.ShouldAllow(
                    Path.Combine(_collectionFolder.FolderPath, "SomeRandomStyles.css")
                ),
                Is.True
            );
        }

        [Test]
        public void Filter_RejectsOtherRootFiles()
        {
            Assert.That(
                _filter.ShouldAllow(Path.Combine(_collectionFolder.FolderPath, "Rubbish.png")),
                Is.False
            );
            Assert.That(
                _filter.ShouldAllow(Path.Combine(_collectionFolder.FolderPath, "Nonsence.htm")),
                Is.False
            );
            // Not absolutely sure we don't want this
            Assert.That(
                _filter.ShouldAllow(
                    Path.Combine(_collectionFolder.FolderPath, "Blah.bloomCollection")
                ),
                Is.False
            );
        }
    }
}
