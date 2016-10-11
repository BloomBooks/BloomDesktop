using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.ComponentModel.Composition;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;

namespace Bloom.Publish
{

// ReSharper disable once InconsistentNaming
	/// <summary>
	/// This class currently just does one thing; it adds a right-click menu item in the Publish tab that saved PNG thumbnails of all the "day" pages of a SHRP Pupil's book.
	/// These are used in the corresponding "Teacher's Guide".
	///
	/// It is built as an MEF "part", exporting what Bloom needs (the menu item) and importing what it needs (e.g. the contents of the book, an html thumbnailer)
	///
	/// Currently (In Dec 2013), it isn't actually an extension because it doesn't have its own DLL. But it does demonstrate that we could trivially have extension dlls coming with
	/// templates, in separate DLLS. The key is keeping the dependences to a minimum so that extension don't break with each version of Bloom.
	///
	/// </summary>
	public class SHRP_PupilBookExtension
	{

		public static bool ExtensionIsApplicable(Book.Book book)
		{
			return book.Title.Contains("Folio") || book.Title.Contains("Pupil") || book.Title.Contains("Primer");
		}

		[Import("PathToBookFolder")]
		public string BookFolder;

		[Import("Language1Iso639Code")]
		public string Language1Iso639Code;

		[Import]
		public Action<int, int, HtmlDom, Action<Image>, Action<Exception>> GetThumbnailAsync;


		[Import] public Func<IEnumerable<HtmlDom>> GetPageDoms;

		[Export("GetPublishingMenuCommands")]
		public IEnumerable<ToolStripItem> GetPublishingMenuCommands()
		{
			yield return new ToolStripSeparator();
			yield return new ToolStripMenuItem("Make Thumbnails For Teacher's Guide", null, MakeThumbnailsForTeachersGuide);
		}

		private void MakeThumbnailsForTeachersGuide(object sender, EventArgs e)
		{

//			var dlg = new SIL.Windows.Forms.Progress.ProgressDialog();
//			dlg.Overview = "Creating thumbnail files...";
//			BackgroundWorker preprocessWorker = new BackgroundWorker();
//			preprocessWorker.DoWork += PlaceThumbnailOrders;
//			dlg.BackgroundWorker = preprocessWorker;
//			dlg.CanCancel = true;
//			dlg.ShowDialog();
			PlaceThumbnailOrders(null,null);
		}

		private void PlaceThumbnailOrders(object sender, DoWorkEventArgs doWorkEventArgs)
		{
			//var state = (ProgressState)doWorkEventArgs.Argument;

			var exportFolder = Path.Combine(BookFolder, "Thumbnails");

			//state.StatusLabel = "Creating thumbnail folder at " + exportFolder;
			if (Directory.Exists(exportFolder))
			{
				//state.StatusLabel = "Deleting existing thumbnail directory";
				SIL.IO.RobustIO.DeleteDirectory(exportFolder, true);
				Thread.Sleep(1000); //let any open windows explorers deal with this before we move on
			}
			Directory.CreateDirectory(exportFolder);
			//state.StatusLabel = "Creating Thumbnail Directory";
			Thread.Sleep(1000); //let any open windows explorers deal with this before we move on

			//state.StatusLabel = "Ordering page thumbnails";

			foreach (var pageDom in GetPageDoms())
			{
				if (null != pageDom.SelectSingleNode("//div[contains(@class,'oddPage') or contains(@class,'evenPage') or contains(@class,'leftPage')  or contains(@class,'rightPage') or contains(@class,'primerPage')]"))
				{
//					if(null != pageDom.SelectSingleNode("//div[contains(@class,'bloom-frontMatter')]"))
//						continue; //c2 p2 had a term intro page with the class "rigthPage" which gives the prior query a false positive

					const double kproportionOfWidthToHeightForB5 = 0.708;
					const int heightInPixels = 700;
					const int widthInPixels = (int) (heightInPixels*kproportionOfWidthToHeightForB5);

					GetThumbnailAsync(widthInPixels, heightInPixels, pageDom, image => ThumbnailReady(exportFolder, pageDom, image),
						error => HandleThumbnailerError(pageDom, error));
				}
			}
			//this folder won't be fully populated yet, but as they watch it will fill up
			Process.Start("explorer.exe", " \"" + exportFolder + "\"");

			//state.WriteToLog("Now sit tight and wait for the thumbnail directory to stop filling up.");
		}

		private void HandleThumbnailerError(HtmlDom pageDom, Exception error)
		{
			throw new NotImplementedException();
		}

		private void ThumbnailReady(string exportFolder, HtmlDom dom, Image image)
		{
			string term;
			string week;
			try
			{
				term = dom.SelectSingleNode("//div[contains(@data-book,'term')]").InnerText.Trim();
				week = dom.SelectSingleNode("//div[contains(@data-book,'week')]").InnerText.Trim();
			}
			catch (Exception e)
			{
				Debug.Fail("Book missing either term or week variable");
				throw new ApplicationException("This page is lacking either a term or week data-book variable.");
			}
			//the selector for day one is different because it doesn't have @data-* attribute
			XmlElement dayNode = dom.SelectSingleNode("//div[contains(@class,'DayStyle')]");
			string page="?";
			// many pupil books don't have a specific day per page

			if (dom.SelectSingleNode("//div[contains(@class,'day5Left')]") != null) // in P2, we have 2 pages for day 5, so we can't use the 'DayStyle' to differentiate them
			{
				page = "5";
			}
			else if (dom.SelectSingleNode("//div[contains(@class,'day5Right')]") != null)
			{
				page = "6";
			}
			else if (dayNode != null)
			{
				page = dayNode.InnerText.Trim();
			}
			else
			{
				if (dom.SelectSingleNode("//div[contains(@class,'page1') or contains(@class,'storyPageLeft')]") != null)
				{
					page = "1";
				}
				else if (dom.SelectSingleNode("//div[contains(@class,'page2') or contains(@class,'storyPageRight')]") != null)
				{
					page = "2";
				}
				else if (dom.SelectSingleNode("//div[contains(@class,'page3') or contains(@class,'thirdPage')]") != null)
				{
					page = "3";
				}
				else if (dom.SelectSingleNode("//div[contains(@class,'page4') or contains(@class,'fourthPage')]") != null)
				{
					page = "4";
				}
				else
				{
					Debug.Fail("Couldn't figure out what page this is.");
				}
			}
			var fileName = Language1Iso639Code + "-t" + term + "-w" + week + "-p" + page + ".png";
			//just doing image.Save() works for .bmp and .jpg, but not .png
			using (var b = new Bitmap(image))
			{
				SIL.IO.RobustIO.SaveImage(b, Path.Combine(exportFolder, fileName));
			}
		}
	}
}
