using System;
using System.IO;
using System.Text;
using Bloom;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests
{
	[TestFixture]
	public class BloomZipFileTests
	{
		[Test]
		public void CreateZipFile_NonAsciiEntryNames()
		{
			// this test is to make sure non-ascii file names are being stored and retrieved correctly

			const string fileName = "मराठी मैथिली संस्कृत हिन्.htm";
			const string fileContents = @"File contents.";

			using (var tempFile = TempFile.WithFilenameInTempFolder(fileName))
			{
				File.WriteAllText(tempFile.Path, fileContents);

				using (var bookZip = TempFile.WithExtension(".zip"))
				{
					var zip = new BloomZipFile(bookZip.Path);
					zip.AddTopLevelFile(tempFile.Path);
					zip.Save();

					using (var zip2 = new ZipFile(bookZip.Path))
					{
						foreach (ZipEntry zipEntry in zip2)
						{
							Console.Out.WriteLine(zipEntry.Name);
							Assert.That(zipEntry.Name, Is.EqualTo(fileName));

							using (var inStream = zip2.GetInputStream(zipEntry))
							{
								byte[] buffer = new byte[zipEntry.Size];
								ICSharpCode.SharpZipLib.Core.StreamUtils.ReadFully(inStream, buffer);

								var testStr = Encoding.Default.GetString(buffer);
								Assert.That(testStr, Is.EqualTo(fileContents));
							}
						}
					}
				}
			}
		}
	}
}
