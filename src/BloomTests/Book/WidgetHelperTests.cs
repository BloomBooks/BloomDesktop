using System;
using System.IO;
using System.Text;
using Bloom;
using Bloom.Book;
using BloomTemp;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Book
{
    public class WidgetHelperTests
    {
        private TemporaryFolder _bookFolder;
        private TempFile _widgetFile;
        private string _widgetShortname;
        private string _activityFolder;
        private string _secondActivityFolder;
        private UrlPathString _firstWidgetSrcPath;
        private UrlPathString _secondWidgetSrcPath;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _bookFolder = new TemporaryFolder("AddWidgetFilesToBookFolderTests");
            _widgetFile = TempFile.WithFilename(
                "My Wi.,dget N&am%e is long and even more than         50.wdgt"
            );
            _widgetShortname = WidgetHelper.GetShortWidgetName(
                Path.GetFileNameWithoutExtension(_widgetFile.Path)
            );
            _activityFolder = Path.Combine(_bookFolder.FolderPath, "activities");
            _secondActivityFolder = Path.Combine(_activityFolder, _widgetShortname + "1");

            SetupZip(zip => { });

            _firstWidgetSrcPath = WidgetHelper.AddWidgetFilesToBookFolder(
                _bookFolder.FolderPath,
                _widgetFile.Path
            );
            _secondWidgetSrcPath = UrlPathString.CreateFromUnencodedString(
                "activities/" + _widgetShortname + "1/" + "index.htm"
            );
        }

        private void SetupZip(Action<ZipFile> changesToMake)
        {
            var zip = ZipFile.Create(_widgetFile.Path);
            zip.BeginUpdate();
            zip.Add(new DataFromString("fake html"), "index.htm", CompressionMethod.Deflated);
            zip.Add(new DataFromString("fake css"), "base.css", CompressionMethod.Deflated);
            zip.Add(
                new DataFromString("fake image"),
                "images/picture.jpg",
                CompressionMethod.Deflated
            );
            changesToMake(zip);
            zip.CommitUpdate();
            zip.Close();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _widgetFile.Dispose();
            _bookFolder.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_widgetFile.Path))
                File.Delete(_widgetFile.Path);
            if (Directory.Exists(_secondActivityFolder))
                RobustIO.DeleteDirectoryAndContents(_secondActivityFolder);
        }

        [Test]
        public void AddWidgetFilesToBookFolder_CreatesExpectedFiles()
        {
            ValidateOriginalActivityFiles();
        }

        [Test]
        public void AddWidgetFilesToBookFolder_ReturnsExpectedPath()
        {
            Assert.That(
                _firstWidgetSrcPath.NotEncoded,
                Is.EqualTo($"activities/{_widgetShortname}/index.htm")
            );
        }

        private void ValidateOriginalActivityFiles()
        {
            var thisActivityFolder = Path.Combine(_activityFolder, _widgetShortname);
            Assert.That(
                File.ReadAllText(Path.Combine(thisActivityFolder, "index.htm")),
                Is.EqualTo("fake html")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(thisActivityFolder, "base.css")),
                Is.EqualTo("fake css")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(thisActivityFolder, "images", "picture.jpg")),
                Is.EqualTo("fake image")
            );
        }

        [Test]
        public void AddWidgetFilesToBookFolder_SameInput_DoesNotDuplicateFolder()
        {
            SetupZip(zip => { });
            Assert.That(
                WidgetHelper.AddWidgetFilesToBookFolder(_bookFolder.FolderPath, _widgetFile.Path),
                Is.EqualTo(_firstWidgetSrcPath)
            ); // the same path, to the same folder

            Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(1));
        }

        [Test]
        public void AddWidgetFilesToBookFolder_OneFileInZipDifferent_MakesNewFolder()
        {
            SetupZip(zip =>
            {
                zip.Add(
                    new DataFromString("fake css modified"),
                    "base.css",
                    CompressionMethod.Deflated
                );
            });

            Assert.That(
                WidgetHelper.AddWidgetFilesToBookFolder(_bookFolder.FolderPath, _widgetFile.Path),
                Is.EqualTo(_secondWidgetSrcPath)
            );

            Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

            // and we should have files for the for the new widget folder.
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "index.htm")),
                Is.EqualTo("fake html")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "base.css")),
                Is.EqualTo("fake css modified")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "picture.jpg")),
                Is.EqualTo("fake image")
            );

            ValidateOriginalActivityFiles(); // We could do this in other tests, but I think once is enough.
        }

        [Test]
        public void AddWidgetFilesToBookFolder_OneExtraFileInZip_MakesNewFolder()
        {
            SetupZip(zip =>
            {
                zip.Add(
                    new DataFromString("fake image 2"),
                    "images/another.jpg",
                    CompressionMethod.Deflated
                ); // add an extra file
            });

            Assert.That(
                WidgetHelper.AddWidgetFilesToBookFolder(_bookFolder.FolderPath, _widgetFile.Path),
                Is.EqualTo(_secondWidgetSrcPath)
            );
            Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

            // and a new set

            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "index.htm")),
                Is.EqualTo("fake html")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "base.css")),
                Is.EqualTo("fake css")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "picture.jpg")),
                Is.EqualTo("fake image")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "another.jpg")),
                Is.EqualTo("fake image 2")
            );
        }

        [Test]
        public void AddWidgetFilesToBookFolder_OneLessFileInZip_MakesNewFolder()
        {
            SetupZip(zip =>
            {
                zip.Delete("base.css"); // remove original file
            });

            Assert.That(
                WidgetHelper.AddWidgetFilesToBookFolder(_bookFolder.FolderPath, _widgetFile.Path),
                Is.EqualTo(_secondWidgetSrcPath)
            );
            Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "index.htm")),
                Is.EqualTo("fake html")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "picture.jpg")),
                Is.EqualTo("fake image")
            );
        }

        [Test]
        public void AddWidgetFilesToBookFolder_OneExtraFileInExtraFolderInZip_MakesNewFolder()
        {
            SetupZip(zip =>
            {
                zip.Add(
                    new DataFromString("fake audio"),
                    "audio/noise.mp3",
                    CompressionMethod.Deflated
                ); // add an extra file
            });

            Assert.That(
                WidgetHelper.AddWidgetFilesToBookFolder(_bookFolder.FolderPath, _widgetFile.Path),
                Is.EqualTo(_secondWidgetSrcPath)
            );
            Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "index.htm")),
                Is.EqualTo("fake html")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "base.css")),
                Is.EqualTo("fake css")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "audio", "noise.mp3")),
                Is.EqualTo("fake audio")
            );
        }

        [Test]
        public void AddWidgetFilesToBookFolder_DifferentIndexFile_MakesNewFolder()
        {
            SetupZip(zip =>
            {
                zip.Add(new DataFromString("fake html"), "index.html", CompressionMethod.Deflated);
            });

            // Note that here, the result is in the second folder, but has a different name for the file.
            Assert.That(
                WidgetHelper.AddWidgetFilesToBookFolder(_bookFolder.FolderPath, _widgetFile.Path),
                Is.EqualTo(
                    UrlPathString.CreateFromUnencodedString(
                        $"activities/{_widgetShortname}1/index.html"
                    )
                )
            );
            Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "index.html")),
                Is.EqualTo("fake html")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "base.css")),
                Is.EqualTo("fake css")
            );
            Assert.That(
                File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "picture.jpg")),
                Is.EqualTo("fake image")
            );
        }
    }

    class DataFromString : IStaticDataSource
    {
        private Stream _content;

        public DataFromString(string input)
        {
            _content = new MemoryStream(Encoding.UTF8.GetBytes(input));
        }

        public Stream GetSource()
        {
            return _content;
        }
    }
}
