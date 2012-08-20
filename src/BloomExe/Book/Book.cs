using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Xml;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Properties;
using Bloom.Publish;
using Gecko;
using Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Palaso.Code;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Progress.LogBox;
using Palaso.Reporting;
using Palaso.Text;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.UI.WindowsForms.ImageToolbox;
using Palaso.Xml;

namespace Bloom.Book
{
	public class ErrorBook : Book
	{
		public readonly Exception Exception;
		private readonly string _folderPath;
		private bool _canDelete;

		/// <summary>
		/// this is a bit of a hack to handle representing a book for which we got an exception while loading the storage... a better architecture wouldn't have this...
		/// </summary>
		public ErrorBook(Exception exception, string folderPath, bool canDelete)
		{
			Exception = exception;
			_folderPath = folderPath;
			Id = folderPath;
			_canDelete = canDelete;
			Logger.WriteEvent("Created ErrorBook with exception message: " + Exception.Message);
		}

		public override string Title
		{
			get
			{
				return Path.GetFileName(FolderPath);//actually gives us the leaf directory name
			}
		}
		public override string FolderPath
		{
			get { return _folderPath; }
		}

		public override bool CanDelete
		{
			get { return _canDelete; }
		}


		public override void GetThumbNailOfBookCoverAsync(bool drawBorderDashed, Action<Image> callback, Action<Exception> errorCallback)
		{
			callback(Resources.Error70x70);
		}

		public XmlDocument GetEditableHtmlDomForPage(IPage page)
		{
			return GetErrorDOM();
		}

		public override bool CanUpdate
		{
			get { return false; }
		}

		public override bool HasFatalError
		{
			get { return true; }
		}

		public override bool Delete()
		{
			var didDelete= Palaso.UI.WindowsForms.FileSystem.ConfirmRecycleDialog.Recycle(_folderPath);
			if(didDelete)
				Logger.WriteEvent("After ErrorBook.Delete({0})", _folderPath);
			return didDelete;
		}

		private XmlDocument GetErrorDOM()
		{
			var dom = new XmlDocument();
			var builder = new StringBuilder();
			builder.Append("<html><body>");
			builder.AppendLine("<p>This book (" + FolderPath + ") has errors.");
			builder.AppendLine(
				"This doesn't mean your work is lost, but it does mean that something is out of date or has gone wrong, and that someone needs to find and fix the problem (and your book).</p>");

			builder.Append(Exception.Message.Replace(Environment.NewLine,"<br/>"));

			builder.Append("</body></html>");
			dom.LoadXml(builder.ToString());
			return dom;
		}

		public override XmlDocument GetPreviewHtmlFileForWholeBook()
		{
			return GetErrorDOM();
		}

		public override XmlDocument RawDom
		{
			get
			{
				throw new ApplicationException("An ErrorBook was asked for a RawDom. The ErrorBook's exception message is "+Exception.Message);
			}
		}

		public override void SetTitle(string t)
		{
			Logger.WriteEvent("An ErrorBook was asked to set title.  The ErrorBook's exception message is "+Exception.Message);
		}
	}

	public class Book
	{
		//public const string ClassOfHiddenElements = "hideMe"; //"visibility:hidden !important; position:fixed  !important;";

		public delegate Book Factory(BookStorage storage, bool projectIsEditable);//autofac uses this

		private readonly ITemplateFinder _templateFinder;
		private readonly CollectionSettings _collectionSettings;

		private  List<string> _builtInConstants = new List<string>(new[] { "bookTitle", "topic", "nameOfLanguage" });
		private HtmlThumbNailer _thumbnailProvider;
		private readonly PageSelection _pageSelection;
		private readonly PageListChangedEvent _pageListChangedEvent;
		private readonly BookRefreshEvent _bookRefreshEvent;
		private IBookStorage _storage;
		private List<IPage> _pagesCache;

		public event EventHandler ContentsChanged;

//        public enum SizeAndShapeChoice
//		{
//			Unknown, A5Landscape, A5Portrait, A4Landscape, A4Portrait, A3Landscape,USLetterPortrait,USLetterLandscape,USHalfLetterPortrait,USHalfLetterLandscape
//		}

		private IProgress _log = new StringBuilderProgress();
		private bool _haveCheckedForErrorsAtLeastOnce;

		//for moq'ing only
		public Book(){}



		public Book(IBookStorage storage, bool projectIsEditable, ITemplateFinder templateFinder,
		   CollectionSettings collectionSettings, HtmlThumbNailer thumbnailProvider,
			PageSelection pageSelection,
			PageListChangedEvent pageListChangedEvent,
			BookRefreshEvent bookRefreshEvent)
		{
			IsInEditableLibrary = projectIsEditable;
			Id = Guid.NewGuid().ToString();

			Guard.AgainstNull(storage,"storage");
			_storage = storage;
			_templateFinder = templateFinder;

			_collectionSettings = collectionSettings;

			_thumbnailProvider = thumbnailProvider;
			_pageSelection = pageSelection;
			_pageListChangedEvent = pageListChangedEvent;
			_bookRefreshEvent = bookRefreshEvent;


			if (IsInEditableLibrary && !HasFatalError)
			{
				UpdateFieldsAndVariables(RawDom);
				WriteLanguageDisplayStyleSheet(); //NB: if you try to do this on a file that's in program files, access will be denied
				RawDom.AddStyleSheet(@"languageDisplay.css");
			}

			Guard.Against(_storage.Dom.InnerXml=="","Bloom could not parse the xhtml of this document");
			//LockedExceptForTranslation = HasSourceTranslations && !_librarySettings.IsSourceCollection;

		}


		public void InvokeContentsChanged(EventArgs e)
		{
			EventHandler handler = ContentsChanged;
			if (handler != null) handler(this, e);
		}

 /*       public SizeAndShapeChoice SizeAndShape
		{
			get
			{
				if(_storage.Dom ==null)//at least during early development, we're allowing books with no actual htm page
				{
					return SizeAndShapeChoice.Unknown;
				}
				var body = _storage.Dom.SelectSingleNodeHonoringDefaultNS("//body");
				var bodyClass = body.GetStringAttribute("class");
				if (bodyClass.Contains("a5Portrait"))
				{
					return SizeAndShapeChoice.A5Portrait;
				}
				else if (bodyClass.Contains("a5Landscape"))
				{
					return SizeAndShapeChoice.A5Landscape;
				}
				else if (bodyClass.Contains("A4Portrait"))
				{
					return SizeAndShapeChoice.A4Portrait;
				}
				else if (bodyClass.Contains("A5Landscape"))
				{
					return SizeAndShapeChoice.A5Landscape;
				}
				else if (bodyClass.Contains("A3Landscape"))
				{
					return SizeAndShapeChoice.A3Landscape;
				}
				else if (bodyClass.Contains("USLetterPortrait"))
				{
					return SizeAndShapeChoice.USLetterPortrait;
				}
				else if (bodyClass.Contains("USLetterLandscape"))
				{
					return SizeAndShapeChoice.USLetterLandscape;
				}
				else if (bodyClass.Contains("USHalfLetterPortrait"))
				{
					return SizeAndShapeChoice.USHalfLetterPortrait;
				}
				else if (bodyClass.Contains("USHalfLetterLandscape"))
				{
					return SizeAndShapeChoice.USHalfLetterLandscape;
				}

				else return SizeAndShapeChoice.Unknown;
			}
		}
		*/

		public enum BookType { Unknown, Template, Shell, Publication }

		/// <summary>
		/// we could get the title from the <title/> element, the name of the html, or the name of the folder...
		/// </summary>
		public virtual string Title
		{
			get
			{
				if (Type == BookType.Publication)
				{
					//REVIEW: evaluate and document when we would choose the value in the html over the name of the folder.
					//1 advantage of the folder is that if you have multiple copies, the folder tells you which one you are looking at


					//                    var node = _storage.Dom.SelectSingleNodeHonoringDefaultNS("//textarea[contains(@class,'vernacularBookTitle')]");
//                    if (node == null)
//                        return "unknown";
//                    return (node.InnerText);
					//var s =  _storage.GetVernacularTitleFromHtml(_librarySettings.Language1Iso639Code);
					var s = XmlUtils.GetTitleOfHtml(RawDom, null);
					if(s==null)
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
				string folderForCachingThumbnail = null;

				folderForCachingThumbnail = _storage.FolderPath;

				_thumbnailProvider.GetThumbnailAsync(folderForCachingThumbnail, _storage.Key, dom, Color.Transparent, drawBorderDashed, callback,errorCallback);
			}
			catch (Exception err)
			{
				Debug.Fail(err.Message);
				callback(Resources.Error70x70);
			}
		}

		public virtual XmlDocument GetEditableHtmlDomForPage(IPage page)
		{
			if (_log.ErrorEncountered)
			{
				return GetErrorDom();
			}

			XmlDocument dom = GetHtmlDomWithJustOnePage(page);
			BookStorage.RemoveModeStyleSheets(dom);
			dom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"basePage.css"));
			dom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"editMode.css"));
			if(LockedDown)
			{
				dom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"editTranslationMode.css"));
			}
			else
			{
				dom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"editOriginalMode.css"));
			}
			_storage.SortStyleSheetLinks(dom);
			AddJavaScriptForEditing(dom);
			AddCoverColor(dom, CoverColor);
			AddUIDictionary(dom);
			AddUISettings(dom);
			BookStarter.UpdateContentLanguageClasses(dom, _collectionSettings.Language1Iso639Code, _collectionSettings.Language2Iso639Code, _collectionSettings.Language3Iso639Code, MultilingualContentLanguage2, MultilingualContentLanguage3);
			return dom;
		}

		/// <summary>
		/// stick in a json with various string values/translations we want to make available to the javascript
		/// </summary>
		/// <param name="singlePageHtmlDom"></param>
		private void AddUIDictionary(XmlDocument singlePageHtmlDom)
		{
			XmlElement dictionaryScriptElement = singlePageHtmlDom.SelectSingleNode("//script[@id='ui-dictionary']") as XmlElement;
			if (dictionaryScriptElement != null)
				dictionaryScriptElement.ParentNode.RemoveChild(dictionaryScriptElement);

			dictionaryScriptElement = singlePageHtmlDom.CreateElement("script");
			dictionaryScriptElement.SetAttribute("type", "text/javascript");
			dictionaryScriptElement.SetAttribute("id", "ui-dictionary");
			var d = new Dictionary<string, string>();
			d.Add(_collectionSettings.Language1Iso639Code, _collectionSettings.Language1Name);
			if (!String.IsNullOrEmpty(_collectionSettings.Language2Iso639Code) && !d.ContainsKey(_collectionSettings.Language2Iso639Code))
				d.Add(_collectionSettings.Language2Iso639Code, _collectionSettings.GetLanguage2Name(_collectionSettings.Language2Iso639Code));
			if (!String.IsNullOrEmpty(_collectionSettings.Language3Iso639Code) && !d.ContainsKey(_collectionSettings.Language3Iso639Code))
				d.Add(_collectionSettings.Language3Iso639Code, _collectionSettings.GetLanguage3Name(_collectionSettings.Language3Iso639Code));

			d.Add("vernacularLang", _collectionSettings.Language1Iso639Code);//use for making the vernacular the first tab
			d.Add("{V}", _collectionSettings.Language1Name);
			d.Add("{N1}", _collectionSettings.GetLanguage2Name(_collectionSettings.Language2Iso639Code));
			d.Add("{N2}", _collectionSettings.GetLanguage3Name(_collectionSettings.Language3Iso639Code));

			AddLocalizedHintContentsToDictionary(singlePageHtmlDom, d);
			dictionaryScriptElement.InnerText = String.Format("function GetDictionary() {{ return {0};}}",JsonConvert.SerializeObject(d));

			singlePageHtmlDom.SelectSingleNode("//head").InsertAfter(dictionaryScriptElement, null);
		}

		private void AddLocalizedHintContentsToDictionary(XmlDocument singlePageHtmlDom, Dictionary<string, string> dictionary)
		{
			string idPrefix="";
			var pageElement = singlePageHtmlDom.SelectSingleNode("//div") as XmlElement;
			if (GetIsFrontMatterPage(pageElement))
			{
				idPrefix = "FrontMatter." + _collectionSettings.XMatterPackName + ".";
			}
			else if (GetIsBackMatterPage(pageElement))
			{
				idPrefix = "BackMatter." + _collectionSettings.XMatterPackName + ".";
			}
			foreach (XmlElement element in singlePageHtmlDom.SelectNodes("//*[@data-hint]"))
			{
				//why aren't we just doing: element.SetAttribute("data-hint", translation);  instead of bothering to write out a dictionary?
				//because (especially since we're currently just assuming it is in english), we would later save it with the translation, and then next time try to translate that, and poplute the
				//list of strings that we tell people to translate
				var key = element.GetAttribute("data-hint");
				if(!dictionary.ContainsKey(key))
				{
					string translation;
					var id = idPrefix + key;
					if(key.Contains("{lang}"))
					{
						translation = LocalizationManager.GetDynamicString("Bloom", id, key, "Put {lang} in your translation, so it can be replaced by the language name.");
					}
					else
					{
						translation = LocalizationManager.GetDynamicString("Bloom", id, key);
					}
					dictionary.Add(key, translation);
				}
			}
		}

		public static bool GetIsFrontMatterPage(XmlElement page)
		{
			return XMatterHelper.IsFrontMatterPage(page);
		}

		public static bool GetIsBackMatterPage(XmlElement page)
		{
			return XMatterHelper.IsBackMatterPage(page);
		}


		private static bool ContainsClass(XmlNode element, string className)
		{
			return ((XmlElement)element).GetAttribute("class").Contains(className);
		}

		/// <summary>
		/// stick in a json with various settings we want to make available to the javascript
		/// </summary>
		private void AddUISettings(XmlDocument dom)
		{
			XmlElement element = dom.SelectSingleNode("//script[@id='ui-settings']") as XmlElement;
			if (element != null)
				element.ParentNode.RemoveChild(element);

			element = dom.CreateElement("script");
			element.SetAttribute("type", "text/javascript");
			element.SetAttribute("id", "ui-settings");
			var d = new Dictionary<string, string>();

			d.Add("urlOfUIFiles", "file:///" + _storage.GetFileLocator().LocateDirectory("ui", "ui files directory"));
			if (!String.IsNullOrEmpty(Settings.Default.LastSourceLanguageViewed))
			{
				d.Add("defaultSourceLanguage", Settings.Default.LastSourceLanguageViewed);
			}

			d.Add("languageForNewTextBoxes", _collectionSettings.Language1Iso639Code);

			d.Add("bloomProgramFolder", Directory.GetParent(FileLocator.GetDirectoryDistributedWithApplication("root")).FullName);

			element.InnerText = String.Format("function GetSettings() {{ return {0};}}", JsonConvert.SerializeObject(d));

			dom.SelectSingleNode("//head").InsertAfter(element, null);
		}

		private static void AddDictionaryValue(XmlDocument dom, XmlElement dictionaryDiv, string key, string value)
		{
			var div = dom.CreateElement("div");
			div.SetAttribute("data-key", key);
			div.InnerText = value;
			dictionaryDiv.AppendChild(div);
		}

		private void AddJavaScriptForEditing(XmlDocument dom)
		{
			XmlElement head = dom.SelectSingleNodeHonoringDefaultNS("//head") as XmlElement;
		   // AddJavascriptFile(dom, head, _storage.GetFileLocator().LocateFile("jquery-1.4.4.min.js"));
			AddJavascriptFile(dom, head, _storage.GetFileLocator().LocateFile("bloomBootstrap.js"));
		}

		private void AddJavascriptFile(XmlDocument dom, XmlElement node, string pathToJavascript)
		{
			XmlElement element = node.AppendChild(dom.CreateElement("script")) as XmlElement;
			element.SetAttribute("type", "text/javascript");
			element.SetAttribute("src", "file://"+ pathToJavascript);
			node.AppendChild(element);
		}

		private XmlDocument GetHtmlDomWithJustOnePage(IPage page)
		{
			var dom = new XmlDocument();
			var relocatableCopyOfDom = _storage.GetRelocatableCopyOfDom(_log);
			var head = relocatableCopyOfDom.SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			dom.LoadXml(@"<html>"+head+"<body></body></html>");
			var body = dom.SelectSingleNodeHonoringDefaultNS("//body");
			var divNodeForThisPage = page.GetDivNodeForThisPage();
			if(divNodeForThisPage==null)
			{
				throw new ApplicationException(String.Format("The request page {0} from book {1} isn't in this book {2}.", page.Id,
															 page.Book.FolderPath, page.Book.FolderPath));
			}
			var pageDom = dom.ImportNode(divNodeForThisPage, true);
			body.AppendChild(pageDom);

//                BookStorage.HideAllTextAreasThatShouldNotShow(dom, iso639CodeToLeaveVisible, Page.GetPageSelectorXPath(dom));

			return dom;
		}


		/// <summary>
		///
		/// </summary>
		/// <param name="page"></param>
		/// <param name="iso639CodeToShow">NB: this isn't always the vernacular. If we're showing template pages, it will be, um, English?</param>
		/// <returns></returns>
		public XmlDocument GetPreviewXmlDocumentForPage(IPage page)
		{
			if(_log.ErrorEncountered)
			{
				return GetErrorDom();
			}
			var dom = GetHtmlDomWithJustOnePage(page);
			dom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"basePage.css"));
			dom.AddStyleSheet(_storage.GetFileLocator().LocateFile(@"previewMode.css"));

			_storage.SortStyleSheetLinks(dom);

			AddCoverColor(dom, CoverColor);
			AddPreviewJScript(dom);

			return dom;
		}

		private static void AddSheet(XmlDocument dom, XmlNode head, string cssFilePath, bool useFullFilePath)
		{
			var link = dom.CreateElement("link");
			link.SetAttribute("rel", "stylesheet");
			if (useFullFilePath)
			{
				link.SetAttribute("href", "file://" + cssFilePath);
			}
			else
			{
				link.SetAttribute("href", Path.GetFileName(cssFilePath));
			}
			link.SetAttribute("type", "text/css");
			head.AppendChild(link);
		}
		public XmlDocument GetPreviewXmlDocumentForFirstPage()
		{
			if (_log.ErrorEncountered)
			{
				return null;
			}

			XmlDocument bookDom = GetBookDomWithStyleSheet("previewMode.css");

			AddCoverColor(bookDom, CoverColor);
			HideEverythingButFirstPageAndRemoveScripts(bookDom);
			return bookDom;
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

		private XmlDocument GetBookDomWithStyleSheet(string cssFileName)
		{
			XmlDocument dom = (XmlDocument) _storage.GetRelocatableCopyOfDom(_log);
			dom.AddStyleSheet(_storage.GetFileLocator().LocateFile(cssFileName));
			_storage.SortStyleSheetLinks(dom);
			return dom;
		}

		public virtual string StoragePageFolder { get { return _storage.FolderPath; } }

		private XmlDocument GetPageListingErrorsWithBook(string contents)
		{
			var dom = new XmlDocument();
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
			dom.LoadXml(builder.ToString());
			return dom;
		}

		private XmlDocument GetErrorDom()
		{
			var dom = new XmlDocument();
			var builder = new StringBuilder();
			builder.Append("<html><body>");
			builder.AppendLine("<p>This book (" + FolderPath + ") has errors.");
			builder.AppendLine(
				"This doesn't mean your work is lost, but it does mean that something is out of date or has gone wrong, and that someone needs to find and fix the problem (and your book).</p>");

			builder.Append(((StringBuilderProgress)_log).Text);

			builder.Append("</body></html>");
			dom.LoadXml(builder.ToString());
			return dom;
		}


		public virtual bool CanDelete
		{
			get { return IsInEditableLibrary; }
		}

		public bool CanPublish
		{
			get { return IsInEditableLibrary && !HasFatalError; }
		}

		/// <summary>
		/// In the Bloom app, only one collection at a time is editable; that's the library they opened. All the other collections of templates, shells, etc., are not editable.
		/// </summary>
		public bool IsInEditableLibrary  { get; private set;}

		public IPage FirstPage
		{
			get { return GetPages().First(); }
		}

		public Book FindTemplateBook()
		{
				Guard.AgainstNull(_templateFinder, "_templateFinder");
				if(Type!=BookType.Publication)
					return null;
				string templateKey = GetMetaValue("pageTemplateSource", "");

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

		private string GetMetaValue(string name, string defaultValue)
		{
			var nameSuggestion = RawDom.SafeSelectNodes("//head/meta[@name='" + name + "']");
			if (nameSuggestion.Count > 0)
			{
				return ((XmlElement)nameSuggestion[0]).GetAttribute("content");
			}
			return defaultValue;
		}

		public BookType Type
		{
			get
			{
				return IsInEditableLibrary ? BookType.Publication : BookType.Template; //TODO
				//return _storage.BookType;
			}
		}

		public virtual XmlDocument RawDom
		{
			get {return  _storage.Dom; }
		}

		public virtual string FolderPath
		{
			get { return _storage.FolderPath; }
		}

		public virtual string Id { get; set; }

		public virtual XmlDocument GetPreviewHtmlFileForWholeBook()
		{
			//we may already know we have an error (we might not discover until later)
			if (HasFatalError)
			{
				return GetErrorDom();
			}
			if (!_storage.LooksOk)
			{
				return GetPageListingErrorsWithBook(_storage.GetValidateErrors());
			}
			var dom= GetBookDomWithStyleSheet("previewMode.css");

			//We may have just run into an error for the first time
			if (HasFatalError)
			{
				return GetErrorDom();
			}

			if (Type == BookType.Shell || Type == BookType.Template)
			{
				RebuildXMatter(dom, new NullProgress());
			}
			// this is normally the vernacular, but when we're previewing a shell, well it won't have anything for the vernacular
			var primaryLanguage = _collectionSettings.Language1Iso639Code;
			if (IsShellOrTemplate) //TODO: this won't be enough, if our national language isn't, say, English, and the shell just doesn't have our national language. But it might have some other language we understand.
				primaryLanguage = _collectionSettings.Language2Iso639Code;

			BookStarter.UpdateContentLanguageClasses(dom, primaryLanguage, _collectionSettings.Language2Iso639Code, _collectionSettings.Language3Iso639Code, MultilingualContentLanguage2, MultilingualContentLanguage3);
			AddCoverColor(dom, CoverColor);

			AddPreviewJScript(dom);
			return dom;
		}

		public void UpdateXMatter(IProgress progress)
		{
			_pagesCache = null;
			RebuildXMatter(RawDom, progress);
			_storage.Save();
			_bookRefreshEvent.Raise(this);
		}

		private void RebuildXMatter(XmlDocument dom, IProgress progress)
		{
			progress.WriteStatus("Gathering Data...");
//now we need the template fields in that xmatter to be updated to this document, this national language, etc.
			var data = LoadDataSetFromCollectionSettings(false);
			GatherDataItemsFromDom(data, "*", RawDom);
			var helper = new XMatterHelper(dom, _collectionSettings.XMatterPackName, _storage.GetFileLocator());
			XMatterHelper.RemoveExistingXMatter(dom);
			Layout layout = Layout.FromDom(dom, Layout.A5Portrait);			//enhance... this is currently just for the whole book. would be better page-by-page, somehow...
			progress.WriteStatus("Injecting XMatter...");
			helper.InjectXMatter(data.WritingSystemCodes, layout);
			BookStarter.PrepareElementsInPageOrDocument(dom, _collectionSettings);
			progress.WriteStatus("Updating Data...");
			UpdateDomWIthDataItems(data, "*", dom);
			if(Type == Book.BookType.Publication)
				UpdateAllImageMetadataHtmlAttributesAndSave(progress);
		}

		public Color CoverColor { get; set; }

		public bool IsShellOrTemplate
		{
			get
			{
				//hack. Eventually we might be able to lock books so that you can't edit them.
				return !IsInEditableLibrary;
			}
		}

		public bool HasSourceTranslations
		{
			get
			{
				//is there a textarea with something other than the vernacular, which has a containing element marked as a translation group?
				var x = _storage.Dom.SafeSelectNodes(String.Format("//*[contains(@class,'bloom-translationGroup')]//textarea[@lang and @lang!='{0}']", _collectionSettings.Language1Iso639Code));
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

				var node = _storage.Dom.SafeSelectNodes(String.Format("//meta[@name='pageTemplateSource']"));
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

				var node = _storage.Dom.SafeSelectNodes(String.Format("//meta[@name='canChangeImages' and @content='false']"));
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

				var node = _storage.Dom.SafeSelectNodes(String.Format("//meta[@name='canChangeLicense' and @content='false']"));
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

				var node = _storage.Dom.SafeSelectNodes(String.Format("//meta[@name='canChangeOriginalAcknowledgments' and @content='false']"));
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

				var node = _storage.Dom.SafeSelectNodes(String.Format("//meta[@name='lockedDownAsShell' and @content='true']"));
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


		/// <summary>
		/// For bilingual or trilingual books, this is the second language to show, after the vernacular
		/// </summary>
		public string MultilingualContentLanguage2
		{
			//REVIEW: this is messy, essentially storing the same datum in a property *and* the data-div.  Would it be too slow to just keep it in the data-div alone?
			get;

			/* only SetMultilingualContentLanguages should use this*/
			private set;
		}

		/// <summary>
		/// For trilingual books, this is the third language to show
		/// </summary>
		public string MultilingualContentLanguage3 { get;
			/* only SetMultilingualContentLanguages should use this*/
			private set;
		}

		public string ThumbnailPath
		{
			get { return Path.Combine(FolderPath, "thumbnail.png"); }
		}

		public virtual bool CanUpdate
		{
			get { return IsInEditableLibrary && !HasFatalError; }
		}


		/// <summary>
		/// In a vernacular library, we want to hide books that are meant only for people making shells
		/// </summary>
		public bool IsSuitableForVernacularLibrary
		{
			get {
				string metaValue = GetMetaValue("SuitableForMakingVernacularBooks", "yes");
				return metaValue == "yes" || metaValue == "definitely"; }//the 'template maker' says "no"
		}


		/// <summary>
		/// In a shell-making library, we want to hide books that are just shells, so rarely make sense as a starting point for more shells
		/// </summary>
		public bool IsSuitableForMakingShells
		{
			get
			{
				string metaValue = GetMetaValue("SuitableForMakingShells", "no");
				return metaValue == "yes" || metaValue == "definitely"; //the 'template maker' says "no|
			}//we imaging a future "unlikely"
		}

		public void SetMultilingualContentLanguages(string language2Code, string language3Code)
		{
			if (language2Code == _collectionSettings.Language1Iso639Code) //can't have the vernacular twice
				language2Code = null;
			if (language3Code == _collectionSettings.Language1Iso639Code)
				language3Code = null;
			if (language2Code == language3Code)	//can't use the same lang twice
				language3Code = null;

			if (String.IsNullOrEmpty(language2Code))
			{
				if(!String.IsNullOrEmpty(language3Code))
				{
					language2Code = language3Code; //can't have a 3 without a 2
					language3Code = null;
				}
				else
					language2Code = null;
			}
			if (language3Code == "")
				language3Code = null;

			MultilingualContentLanguage2 = language2Code;
			MultilingualContentLanguage3 = language3Code;

			RemoveDataDivElement("contentLanguage1");
			RemoveDataDivElement("contentLanguage2");
			RemoveDataDivElement("contentLanguage3");
			AddDataDivBookVariable("contentLanguage1", "*", _collectionSettings.Language1Iso639Code);
			if (MultilingualContentLanguage2 != null)
			{
				AddDataDivBookVariable("contentLanguage2", "*", language2Code);
			}
			if (MultilingualContentLanguage3 != null)
			{
				AddDataDivBookVariable("contentLanguage3", "*", language3Code);
			}
		}

		private void AddCoverColor(XmlDocument dom, Color coverColor)
		{
			var colorValue = ColorTranslator.ToHtml(coverColor);
//            var colorValue = String.Format("#{0:X2}{1:X2}{2:X2}", coverColor.R, coverColor.G, coverColor.B);
			XmlElement colorStyle = dom.CreateElement("style");
			colorStyle.SetAttribute("type","text/css");
			colorStyle.InnerXml = @"<!--

				DIV.coverColor  TEXTAREA	{		background-color: colorValue;	}
				DIV.bloom-page.coverColor	{		background-color: colorValue;	}
				-->".Replace("colorValue", colorValue);//string.format has a hard time with all those {'s

			var header = dom.SelectSingleNodeHonoringDefaultNS("//head");
			header.AppendChild(colorStyle);
		}


		/// <summary>
		/// Make stuff readonly, which isn't doable via css, surprisingly
		/// </summary>
		/// <param name="dom"></param>
		private void AddPreviewJScript(XmlDocument dom)
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

			XmlElement head = dom.SelectSingleNodeHonoringDefaultNS("//head") as XmlElement;
			AddJavascriptFile(dom, head, _storage.GetFileLocator().LocateFile("bloomPreviewBootstrap.js"));
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
				_pagesCache = new List<IPage>();

				int pageNumber = 0;
				foreach (XmlElement pageNode in _storage.Dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
				{
					//review: we want to show titles for template books, numbers for other books.
					//this here requires that titles be removed when the page is inserted, kind of a hack.
					var caption = GetPageLabelFromDiv(pageNode);
					if (String.IsNullOrEmpty(caption))
					{
						caption = "";//we aren't keeping these up to date yet as thing move around, so.... (pageNumber + 1).ToString();
					}
					_pagesCache.Add(CreatePageDecriptor(pageNode, caption, _collectionSettings.Language1Iso639Code));
					++pageNumber;
				}
			}

			foreach (var page in _pagesCache)
			{
				yield return page;
			}
		}


		public IEnumerable<IPage> GetTemplatePages()
		{
			if (_log.ErrorEncountered)
				yield break;

			foreach (XmlElement pageNode in _storage.Dom.SafeSelectNodes("//div[contains(@class,'bloom-page') and not(contains(@data-page, 'singleton'))]"))
			{
				var caption = GetPageLabelFromDiv(pageNode);
				var iso639CodeToShow = "";//REVIEW: should it be "en"?  what will the Lorum Ipsum's be?
				yield return CreatePageDecriptor(pageNode, caption, iso639CodeToShow);
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

		private IPage CreatePageDecriptor(XmlElement pageNode, string caption, string iso639Code)//, Action<Image> thumbNailReadyCallback)
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
			return _storage.Dom.SelectSingleNodeHonoringDefaultNS(page.XPathToDiv) as XmlElement;
		}

		public void InsertPageAfter(IPage pageBefore, IPage templatePage)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			ClearPagesCache();

			XmlDocument dom = _storage.Dom;
			var templatePageDiv = templatePage.GetDivNodeForThisPage();
			var newPageDiv = dom.ImportNode(templatePageDiv, true) as XmlElement;

			BookStarter.SetupIdAndLineage(templatePageDiv, newPageDiv);
			BookStarter.SetupPage(newPageDiv, _collectionSettings, MultilingualContentLanguage2, MultilingualContentLanguage3);//, LockedExceptForTranslation);
			ClearEditableValues(newPageDiv);
			newPageDiv.RemoveAttribute("title"); //titles are just for templates [Review: that's not true for front matter pages, but at the moment you can't insert those, so this is ok]C:\dev\Bloom\src\BloomExe\StyleSheetService.cs

			var elementOfPageBefore = FindPageDiv(pageBefore);
			elementOfPageBefore.ParentNode.InsertAfter(newPageDiv, elementOfPageBefore);
			_pageSelection.SelectPage(CreatePageDecriptor(newPageDiv, "should not show", _collectionSettings.Language1Iso639Code));

			_storage.Save();
			if (_pageListChangedEvent != null)
				_pageListChangedEvent.Raise(null);

			InvokeContentsChanged(null);
		}



		private void ClearEditableValues(XmlElement newPageElement)
		{
			foreach (XmlElement editNode in newPageElement.SafeSelectNodes(String.Format("//*[@lang='{0}']", _collectionSettings.Language1Iso639Code)))
			{
				if (editNode.InnerText.ToLower().StartsWith("lorem ipsum"))
				{
					editNode.InnerText = String.Empty;
				}
			}
		}

		public void DeletePage(IPage page)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			if(GetPageCount() <2)
				return;

			ClearPagesCache();

			var pageNode = FindPageDiv(page);
		   pageNode.ParentNode.RemoveChild(pageNode);
	//        InvokePageDeleted(page);

			var prevNod = pageNode.PreviousSibling;
			if(prevNod == null)
			{
				_pageSelection.SelectPage(FirstPage);
			}
			else
			{
				_pageSelection.SelectPage(FirstPage);

				//todo       var previousPage = GetPageFromNode(pageNode);
//                _pageSelection.SelectPage(previousPage);
			}
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
		public void SavePage(XmlDocument pageDom)
		{
			Debug.Assert(IsInEditableLibrary);

			XmlElement divElement = (XmlElement) pageDom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, 'bloom-page')]");
			string pageDivId = divElement.GetAttribute("id");

			var page = GetPageFromStorage(pageDivId);
			page.InnerXml = divElement.InnerXml;

			//notice, we supply this pageDom paramenter which means "read from this only", so that what you just did overwrites other instances in the doc, including the data-div
			var data = UpdateVariablesAndDataDiv(pageDom);

			try
			{
				_storage.Save();
			}
			catch (Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error, "There was a problem saving");
			}

			//todo: first page only:
			var oldPath = FolderPath;
			UpdateBookFolderAndFileNames(data);

			//Enhance: if this is only used to re-show the thumbnail, why not limit it to if this is the cover page?
			//e.g., look for the class "cover"
			InvokeContentsChanged(null);//enhance: above we could detect if anything actually changed
		}


		/// <summary>
		/// Gets the first element with the given tag & id, within the page-div with the given id.
		/// </summary>
		private XmlElement GetStorageNode(string pageDivId, string tag, string elementId)
		{
			var query = String.Format("//div[@id='{0}']//{1}[@id='{2}']", pageDivId, tag, elementId);
			var matches = _storage.Dom.SafeSelectNodes(query);
			if (matches.Count != 1)
			{
				throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
			}
			return (XmlElement)matches[0];
		}


		/// <summary>
		/// Gets the first element with the given tag & id, within the page-div with the given id.
		/// </summary>
		private XmlElement GetPageFromStorage(string pageDivId)
		{
			var query = String.Format("//div[@id='{0}']", pageDivId);
			var matches = _storage.Dom.SafeSelectNodes(query);
			if (matches.Count != 1)
			{
				throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
			}
			return (XmlElement)matches[0];
		}

		/// <summary>
		/// Go through the document, reading in values from fields, and then pushing variable values back into fields.
		/// Here we're calling "fields" the html supplying or receiving the data, and "variables" being key-value pairs themselves, which
		/// are, for library variables, saved in a separate file.
		/// </summary>
		/// <param name="domToRead">Set this parameter to, say, a page that the user just edited, to limit reading to it, so its values don't get overriden by previous pages.
		/// Supply the whole dom if nothing has priority (which will mean the data-div will win, because it is first)</param>
		public DataSet UpdateFieldsAndVariables(XmlDocument domToRead)
		{
			var data = LoadDataSetFromCollectionSettings(false);

			// The first encountered value for data-book/data-library wins... so the rest better be read-only to the user, or they're in for some frustration!
			// If we don't like that, we'd need to create an event to notice when field are changed.

			GatherDataItemsFromDom(data, "*", domToRead);

			foreach (var item in data.TextVariables)
			{
				foreach (var form in item.Value.TextAlternatives.Forms)
				{
					Debug.WriteLine("Gathered: {0}[{1}]={2}", item.Key,form.WritingSystemId, form.Form );
				}
			}
			UpdateDomWIthDataItems(data, "*", RawDom);

			UpdateTitle(data);
			return data;
		}

		private void UpdateTitle(DataSet data)
		{
			DataItem title;

			if (data.TextVariables.TryGetValue("bookTitle", out title))
			{
				XmlUtils.GetOrCreateElement(RawDom,"//html", "head");
				var t = title.TextAlternatives.GetBestAlternativeString(new string[] {_collectionSettings.Language1Iso639Code});
				SetTitle(t);
			}
		}

		public virtual void SetTitle(string t)
		{
			if (!String.IsNullOrEmpty(t.Trim()))
			{
				var titleNode = XmlUtils.GetOrCreateElement(RawDom, "//head", "title");
				//ah, but maybe that contains html element in there, like <br/> where the user typed a return in the title,

				//so we set the xml (not the text) of the node
				titleNode.InnerXml = t;
				//then ask it for the text again (will drop the xml)
				var justTheText = titleNode.InnerText.Replace("\r\n", " ").Replace("\n", " ").Replace("  ", " ");
				//then clear it
				titleNode.InnerXml = "";
				//and set the text again!
				titleNode.InnerText = justTheText;
			}
		}

		private void UpdateBookFolderAndFileNames(DataSet data)
		{
			UpdateTitle(data);
			_storage.UpdateBookFileAndFolderName(_collectionSettings);
		}


		/// <summary>
		///
		/// </summary>
		/// <param name="makeGeneric">When we're showing shells, we don't wayt to make it confusing by populating them with this library's data</param>
		/// <returns></returns>
		private DataSet LoadDataSetFromCollectionSettings(bool makeGeneric)
		{
			var data = new DataSet();


			data.WritingSystemCodes.Add("N1", _collectionSettings.Language2Iso639Code);
			data.WritingSystemCodes.Add("N2", _collectionSettings.Language3Iso639Code);

			if (makeGeneric)
			{
				data.WritingSystemCodes.Add("V", _collectionSettings.Language2Iso639Code);//This is not an error; we don't want to use the verncular when we're just previewing a book in a non-verncaulr collection
				data.AddGenericLanguageString("iso639Code", _collectionSettings.Language1Iso639Code, true); //review: maybe this should be, like 'xyz"
				data.AddGenericLanguageString("nameOfLanguage", "(Your Language Name)", true);
				data.AddGenericLanguageString("nameOfNationalLanguage1", "(Region Lang)", true);
				data.AddGenericLanguageString("nameOfNationalLanguage2", "(National Lang)", true);
				data.AddGenericLanguageString("country", "Your Country", true);
				data.AddGenericLanguageString("province", "Your Province", true);
				data.AddGenericLanguageString("district", "Your District", true);
				data.AddGenericLanguageString("languageLocation", "(Language Location)", true);
			}
			else
			{
				data.WritingSystemCodes.Add("V", _collectionSettings.Language1Iso639Code);
				data.AddLanguageString("*", "nameOfLanguage", _collectionSettings.Language1Name, true);
				data.AddLanguageString("*", "nameOfNationalLanguage1", _collectionSettings.GetLanguage2Name(_collectionSettings.Language2Iso639Code), true);
				data.AddLanguageString("*", "nameOfNationalLanguage2", _collectionSettings.GetLanguage3Name(_collectionSettings.Language2Iso639Code), true);
				data.AddGenericLanguageString("iso639Code", _collectionSettings.Language1Iso639Code, true);
				data.AddGenericLanguageString("country", _collectionSettings.Country, true);
				data.AddGenericLanguageString("province", _collectionSettings.Province, true);
				data.AddGenericLanguageString("district", _collectionSettings.District, true);
				string location = "";
				if(!String.IsNullOrEmpty(_collectionSettings.Province))
					location +=  _collectionSettings.Province+@", ";
				if (!String.IsNullOrEmpty(_collectionSettings.District))
					location +=  _collectionSettings.District;

				location = location.TrimEnd(new[] {' '}).TrimEnd(new[] {','});

				if (!String.IsNullOrEmpty(_collectionSettings.Country))
				{
					location += "<br/>"+_collectionSettings.Country;
				}

				data.AddGenericLanguageString("languageLocation", location, true);
			}
			return data;
		}





		private void Check()
		{
//    		XmlNode p = RawDom.SelectSingleNode("//p[@class='titleV']");
//    		Debug.Assert(p.InnerXml.Length < 25);
		}

		/// <summary>
		/// Move a page to somewhere else in the book
		/// </summary>
		public bool RelocatePage(IPage page, int indexOfItemAfterRelocation)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			if(!CanRelocatePageAsRequested(page, indexOfItemAfterRelocation))
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
			return _storage.Dom.SafeSelectNodes("/html/body/div[contains(@class,'bloom-page')]");
		}

		private bool CanRelocatePageAsRequested(IPage page, int indexOfItemAfterRelocation)
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
				_storage.Dom.SelectSingleNode("(/html/body/div[contains(@class,'bloom-frontMatter')])[last()]") as XmlElement;
			if(lastFrontMatterPage==null)
				return -1;
			return GetIndexOfPage(lastFrontMatterPage);
		}

		private int GetIndexOfFirstBackMatterPage()
		{
			XmlElement firstBackMatterPage =
				_storage.Dom.SelectSingleNode("(/html/body/div[contains(@class,'bloom-backMatter')])[position()=1]") as XmlElement;
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

		public virtual bool Delete()
		{
			return _storage.DeleteBook();
		}


		public XmlDocument GetDomForPrinting(PublishModel.BookletPortions bookletPortion)
		{
			var dom = GetBookDomWithStyleSheet("previewMode.css");
			//dom.LoadXml(_storage.Dom.OuterXml);

			//whereas the base is to our embedded server during editing, it's to the file folder
			//when we make a PDF, because we wan the PDF to use the original hi-res versions
			BookStorage.SetBaseForRelativePaths(dom, FolderPath, false);

			switch (bookletPortion)
			{
				case PublishModel.BookletPortions.None:
					break;
				case PublishModel.BookletPortions.BookletCover:
					HidePages(dom, p=>!p.GetAttribute("class").ToLower().Contains("cover"));
					break;
				 case PublishModel.BookletPortions.BookletPages:
					HidePages(dom, p=>p.GetAttribute("class").ToLower().Contains("cover"));
					break;
				default:
					throw new ArgumentOutOfRangeException("bookletPortion");
			}
			AddCoverColor(dom, Color.White);
			AddPreviewJScript(dom);
			return dom;
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
			if(_storage.Dom.SafeSelectNodes(String.Format("//meta[@name='defaultBookletLayout' and @content='Calendar']")).Count>0)
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
		/// Create or update the data div with all the data-book values in the document
		/// </summary>
		/// <param name="domToRead">Set this parameter to, say, a page that the user just edited, to limit reading to it, so its values don't get overriden by previous pages.
		/// Supply the whole dom if nothing has priority (which will mean the data-div will win, because it is first)</param>
		public DataSet UpdateVariablesAndDataDiv(XmlDocument domToRead)
		{
			XmlElement dataDiv = GetOrCreateDataDiv();


			Debug.WriteLine("before update: " + dataDiv.OuterXml);

			var data = UpdateFieldsAndVariables(domToRead);
			data.UpdateGenericLanguageString("contentLanguage1",_collectionSettings.Language1Iso639Code, false);
			data.UpdateGenericLanguageString("contentLanguage2", String.IsNullOrEmpty(MultilingualContentLanguage2) ? null : MultilingualContentLanguage2, false);
			data.UpdateGenericLanguageString("contentLanguage3", String.IsNullOrEmpty(MultilingualContentLanguage3) ? null : MultilingualContentLanguage3, false);

			Debug.WriteLine("xyz: " + dataDiv.OuterXml);
			foreach (var v in data.TextVariables)
			{
				if (v.Value.IsCollectionValue)
					continue;//we don't save these out here

				//Debug.WriteLine("before: " + dataDiv.OuterXml);

				foreach (var languageForm in v.Value.TextAlternatives.Forms)
				{
					XmlNode node = dataDiv.SelectSingleNode(String.Format("div[@data-book='{0}' and @lang='{1}']", v.Key, languageForm.WritingSystemId));
					if (null == node)
					{
						Debug.WriteLine("creating in datadiv: {0}[{1}]={2}", v.Key, languageForm.WritingSystemId, languageForm.Form);

						AddDataDivBookVariable(v.Key, languageForm.WritingSystemId, languageForm.Form);
						Debug.WriteLine("nop: " + dataDiv.OuterXml);
					}
					else
					{
						if(languageForm.Form==null)//a null value removes the entry entirely
						{
							node.ParentNode.RemoveChild(node);
						}
						else
						{
							node.InnerXml = languageForm.Form;
						}
						Debug.WriteLine("updating in datadiv: {0}[{1}]={2}", v.Key, languageForm.WritingSystemId, languageForm.Form);
						Debug.WriteLine("now: " + dataDiv.OuterXml);
					}
				}
			}
			Debug.WriteLine("after update: " + dataDiv.OuterXml);
			return data;
		}

		private void AddDataDivBookVariable(string key, string lang, string form)
		{
			var d = RawDom.CreateElement("div");
			d.SetAttribute("data-book", key);
			d.SetAttribute("lang", lang);
			d.InnerXml = form;
			GetOrCreateDataDiv().AppendChild(d);
		}

		private void RemoveDataDivElement(string key)
		{
			var dataDiv = GetOrCreateDataDiv();
			foreach(XmlNode e in  dataDiv.SafeSelectNodes(String.Format("div[@data-book='{0}']", key)))
			{
				dataDiv.RemoveChild(e);
			}
		}



		private XmlElement GetOrCreateDataDiv()
		{
			var dataDiv = RawDom.SelectSingleNode("//div[@id='bloomDataDiv']") as XmlElement;
			if (dataDiv == null)
			{
				dataDiv = RawDom.CreateElement("div");
				dataDiv.SetAttribute("id", "bloomDataDiv");
				RawDom.SelectSingleNode("//body").InsertAfter(dataDiv, null);
			}
			return dataDiv;
		}

		/// <summary>
		/// walk throught the sourceDom, collecting up values from elements that have data-book or data-library attributes.
		/// </summary>
		private void GatherDataItemsFromDom(DataSet data, string elementName, XmlNode sourceDom /* can be the whole sourceDom or just a page */)
		{
			try
			{
				string query = String.Format("//{0}[(@data-book or @data-library)]", elementName);

				var nodesOfInterest = sourceDom.SafeSelectNodes(query);

				foreach (XmlElement node in nodesOfInterest)
				{
					bool isLibrary = false;

					var key = node.GetAttribute("data-book").Trim();
					if (key == String.Empty)
					{
						key = node.GetAttribute("data-library").Trim();
						isLibrary = true;
					}

					string value = node.InnerXml.Trim();//may contain formatting
					if (node.Name.ToLower() == "img")
					{
						value = node.GetAttribute("src");
						//Make the name of the image safe for showing up in raw html (not just in the relatively safe confines of the src attribut),
						//becuase it's going to show up between <div> tags.  E.g. "Land & Water.png" as the cover page used to kill us.
						value = WebUtility.HtmlEncode(WebUtility.HtmlDecode(value));
					}
					if (!String.IsNullOrEmpty(value) && !value.StartsWith("{"))//ignore placeholder stuff like "{Book Title}"; that's not a value we want to collect
					{
						var lang = node.GetOptionalStringAttribute("lang", "*");
						if (lang == "")//the above doesn't stop a "" from getting through
							lang = "*";
						if ((elementName.ToLower() == "textarea" || elementName.ToLower() == "input" || node.GetOptionalStringAttribute("contenteditable", "false") == "true") && (lang == "V" || lang == "N1" || lang == "N2"))
						{
							throw new ApplicationException("Editable element (e.g. TextArea) should not have placeholder @lang attributes (V,N1,N2): " + _storage.FileName + "\r\n\r\n" + node.OuterXml);
						}

						//if we don't have a value for this variable and this language, add it
						if (!data.TextVariables.ContainsKey(key))
						{
							var t = new MultiTextBase();
							t.SetAlternative(lang, value);
							data.TextVariables.Add(key, new DataItem(t, isLibrary));
						}
						else if (!data.TextVariables[key].TextAlternatives.ContainsAlternative(lang))
						{
							var t = data.TextVariables[key].TextAlternatives;
							t.SetAlternative(lang, value);
						}
					}
				}

				//review: the desired behavior here is not clear. At the moment I'm saying, if we have a CL2 or CL3, I don't care what's in the xml, it's probably just behind
				if(String.IsNullOrEmpty(MultilingualContentLanguage2))
					MultilingualContentLanguage2 = data.TextVariables.ContainsKey("contentLanguage2") ? data.TextVariables["contentLanguage2"].TextAlternatives["*"] : null;
				if (String.IsNullOrEmpty(MultilingualContentLanguage3))
					MultilingualContentLanguage3 = data.TextVariables.ContainsKey("contentLanguage3") ? data.TextVariables["contentLanguage3"].TextAlternatives["*"] : null;
			}
			catch (Exception error)
			{
				throw new ApplicationException("Error in GatherDataItemsFromDom(," + elementName + "). RawDom was:\r\n" + RawDom.OuterXml, error);
			}
		}

		/// <summary>
		/// Where, for example, somewhere on a page something has data-book='foo' lan='fr',
		/// we set the value of that element to French subvalue of the data item 'foo', if we have one.
		/// </summary>
		private void UpdateDomWIthDataItems(DataSet data, string elementName, XmlDocument targetDom)
		{
			Check();

			try
			{
				string query = String.Format("//{0}[(@data-book or @data-library)]", elementName);
				var nodesOfInterest = targetDom.SafeSelectNodes(query);

				foreach (XmlElement node in nodesOfInterest)
				{
					var key = node.GetAttribute("data-book").Trim();
					if (key == String.Empty)
						key = node.GetAttribute("data-library").Trim();//"library" is the old name for what is now "collection"
					if (!String.IsNullOrEmpty(key) && data.TextVariables.ContainsKey(key))
					{
						if (node.Name.ToLower() == "img")
						{
							var imageName = WebUtility.HtmlDecode(data.TextVariables[key].TextAlternatives.GetFirstAlternative());
							node.SetAttribute("src", imageName);
						}
						else
						{
							var lang = node.GetOptionalStringAttribute("lang", "*");
							if (lang == "N1" || lang == "N2" || lang == "V")
								lang = data.WritingSystemCodes[lang];

//							//see comment later about the inability to clear a value. TODO: when we re-write Bloom, make sure this is possible
//							if(data.TextVariables[key].TextAlternatives.Forms.Length==0)
//							{
//								//no text forms == desire to remove it. THe multitextbase prohibits empty strings, so this is the best we can do: completly remove the item.
//								targetDom.RemoveChild(node);
//							}
//							else
							if (!String.IsNullOrEmpty(lang)) //N2, in particular, will often be missing
							{
								string s = data.TextVariables[key].TextAlternatives.GetBestAlternativeString(new string[] { lang, "*"});//, "en", "fr", "es" });//review: I really hate to lose the data, but I admit, this is trying a bit too hard :-)


								//NB: this was the focus of a multi-hour bug search, and it's not clear that I got it right.
								//The problem is that the title page has N1 and n2 alternatives for title, the cover may not.
								//the gather page was gathering no values for those alternatives (why not), and so GetBestAlternativeSTring
								//was giving "", which we then used to remove our nice values.
								//REVIEW: what affect will this have in other pages, other circumstances. Will it make it impossible to clear a value?
								//Hoping not, as we are differentiating between "" and just not being in the multitext at all.
								//don't overwrite a datadiv alternative with empty just becuase this page has no value for it.
								if (s == "" && !data.TextVariables[key].TextAlternatives.ContainsAlternative(lang))
									continue;

								//hack: until I think of a more elegant way to avoid repeating the language name in N2 when it's the exact same as N1...
								if (data.WritingSystemCodes.Count !=0 && lang == data.WritingSystemCodes["N2"] && s == data.TextVariables[key].TextAlternatives.GetBestAlternativeString(new string[] { data.WritingSystemCodes["N1"], "*" }))
								{
									s = ""; //don't show it in N2, since it's the same as N1
								}
								node.InnerXml = s;
								//meaning, we'll take "*" if you have it but not the exact choice. * is used for languageName, at least in dec 2011
							}
						}
					}
					else
					{
						//Review: Leave it to the ui to let them fill it in?  At the moment, we're only allowing that on textarea's. What if it's something else?
					}
					//Debug.WriteLine("123: "+key+" "+ RawDom.SelectSingleNode("//div[@id='bloomDataDiv']").OuterXml);


				}
			}
			catch (Exception error)
			{
				throw new ApplicationException("Error in MakeAllFieldsOfElementTypeConsistent(," + elementName + "). RawDom was:\r\n" + targetDom.OuterXml, error);
			}
			Check();
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

			foreach (XmlElement div in RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				BookStarter.PrepareElementsInPageOrDocument(div, _collectionSettings);
				BookStarter.UpdateContentLanguageClasses(div, _collectionSettings.Language1Iso639Code, _collectionSettings.Language2Iso639Code, _collectionSettings.Language3Iso639Code, MultilingualContentLanguage2, MultilingualContentLanguage3);
			}

		}

		public void RebuildThumbNailAsync(Action<Book, Image> callback, Action<Book, Exception> errorCallback)
		{
			_storage.RemoveBookThumbnail();
			_thumbnailProvider.RemoveFromCache(_storage.Key);
			GetThumbNailOfBookCoverAsync(Type != BookType.Publication, image=>callback(this,image),
				error=>
					{
						//Enhance; this isn't a very satisfying time to find out, because it's only going to happen if we happen to be rebuilding the thumbnail.
						//It does help in the case where things are bad, so no thumbnail was created, but by then probably the user has already had some big error.
						//On the other hand, given that they have this bad book in their collection now, it's good to just remind them that it's broken and not
						//keep showing green error boxes.
						CheckForErrors();
						errorCallback(this, error);
					});
		}

		private void CheckForErrors()
		{
			var errors = _storage.GetValidateErrors();
			_haveCheckedForErrorsAtLeastOnce = true;
			if (!String.IsNullOrEmpty(errors))
			{
				_log.WriteError(errors);
			}
		}

		public Layout GetLayout()
		{
			return Layout.FromDom(RawDom, Layout.A5Portrait);
		}

		public IEnumerable<Layout> GetLayoutChoices()
		{
			try
			{
				return SizeAndOrientation.GetLayoutChoices(RawDom, _storage.GetFileLocator());
			}
			catch (Exception error)
			{
				_log.WriteError(error.Message);
				throw error;
			}
		}
//
//		public IEnumerable GetLayoutChoices()
//		{
//			if (choice.Options.TryGetValue("Layout", out layouts))
//				{
//					foreach (var layout in layouts)
//					{
//		}

		public void SetLayout(Layout layout)
		{
			SizeAndOrientation.AddClassesForLayout(RawDom, layout);
		}

		public void UpdateLicenseMetdata(Metadata metadata)
		{
			var data = new DataSet();
			GatherDataItemsFromDom(data, "*", RawDom);

			var copyright = metadata.CopyrightNotice;
			data.UpdateLanguageString("*", "copyright", copyright, false);

			var description = metadata.License.GetDescription("en");
			data.UpdateLanguageString("en","licenseDescription", description, false);

			var licenseUrl = metadata.License.Url;
			data.UpdateLanguageString("*", "licenseUrl", licenseUrl, false);

			var licenseNotes = metadata.License.RightsStatement;
			data.UpdateLanguageString("*", "licenseNotes", licenseNotes, false);

			var licenseImageName = metadata.License.GetImage() == null ? "" : "license.png";
			data.UpdateGenericLanguageString("licenseImage", licenseImageName, false);


			UpdateDomWIthDataItems(data, "*",RawDom);

			//UpdateDomWIthDataItems() is not able to remove items yet, so we do it explicity

			RemoveDataDivElementIfEmptyValue("licenseDescription", description);
			RemoveDataDivElementIfEmptyValue("licenseImage", licenseImageName);
			RemoveDataDivElementIfEmptyValue("licenseUrl", licenseUrl);
			RemoveDataDivElementIfEmptyValue("copyright", copyright);
			RemoveDataDivElementIfEmptyValue("licenseNotes", licenseNotes);
		}

		private void RemoveDataDivElementIfEmptyValue(string key, string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				foreach (XmlElement node in RawDom.SafeSelectNodes("//div[@id='bloomDataDiv']//div[@data-book='" + key + "']"))
				{
					node.ParentNode.RemoveChild(node);
				}
			}
		}

		public Metadata GetLicenseMetadata()
		{
			var data = new DataSet();
			GatherDataItemsFromDom(data,"*", RawDom);
			var metadata = new Metadata();
			DataItem d;
			if(data.TextVariables.TryGetValue("copyright", out d))
			{
				metadata.CopyrightNotice = d.TextAlternatives.GetFirstAlternative();
			}
			string licenseUrl="";
			if (data.TextVariables.TryGetValue("licenseUrl", out d))
			{
				licenseUrl = d.TextAlternatives.GetFirstAlternative();
			}

			//Enhance: have a place for notes (amendments to license). It's already in the frontmatter, under "licenseNotes"
			if (licenseUrl == null || licenseUrl.Trim() == "")
			{
				//NB: we are mapping "RightsStatement" (which comes from XMP-dc:Rights) to "LicenseNotes" in the html.
				//custom licenses live in this field
				if (data.TextVariables.TryGetValue("licenseNotes", out d))
				{
					var licenseNotes = d.TextAlternatives.GetFirstAlternative();

					metadata.License = new CustomLicense() {RightsStatement = licenseNotes};
				}
				else
				{
					//how to detect a null license was chosen? We're using the fact that it has a description, but nothing else.
					if (data.TextVariables.TryGetValue("licenseDescription", out d))
					{
						metadata.License = new NullLicense(); //"contact the copyright owner
					}
					else
					{
						//looks like the first time. Nudge them with a nice default
						metadata.License = new CreativeCommonsLicense(true, true, CreativeCommonsLicense.DerivativeRules.Derivatives);
					}
				}
			}
			else
			{
				metadata.License = CreativeCommonsLicense.FromLicenseUrl(licenseUrl);
			}
			return metadata;
		}


		public IEnumerable<string> GetImagePaths()
		{
			foreach (var path in Directory.EnumerateFiles(_storage.FolderPath).Where(s => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
			{
				if ((path.ToLower() =="placeholder.png") || path.ToLower()==("license.png") || path.ToLower() == ("thumbnail.png"))
					continue;
				yield return path;
			}
		}

		/// <summary>
		/// This is used when the user elects to apply the same image metadata to all images.
		/// </summary>
		public void CopyImageMetadataToWholeBookAndSave(Metadata metadata, IProgress progress)
		{
			progress.WriteStatus("Starting...");

			//First update the images themselves

			int completed = 0;
			var imgElements = GetImagePaths();
			foreach (string path in imgElements)
			{
				progress.ProgressIndicator.PercentCompleted = (int)(100.0 * (float)completed / imgElements.Count());
				progress.WriteStatus("Copying to "+ Path.GetFileName(path));
				using (var image = PalasoImage.FromFile(path))
				{
					image.Metadata = metadata;
					image.SaveUpdatedMetadataIfItMakesSense();
				}
				++completed;
			}

			//Now update the html attributes which echo some of it, and is used by javascript to overlay displays related to
			//whether the info is there or missing or whatever.

			foreach (XmlElement img in RawDom.SafeSelectNodes("//img"))
			{
				UpdateImgMetdataAttributesToMatchImage(img, progress, metadata);
			}

			_storage.Save();
		}

		public void UpdateImgMetdataAttributesToMatchImage(XmlElement imgElement, IProgress progress)
		{
			UpdateImgMetdataAttributesToMatchImage(imgElement, progress, null);
		}

		private void UpdateImgMetdataAttributesToMatchImage(XmlElement imgElement, IProgress progress, Metadata metadata)
		{
			//see also PageEditingModel.UpdateMetadataAttributesOnImage(), which does the same thing but on the browser dom
			var fileName = imgElement.GetOptionalStringAttribute("src", string.Empty).ToLower();
			if (fileName == "placeholder.png" || fileName == "license.png")
				return;

			if(string.IsNullOrEmpty(fileName))
			{
				Logger.WriteEvent("Book.UpdateImgMetdataAttributesToMatchImage() Warning: img has no or empty src attribute");
				//Debug.Fail(" (Debug only) img has no or empty src attribute");
				return; // they have bigger problems, which aren't appropriate to deal with here.
			}
			if(metadata ==null)
			{
				progress.WriteStatus("Reading metadata from "+fileName);
				var path = FolderPath.CombineForPath(fileName);
				if (!File.Exists(path)) // they have bigger problems, which aren't appropriate to deal with here.
				{
					imgElement.RemoveAttribute("data-copyright");
					imgElement.RemoveAttribute("data-creator");
					imgElement.RemoveAttribute("data-license");
					Logger.WriteEvent("Book.UpdateImgMetdataAttributesToMatchImage()  Image " + path + " is missing");
					Debug.Fail(" (Debug only) Image " + path + " is missing");
					return;
				}
				using (var image = PalasoImage.FromFile(path))
				{
					metadata = image.Metadata;
				}
			}

			progress.WriteStatus("Writing metadata to HTML for " + fileName);

			imgElement.SetAttribute("data-copyright",
							 String.IsNullOrEmpty(metadata.CopyrightNotice) ? "" : metadata.CopyrightNotice);
			imgElement.SetAttribute("data-creator", String.IsNullOrEmpty(metadata.Creator) ? "" : metadata.Creator);
			imgElement.SetAttribute("data-license", metadata.License == null ? "" : metadata.License.ToString());
		}


		/// <summary>
		/// We mirror several metadata tags in the html for quick access by the UI.
		/// This method makes sure they are all up to date.
		/// </summary>
		/// <param name="progress"> </param>
		public void UpdateAllImageMetadataHtmlAttributesAndSave(IProgress progress)
		{
			//Update the html attributes which echo some of it, and is used by javascript to overlay displays related to
			//whether the info is there or missing or whatever.

			var imgElements = RawDom.SafeSelectNodes("//img");
			int completed=0;
			foreach (XmlElement img in imgElements)
			{
				progress.ProgressIndicator.PercentCompleted =(int) (100.0* (float)completed/(float)imgElements.Count);
				//("Updating image metadata in html for +");
				UpdateImgMetdataAttributesToMatchImage(img, progress);
				completed++;
			}

			_storage.Save();
		}
	}
}
