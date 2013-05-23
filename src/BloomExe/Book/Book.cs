using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Properties;
using Bloom.Publish;
using Palaso.Code;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.Xml;

namespace Bloom.Book
{
	public class Book
	{
		public delegate Book Factory(BookInfo info, IBookStorage storage);//autofac uses this

		private readonly ITemplateFinder _templateFinder;
		private readonly CollectionSettings _collectionSettings;

		private readonly HtmlThumbNailer _thumbnailProvider;
		private readonly PageSelection _pageSelection;
		private readonly PageListChangedEvent _pageListChangedEvent;
		private readonly BookRefreshEvent _bookRefreshEvent;
		private readonly IBookStorage _storage;
		private List<IPage> _pagesCache;

		public event EventHandler ContentsChanged;

		private IProgress _log = new StringBuilderProgress();
		private bool _haveCheckedForErrorsAtLeastOnce;
		private readonly BookData _bookData;

		//for moq'ing only
		public Book(){}

		public Book(BookInfo info, IBookStorage storage, ITemplateFinder templateFinder,
		   CollectionSettings collectionSettings, HtmlThumbNailer thumbnailProvider,
			PageSelection pageSelection,
			PageListChangedEvent pageListChangedEvent,
			BookRefreshEvent bookRefreshEvent)
		{
			BookInfo = info;

			Guard.AgainstNull(storage,"storage");

			_storage = storage;

			//this is a hack to keep these two in sync (in one direction)
			_storage.FolderPathChanged +=(x,y)=>BookInfo.FolderPath = _storage.FolderPath;

			_templateFinder = templateFinder;

			_collectionSettings = collectionSettings;

			_thumbnailProvider = thumbnailProvider;
			_pageSelection = pageSelection;
			_pageListChangedEvent = pageListChangedEvent;
			_bookRefreshEvent = bookRefreshEvent;
			_bookData = new BookData(OurHtmlDom,
					_collectionSettings, UpdateImageMetadataAttributes);

			if (IsEditable && !HasFatalError)
			{
				_bookData.SynchronizeDataItemsThroughoutDOM();

				WriteLanguageDisplayStyleSheet(); //NB: if you try to do this on a file that's in program files, access will be denied
				OurHtmlDom.AddStyleSheet(@"languageDisplay.css");
			}

			Guard.Against(OurHtmlDom.RawDom.InnerXml=="","Bloom could not parse the xhtml of this document");
		}


		public void InvokeContentsChanged(EventArgs e)
		{
			EventHandler handler = ContentsChanged;
			if (handler != null) handler(this, e);
		}

		public enum BookType { Unknown, Template, Shell, Publication }

		/// <summary>
		/// If we have to just show title in one language, which should it be?
		/// Note, this isn't going to be the best for choosing a filename, which we are more likely to want in a national language
		/// </summary>
		public string TitleBestForUserDisplay
		{
			get
			{
				var list = new List<string>();
				list.Add(_collectionSettings.Language1Iso639Code);
				if (_collectionSettings.Language2Iso639Code != null)
					list.Add(_collectionSettings.Language2Iso639Code);
				if (_collectionSettings.Language3Iso639Code != null)
					list.Add(_collectionSettings.Language3Iso639Code);
				list.Add("en");

				var title = _bookData.GetMultiTextVariableOrEmpty("bookTitle");
				var t = title.GetBestAlternativeString(list);
				if(string.IsNullOrEmpty(t))
				{
					return "Title Missing";
				}
				t = t.Replace("<br />", " ").Replace("\r\n"," ").Replace("  "," ");
				return t;
			}
		}

		/// <summary>
		/// we could get the title from the <title/> element, the name of the html, or the name of the folder...
		/// </summary>
		public virtual string Title
		{
			get
			{
				Debug.Assert(BookInfo.FolderPath == _storage.FolderPath);

				if (Type == BookType.Publication)
				{
					//REVIEW: evaluate and explain when we would choose the value in the html over the name of the folder.
					//1 advantage of the folder is that if you have multiple copies, the folder tells you which one you are looking at
					var s = OurHtmlDom.Title;
					if(string.IsNullOrEmpty(s))
						return Path.GetFileName(_storage.FolderPath);
					return s;
				}
				else //for templates and such, we can already just use the folder name
				{
					return Path.GetFileName(_storage.FolderPath);
				}
			}
		}

		public virtual void GetThumbNailOfBookCoverAsync(bool drawBorderDashed, Action<Image> callback, Action<Exception> errorCallback)
		{
			try
			{
				if (HasFatalError) //NB: we might not know yet... we don't fully load every book just to show its thumbnail
				{
					callback(Resources.Error70x70);
				}
				Image thumb;
				if (_storage.TryGetPremadeThumbnail(out thumb))
				{
					callback(thumb);
					return;
				}

				var dom = GetPreviewXmlDocumentForFirstPage();
				if (dom == null)
				{
					callback(Resources.Error70x70);
					return;
				}
				string folderForCachingThumbnail;

				folderForCachingThumbnail = _storage.FolderPath;

				_thumbnailProvider.GetThumbnailAsync(folderForCachingThumbnail, _storage.Key, dom, Color.Transparent, drawBorderDashed, callback,errorCallback);
			}
			catch (Exception err)
			{
				callback(Resources.Error70x70);
				Debug.Fail(err.Message);
			}
		}

		public virtual HtmlDom GetEditableHtmlDomForPage(IPage page)
		{
			if (_log.ErrorEncountered)
			{
				return GetErrorDom();
			}

			var pageDom = GetHtmlDomWithJustOnePage(page);
			pageDom.RemoveModeStyleSheets();
			pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"basePage.css"));
			pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"editMode.css"));
			if(LockedDown)
			{
				pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"editTranslationMode.css"));
			}
			else
			{
				pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"editOriginalMode.css"));
			}
			pageDom.SortStyleSheetLinks();
			AddJavaScriptForEditing(pageDom);
			AddCoverColor(pageDom, CoverColor);
			RuntimeInformationInjector.AddUIDictionaryToDom(pageDom, _collectionSettings);
			RuntimeInformationInjector.AddUISettingsToDom(pageDom, _collectionSettings, _storage.GetFileLocator());
			UpdateMultilingualSettings(pageDom);
			return pageDom;
		}

		private void AddJavaScriptForEditing(HtmlDom dom)
		{
			dom.AddJavascriptFile(_storage.GetFileLocator().LocateFile("bloomBootstrap.js"));
		}


		private void UpdateMultilingualSettings(HtmlDom dom)
		{
			TranslationGroupManager.UpdateContentLanguageClasses(dom.RawDom, _collectionSettings.Language1Iso639Code,
													 _collectionSettings.Language2Iso639Code,
													 _collectionSettings.Language3Iso639Code, _bookData.MultilingualContentLanguage2,
													 _bookData.MultilingualContentLanguage3);
		}

		private HtmlDom GetHtmlDomWithJustOnePage(IPage page)
		{
			var headXml = _storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			var dom = new HtmlDom(@"<html>" + headXml + "<body></body></html>");
			dom = _storage.MakeDomRelocatable(dom, _log);

			var body = dom.RawDom.SelectSingleNodeHonoringDefaultNS("//body");
			var divNodeForThisPage = page.GetDivNodeForThisPage();
			if(divNodeForThisPage==null)
			{
				throw new ApplicationException(String.Format("The requested page {0} from book {1} isn't in this book {2}.", page.Id,
															 page.Book.FolderPath, FolderPath));
			}
			var pageDom = dom.RawDom.ImportNode(divNodeForThisPage, true);
			body.AppendChild(pageDom);

//                BookStorage.HideAllTextAreasThatShouldNotShow(dom, iso639CodeToLeaveVisible, Page.GetPageSelectorXPath(dom));

			return dom;
		}


		public HtmlDom GetPreviewXmlDocumentForPage(IPage page)
		{
			if(_log.ErrorEncountered)
			{
				return GetErrorDom();
			}
			var pageDom = GetHtmlDomWithJustOnePage(page);
			pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"basePage.css"));
			pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"previewMode.css"));

			pageDom.SortStyleSheetLinks();

			AddCoverColor(pageDom, CoverColor);
			AddPreviewJScript(pageDom);

			return pageDom;
		}

		public XmlDocument GetPreviewXmlDocumentForFirstPage()
		{
			if (_log.ErrorEncountered)
			{
				return null;
			}

			var bookDom = GetBookDomWithStyleSheets("previewMode.css","thumbnail.css");

			AddCoverColor(bookDom, CoverColor);
			HideEverythingButFirstPageAndRemoveScripts(bookDom.RawDom);
			return bookDom.RawDom;
		}

		private static void HideEverythingButFirstPageAndRemoveScripts(XmlDocument bookDom)
		{
			bool onFirst = true;
			foreach (XmlElement node in bookDom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]"))
			{
				if (!onFirst)
				{
					node.SetAttribute("style", "", "display:none");
				}
				onFirst =false;
			}
			foreach (XmlElement node in bookDom.SafeSelectNodes("//script"))
			{
				//TODO: this removes image scaling, which is ok so long as it's already scaled with width/height attributes
				node.ParentNode.RemoveChild(node);
			}
		}

		private static void HidePages(XmlDocument bookDom, Func<XmlElement, bool> hidePredicate)
		{
			foreach (XmlElement node in bookDom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]"))
			{
				if (hidePredicate(node))
				{
					node.SetAttribute("style", "", "display:none");
				}
			}
		}

		private HtmlDom GetBookDomWithStyleSheets(params string[] cssFileNames)
		{
			var dom = _storage.GetRelocatableCopyOfDom(_log);
			var fileLocator = _storage.GetFileLocator();
			foreach (var cssFileName in cssFileNames)
			{
				dom.AddStyleSheet(fileLocator.LocateFile(cssFileName));
			}
			dom.SortStyleSheetLinks();
			return dom;
		}

		public virtual string StoragePageFolder { get { return _storage.FolderPath; } }

		private HtmlDom GetPageListingErrorsWithBook(string contents)
		{
			var builder = new StringBuilder();
			builder.Append("<html><body>");
			builder.AppendLine("<p>This book (" + StoragePageFolder + ") has errors.");
			builder.AppendLine(
				"This doesn't mean your work is lost, but it does mean that something is out of date or has gone wrong, and that someone needs to find and fix the problem (and your book).</p>");

			foreach (var line in contents.Split(new []{'\n'}))
			{
				builder.AppendFormat("<li>{0}</li>\n", WebUtility.HtmlEncode(line));
			}
			builder.Append("</body></html>");
			return new HtmlDom(builder.ToString());
		}

		private HtmlDom GetErrorDom()
		{
			var builder = new StringBuilder();
			builder.Append("<html><body>");
			builder.AppendLine("<p>This book (" + FolderPath + ") has errors.");
			builder.AppendLine(
				"This doesn't mean your work is lost, but it does mean that something is out of date or has gone wrong, and that someone needs to find and fix the problem (and your book).</p>");

			builder.Append(((StringBuilderProgress)_log).Text);

			builder.Append("</body></html>");
			return new HtmlDom(builder.ToString());
		}


		public virtual bool CanDelete
		{
			get { return IsEditable; }
		}

		public bool CanPublish
		{
			get { return IsEditable && !HasFatalError; }
		}

		/// <summary>
		/// In the Bloom app, only one collection at a time is editable; that's the library they opened. All the other collections of templates, shells, etc., are not editable.
		/// </summary>
		public bool IsEditable { get { return BookInfo.IsEditable; } }

		public IPage FirstPage
		{
			get { return GetPages().First(); }
		}

		public Book FindTemplateBook()
		{
				Guard.AgainstNull(_templateFinder, "_templateFinder");
				if(Type!=BookType.Publication)
					return null;
				string templateKey = OurHtmlDom.GetMetaValue("pageTemplateSource", "");

				Book book=null;
				if (!String.IsNullOrEmpty(templateKey))
				{
					if (templateKey.ToLower() == "basicbook")//catch this pre-beta spelling with no space
						templateKey = "Basic Book";
					book = _templateFinder.FindTemplateBook(templateKey);
					if(book==null)
					{
						ErrorReport.NotifyUserOfProblem("Bloom could not find the source of template pages named {0} (as in {0}.htm).\r\nThis comes from the <meta name='pageTemplateSource' content='{0}'/>.\r\nCheck that name matches the html exactly.",templateKey);
					}
				}
				if(book==null)
				{
					//re-use the pages in the document itself. This is useful when building
					//a new, complicated shell, which often have repeating pages but
					//don't make sense as a new kind of template.
					return this;
				}
				return book;
		}

		public BookType TypeOverrideForUnitTests;

		public BookType Type
		{
			get
			{
				if(TypeOverrideForUnitTests != BookType.Unknown)
					return TypeOverrideForUnitTests;

				return IsEditable ? BookType.Publication : BookType.Template; //TODO
				//return _storage.BookType;
			}
		}

		public HtmlDom OurHtmlDom
		{
			get { return _storage.Dom;}
		}

		public virtual XmlDocument RawDom
		{
			get {return  OurHtmlDom.RawDom; }
		}

		public virtual string FolderPath
		{
			get { return _storage.FolderPath; }
		}

		public virtual HtmlDom GetPreviewHtmlFileForWholeBook()
		{
			//we may already know we have an error (we might not discover until later)
			if (HasFatalError)
			{
				return GetErrorDom();
			}
			if (!_storage.GetLooksOk())
			{
				return GetPageListingErrorsWithBook(_storage.GetValidateErrors());
			}
			var previewDom= GetBookDomWithStyleSheets("previewMode.css");

			//We may have just run into an error for the first time
			if (HasFatalError)
			{
				return GetErrorDom();
			}

			//shells & templates are stored without frontmatter. This will add and update the frontmatter to our preivew dom
			if (Type == BookType.Shell || Type == BookType.Template)
			{
				BringBookUpToDate(previewDom, new NullProgress());
			}
			// this is normally the vernacular, but when we're previewing a shell, well it won't have anything for the vernacular
			var primaryLanguage = _collectionSettings.Language1Iso639Code;
			if (IsShellOrTemplate) //TODO: this won't be enough, if our national language isn't, say, English, and the shell just doesn't have our national language. But it might have some other language we understand.
				primaryLanguage = _collectionSettings.Language2Iso639Code;

			TranslationGroupManager.UpdateContentLanguageClasses(previewDom.RawDom, primaryLanguage, _collectionSettings.Language2Iso639Code, _collectionSettings.Language3Iso639Code, _bookData.MultilingualContentLanguage2, _bookData.MultilingualContentLanguage3);
			AddCoverColor(previewDom, CoverColor);

			AddPreviewJScript(previewDom);
			return previewDom;
		}

		public void BringBookUpToDate(IProgress progress)
		{
			_pagesCache = null;
			BringBookUpToDate(OurHtmlDom, progress);
			if (Type == Book.BookType.Publication)
			{
				ImageUpdater.UpdateAllHtmlDataAttributesForAllImgElements(FolderPath, OurHtmlDom, progress);
				UpdatePageFromFactoryTemplates(OurHtmlDom, progress);
				ImageUpdater.CompressImages(FolderPath, progress);
				_storage.Save();
			}
			_storage.Save();
			_bookRefreshEvent.Raise(this);
		}

		private void BringBookUpToDate(HtmlDom bookDOM /* may be a 'preview' version*/, IProgress progress)
		{
			progress.WriteStatus("Gathering Data...");
			var helper = new XMatterHelper(bookDOM, _collectionSettings.XMatterPackName, _storage.GetFileLocator());
			XMatterHelper.RemoveExistingXMatter(bookDOM);
			Layout layout = Layout.FromDom(bookDOM, Layout.A5Portrait);			//enhance... this is currently just for the whole book. would be better page-by-page, somehow...
			progress.WriteStatus("Injecting XMatter...");
			helper.InjectXMatter(FolderPath, _bookData.GetWritingSystemCodes(), layout);
			TranslationGroupManager.PrepareElementsInPageOrDocument(bookDOM.RawDom, _collectionSettings);
			progress.WriteStatus("Updating Data...");


			//hack
			if(bookDOM == OurHtmlDom)//we already have a data for this
			{
				_bookData.SynchronizeDataItemsThroughoutDOM();
			}
			else //used for making a preview dom
			{
				var bd = new BookData(bookDOM, _collectionSettings, UpdateImageMetadataAttributes);
				bd.SynchronizeDataItemsThroughoutDOM();
			}
		}

		private void UpdateImageMetadataAttributes(XmlElement imgNode)
		{
			ImageUpdater.UpdateImgMetdataAttributesToMatchImage(FolderPath, imgNode, new NullProgress());
		}

		private void UpdatePageFromFactoryTemplates(HtmlDom bookDom, IProgress progress)
		{
			var originalLayout = Layout.FromDom(bookDom, Layout.A5Portrait);

			var templatePath = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "Basic Book");

			var templateDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(templatePath.CombineForPath("templatePages.htm"));

			progress.WriteStatus("Updating pages that were based on Basic Book...");
			foreach (XmlElement templatePageDiv in templateDom.SafeSelectNodes("//body/div"))
			{
				var templateId = templatePageDiv.GetStringAttribute("id");
				if (string.IsNullOrEmpty(templateId))
					return;

				var templatePageClasses = templatePageDiv.GetAttribute("class");
				//note, lineage is a series of guids separated by a semicolon
				foreach (XmlElement pageDiv in bookDom.SafeSelectNodes("//body/div[contains(@data-pagelineage, '" + templateId + "')]"))
				{
					pageDiv.SetAttribute("class", templatePageClasses);

					//now for all the editable elements within the page
					int count = 0;
					foreach (XmlElement templateElement in templatePageDiv.SafeSelectNodes("div/div"))
					{
						UpdateDivInsidePage(count, templateElement, pageDiv, progress);
						++count;
					}
				}
			}

			//custom layout gets messed up when we copy classes over from, for example, Basic Book
			SetLayout(originalLayout);

			//Likewise, the multilingual settings (e.g. bloom-bilingual) get messed up, so restore those
			UpdateMultilingualSettings(bookDom);
		}

		private static void UpdateDivInsidePage(int zeroBasedCount, XmlElement templateElement, XmlElement targetPage, IProgress progress)
		{
			XmlElement targetElement = targetPage.SelectSingleNode("div/div[" + (zeroBasedCount + 1).ToString(CultureInfo.InvariantCulture) + "]") as XmlElement;
			if (targetElement == null)
			{
				progress.WriteError("Book had less than the expected number of divs on page " + targetPage.GetAttribute("id") +
									", so it cannot be completely updated.");
				return;
			}
			targetElement.SetAttribute("class", templateElement.GetAttribute("class"));
		}

		public Color CoverColor { get { return BookInfo.CoverColor; } }

		public bool IsShellOrTemplate
		{
			get
			{
				//hack. Eventually we might be able to lock books so that you can't edit them.
				return !IsEditable;
			}
		}

		public bool HasSourceTranslations
		{
			get
			{
				//is there a textarea with something other than the vernacular, which has a containing element marked as a translation group?
				var x = OurHtmlDom.SafeSelectNodes(String.Format("//*[contains(@class,'bloom-translationGroup')]//textarea[@lang and @lang!='{0}']", _collectionSettings.Language1Iso639Code));
				return x.Count > 0;
			}

		}

		/*
		 *					Basic Book		Shellbook		Calendar		Picture Dictionary		Picture Dictionary Premade
		 *	Change Images		y				n				y					y					y
		 *	UseSrcForTmpPgs		y				n				n					y					y
		 *	remove pages		y				n				n					y					y
		 *	change orig creds	y				n				n					y					no?
		 *	change license		y				n				y					y					no?
		 */

		/*
		 *  The current design: for all these settings, put them in meta, except, override them all with the "LockDownForShell" setting, which can be specified in a meta tag.
		 *  The default for all permissions is 'true', so don't specify them in a document unless you want to withhold the permission.
		 *  See UseSourceForTemplatePages for one the exception.
		 */

		/// <summary>
		/// This one is a bit different becuase we just imply that it's false unless at least one pageTemplateSource is specified.
		/// </summary>
		public bool UseSourceForTemplatePages
		{
			get
			{
				if (LockedDown)
					return false;

				var node = OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='pageTemplateSource']"));
				return node.Count > 0;
			}
		}

		/// <summary>
		/// Don't allow (or at least don't encourage) changing the images
		/// </summary>
		/// <remarks>In April 2012, we don't yet have an example of a book which would explicitly
		/// restrict changing images. Shells do, of course, but do so by virtue of their lockedDownAsShell being set to 'true'.</remarks>
		public bool CanChangeImages
		{
			get
			{
				if (LockedDown)
					return false;

				var node = OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='canChangeImages' and @content='false']"));
				return node.Count == 0;
			}
		}

		/// <summary>
		/// This is useful if you are allowing people to make major changes, but want to insist that derivatives carry the same license
		/// </summary>
		public bool CanChangeLicense
		{
			get
			{
				if (LockedDown)
					return false;

				var node = OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='canChangeLicense' and @content='false']"));
				return node.Count == 0;
			}
		}


		/// <summary>
		/// This is useful if you are allowing people to make major changes, but want to preserve acknowledments, for example, for the jscript programmers on Wall Calendar, or
		/// the person who put together a starter picture-dictionary.
		/// </summary>
		public bool CanChangeOriginalAcknowledgments
		{
			get
			{
				if (LockedDown)
					return false;

				var node = OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='canChangeOriginalAcknowledgments' and @content='false']"));
				return node.Count == 0;
			}
		}

		/// <summary>
		/// A book is lockedDown if it says it is AND we're not in a shell-making library
		/// </summary>
		public bool LockedDown
		{
			get
			{
				if(_collectionSettings.IsSourceCollection) //nothing is locked if we're in a shell-making library
					return false;

				var node = OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='lockedDownAsShell' and @content='true']"));
				return node.Count > 0;
			}
		}

//		/// <summary>
//        /// Is this a shell we're translating? And if so, is this a shell-making project?
//        /// </summary>
//        public bool LockedExceptForTranslation
//        {
//            get
//            {
//            	return !_librarySettings.IsSourceCollection &&
//            	       RawDom.SafeSelectNodes("//meta[@name='editability' and @content='translationOnly']").Count > 0;
//            }
//        }

		public string CategoryForUsageReporting
		{
			get
			{
				if (_collectionSettings.IsSourceCollection)
				{
					return "ShellEditing";
				}
				else if (LockedDown)
				{
					return "ShellTranslating";
				}
				else
				{
					return "CustomVernacularBook";
				}
			}
		}



		public virtual bool HasFatalError
		{
			get { return _log.ErrorEncountered; }
		}


		public string ThumbnailPath
		{
			get { return Path.Combine(FolderPath, "thumbnail.png"); }
		}

		public virtual bool CanUpdate
		{
			get { return IsEditable && !HasFatalError; }
		}


		/// <summary>
		/// In a vernacular library, we want to hide books that are meant only for people making shells
		/// </summary>
		public bool IsSuitableForVernacularLibrary
		{
			get {
				string metaValue = OurHtmlDom.GetMetaValue("SuitableForMakingVernacularBooks", "yes");
				return metaValue == "yes" || metaValue == "definitely"; }//the 'template maker' says "no"
		}


		//discontinuing this for now becuase we need to know whether to show the book when all we have is a bookinfo, not access to the
		//dom like this requires. We'll just hard code the names of the experimental things.
//        public bool IsExperimental
//        {
//            get
//            {
//                string metaValue = OurHtmlDom.GetMetaValue("experimental", "false");
//                return metaValue == "true" || metaValue == "yes";
//            }
//        }

		/// <summary>
		/// In a shell-making library, we want to hide books that are just shells, so rarely make sense as a starting point for more shells
		/// </summary>
		public bool IsSuitableForMakingShells
		{
			get
			{
				string metaValue = OurHtmlDom.GetMetaValue("SuitableForMakingShells", "no");
				return metaValue == "yes" || metaValue == "definitely"; //the 'template maker' says "no|
			}//we imaging a future "unlikely"
		}

		/// <summary>
		/// For bilingual or trilingual books, this is the second language to show, after the vernacular
		/// </summary>
		public string MultilingualContentLanguage2
		{
			get { return _bookData.MultilingualContentLanguage2; }
		}

		/// <summary>
		/// For trilingual books, this is the third language to show
		/// </summary>
		public string MultilingualContentLanguage3
		{
			get { return _bookData.MultilingualContentLanguage3; }
		}

		public BookInfo BookInfo { get; private set; }


		public void SetMultilingualContentLanguages(string language2Code, string language3Code)
		{
			_bookData.SetMultilingualContentLanguages(language2Code, language3Code);
		}



		private void AddCoverColor(HtmlDom dom, Color coverColor)
		{
			var colorValue = ColorTranslator.ToHtml(coverColor);
//            var colorValue = String.Format("#{0:X2}{1:X2}{2:X2}", coverColor.R, coverColor.G, coverColor.B);
			XmlElement colorStyle = dom.RawDom.CreateElement("style");
			colorStyle.SetAttribute("type","text/css");
			colorStyle.InnerXml = @"<!--

				DIV.coverColor  TEXTAREA	{		background-color: colorValue;	}
				DIV.bloom-page.coverColor	{		background-color: colorValue;	}
				-->".Replace("colorValue", colorValue);//string.format has a hard time with all those {'s

			var header = dom.RawDom.SelectSingleNodeHonoringDefaultNS("//head");
			header.AppendChild(colorStyle);
		}


		/// <summary>
		/// Make stuff readonly, which isn't doable via css, surprisingly
		/// </summary>
		/// <param name="dom"></param>
		private void AddPreviewJScript(HtmlDom dom)
		{
//			XmlElement header = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//head");
//			AddJavascriptFile(dom, header, _storage.GetFileLocator().LocateFile("jquery.js"));
//			AddJavascriptFile(dom, header, _storage.GetFileLocator().LocateFile("jquery.myimgscale.js"));
//
//			XmlElement script = dom.CreateElement("script");
//			script.SetAttribute("type", "text/javascript");
//			script.InnerText = @"jQuery(function() {
//						$('textarea').focus(function() {$(this).attr('readonly','readonly');});
//
//						//make images scale up to their container without distorting their proportions, while being centered within it.
//						$('img').scaleImage({ scale: 'fit' }); //uses jquery.myimgscale.js
//			})";
//			header.AppendChild(script);

			dom.AddJavascriptFile(_storage.GetFileLocator().LocateFile("bloomPreviewBootstrap.js"));
		}

		public IEnumerable<IPage> GetPages()
		{
			if (!_haveCheckedForErrorsAtLeastOnce)
			{
				CheckForErrors();
			}

			if (_log.ErrorEncountered)
				yield break;

			if (_pagesCache == null)
			{
				BuildPageCache();
			}

			foreach (var page in _pagesCache)
			{
				yield return page;
			}
		}

		private void BuildPageCache()
		{
			_pagesCache = new List<IPage>();

			foreach (XmlElement pageNode in OurHtmlDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				//review: we want to show titles for template books, numbers for other books.
				//this here requires that titles be removed when the page is inserted, kind of a hack.
				var caption = GetPageLabelFromDiv(pageNode);
				if (String.IsNullOrEmpty(caption))
				{
					caption = "";
						//we aren't keeping these up to date yet as thing move around, so.... (pageNumber + 1).ToString();
				}
				_pagesCache.Add(CreatePageDecriptor(pageNode, caption));
			}
		}


		private IPage GetPageToShowAfterDeletion(IPage page)
		{
			Guard.AgainstNull(_pagesCache, "_pageCache");
			var matchingPageEvenIfNotActualObject = _pagesCache.First(p => p.Id == page.Id);
			Guard.AgainstNull(matchingPageEvenIfNotActualObject, "Couldn't find page with matching id in cache");
			var index = _pagesCache.IndexOf(matchingPageEvenIfNotActualObject);
			Guard.Against(index <0, "Couldn't find page in cache");

			if (index == _pagesCache.Count - 1)//if it's the last page
			{
				if (index < 1) //if it's the only page
					throw new ApplicationException("Bloom should not have allowed you to delete the last remaining page.");
				return _pagesCache[index - 1];//give the preceding page
			}


			return _pagesCache[index + 1]; //give the following page
		}

		public IEnumerable<IPage> GetTemplatePages()
		{
			if (_log.ErrorEncountered)
				yield break;

			foreach (XmlElement pageNode in OurHtmlDom.SafeSelectNodes("//div[contains(@class,'bloom-page') and not(contains(@data-page, 'singleton'))]"))
			{
				var caption = GetPageLabelFromDiv(pageNode);
				yield return CreatePageDecriptor(pageNode, caption);
			}
		}

		private static string GetPageLabelFromDiv(XmlElement pageNode)
		{
//todo: try to get the one with the current UI language
			//var pageLabelDivs = pageNode.SelectNodes("div[contains(@class,'pageLabel')]");

			var englishDiv = pageNode.SelectSingleNode("div[contains(@class,'pageLabel') and @lang='en']");
			var caption = (englishDiv == null) ? String.Empty : englishDiv.InnerText;
			return caption;
		}

		private IPage CreatePageDecriptor(XmlElement pageNode, string caption)//, Action<Image> thumbNailReadyCallback)
		{
			return new Page(this, pageNode, caption,
//				   ((page) => _thumbnailProvider.GetThumbnailAsync(String.Empty, page.Id, GetPreviewXmlDocumentForPage(page, iso639Code), Color.White, false, thumbNailReadyCallback)),
//					//	(page => GetPageThumbNail()),
						(page => FindPageDiv(page)));
		}

		public Image GetPageThumbNail()
		{
			return Resources.Error70x70;
		}

		private XmlElement FindPageDiv(IPage page)
		{
			//review: could move to page
			return OurHtmlDom.RawDom.SelectSingleNodeHonoringDefaultNS(page.XPathToDiv) as XmlElement;
		}

		public void InsertPageAfter(IPage pageBefore, IPage templatePage)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			ClearPagesCache();

			XmlDocument dom = OurHtmlDom.RawDom;
			var templatePageDiv = templatePage.GetDivNodeForThisPage();
			var newPageDiv = dom.ImportNode(templatePageDiv, true) as XmlElement;

			BookStarter.SetupIdAndLineage(templatePageDiv, newPageDiv);
			BookStarter.SetupPage(newPageDiv, _collectionSettings, _bookData.MultilingualContentLanguage2, _bookData.MultilingualContentLanguage3);//, LockedExceptForTranslation);
			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPageDiv, GetLayout());
			newPageDiv.RemoveAttribute("title"); //titles are just for templates [Review: that's not true for front matter pages, but at the moment you can't insert those, so this is ok]C:\dev\Bloom\src\BloomExe\StyleSheetService.cs

			var elementOfPageBefore = FindPageDiv(pageBefore);
			elementOfPageBefore.ParentNode.InsertAfter(newPageDiv, elementOfPageBefore);

			BuildPageCache();
			var newPage = GetPages().First(p=>p.GetDivNodeForThisPage() == newPageDiv);
			Guard.AgainstNull(newPage,"could not find the page we just added");
			_pageSelection.SelectPage(newPage);
			//_pageSelection.SelectPage(CreatePageDecriptor(newPageDiv, "should not show", _collectionSettings.Language1Iso639Code));

			_storage.Save();
			if (_pageListChangedEvent != null)
				_pageListChangedEvent.Raise(null);

			InvokeContentsChanged(null);
		}



		public void DeletePage(IPage page)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			if(GetPageCount() <2)
				return;

			var pageToShowNext = GetPageToShowAfterDeletion(page);

			ClearPagesCache();
			//_pagesCache.Remove(page);

			var pageNode = FindPageDiv(page);
		   pageNode.ParentNode.RemoveChild(pageNode);

		   _pageSelection.SelectPage(pageToShowNext);
			_storage.Save();
			if(_pageListChangedEvent !=null)
				_pageListChangedEvent.Raise(null);

			InvokeContentsChanged(null);
		}

		private void ClearPagesCache()
		{
			_pagesCache = null;
		}

		private int GetPageCount()
		{
			return GetPages().Count();
		}


		/// <summary>
		/// Earlier, we handed out a single-page version of the document. Now it has been edited,
		/// so we now we need to fold changes back in
		/// </summary>
		public void SavePage(HtmlDom editedPageDom)
		{
			Debug.Assert(IsEditable);
			try
			{
				//replace the corresponding page contents in our DOM with what is in this PageDom
				XmlElement divElement = editedPageDom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, 'bloom-page')]");
				string pageDivId = divElement.GetAttribute("id");
				var page = GetPageFromStorage(pageDivId);
				page.InnerXml = divElement.InnerXml;

				 _bookData.SuckInDataFromEditedDom(editedPageDom);
//                 _collectionSettings.UpdateCustomValuesAndSave(_bookData.GetCollectionVariables());

				try
				{
					_storage.Save();
				}
				catch (Exception error)
				{
					ErrorReport.NotifyUserOfProblem(error, "There was a problem saving");
				}

				_storage.UpdateBookFileAndFolderName(_collectionSettings);
				//review used to have   UpdateBookFolderAndFileNames(data);


				//Enhance: if this is only used to re-show the thumbnail, why not limit it to if this is the cover page?
				//e.g., look for the class "cover"
				InvokeContentsChanged(null); //enhance: above we could detect if anything actually changed
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Bloom had trouble saving a page. Please click Details below and report this to us. Then quit Bloom, run it again, and check to see if the page you just edited is missing anything. Sorry!");
			}
		}


//        /// <summary>
//        /// Gets the first element with the given tag & id, within the page-div with the given id.
//        /// </summary>
//        private XmlElement GetStorageNode(string pageDivId, string tag, string elementId)
//        {
//            var query = String.Format("//div[@id='{0}']//{1}[@id='{2}']", pageDivId, tag, elementId);
//            var matches = OurHtmlDom.SafeSelectNodes(query);
//            if (matches.Count != 1)
//            {
//                throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
//            }
//            return (XmlElement)matches[0];
//        }


		/// <summary>
		/// Gets the first element with the given tag & id, within the page-div with the given id.
		/// </summary>
		private XmlElement GetPageFromStorage(string pageDivId)
		{
			var query = String.Format("//div[@id='{0}']", pageDivId);
			var matches = OurHtmlDom.SafeSelectNodes(query);
			if (matches.Count != 1)
			{
				throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
			}
			return (XmlElement)matches[0];
		}

		/// <summary>
		/// Move a page to somewhere else in the book
		/// </summary>
		public bool RelocatePage(IPage page, int indexOfItemAfterRelocation)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			if(!CanRelocatePageAsRequested(indexOfItemAfterRelocation))
			{

				return false;
			}

			ClearPagesCache();

			var pages = GetPageElements();
			var pageDiv = FindPageDiv(page);
			var body = pageDiv.ParentNode;
				body.RemoveChild(pageDiv);
			if(indexOfItemAfterRelocation == 0)
			{
				body.InsertBefore(pageDiv, body.FirstChild);
			}
			else
			{
				body.InsertAfter(pageDiv, pages[indexOfItemAfterRelocation-1]);
			}

			_storage.Save();
			InvokeContentsChanged(null);
			return true;
		}

		private XmlNodeList GetPageElements()
		{
			return OurHtmlDom.SafeSelectNodes("/html/body/div[contains(@class,'bloom-page')]");
		}

		private bool CanRelocatePageAsRequested(int indexOfItemAfterRelocation)
		{
			int upperBounds = GetIndexOfFirstBackMatterPage();
			if (upperBounds < 0)
				upperBounds = 10000;

			return indexOfItemAfterRelocation > GetIndexLastFrontkMatterPage ()
				&& indexOfItemAfterRelocation < upperBounds;
		}

		private int GetIndexLastFrontkMatterPage()
		{
			XmlElement lastFrontMatterPage =
				OurHtmlDom.RawDom.SelectSingleNode("(/html/body/div[contains(@class,'bloom-frontMatter')])[last()]") as XmlElement;
			if(lastFrontMatterPage==null)
				return -1;
			return GetIndexOfPage(lastFrontMatterPage);
		}

		private int GetIndexOfFirstBackMatterPage()
		{
			XmlElement firstBackMatterPage =
				OurHtmlDom.RawDom.SelectSingleNode("(/html/body/div[contains(@class,'bloom-backMatter')])[position()=1]") as XmlElement;
			if (firstBackMatterPage == null)
				return -1;
			return GetIndexOfPage(firstBackMatterPage);
		}

		private int GetIndexOfPage(XmlElement pageElement)
		{
			var elements = GetPageElements();
			for (int i = 0; i < elements.Count; i++)
			{
				if (elements[i] == pageElement)
					return i;
			}
			return -1;
		}

		public XmlDocument GetDomForPrinting(PublishModel.BookletPortions bookletPortion)
		{
			var printingDom = GetBookDomWithStyleSheets("previewMode.css");
			//dom.LoadXml(OurHtmlDom.OuterXml);

			//whereas the base is to our embedded server during editing, it's to the file folder
			//when we make a PDF, because we wan the PDF to use the original hi-res versions
			BookStorage.SetBaseForRelativePaths(printingDom, FolderPath, false);

			switch (bookletPortion)
			{
				case PublishModel.BookletPortions.None:
					break;
				case PublishModel.BookletPortions.BookletCover:
					HidePages(printingDom.RawDom, p=>!p.GetAttribute("class").ToLower().Contains("cover"));
					break;
				 case PublishModel.BookletPortions.BookletPages:
					HidePages(printingDom.RawDom, p => p.GetAttribute("class").ToLower().Contains("cover"));
					break;
				default:
					throw new ArgumentOutOfRangeException("bookletPortion");
			}
			AddCoverColor(printingDom, Color.White);
			AddPreviewJScript(printingDom);
			return printingDom.RawDom;
		}

		/// <summary>
		/// this is used for configuration, where we do want to offer up the original file.
		/// </summary>
		/// <returns></returns>
		public string GetPathHtmlFile()
		{
			return _storage.PathToExistingHtml;
		}



		public PublishModel.BookletLayoutMethod GetDefaultBookletLayout()
		{
			//NB: all we support at the moment is specifying "Calendar"
			if(OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='defaultBookletLayout' and @content='Calendar']")).Count>0)
				return PublishModel.BookletLayoutMethod.Calendar;
			else
				return PublishModel.BookletLayoutMethod.SideFold;
		}

		/// <summary>
		/// This stylesheet is used to hide all the elements we don't want to show, e.g. because they are not in the languages of this publication.
		/// We read in the template version, then replace some things and write it out to the publication folder.
		/// </summary>
		private void WriteLanguageDisplayStyleSheet( )
		{
			var template = File.ReadAllText(_storage.GetFileLocator().LocateFile("languageDisplayTemplate.css"));
			var path = _storage.FolderPath.CombineForPath("languageDisplay.css");
			if (File.Exists(path))
				File.Delete(path);
			File.WriteAllText(path, template.Replace("VERNACULAR", _collectionSettings.Language1Iso639Code).Replace("NATIONAL", _collectionSettings.Language2Iso639Code));

			//ENHANCE: this works for editable books, but for shell collections, it would be nice to show the national language of the user... e.g., when browsing shells,
			//see the French.  But we don't want to be changing those collection folders at runtime if we can avoid it. So, this style sheet could be edited in memory, at runtime.
		}


		/// <summary>
		///Under normal conditions, this isn't needed, because it is done when a book is first created. But thing might have changed:
		/// *changing xmatter pack, and update to it, changing the languages, etc.
		/// *the book was dragged from another project
		/// *the editing language was changed.
		/// Under those conditions, if we didn't, for example, do a PrepareElementsInPageOrDocument, we would end up with no
		/// editable items, because there are no elements in our language.
		/// </summary>
		public void PrepareForEditing()
		{
			// I may re-enable this later....			RebuildXMatter(RawDom);

			var language1Iso639Code = _collectionSettings.Language1Iso639Code;
			var language2Iso639Code = _collectionSettings.Language2Iso639Code;
			var language3Iso639Code = _collectionSettings.Language3Iso639Code;
			var multilingualContentLanguage2 = _bookData.MultilingualContentLanguage2;
			var multilingualContentLanguage3 = _bookData.MultilingualContentLanguage3;
		   foreach (XmlElement div in OurHtmlDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				TranslationGroupManager.PrepareElementsInPageOrDocument(div, _collectionSettings);
				TranslationGroupManager.UpdateContentLanguageClasses(div, language1Iso639Code, language2Iso639Code, language3Iso639Code, multilingualContentLanguage2, multilingualContentLanguage3);
			}
		}

		public void RebuildThumbNailAsync(Action<BookInfo, Image> callback, Action<BookInfo, Exception> errorCallback)
		{
			if (!_storage.RemoveBookThumbnail())
				return;

			_thumbnailProvider.RemoveFromCache(_storage.Key);
			GetThumbNailOfBookCoverAsync(Type != BookType.Publication, image=>callback(this.BookInfo,image),
				error=>
					{
						//Enhance; this isn't a very satisfying time to find out, because it's only going to happen if we happen to be rebuilding the thumbnail.
						//It does help in the case where things are bad, so no thumbnail was created, but by then probably the user has already had some big error.
						//On the other hand, given that they have this bad book in their collection now, it's good to just remind them that it's broken and not
						//keep showing green error boxes.
						CheckForErrors();
						errorCallback(this.BookInfo, error);
					});
		}

		public string CheckForErrors()
		{
			var errors = _storage.GetValidateErrors();
			_haveCheckedForErrorsAtLeastOnce = true;
			if (!String.IsNullOrEmpty(errors))
			{
				_log.WriteError(errors);
			}
			return errors ?? "";
		}

		public Layout GetLayout()
		{
			return Layout.FromDom(OurHtmlDom, Layout.A5Portrait);
		}

		public IEnumerable<Layout> GetLayoutChoices()
		{
			try
			{
				return SizeAndOrientation.GetLayoutChoices(OurHtmlDom, _storage.GetFileLocator());
			}
			catch (Exception error)
			{
				_log.WriteError(error.Message);
				throw error;
			}
		}

		public void SetLayout(Layout layout)
		{
			SizeAndOrientation.AddClassesForLayout(OurHtmlDom, layout);
		}


		/// <summary>
		/// This is used when the user elects to apply the same image metadata to all images.
		/// </summary>
		public void CopyImageMetadataToWholeBookAndSave(Metadata metadata, IProgress progress)
		{
			ImageUpdater.CopyImageMetadataToWholeBook(_storage.FolderPath,OurHtmlDom, metadata, progress);
			_storage.Save();
		}

		public Metadata GetLicenseMetadata()
		{
			return _bookData.GetLicenseMetadata();
		}

		public void UpdateLicenseMetdata(Metadata metadata)
		{
			_bookData.SetLicenseMetdata(metadata);
		}

		public void SetTitle(string name)
		{
			OurHtmlDom.Title = name;
		}
	}
}
