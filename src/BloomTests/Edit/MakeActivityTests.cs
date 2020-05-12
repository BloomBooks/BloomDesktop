using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Bloom;
using Bloom.Edit;
using BloomTemp;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.IO;
using RobustIO = SIL.IO.RobustIO;

namespace BloomTests.Edit
{
	public class MakeActivityTests
	{
		private TemporaryFolder _bookFolder;
		private TempFile _widgetFile;
		private string _activityFolder;
		private string _secondActivityFolder;
		private string _firstWidgetSrcPath;
		private string _secondWidgetSrcPath;
		[OneTimeSetUp]
		public void OneTimeSetup()
		{
			_bookFolder = new TemporaryFolder("MakeActivityTests");
			_widgetFile = TempFile.WithExtension("wdgt");
			_activityFolder = Path.Combine(_bookFolder.FolderPath, "activities");
			_secondActivityFolder =
				Path.Combine(_activityFolder, Path.GetFileNameWithoutExtension(_widgetFile.Path) + "2");

			SetupZip(zip => { });

			_firstWidgetSrcPath = EditingModel.MakeActivity(_bookFolder.FolderPath, _widgetFile.Path);
			_secondWidgetSrcPath =
				"activities/" + Path.GetFileNameWithoutExtension(_widgetFile.Path) + "2/" + "index.htm";
		}

		private void SetupZip(Action<ZipFile> changesToMake)
		{
			var zip = ZipFile.Create(_widgetFile.Path);
			zip.BeginUpdate();
			zip.Add(new DataFromString("fake html"), "index.htm", CompressionMethod.Deflated);
			zip.Add(new DataFromString("fake css"), "base.css", CompressionMethod.Deflated);
			zip.Add(new DataFromString("fake image"), "images/picture.jpg", CompressionMethod.Deflated);
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
		public void MakeActivity_CreatesExpectedFiles()
		{
			ValidateOriginalActivityFiles();
		}

		[Test]
		public void MakeActivity_ReturnsExpectedPath()
		{
			Assert.That(_firstWidgetSrcPath, Is.EqualTo("activities/" + Path.GetFileNameWithoutExtension(_widgetFile.Path) + "/" + "index.htm"));
		}

		private void ValidateOriginalActivityFiles()
		{
			var thisActivityFolder =
				Path.Combine(_activityFolder, Path.GetFileNameWithoutExtension(_widgetFile.Path));
			Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "index.htm")), Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "base.css")), Is.EqualTo("fake css"));
			Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));
		}

		[Test]
		public void MakeActivity_SameInput_DoesNotDuplicateFolder()
		{
			SetupZip(zip => { });
			Assert.That(EditingModel.MakeActivity(_bookFolder.FolderPath, _widgetFile.Path), Is.EqualTo(_firstWidgetSrcPath)); // the same path, to the same folder

			Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(1));
		}

		[Test]
		public void MakeActivity_OneFileInZipDifferent_MakesNewFolder()
		{
			SetupZip(zip => {
				zip.Add(new DataFromString("fake css modified"), "base.css", CompressionMethod.Deflated);
			});

			Assert.That(EditingModel.MakeActivity(_bookFolder.FolderPath, _widgetFile.Path), Is.EqualTo(_secondWidgetSrcPath));

			Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

			// and we should have files for the for the new widget folder.
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "index.htm")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "base.css")),
				Is.EqualTo("fake css modified"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));

			ValidateOriginalActivityFiles(); // We could do this in other tests, but I think once is enough.
		}

		[Test]
		public void MakeActivity_OneExtraFileInZip_MakesNewFolder()
		{
			SetupZip(zip =>
			{
				zip.Add(new DataFromString("fake image 2"), "images/another.jpg",
					CompressionMethod.Deflated); // add an extra file
			});

			Assert.That(EditingModel.MakeActivity(_bookFolder.FolderPath, _widgetFile.Path), Is.EqualTo(_secondWidgetSrcPath));
			Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

			// and a new set

			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "index.htm")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "base.css")), Is.EqualTo("fake css"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "another.jpg")),
				Is.EqualTo("fake image 2"));
		}

		[Test]
		public void MakeActivity_OneLessFileInZip_MakesNewFolder()
		{
			SetupZip(zip =>
			{
				zip.Delete("base.css"); // remove original file
			});

			Assert.That(EditingModel.MakeActivity(_bookFolder.FolderPath, _widgetFile.Path), Is.EqualTo(_secondWidgetSrcPath));
			Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "index.htm")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));
		}

		[Test]
		public void MakeActivity_OneExtraFileInExtraFolderInZip_MakesNewFolder()
		{
			SetupZip(zip =>
			{
				zip.Add(new DataFromString("fake audio"), "audio/noise.mp3",
					CompressionMethod.Deflated); // add an extra file
			});

			Assert.That(EditingModel.MakeActivity(_bookFolder.FolderPath, _widgetFile.Path), Is.EqualTo(_secondWidgetSrcPath));
			Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "index.htm")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "base.css")), Is.EqualTo("fake css"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "audio", "noise.mp3")),
				Is.EqualTo("fake audio"));
		}

		[Test]
		public void MakeActivity_DifferentIndexFile_MakesNewFolder()
		{
			SetupZip(zip =>
			{
				zip.Add(new DataFromString("fake html"), "index.html", CompressionMethod.Deflated);
			});

			// Note that here, the result is in the second folder, but has a different name for the file.
			Assert.That(EditingModel.MakeActivity(_bookFolder.FolderPath, _widgetFile.Path),
				Is.EqualTo("activities/" + Path.GetFileNameWithoutExtension(_widgetFile.Path) + "2/" + "index.html"));
			Assert.That(Directory.GetDirectories(_activityFolder), Has.Length.EqualTo(2));

			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "index.html")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "base.css")), Is.EqualTo("fake css"));
			Assert.That(File.ReadAllText(Path.Combine(_secondActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));
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
