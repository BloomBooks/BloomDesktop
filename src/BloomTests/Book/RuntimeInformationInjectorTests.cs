using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Collection;
using NUnit.Framework;

namespace BloomTests.Book
{
    public class RuntimeInformationInjectorTests
    {
        private HtmlDom _bookDom;

        private void SetDom(string bodyContents)
        {
            _bookDom = new HtmlDom(@"<html ><head></head><body>" + bodyContents + "</body></html>");
        }

        [Test]
        public void AddLanguagesUsedInPage_AddsOnlyAppropriateNames()
        {
            SetDom(
                @"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='1'>english</textarea>
							<textarea lang='fub' id='2'>originalVernacular</textarea>
						</p>
					</div>
					<div class='bloom-page' id='guid3'>
						<p>
							<div lang='ant' id='4'>more</div>
							<div  lang='xyz' id='3'>original2</div>
                            <div lang='fr' id='5'>This is french</div>
						</p>
					</div>
			"
            );
            var settings = new CollectionSettings();
            var french = new WritingSystem(() => "en") { Tag = "fr" };
            french.SetName("My fancy French", true);
            settings.AllLanguages.Add(french);
            var d = new Dictionary<string, string>();
            d["en"] = "Anglais";

            RuntimeInformationInjector.AddLanguagesUsedInPage(_bookDom.RawDom, d, settings);

            Assert.That(d["en"], Is.EqualTo("Anglais"), "Should not have replaced an existing key");
            Assert.That(d["fub"], Is.EqualTo("Adamawa Fulfulde"));
            Assert.That(
                d["ant"],
                Is.EqualTo("Antakarinya"),
                "Should find in divs as well as textareas"
            );
            Assert.That(
                d["fr"],
                Is.EqualTo("My fancy French"),
                "Should use the name from the settings"
            );
            Assert.That(
                d.Keys,
                Has.Count.EqualTo(4),
                "should not have added anything for xyz, since not in db"
            );
        }

        [TestCase("twi-Latn")]
        [TestCase("invalid")]
        [TestCase("zzz")]
        public void AddLanguagesUsedInPage_InvalidLanguageSubTag_DoesNotThrow(string invalidSubTag)
        {
            SetDom(
                @"<div class='bloom-page' id='guid5'>
					<div lang='en' id='4'>some text</div>
				</div>"
            );
            var settings = new CollectionSettings() { Language2Tag = invalidSubTag };

            RuntimeInformationInjector.AddLanguagesUsedInPage(
                _bookDom.RawDom,
                new Dictionary<string, string>(),
                settings
            );
        }
    }
}
