using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.Properties;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Xml;

namespace Bloom
{
	/// <summary>
	/// This class is a place to gather the methods that have to do with making thumbnails of pages of books.
	/// Three of the methods were previously methods of Book itself, but the fourth (MakeThumbnailOfCover)
	/// needed to do too much UI stuff to belong in a model class, so it seemed worth pulling all this
	/// out to a new class.
	/// In live code there is typically only one instance of this created by the ApplicationServer.
	/// In test code they may be created as needed; nothing requires this to be a singleton.
	/// Indeed, it could be a static class except that it requires the HtmlThumbNailer.
	/// </summary>
	public class BookThumbNailer
	{
		private readonly HtmlThumbNailer _thumbnailProvider;

		public BookThumbNailer(HtmlThumbNailer thumbNailer)
		{
			_thumbnailProvider = thumbNailer;
		}

		public void CancelOrder(Guid requestId)
		{
			_thumbnailProvider.CancelOrder(requestId);
		}

		public HtmlThumbNailer HtmlThumbNailer { get { return _thumbnailProvider;} }

		private void GetThumbNailOfBookCover(Book.Book book, HtmlThumbNailer.ThumbnailOptions thumbnailOptions, Action<Image> callback, Action<Exception> errorCallback, bool async)
		{
			if (book is ErrorBook)
			{
				callback(Resources.Error70x70);
				return;
			}
			try
			{
				if (book.HasFatalError) //NB: we might not know yet... we don't fully load every book just to show its thumbnail
				{
					callback(Resources.Error70x70);
					return;
				}
				GenerateImageForWeb(book);

				Image thumb;
				if (book.Storage.TryGetPremadeThumbnail(thumbnailOptions.FileName, out thumb))
				{
					callback(thumb);
					return;
				}

#if USE_HTMLTHUMBNAILER_FOR_COVER
				var dom = book.GetPreviewXmlDocumentForFirstPage();
				if (dom == null)
				{
					callback(Resources.Error70x70);
					return;
				}
				string folderForCachingThumbnail;

				folderForCachingThumbnail = book.StoragePageFolder;
				_thumbnailProvider.GetThumbnail(folderForCachingThumbnail, book.Storage.Key, dom, thumbnailOptions, callback, errorCallback, async);
#else
				if (!CreateThumbnailOfCoverImage(book, thumbnailOptions, callback))
				{
					callback(Resources.Error70x70);
				}
#endif
			}
			catch (Exception err)
			{
				callback(Resources.Error70x70);
				errorCallback(err);
				Debug.Fail(err.Message);
			}
		}

		/// <summary>
		/// Generates a web thumbnail image from the book's front cover image.
		/// The resulting image will fit into a 200px x 200px box with no loss of aspect ratio.
		/// This means that either the height or the width will be 200px.
		/// </summary>
		/// <param name="book"></param>
		public static void GenerateImageForWeb(Book.Book book)
		{
			HtmlThumbNailer.ThumbnailOptions options = new HtmlThumbNailer.ThumbnailOptions
			{
				Height = 200,
				Width = 200,
				CenterImageUsingTransparentPadding = false,
				FileName = "coverImage200.jpg"
			};
			CreateThumbnailOfCoverImage(book, options);
		}

		private static bool CreateThumbnailOfCoverImage(Book.Book book, HtmlThumbNailer.ThumbnailOptions options, Action<Image> callback = null)
		{
			var imageSrc = book.GetCoverImagePath();
			if (string.IsNullOrEmpty(imageSrc) || (Path.GetFileName(imageSrc) == "placeHolder.png" && !Regex.IsMatch(options.FileName, "thumbnail(-[0-9]+)?\\.png")))
			{
				Debug.WriteLine(book.StoragePageFolder + " does not have a cover image.");
				return false;
			}
			var size = Math.Max(options.Width, options.Height);
			var destFilePath = Path.Combine(book.StoragePageFolder, options.FileName);
			// Writing a transparent image to a file, then reading it in again appears to be the only
			// way to get the thumbnail image to draw with the book's cover color background reliably.
			var transparentImageFile = Path.Combine(Path.GetTempPath(), "Bloom", "Transparent", Path.GetFileName(imageSrc));
			Directory.CreateDirectory(Path.GetDirectoryName(transparentImageFile));
			try
			{
				if (RuntimeImageProcessor.MakePngBackgroundTransparent(imageSrc, transparentImageFile))
					imageSrc = transparentImageFile;
				using (var coverImage = PalasoImage.FromFile(imageSrc))
				{
					if (imageSrc == transparentImageFile)
						coverImage.Image = MakeImageOpaque(coverImage.Image, book.GetCoverColor());
					if (options.CenterImageUsingTransparentPadding)
						coverImage.Image = ImageUtils.CenterImageIfNecessary(new Size(size, size), coverImage.Image);
					else
						coverImage.Image = ImageUtils.ResizeImageIfNecessary(new Size(size, size), coverImage.Image);
					switch(Path.GetExtension(destFilePath).ToLowerInvariant())
					{
					case ".jpg":
					case ".jpeg":
						ImageUtils.SaveAsTopQualityJpeg(coverImage.Image, destFilePath);
						break;
					default:
						PalasoImage.SaveImageRobustly(coverImage, destFilePath);
						break;
					}
					if (callback != null)
						callback(coverImage.Image.Clone() as Image);	// don't leave GC to chance
				}
			}
			finally
			{
				if (File.Exists(transparentImageFile))
					SIL.IO.RobustFile.Delete(transparentImageFile);
			}
			return true;
		}

		private static Image MakeImageOpaque(Image source, string coverColorString)
		{
			var target = new Bitmap(source.Width, source.Height);
			Color coverColor;
			ImageUtils.TryCssColorFromString(coverColorString, out coverColor);
			ImageUtils.DrawImageWithOpaqueBackground(source, target, coverColor);
			return target;
		}

		/// <summary>
		/// Make a thumbnail image of a book's front cover.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="height">Optional parameter. If unspecified, use defaults</param>
		public void MakeThumbnailOfCover(Book.Book book, int height = -1, Guid requestId = new Guid())
		{
			HtmlThumbNailer.ThumbnailOptions options = new HtmlThumbNailer.ThumbnailOptions
			{
				//since this is destined for HTML, it's much easier to handle if there is no pre-padding
				CenterImageUsingTransparentPadding = false,
				RequestId = requestId
			};

			if (height != -1)
			{
				options.Height = height;
				options.Width = -1;
				options.FileName = "thumbnail-" + height + ".png";
			}
			// else use the defaults

			RebuildThumbNailNow(book, options);
		}

		///   <summary>
		///   Currently used by the image server
		///   to get thumbnails that are used in the add page dialog. Since this dialog can show
		///   an enlarged version of the page, we generate these at a higher resolution than usual.
		///   Also, to make more realistic views of template pages we insert fake text wherever
		///   there is an empty edit block.
		///
		///   The result is cached for possible future use so the caller should not dispose of it.
		///   </summary>
		/// <param name="book"></param>
		/// <param name="page"></param>
		/// <param name="isLandscape"></param>
		/// <param name="mustRegenerate"></param>
		/// <returns></returns>
		public Image GetThumbnailForPage(Book.Book book, IPage page, bool isLandscape, bool mustRegenerate = false)
		{
			var pageDom = book.GetThumbnailXmlDocumentForPage(page);
			var thumbnailOptions = new HtmlThumbNailer.ThumbnailOptions()
			{
				BackgroundColor = Color.White,// matches the hand-made previews.
				BorderStyle = HtmlThumbNailer.ThumbnailOptions.BorderStyles.None, // allows the HTML to add its preferred border in the larger preview
				CenterImageUsingTransparentPadding = true,
				MustRegenerate = mustRegenerate
			};
			var pageDiv = pageDom.RawDom.SafeSelectNodes("descendant-or-self::div[contains(@class,'bloom-page')]").Cast<XmlElement>().FirstOrDefault();
			// The actual page size is rather arbitrary, but we want the right ratio for A4.
			// Using the actual A4 sizes in mm makes a big enough image to look good in the larger
			// preview box on the right as well as giving exactly the ratio we want.
			// We need to make the image the right shape to avoid some sort of shadow/box effects
			// that I can't otherwise find a way to get rid of.
			if (isLandscape)
			{
				thumbnailOptions.Width = 297;
				thumbnailOptions.Height = 210;
				pageDiv.SetAttribute("class", pageDiv.Attributes["class"].Value.Replace("Portrait", "Landscape"));
			}
			else
			{
				thumbnailOptions.Width = 210;
				thumbnailOptions.Height = 297;
				// On the offchance someone makes a template with by-default-landscape pages...
				pageDiv.SetAttribute("class", pageDiv.Attributes["class"].Value.Replace("Landscape", "Portrait"));
			}
			// In different books (or even the same one) in the same session we may have portrait and landscape
			// versions of the same template page. So we must use different IDs.
			return _thumbnailProvider.GetThumbnail(page.Id + (isLandscape ? "L" : ""), pageDom, thumbnailOptions);
		}

		/// <summary>
		/// Will call either 'callback' or 'errorCallback' UNLESS the thumbnail is readonly, in which case it will do neither.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="thumbnailOptions"></param>
		/// <param name="callback"></param>
		/// <param name="errorCallback"></param>
		public void RebuildThumbNailAsync(Book.Book book, HtmlThumbNailer.ThumbnailOptions thumbnailOptions,
			Action<BookInfo, Image> callback, Action<BookInfo, Exception> errorCallback)
		{
			RebuildThumbNail(book, thumbnailOptions, callback, errorCallback, true);
		}

		/// <summary>
		/// Will make a new thumbnail (or throw) UNLESS the thumbnail is readonly, in which case it will do nothing.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="thumbnailOptions"></param>
		private void RebuildThumbNailNow(Book.Book book, HtmlThumbNailer.ThumbnailOptions thumbnailOptions)
		{
			RebuildThumbNail(book, thumbnailOptions, (info, image) => { },
				(info, ex) =>
				{
					throw ex;
				}, false);
		}

		/// <summary>
		/// Will call either 'callback' or 'errorCallback' UNLESS the thumbnail is readonly, in which case it will do neither.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="thumbnailOptions"></param>
		/// <param name="callback"></param>
		/// <param name="errorCallback"></param>
		private void RebuildThumbNail(Book.Book book, HtmlThumbNailer.ThumbnailOptions thumbnailOptions,
			Action<BookInfo, Image> callback, Action<BookInfo, Exception> errorCallback, bool async)
		{
			try
			{
				if(!book.Storage.RemoveBookThumbnail(thumbnailOptions.FileName))
				{
					// thumbnail is marked readonly, so just use it
					Image thumb;
					book.Storage.TryGetPremadeThumbnail(thumbnailOptions.FileName, out thumb);
					callback(book.BookInfo, thumb);
					return;
				}

				_thumbnailProvider.RemoveFromCache(book.Storage.Key);

				thumbnailOptions.BorderStyle = (book.IsSuitableForMakingShells)
					? HtmlThumbNailer.ThumbnailOptions.BorderStyles.Dashed // unique style for templates
					: HtmlThumbNailer.ThumbnailOptions.BorderStyles.Solid;
				GetThumbNailOfBookCover(book, thumbnailOptions, image => callback(book.BookInfo, image),
					error =>
					{
						//Enhance; this isn't a very satisfying time to find out, because it's only going to happen if we happen to be rebuilding the thumbnail.
						//It does help in the case where things are bad, so no thumbnail was created, but by then probably the user has already had some big error.
						//On the other hand, given that they have this bad book in their collection now, it's good to just remind them that it's broken and not
						//keep showing green error boxes.
						book.CheckForErrors();
						errorCallback(book.BookInfo, error);
					}, async);
			}
			catch(Exception error)
			{
				NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, "Problem creating book thumbnail ", exception: error);
			}
		}
	}
}
