using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Collection;
using NUnit.Framework;

namespace BloomTests.Collection
{
	[TestFixture]
	public class CollectionSettingsTests
	{
		/// <summary>
		/// This is a regression test related to https://jira.sil.org/browse/BL-685.
		/// Apparently calculating the name is expensive, so it is cached. This
		/// test ensures that the cache doesn't keep the name from tracking the iso.
		/// </summary>
		[Test]
		public void Language1IsoCodeChanged_NameChangedToo()
		{
			var info = new NewCollectionSettings()
			{
				Language1Iso639Code = "fr"
			};
			var settings = new CollectionSettings(info);
			Assert.AreEqual("French", settings.GetLanguage1Name("en"));
			settings.Language1Iso639Code = "en";
			Assert.AreEqual("English", settings.GetLanguage1Name("en"));
		}
	}
}
