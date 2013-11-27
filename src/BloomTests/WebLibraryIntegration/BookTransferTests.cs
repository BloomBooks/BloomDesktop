using System;
using System.IO;
using System.Linq;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using NUnit.Framework;

namespace BloomTests.WebLibraryIntegration
{
	[TestFixture]
	public class BookTransferTests
	{
		 private TemporaryFolder _workFolder;
		private string _workFolderPath;

		[SetUp]
		public void Setup()
		{
			_workFolder = new TemporaryFolder("unittest");
			_workFolderPath = _workFolder.FolderPath;
			Assert.AreEqual(0,Directory.GetDirectories(_workFolderPath).Count(),"Some stuff was left over from a previous test");
			Assert.AreEqual(0, Directory.GetFiles(_workFolderPath).Count(),"Some stuff was left over from a previous test");
		}

		[TearDown]
		public void TearDown()
		{
			_workFolder.Dispose();
		}
	}
}
