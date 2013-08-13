using System;
using System.Collections.Generic;
using System.ComponentModel;
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
				maker.MakePdf(input.Path, output.Path, "a5", false, PublishModel.BookletLayoutMethod.SideFold, PublishModel.BookletPortions.AllPagesNoBooklet, null);
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
				maker.MakePdf(input.Path, output.Path, "A5", false, PublishModel.BookletLayoutMethod.SideFold, PublishModel.BookletPortions.BookletPages,null);
				//we don't actually have a way of knowing it did a booklet
				Assert.IsTrue(File.Exists(output.Path));
			}
		}

		/// <summary>
		/// This tests for a regretion on BL-81, BL-96, BL-76; wkhtmltopdf itself couldn't handle file names anything up out of ascii-land
		/// </summary>
		[Test]
		public void MakePdf_BookNameIsChinese_OutputsPdf()
		{
			var maker = new PdfMaker();
			using (var input = TempFile.WithFilename("北京.htm"))
			using (var output = TempFile.WithFilename("北京.pdf"))
			{
				File.WriteAllText(input.Path, "<html><body>北京</body></html>");
				File.Delete(output.Path);
				maker.MakePdf(input.Path, output.Path, "A5", false, PublishModel.BookletLayoutMethod.SideFold, PublishModel.BookletPortions.BookletPages, null);
				//we don't actually have a way of knowing it did a booklet
				Assert.IsTrue(File.Exists(output.Path));
			}

			using (var input = TempFile.WithFilename("എന്റെ ബുക്ക്.htm"))
			using (var output = TempFile.WithFilename("എന്റെ ബുക്ക്.pdf"))
			{
				File.WriteAllText(input.Path, "<html><body>എന്റെ ബുക്ക്</body></html>");
				File.Delete(output.Path);
				maker.MakePdf(input.Path, output.Path, "A5", false, PublishModel.BookletLayoutMethod.SideFold, PublishModel.BookletPortions.BookletPages, null);
				//we don't actually have a way of knowing it did a booklet
				Assert.IsTrue(File.Exists(output.Path));
			}
		}

	}
}
