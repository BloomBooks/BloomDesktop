using Bloom.ToPalaso;
using NUnit.Framework;
using SIL.Windows.Forms.WritingSystems;

namespace BloomTests.ToPalaso
{
	[TestFixture]
	public class LanguageLookupModelExtensionsTests
	{
		/// <summary>
		/// This is the one the extension was made for, finds a name that matches only by 3-letter code
		/// </summary>
		[Test]
		public void GetBestLanguageName_ForARA_FindsArabic()
		{
			var sut = new LanguageLookupModel();
			string name;
			Assert.That(sut.GetBestLanguageName("ara", out name), Is.True);
			Assert.That(name, Is.EqualTo("Arabic"));
		}

		/// <summary>
		/// Routine match in StandardSubtags.RegisteredLanguages.
		/// </summary>
		[Test]
		public void GetBestLanguageName_ForFR_FindsFrench()
		{
			var sut = new LanguageLookupModel();
			string name;
			Assert.That(sut.GetBestLanguageName("fr", out name), Is.True);
			Assert.That(name, Is.EqualTo("French"));
		}

		/// <summary>
		/// No match at all.
		/// </summary>
		[Test]
		public void GetBestLanguageName_ForXYZ_FindsXYZ()
		{
			var sut = new LanguageLookupModel();
			string name;
			Assert.That(sut.GetBestLanguageName("xyz", out name), Is.False);
			Assert.That(name, Is.EqualTo("xyz"));
		}

		/// <summary>
		/// In this test, StandardSubtags.RegisteredLanguages has some, but none have the exact right code.
		/// </summary>
		[Test]
		public void GetBestLanguageName_ForArab_FindsArab()
		{
			var sut = new LanguageLookupModel();
			string name;
			Assert.That(sut.GetBestLanguageName("arab", out name), Is.False);
			Assert.That(name, Is.EqualTo("arab"));
		}

		/// <summary>
		/// In this test, StandardSubtags.RegisteredLanguages has some, but none have the exact right code.
		/// </summary>
		[Test]
		public void GetBestLanguageName_ForNaskapiLatin_FindsNaskapi()
		{
			var sut = new LanguageLookupModel();
			string name;
			Assert.That(sut.GetBestLanguageName("nsk-Latn", out name), Is.True);
			Assert.That(name, Is.EqualTo("Naskapi"));
		}
	}
}
