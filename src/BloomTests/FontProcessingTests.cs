using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.FontProcessing;
using Newtonsoft.Json;
using NUnit.Framework;
using SIL.PlatformUtilities;

namespace BloomTests
{
	[TestFixture]
	public class FontProcessingTests
	{
		IDictionary<string, FontMetadata> _fontMetadata;
		int _fontCount;

		[OneTimeSetUp]
		public void FontProcessingSetup()
		{
			//var starting = DateTime.Now;
			var fontMetadata = FontsApi.GetAllFontMetadata();	// loads everything in before returning.
			_fontCount = fontMetadata.Count();
			_fontMetadata = FontsApi.AvailableFontMetadataDictionary;

			//var finished = DateTime.Now;
			//Console.WriteLine("DEBUG Font metadata setup took {0}", finished - starting);
			//var json = JsonConvert.SerializeObject(fontMetadata);
			//Console.WriteLine("DEBUG generated json = {0}", json);
		}

		[Test]
		public void BasicFontMetadataCheck()
		{
			Assert.That(_fontMetadata.Count, Is.GreaterThan(0));
			Assert.That(_fontMetadata.Count, Is.EqualTo(_fontCount));
			if (Platform.IsWindows)
			{
				Assert.That(_fontMetadata.Keys, Does.Contain("Arial"));
				var arialMeta = _fontMetadata["Arial"];
				Assert.That(arialMeta.manufacturer, Does.Contain("Monotype"));
				Assert.That(arialMeta.license, Does.Contain("Microsoft supplied font"));
				Assert.That(arialMeta.copyright, Does.Contain("Monotype"));
				Assert.That(arialMeta.fsType, Is.EqualTo("Editable"));
				Assert.That(arialMeta.determinedSuitability, Is.EqualTo("ok"));
				Assert.That(arialMeta.determinedSuitabilityNotes, Is.EqualTo("fsType from reliable source"));
			}
		}
	}
}
