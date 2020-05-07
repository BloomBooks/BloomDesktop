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
		private TemporaryFolder bookFolder;
		private TempFile widgetFile;
		private string activityFolder;
		private string secondActivityFolder;
		private string firstWidgetSrcPath;
		private string secondWidgetSrcPath;
		[OneTimeSetUp]
		public void Setup()
		{
			bookFolder = new TemporaryFolder("MakeActivityTests");
			widgetFile = TempFile.WithExtension("wdgt");
			activityFolder = Path.Combine(bookFolder.FolderPath, "activities");
			secondActivityFolder =
				Path.Combine(activityFolder, Path.GetFileNameWithoutExtension(widgetFile.Path) + "2");

			// This is a default state for the zip, to which each test should return it if it modifies it.
			var zip = ZipFile.Create(widgetFile.Path);
			zip.BeginUpdate();
			zip.Add(new DataFromString("fake html"), "index.htm", CompressionMethod.Deflated);
			zip.Add(new DataFromString("fake css"), "base.css", CompressionMethod.Deflated);
			zip.Add(new DataFromString("fake image"), "images/picture.jpg", CompressionMethod.Deflated);
			zip.CommitUpdate();
			zip.Close();

			firstWidgetSrcPath = EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path);
			secondWidgetSrcPath =
				"activities/" + Path.GetFileNameWithoutExtension(widgetFile.Path) + "2/" + "index.htm";
		}
		[OneTimeTearDown]
		public void TearDown()
		{
			widgetFile.Dispose();
			bookFolder.Dispose();
		}

		[Test]
		public void MakeActivity_CreatesExpectedFiles()
		{
			ValidateOriginalActivityFiles();
		}

		[Test]
		public void MakeActivity_ReturnsExpectedPath()
		{
			Assert.That(firstWidgetSrcPath, Is.EqualTo("activities/" + Path.GetFileNameWithoutExtension(widgetFile.Path) + "/" + "index.htm"));
		}

		private void ValidateOriginalActivityFiles()
		{
			var thisActivityFolder =
				Path.Combine(activityFolder, Path.GetFileNameWithoutExtension(widgetFile.Path));
			Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "index.htm")), Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "base.css")), Is.EqualTo("fake css"));
			Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));
		}

		[Test]
		public void MakeActivity_SameInput_DoesNotDuplicateFolder()
		{
			Assert.That(EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path), Is.EqualTo(firstWidgetSrcPath)); // the same path, to the same folder

			Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(1));
		}

		[Test]
		public void MakeActivity_OneFileInZipDifferent_MakesNewFolder()
		{
			var zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Add(new DataFromString("fake css modified"), "base.css", CompressionMethod.Deflated);
			zip.CommitUpdate();
			zip.Close();

			Assert.That(EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path), Is.EqualTo(secondWidgetSrcPath));

			Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(2));

			// and we should have files for the for the new widget folder.
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "index.htm")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "base.css")),
				Is.EqualTo("fake css modified"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));

			ValidateOriginalActivityFiles(); // We could do this in other tests, but I think once is enough.

			// restore the standard state of the zip file.
			zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Add(new DataFromString("fake css"), "base.css", CompressionMethod.Deflated); // back to original
			zip.CommitUpdate();
			zip.Close();

			RobustIO.DeleteDirectoryAndContents(secondActivityFolder);
		}

		[Test]
		public void MakeActivity_OneExtraFileInZip_MakesNewFolder()
		{
			var zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Add(new DataFromString("fake image 2"), "images/another.jpg",
				CompressionMethod.Deflated); // add an extra file
			zip.CommitUpdate();
			zip.Close();

			Assert.That(EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path), Is.EqualTo(secondWidgetSrcPath));
			Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(2));

			// and a new set

			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "index.htm")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "base.css")), Is.EqualTo("fake css"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "images", "another.jpg")),
				Is.EqualTo("fake image 2"));

			zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Delete("images/another.jpg"); // remove an extra file
			zip.CommitUpdate();
			zip.Close();

			RobustIO.DeleteDirectoryAndContents(secondActivityFolder);
		}

		[Test]
		public void MakeActivity_OneLessFileInZip_MakesNewFolder()
		{
			var zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Delete("base.css"); // remove original file
			zip.CommitUpdate();
			zip.Close();

			Assert.That(EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path), Is.EqualTo(secondWidgetSrcPath));
			Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(2));

			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "index.htm")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));

			zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Add(new DataFromString("fake css"), "base.css", CompressionMethod.Deflated); // restore original
			zip.CommitUpdate();
			zip.Close();

			RobustIO.DeleteDirectoryAndContents(secondActivityFolder);
		}

		[Test]
		public void MakeActivity_OneExtraFileInExtraFolderInZip_MakesNewFolder()
		{
			var zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Add(new DataFromString("fake audio"), "audio/noise.mp3",
				CompressionMethod.Deflated); // add an extra file
			zip.CommitUpdate();
			zip.Close();

			Assert.That(EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path), Is.EqualTo(secondWidgetSrcPath));
			Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(2));

			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "index.htm")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "base.css")), Is.EqualTo("fake css"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "audio", "noise.mp3")),
				Is.EqualTo("fake audio"));

			zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Delete( "audio/noise.mp3");
			zip.CommitUpdate();
			zip.Close();
			RobustIO.DeleteDirectoryAndContents(secondActivityFolder);
		}

		[Test]
		public void MakeActivity_DifferentIndexFile_MakesNewFolder()
		{
			var zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Add(new DataFromString("fake html"), "index.html", CompressionMethod.Deflated);
			zip.Delete("index.htm");
			zip.CommitUpdate();
			zip.Close();

			// Note that here, the result is in the second folder, but has a different name for the file.
			Assert.That(EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path),
				Is.EqualTo("activities/" + Path.GetFileNameWithoutExtension(widgetFile.Path) + "2/" + "index.html"));
			Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(2));

			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "index.html")),
				Is.EqualTo("fake html"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "base.css")), Is.EqualTo("fake css"));
			Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "images", "picture.jpg")),
				Is.EqualTo("fake image"));

			zip = new ZipFile(widgetFile.Path);
			zip.BeginUpdate();
			zip.Add(new DataFromString("fake html"), "index.htm", CompressionMethod.Deflated);
			zip.Delete("index.html"); zip.CommitUpdate();
			zip.Close();
			RobustIO.DeleteDirectoryAndContents(secondActivityFolder);
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
