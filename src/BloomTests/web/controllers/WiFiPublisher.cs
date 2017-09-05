using System.IO;
using BloomTemp;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
	public class WiFiPublisher
	{
		[Test]
		public void MakeVersionCode_DistinguishesTextChanges()
		{
			var template = @"<!DOCTYPE html><html><head></head><body><div>{0}</div></body></html>";
			var first = string.Format(template, "abc");
			var second = string.Format(template, "abd");
			Assert.That(Bloom.Book.Book.MakeVersionCode(first), Is.Not.EqualTo(Bloom.Book.Book.MakeVersionCode(second)));
		}

		[TestCase("abc", "ab c")]
		[TestCase("<p>ab&nbsp; cd</p>", "<p>ab cd</p>")]
		[TestCase("<p>ab</p>  <p>cd</p>", "<p>ab cd</p>")]
		public void MakeVersionCode_DistinguishesSignificantWhitespaceChanges(string firstArg, string secondArg)
		{
			var template = @"<!DOCTYPE html><html><head></head><body><div>{0}</div></body></html>";
			var first = string.Format(template, firstArg);
			var second = string.Format(template, secondArg);
			Assert.That(Bloom.Book.Book.MakeVersionCode(first), Is.Not.EqualTo(Bloom.Book.Book.MakeVersionCode(second)));
		}

		[Test]
		public void MakeVersionCode_DistinguishesStructuralChanges()
		{
			var template = @"<!DOCTYPE html><html><head></head><body><div>{0}</div></body></html>";
			var first = string.Format(template, "abc");
			var second = string.Format(template, "<p>abc<p>");
			Assert.That(Bloom.Book.Book.MakeVersionCode(first), Is.Not.EqualTo(Bloom.Book.Book.MakeVersionCode(second)));
		}

		[Test]
		public void MakeVersionCode_IgnoresInsignificantWhitespaceChanges()
		{
			var first = @"<!DOCTYPE html><html><head></head><body><div>abc</div></body></html>";
			var second = @"<!DOCTYPE html>
<html>
	<head>
	</head>
	<body> <div>abc</div>
	</body>
</html>";
			Assert.That(Bloom.Book.Book.MakeVersionCode(first), Is.EqualTo(Bloom.Book.Book.MakeVersionCode(second)));
		}

		[Test]
		public void MakeVersionCode_IgnoresIndentsInStylesheet()
		{
			var first = @"<!DOCTYPE html><html><head>
    <style type='text/css'>
                    DIV.coverColor  TEXTAREA        {               background-color: #C2A6BF !important;   }
                    DIV.bloom-page.coverColor       {               background-color: #C2A6BF !important;   }
    </style>
</head><body><div>abc</div></body></html>";
			var second = @"<!DOCTYPE html><html><head>
    <style type='text/css'>
                        DIV.coverColor  TEXTAREA        {               background-color: #C2A6BF !important;   }
                        DIV.bloom-page.coverColor       {               background-color: #C2A6BF !important;   }
    </style>
</head><body><div>abc</div></body></html>";
			Assert.That(Bloom.Book.Book.MakeVersionCode(first), Is.EqualTo(Bloom.Book.Book.MakeVersionCode(second)));
		}

		[Test]
		public void MakeVersionCode_IgnoresPageIdChanges()
		{
			var template = @"<!DOCTYPE html><html><head></head><body><div class='bloom-page' id='{0}'>abc</div></body></html>";
			var first = string.Format(template, "934245d2-94dd-4f40-8b53-279867d8e07b");
			var second = string.Format(template, "d9a71953-6cf4-475a-8236-36d509ff8e1c");
			Assert.That(Bloom.Book.Book.MakeVersionCode(first), Is.EqualTo(Bloom.Book.Book.MakeVersionCode(second)));
		}

		[Test]
		public void MakeVersionCode_DoesNotIgnoreIdInContent()
		{
			var template = @"<!DOCTYPE html><html><head></head><body><div class='bloom-page'>id='{0}'</div></body></html>";
			var first = string.Format(template, "934245d2-94dd-4f40-8b53-279867d8e07b");
			var second = string.Format(template, "d9a71953-6cf4-475a-8236-36d509ff8e1c");
			Assert.That(Bloom.Book.Book.MakeVersionCode(first), Is.Not.EqualTo(Bloom.Book.Book.MakeVersionCode(second)));
		}

		[Test]
		public void MakeVersionCode_ConsidersFilesInFolder_ButNotHtm()
		{
			var template = @"<!DOCTYPE html><html><head></head><body><div class='bloom-page' id='{0}'>abc</div></body></html>";
			var first = string.Format(template, "934245d2-94dd-4f40-8b53-279867d8e07b");
			var second = string.Format(template, "d9a71953-6cf4-475a-8236-36d509ff8e1c");
			using (var tempFolder = new TemporaryFolder("MakeVersionCode_ConsidersFilesInFolder_ButNotHtm"))
			{
				var htmPath = Path.Combine(tempFolder.FolderPath,"main.htm");
				File.WriteAllText(htmPath, first);
				var firstVersionCode = Bloom.Book.Book.MakeVersionCode(first, htmPath);
				File.WriteAllText(htmPath, second);
				var secondVersionCode = Bloom.Book.Book.MakeVersionCode(second, htmPath);
				// This is not a significant change in the HTML (even though the htm file in the folder is also different).
				Assert.That(firstVersionCode, Is.EqualTo(secondVersionCode));
				// But a new file in the folder is significant
				var extraFilePath = Path.Combine(tempFolder.FolderPath, "nonsense.txt");
				File.WriteAllText(extraFilePath, @"nonsense");
				var thirdVersionCode = Bloom.Book.Book.MakeVersionCode(second, htmPath);
				Assert.That(thirdVersionCode, Is.Not.EqualTo(secondVersionCode));
				// so is a change in that file
				File.WriteAllText(extraFilePath, @"rubbish");
				var fourthVersionCode = Bloom.Book.Book.MakeVersionCode(second, htmPath);
				Assert.That(fourthVersionCode, Is.Not.EqualTo(thirdVersionCode));

			}
		}
	}
}
