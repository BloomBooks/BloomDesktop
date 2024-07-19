using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Book
{
    public class BookFileFilterTests
    {
        // The 'normal' book has an html file whose name matches the folder.
        // Relatively unusually, it also has a configuration.html (which should get copied
        // only when ForEditing is true) and another HTML file (which should not ever).
        private TemporaryFolder _normalBookFolder;
        private string _normalBookFolderPath;
        private string _normalBookPath;
        private string _configHtmlPath;
        private string _otherHtmlPath;
        private int _normalBookPrefixLength;
        private BookFileFilter _normalFilter; // initialized to filter normal book
        private BookFileFilter _filterForEdit; // initialized to filter normal book for editing
        private BookFileFilter _filterForInteractive; // initialized to filter normal book for interactive (but not editing)

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _normalBookFolder = new TemporaryFolder("Book File Filter");
            _normalBookFolderPath = _normalBookFolder.FolderPath;
            _normalBookPrefixLength = _normalBookFolderPath.Length + 1; // include following slash
            Directory.CreateDirectory(_normalBookFolderPath);
            _normalBookPath = Path.Combine(
                _normalBookFolderPath,
                Path.GetFileName(_normalBookFolderPath) + ".htm"
            );
            _configHtmlPath = Path.Combine(_normalBookFolderPath, "configuration.html");
            _otherHtmlPath = Path.Combine(_normalBookFolderPath, "other.html");
            var normalHtmlContent =
                $@"<html><head></head><body>
					<div class='bloom-page numberedPage' id='page1' data-page-number='1' data-backgroundaudio='Abcdefg.mp3' data-correct-sound='right.mp3' data-wrong-sound='bad.mp3'>
						<div class='marginBox'>
							<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"" data-sound='playme.mp3'>
	                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 44px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" data-audiorecordingmode=""Sentence"">
	                                <p><span id=""i1083a390-c1ef-41d2-a55d-815eacb5c08c"" class=""audio-sentence"" recordingmd5=""5b5efdab7f705554614a6383ae6d9469"" data-duration=""3.004082"">This is some akl data</span></p>
	                            </div>
								<div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on audio-sentence"" id=""i1083a390-c1ef-41d2-a55d-815eacb5c08d"" recordingmd5=""5b5efdab7f705554614a6383ae6d9469"" data-duration=""3.004082"" style=""min-height: 44px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""es"" contenteditable=""true"" data-languagetipcontent=""Spanish"" data-audiorecordingmode=""TextBox"">
	                                <p>This is Spanish</p>
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
            var configHtmlContent = $@"<html><head></head><body></body></html>";
            RobustFile.WriteAllText(_normalBookPath, normalHtmlContent);
            RobustFile.WriteAllText(_configHtmlPath, configHtmlContent);
            RobustFile.WriteAllText(_otherHtmlPath, configHtmlContent);
            _normalFilter = new BookFileFilter(_normalBookFolderPath);
            _filterForEdit = new BookFileFilter(_normalBookFolderPath)
            {
                IncludeFilesForContinuedEditing = true
            };
            _filterForInteractive = new BookFileFilter(_normalBookFolderPath)
            {
                IncludeFilesNeededForBloomPlayer = true
            };
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _normalBookFolder.Dispose();
        }

        [Test]
        public void FilterPaths_PassesThingsThatPassFilter()
        {
            var bookFolder = @"c:\Users\Joe\Documents\Bloom\My Book";
            var data = new[]
            {
                Tuple.Create(bookFolder + "/Basic Book.css", true),
                Tuple.Create(bookFolder + "/My Book.pdf", false),
                Tuple.Create(bookFolder + "/license.png", true),
                Tuple.Create(bookFolder + "/My data.db", false),
            };
            var result = _normalFilter.FilterPaths(data.Select(t => t.Item1).ToArray(), bookFolder);
            foreach (var t in data)
            {
                if (t.Item2)
                {
                    Assert.That(result, Has.Member(t.Item1));
                }
                else
                {
                    Assert.That(result, Has.No.Member(t.Item1));
                }
            }
        }

        [Test]
        public void Filter_PassesOnlyRootHtmlFile()
        {
            Assert.That(
                _normalFilter.FilterRelative(_normalBookPath.Substring(_normalBookPrefixLength)),
                Is.True
            );
            Assert.That(
                _normalFilter.FilterRelative(_configHtmlPath.Substring(_normalBookPrefixLength)),
                Is.False
            );
            Assert.That(
                _normalFilter.FilterRelative(_otherHtmlPath.Substring(_normalBookPrefixLength)),
                Is.False
            );
        }

        [Test]
        public void Filter_ForEdit_PassesRootHtmlFileAndConfig()
        {
            Assert.That(
                _filterForEdit.FilterRelative(_normalBookPath.Substring(_normalBookPrefixLength)),
                Is.True
            );
            Assert.That(
                _filterForEdit.FilterRelative(_configHtmlPath.Substring(_normalBookPrefixLength)),
                Is.True
            );
            Assert.That(
                _filterForEdit.FilterRelative(_otherHtmlPath.Substring(_normalBookPrefixLength)),
                Is.False
            );
        }

        [Test]
        public void Filter_DoesNotPassStuffInUnwantedFolders()
        {
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("rubbish", "something.png")),
                Is.False
            );
        }

        [Test]
        public void Filter_ForInteractive_PassesEverythingInActivities()
        {
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("activities", "something.png")),
                Is.False
            );
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("activities", "audio", "mynoise.mp3")),
                Is.False
            );
            // Including subfolders; make sure to test mp3, wav, a subfolder called audio, video, files with extensions we normally exclude
            Assert.That(
                _filterForInteractive.FilterRelative(Path.Combine("activities", "something.png")),
                Is.True
            );
            Assert.That(
                _filterForInteractive.FilterRelative(
                    Path.Combine("activities", "audio", "mynoise.mp3")
                ),
                Is.True
            );
            Assert.That(
                _filterForInteractive.FilterRelative(
                    Path.Combine("activities", "video", "myvideo.mp4")
                ),
                Is.True
            );
            Assert.That(
                _filterForInteractive.FilterRelative(
                    Path.Combine("activities", "something.unknown")
                ),
                Is.True
            );
            Assert.That(
                _filterForInteractive.FilterRelative(Path.Combine("activities", "placeHolder.png")),
                Is.True
            );
            Assert.That(
                _filterForInteractive.FilterRelative(
                    Path.Combine("activities", "thumbnail-256.png")
                ),
                Is.True
            );
        }

        [Test]
        public void Filter_ForInteractive_PassesVersionDotTxt()
        {
            Assert.That(_filterForInteractive.FilterRelative("version.txt"), Is.True);
        }

        [Test]
        public void Filter_PassesImagesInRootFolder()
        {
            Assert.That(_normalFilter.FilterRelative("green elephants.png"), Is.True);
            Assert.That(_normalFilter.FilterRelative("pink frogs.jpg"), Is.True);
            Assert.That(_normalFilter.FilterRelative("yellow signs.svg"), Is.True);
            Assert.That(_normalFilter.FilterRelative("dirty thumbnails--cleaning.png"), Is.True);
        }

        [Test]
        public void Filter_ExcludesPlaceholdersInRootFolder()
        {
            Assert.That(_normalFilter.FilterRelative("placeHolder.png"), Is.False);
            Assert.That(_normalFilter.FilterRelative("placeHolder122.png"), Is.False);
        }

        [Test]
        public void Filter_ForEdit_PassesExtraExtensions()
        {
            Assert.That(_normalFilter.FilterRelative("something.md"), Is.False);
            Assert.That(_filterForEdit.FilterRelative("something.md"), Is.True);
        }

        [Test]
        public void Filter_ForBloomPlayer_PassesSpecialAudioFiles()
        {
            Assert.That(_normalFilter.FilterRelative(Path.Combine("audio", "right.mp3")), Is.False);
            Assert.That(_normalFilter.FilterRelative(Path.Combine("audio", "wrong.mp3")), Is.False);
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("audio", "playme.mp3")),
                Is.False
            );

            Assert.That(
                _filterForInteractive.FilterRelative(Path.Combine("audio", "right.mp3")),
                Is.True
            );
            Assert.That(
                _filterForInteractive.FilterRelative(Path.Combine("audio", "bad.mp3")),
                Is.True
            );
            Assert.That(
                _filterForInteractive.FilterRelative(Path.Combine("audio", "playme.mp3")),
                Is.True
            );
        }

        // For this we need a BookStorage. Possibly make a new constructor that just initializes the DOM, which is all many methods need.
        [Test]
        public void Filter_PassesAudio_ForRequestedLanguages()
        {
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("audio", "i1083a390-c1ef-41d2-a55d-815eacb5c08c.mp3")
                ),
                Is.False
            );
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("audio", "i1083a390-c1ef-41d2-a55d-815eacb5c08e.mp3")
                ),
                Is.False
            );
            _normalFilter.NarrationLanguages = new[] { "akl", "es" };
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("audio", "i1083a390-c1ef-41d2-a55d-815eacb5c08c.mp3")
                ),
                Is.True
            );
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("audio", "i1083a390-c1ef-41d2-a55d-815eacb5c08d.mp3")
                ),
                Is.True
            );
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("audio", "i1083a390-c1ef-41d2-a55d-815eacb5c08e.mp3")
                ),
                Is.False
            ); // wrong language
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("audio", "does not exist.mp3")),
                Is.False
            );
            _normalFilter.NarrationLanguages = null; // means everything
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("audio", "i1083a390-c1ef-41d2-a55d-815eacb5c08c.mp3")
                ),
                Is.True
            );
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("audio", "i1083a390-c1ef-41d2-a55d-815eacb5c08d.mp3")
                ),
                Is.True
            );
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("audio", "i1083a390-c1ef-41d2-a55d-815eacb5c08e.mp3")
                ),
                Is.True
            );
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("audio", "does not exist.mp3")),
                Is.False
            );
        }

        [Test]
        public void Filter_PassesMusicAudio()
        {
            _normalFilter.WantMusic = false;
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("audio", "Abcdefg.mp3")),
                Is.False
            );
            _normalFilter.WantMusic = true;
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("audio", "Abcdefg.mp3")),
                Is.True
            );
            Assert.That(_normalFilter.FilterRelative(Path.Combine("audio", "12345.mp3")), Is.False);
        }

        [Test]
        public void Filter_PassesVideoIfRequested()
        {
            _normalFilter.WantVideo = false;
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("video", "847700f2-cfa3-41a4-8f5b-29198b8eca28.mp4")
                ),
                Is.False
            );
            _normalFilter.WantVideo = true;
            Assert.That(
                _normalFilter.FilterRelative(
                    Path.Combine("video", "847700f2-cfa3-41a4-8f5b-29198b8eca28.mp4")
                ),
                Is.True
            );
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("video", "Unused.mp4")),
                Is.False
            );
        }

        [Test]
        public void Filter_PassesTemplatesIfForEditing()
        {
            Assert.That(
                _normalFilter.FilterRelative(Path.Combine("templates", "somefile.svg")),
                Is.False
            );
            Assert.That(
                _filterForEdit.FilterRelative(Path.Combine("templates", "somefile.svg")),
                Is.True
            );
        }

        [Test]
        public void Filter_PassesBookOrderForUpload()
        {
            Assert.That(_normalFilter.FilterRelative("myfile.BloomBookOrder"), Is.False);
            _normalFilter.AlwaysAccept("myfile.BloomBookOrder");
            Assert.That(_normalFilter.FilterRelative("myfile.BloomBookOrder"), Is.True);
        }
    }
}
