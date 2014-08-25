using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
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
                File.WriteAllText(input.Path, "<html><body>Hello</body></html>");
                File.Delete(output.Path);
                RunMakePdf((worker, args, owner) => 
                    maker.MakePdf(input.Path, output.Path, "a5", false, PublishModel.BookletLayoutMethod.SideFold,
                        PublishModel.BookletPortions.AllPagesNoBooklet, worker, args, owner));
                //we don't actually have a way of knowing it did a booklet
                Assert.IsTrue(File.Exists(output.Path));
            }
        }

        // The new implementation of MakePdf has to be run on a background thread, because the thread on which it is called
        // does a wait/sleep loop until gecko, on the main UI thread, does the work.
        void RunMakePdf(Action<BackgroundWorker, DoWorkEventArgs, Form> task)
        {
            var owner = new Form();
            var dummy = owner.Handle; // Must invoke this to get a handle so we can invoke
            var worker = new BackgroundWorker();
            bool finished = false;
            worker.RunWorkerCompleted += (sender, args) =>
            {
                finished = true;
            };
            worker.DoWork += (sender, args) => task(worker, args, owner);
            worker.RunWorkerAsync();
            while (!finished)
            {
                Application.DoEvents();
                Thread.Sleep(10);
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
                RunMakePdf((worker, args, owner) => 
                    maker.MakePdf(input.Path, output.Path, "A5", false, PublishModel.BookletLayoutMethod.SideFold, PublishModel.BookletPortions.BookletPages, worker, args, owner));
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
                RunMakePdf((worker, args, owner) =>
                    maker.MakePdf(input.Path, output.Path, "A5", false, PublishModel.BookletLayoutMethod.SideFold, PublishModel.BookletPortions.BookletPages, worker, args, owner));
				//we don't actually have a way of knowing it did a booklet
				Assert.IsTrue(File.Exists(output.Path));
			}

			using (var input = TempFile.WithFilename("എന്റെ ബുക്ക്.htm"))
			using (var output = TempFile.WithFilename("എന്റെ ബുക്ക്.pdf"))
			{
				File.WriteAllText(input.Path, "<html><body>എന്റെ ബുക്ക്</body></html>");
				File.Delete(output.Path);
                RunMakePdf((worker, args, owner) =>
                    maker.MakePdf(input.Path, output.Path, "A5", false, PublishModel.BookletLayoutMethod.SideFold, PublishModel.BookletPortions.BookletPages, worker, args, owner));
				//we don't actually have a way of knowing it did a booklet
				Assert.IsTrue(File.Exists(output.Path));
			}
		}
		
    }
}
