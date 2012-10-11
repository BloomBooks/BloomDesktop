using System;
using System.IO;
using Bloom.CollectionChoosing;
using Bloom.ToPalaso;
using NUnit.Framework;

namespace BloomTests.ToPalaso
{
	[TestFixture]
	public class MostRecentPathsTests
	{
		private MostRecentPathsList _MostRecentPathsList;

		[SetUp]
		public void Setup()
		{
			_MostRecentPathsList = new MostRecentPathsList();
		}

		[TearDown]
		public void TearDown() {}

		private class TempFile: IDisposable
		{
			private readonly string fileName;

			public TempFile()
			{
				fileName = Path.GetTempFileName();
			}

			public string FileName
			{
				get { return fileName; }
			}

			public void Dispose()
			{
				File.Delete(FileName);
			}
		}

		[Test]
		public void AddNewPath_PathExists_PathAtTopOfList()
		{
			using (TempFile existingFile = new TempFile())
			{
				_MostRecentPathsList.AddNewPath(existingFile.FileName);
				string[] mruPaths = _MostRecentPathsList.Paths;
				Assert.AreEqual(1, mruPaths.Length);
				Assert.AreEqual(existingFile.FileName, mruPaths[0]);
			}
		}

		[Test]
		public void AddNewPath_PathDoesNotExist_ReturnsFalse()
		{
			string nonExistentFileName = Path.GetRandomFileName();
			Assert.AreEqual(false, _MostRecentPathsList.AddNewPath(nonExistentFileName));
		}

		[Test]
		[NUnit.Framework.Category("UsesObsoleteExpectedExceptionAttribute"), ExpectedException(typeof (ArgumentNullException))]
		public void AddNewPath_NullPath_Throws()
		{
			_MostRecentPathsList.AddNewPath(null);
		}

		[Test]
		public void GetPaths_AddTwoFiles_BothFilesInListInInverseOrder()
		{
			using (TempFile firstFileIn = new TempFile(), secondFileIn = new TempFile())
			{
				_MostRecentPathsList.AddNewPath(firstFileIn.FileName);
				_MostRecentPathsList.AddNewPath(secondFileIn.FileName);
				string[] MostRecentPathsListPaths = _MostRecentPathsList.Paths;
				foreach (string path in MostRecentPathsListPaths)
				{
					Console.WriteLine(path);
				}
				Assert.AreEqual(2, MostRecentPathsListPaths.Length);
				Assert.AreEqual(secondFileIn.FileName, MostRecentPathsListPaths[0]);
				Assert.AreEqual(firstFileIn.FileName, MostRecentPathsListPaths[1]);
			}
		}

		[Test]
		public void SetPaths_Null_ClearsPaths()
		{
			using (TempFile file1 = new TempFile(), file2 = new TempFile())
			{
				_MostRecentPathsList.Paths = new string[] {file1.FileName, file2.FileName};
				_MostRecentPathsList.Paths = null;
				Assert.IsNotNull(_MostRecentPathsList.Paths);
				foreach (string path in _MostRecentPathsList.Paths)
				{
					Console.WriteLine(path);
				}
				//Assert.AreEqual(0, _MostRecentPathsList.Paths.Length);
				Assert.IsEmpty(_MostRecentPathsList.Paths);
			}
		}

		[Test]
		public void SetPaths_InitializeWithValues_ValuesWereInitialized()
		{
			using (TempFile file1 = new TempFile(), file2 = new TempFile(), file3 = new TempFile())
			{
				_MostRecentPathsList.Paths = new string[] {file1.FileName, file2.FileName, file3.FileName};
				Assert.AreEqual(3, _MostRecentPathsList.Paths.Length);
				Assert.AreEqual(file1.FileName, _MostRecentPathsList.Paths[0]);
				Assert.AreEqual(file2.FileName, _MostRecentPathsList.Paths[1]);
				Assert.AreEqual(file3.FileName, _MostRecentPathsList.Paths[2]);
			}
		}

		[Test]
		public void
				AddNewPath_AddPathThatIsAlreadyInMruPaths_PathIsRemovedFromOldPositionAndMovedToTopPosition
				()
		{
			using (TempFile file1 = new TempFile(), file2 = new TempFile(), file3 = new TempFile())
			{
				_MostRecentPathsList.Paths = new string[] {file1.FileName, file2.FileName, file3.FileName};
				_MostRecentPathsList.AddNewPath(file2.FileName);
				string[] mruPaths = _MostRecentPathsList.Paths;
				Assert.AreEqual(3, mruPaths.Length);
				Assert.AreEqual(file2.FileName, mruPaths[0]);
				Assert.AreEqual(file1.FileName, mruPaths[1]);
				Assert.AreEqual(file3.FileName, mruPaths[2]);
			}
		}

		[Test]
		public void SetPaths_MultipleInstancesOfSamePath_OnlyMostRecentInstanceIsStored()
		{
			using (TempFile file1 = new TempFile(), file2 = new TempFile(), file3 = new TempFile())
			{
				_MostRecentPathsList.Paths = new string[]
										 {
												 file1.FileName, file2.FileName, file1.FileName,
												 file3.FileName
										 };
				Assert.AreEqual(3, _MostRecentPathsList.Paths.Length);
				Assert.AreEqual(file1.FileName, _MostRecentPathsList.Paths[0]);
			}
		}
	}
}
