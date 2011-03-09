using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Publish;
using NUnit.Framework;
using Palaso.IO;

namespace BloomTests
{
	[TestFixture]
	public class PdfMakerTests
	{
		[Test]
		public void MakePdf_BookStyleIsNone_OutputsPdf()
		{
			var maker = new PdfMaker();
			using (var input = TempFile.WithExtension("htm"))
			using (var output = new TempFile())
			{
				File.WriteAllText(input.Path,"<html><body>Hello</body></html>");
				File.Delete(output.Path);
				maker.MakePdf(input.Path, output.Path, PublishModel.BookletStyleChoices.None);
				//we don't actually have a way of knowing it did a booklet
				Assert.IsTrue(File.Exists(output.Path));
			}
		}

		[Test]
		public void MakePdf_BookStyleIsBooklet_OutputsPdf()
		{
			var maker = new PdfMaker();
			using (var input = TempFile.WithExtension("htm"))
			using (var output = new TempFile())
			{
				File.WriteAllText(input.Path, "<html><body>Hello</body></html>");
				File.Delete(output.Path);
				maker.MakePdf(input.Path, output.Path, PublishModel.BookletStyleChoices.BookletPages);
				//we don't actually have a way of knowing it did a booklet
				Assert.IsTrue(File.Exists(output.Path));
			}
		}
	}
}
