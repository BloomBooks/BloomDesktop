using Bloom.ToPalaso;
using NUnit.Framework;
using SIL.Windows.Forms.WritingSystems;

namespace BloomTests.ToPalaso
{
	[TestFixture]
	public class LanguageLookupModelExtensionsTests
	{
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
		/// We test various 2 and 3-letter codes to make sure they get the expected language name.
		/// We also make sure that various tags get stripped off when searching for Best Name.
		/// The method for getting the "General code" (w/o Script/Region/Variant codes) has an exception for Chinese.
		/// But this method doesn't trigger it (which is okay at this point).
		/// </summary>
		[Test]
		[TestCase("ara", "Arabic")] // classic 3-letter code
		[TestCase("fr", "French")] // classic 2-letter code
		[TestCase("nsk", "Naskapi")]
		[TestCase("nsk-Latn", "Naskapi")]
		[TestCase("nsk-Latn-x-Quebec", "Naskapi")]
		[TestCase("nsk-Latn-CA-x-Quebec", "Naskapi")]
		[TestCase("nsk-misc-garbage", "Naskapi")]
		[TestCase("shu", "Chadian Arabic")]
		[TestCase("shu-arab", "Chadian Arabic")]
		[TestCase("shu-latn", "Chadian Arabic")]
		[TestCase("sok", "Sokoro")] // Should not be required to have a '-Latn' tag.
		[TestCase("zh-CN", "Chinese")]
		[TestCase("zho", "Chinese")]
		public void GetBestLanguageName_ForLotsOfVariants_FindsExpectedName(string codeVariant, string expectedResult)
		{
			var sut = new LanguageLookupModel();
			string name;
			Assert.That(sut.GetBestLanguageName(codeVariant, out name), Is.True);
			Assert.That(name, Is.EqualTo(expectedResult));
		}
	}
}
