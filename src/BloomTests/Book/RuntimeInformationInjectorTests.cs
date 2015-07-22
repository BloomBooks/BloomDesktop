using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
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
			SetDom(@"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='1'>english</textarea>
							<textarea lang='fub' id='2'>originalVernacular</textarea>
						</p>
					</div>
					<div class='bloom-page' id='guid3'>
						<p>
							<div lang='ant' id='4'>more</div>
							<div  lang='xyz' id='3'>original2</div>
						</p>
					</div>
			");
			var d = new Dictionary<string, string>();
			d["en"] = "Anglais";

			RuntimeInformationInjector.AddLanguagesUsedInPage(_bookDom.RawDom, d);

			Assert.That(d["en"], Is.EqualTo("Anglais"), "Should not have replaced an existing key");
			Assert.That(d["fub"], Is.EqualTo("Adamawa Fulfulde"));
			Assert.That(d["ant"], Is.EqualTo("Antakarinya"), "Should find in divs as well as textareas");
			Assert.That(d.Keys, Has.Count.EqualTo(3), "should not have added anything for xyz, since not in db");
		}
	}
}
