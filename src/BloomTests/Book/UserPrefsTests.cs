using Bloom.Book;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Book
{
	[TestFixture]
	public class UserPrefsTests
	{
		[Test]
		public void LoadOrMakeNew_EmptyFile_GivesNewPrefs()
		{
			using (var t = new TempFile(""))
			{
				var up = UserPrefs.LoadOrMakeNew(t.Path);
				Assert.That(up.MostRecentPage == 0);
			}
		}
		[Test]
		public void LoadOrMakeNew_CorrupFile_GivesNewPrefs()
		{
			using (var t = new TempFile("hellow world"))
			{
				var up = UserPrefs.LoadOrMakeNew(t.Path);
				Assert.That(up.MostRecentPage == 0);
			}
		}
		[Test]
		public void LoadOrMakeNew_MostRecentPage_ReadsCorrectly()
		{
			using (var t = new TempFile("{\"mostRecentPage\":3}"))
			{
				var up = UserPrefs.LoadOrMakeNew(t.Path);
				Assert.That(up.MostRecentPage == 3);
			}
		}
	}
}
