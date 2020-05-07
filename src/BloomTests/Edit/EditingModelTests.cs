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

namespace BloomTests.Edit
{
	public class EditingModelTests
	{
		[TestCase("index.htm")]
		[TestCase("index.html")]
		public void MakeActivity_CreatesExpectedFilesAndElements_ButNotDuplicates(string indexFileName)
		{
			// This is a monster but all the state we create in setting up for the test of a first-time
			// copy is useful in subsequent tests about duplicates and near-duplicates.
			using (var bookFolder = new TemporaryFolder("MakeActivity_CreatesExpectedFilesAndElements"))
			{
				using (var widgetFile = TempFile.WithExtension("wdgt"))
				{
					//var fsOut = RobustFile.Create(widgetFile.Path);
					//var zipStream = new ZipOutputStream(fsOut);
					//zipStream.SetLevel(1); // fast, poor compression is fine for this
					//zipStream.Close();
					var zip = ZipFile.Create(widgetFile.Path);
					zip.BeginUpdate();
					zip.Add(new DataFromString("fake html"), indexFileName, CompressionMethod.Deflated);
					zip.Add(new DataFromString("fake css"), "base.css", CompressionMethod.Deflated);
					zip.Add(new DataFromString("fake image"), "images/picture.jpg", CompressionMethod.Deflated);
					zip.CommitUpdate();
					zip.Close();

					var doc = new XmlDocument();
					var page = doc.CreateElement("div");
					doc.AppendChild(page);
					page.SetAttribute("class", "bloom-page A5Portrait");
					var container = doc.CreateElement("div");
					page.AppendChild(container);
					container.SetAttribute("class", "bloom-widgetContainer bloom-noWidgetSelected");

					EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path, new ElementProxy(container));

					var activityFolder = Path.Combine(bookFolder.FolderPath, "activities");
					var thisActivityFolder = Path.Combine(activityFolder, Path.GetFileNameWithoutExtension(widgetFile.Path));
					Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, indexFileName)), Is.EqualTo("fake html"));
					Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "base.css")), Is.EqualTo("fake css"));
					Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "images", "picture.jpg")), Is.EqualTo("fake image"));

					var iframe = container.GetElementsByTagName("iframe")[0];
					Assert.That(iframe.Attributes["src"].Value, Is.EqualTo("activities/" + Path.GetFileNameWithoutExtension(widgetFile.Path) + "/" + indexFileName));
					Assert.That(container.Attributes["class"].Value, Is.EqualTo("bloom-widgetContainer")); // no image selected class is gone
					Assert.That(page.Attributes["class"].Value, Does.Contain("bloom-interactive-page"));
					Assert.That(page.Attributes["data-activity"].Value, Is.EqualTo("iframe"));

					if (indexFileName == "index.htm")
						return; // all the rest is not useful to do twice.

					// Now, do it again, on the same element and with the same input data.
					EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path, new ElementProxy(container));

					// We should not make a second iframe when it's already there.
					Assert.That(container.ChildNodes.Count, Is.EqualTo(1));
					// Nor make a duplicate when the new activity has exactly the same content.
					Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(1));

					zip = new ZipFile(widgetFile.Path);
					zip.BeginUpdate();
					zip.Add(new DataFromString("fake css modified"), "base.css", CompressionMethod.Deflated);
					zip.CommitUpdate();
					zip.Close();

					// This time we should make a second folder, since the content of one file is different.
					EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path, new ElementProxy(container));
					Assert.That(container.ChildNodes.Count, Is.EqualTo(1));
					// Nor make a duplicate when the new activity has exactly the same content.
					Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(2));

					// and we should have files for the for the new widget folder.
					var secondActivityFolder = Path.Combine(activityFolder, Path.GetFileNameWithoutExtension(widgetFile.Path)+ "2");
					Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, indexFileName)), Is.EqualTo("fake html"));
					Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "base.css")), Is.EqualTo("fake css modified"));
					Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "images", "picture.jpg")), Is.EqualTo("fake image"));

					zip = new ZipFile(widgetFile.Path);
					zip.BeginUpdate();
					zip.Add(new DataFromString("fake css"), "base.css", CompressionMethod.Deflated); // back to original
					zip.Add(new DataFromString("fake image 2"), "images/another.jpg", CompressionMethod.Deflated); // add an extra file
					zip.CommitUpdate();
					zip.Close();

					// Again, a new folder, since (compared to the original) we have an extra file.
					EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path, new ElementProxy(container));
					Assert.That(container.ChildNodes.Count, Is.EqualTo(1));
					Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(3));

					// and a new set
					var thirdActivityFolder = Path.Combine(activityFolder, Path.GetFileNameWithoutExtension(widgetFile.Path) + "3");
					Assert.That(File.ReadAllText(Path.Combine(thirdActivityFolder, indexFileName)), Is.EqualTo("fake html"));
					Assert.That(File.ReadAllText(Path.Combine(thirdActivityFolder, "base.css")), Is.EqualTo("fake css"));
					Assert.That(File.ReadAllText(Path.Combine(thirdActivityFolder, "images", "picture.jpg")), Is.EqualTo("fake image"));
					Assert.That(File.ReadAllText(Path.Combine(thirdActivityFolder, "images", "another.jpg")), Is.EqualTo("fake image 2"));

					zip = new ZipFile(widgetFile.Path);
					zip.BeginUpdate();
					zip.Delete( "base.css"); // remove original file
					zip.Delete( "images/another.jpg"); // remove an extra file
					zip.CommitUpdate();
					zip.Close();

					// Another new folder, this time due to a missing file
					EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path, new ElementProxy(container));
					Assert.That(container.ChildNodes.Count, Is.EqualTo(1));
					Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(4));

					var fourthActivityFolder = Path.Combine(activityFolder, Path.GetFileNameWithoutExtension(widgetFile.Path) + "4");
					Assert.That(File.ReadAllText(Path.Combine(fourthActivityFolder, indexFileName)), Is.EqualTo("fake html"));
					Assert.That(File.ReadAllText(Path.Combine(fourthActivityFolder, "images", "picture.jpg")), Is.EqualTo("fake image"));

					zip = new ZipFile(widgetFile.Path);
					zip.BeginUpdate();
					zip.Add(new DataFromString("fake css"), "base.css", CompressionMethod.Deflated); // restore original
					zip.Add(new DataFromString("fake audio"), "audio/noise.mp3", CompressionMethod.Deflated); // add an extra file
					zip.CommitUpdate();
					zip.Close();

					// And this final one checks that we can detect a missing thing in a completely new folder.
					EditingModel.MakeActivity(bookFolder.FolderPath, widgetFile.Path, new ElementProxy(container));
					Assert.That(container.ChildNodes.Count, Is.EqualTo(1));
					Assert.That(Directory.GetDirectories(activityFolder), Has.Length.EqualTo(5));

					var fifthActivityFolder = Path.Combine(activityFolder, Path.GetFileNameWithoutExtension(widgetFile.Path) + "5");
					Assert.That(File.ReadAllText(Path.Combine(fifthActivityFolder, indexFileName)), Is.EqualTo("fake html"));
					Assert.That(File.ReadAllText(Path.Combine(fifthActivityFolder, "base.css")), Is.EqualTo("fake css"));
					Assert.That(File.ReadAllText(Path.Combine(fifthActivityFolder, "audio", "noise.mp3")), Is.EqualTo("fake audio"));

					// The files for all the earlier widgets should still be around
					Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, indexFileName)), Is.EqualTo("fake html"));
					Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "base.css")), Is.EqualTo("fake css"));
					Assert.That(File.ReadAllText(Path.Combine(thisActivityFolder, "images", "picture.jpg")), Is.EqualTo("fake image"));

					Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, indexFileName)), Is.EqualTo("fake html"));
					Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "base.css")), Is.EqualTo("fake css modified"));
					Assert.That(File.ReadAllText(Path.Combine(secondActivityFolder, "images", "picture.jpg")), Is.EqualTo("fake image"));
					thirdActivityFolder = Path.Combine(activityFolder, Path.GetFileNameWithoutExtension(widgetFile.Path) + "3");

					Assert.That(File.ReadAllText(Path.Combine(thirdActivityFolder, "base.css")), Is.EqualTo("fake css"));
					Assert.That(File.ReadAllText(Path.Combine(thirdActivityFolder, "images", "picture.jpg")), Is.EqualTo("fake image"));
					Assert.That(File.ReadAllText(Path.Combine(thirdActivityFolder, "images", "another.jpg")), Is.EqualTo("fake image 2"));

					Assert.That(File.ReadAllText(Path.Combine(fourthActivityFolder, indexFileName)), Is.EqualTo("fake html"));
					Assert.That(File.ReadAllText(Path.Combine(fourthActivityFolder, "images", "picture.jpg")),
						Is.EqualTo("fake image"));
				}
			}
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
