using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SendReceive;
using Bloom.ToPalaso.Experimental;
using Bloom.web;
using BloomTemp;
using DesktopAnalytics;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.UI.WindowsForms.ImageToolbox;
using Gecko;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly LanguageSettings _languageSettings;
		private readonly DeletePageCommand _deletePageCommand;
		private readonly LocalizationChangedEvent _localizationChangedEvent;
		private readonly CollectionSettings _collectionSettings;
		private readonly SendReceiver _sendReceiver;
		private HtmlDom _domForCurrentPage;
		public bool Visible;
		private Book.Book _currentlyDisplayedBook;
		private EditingView _view;
		private List<ContentLanguage> _contentLanguages;
		private IPage _previouslySelectedPage;
		private bool _inProcessOfDeleting;
		private string _accordionFolder;
		private EnhancedImageServer _server;

		//public event EventHandler UpdatePageList;

		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection, PageSelection pageSelection,
			LanguageSettings languageSettings,
			TemplateInsertionCommand templateInsertionCommand,
			PageListChangedEvent pageListChangedEvent,
			RelocatePageEvent relocatePageEvent,
			BookRefreshEvent bookRefreshEvent,
			DeletePageCommand deletePageCommand,
			SelectedTabChangedEvent selectedTabChangedEvent,
			SelectedTabAboutToChangeEvent selectedTabAboutToChangeEvent,
			LibraryClosing libraryClosingEvent,
			LocalizationChangedEvent localizationChangedEvent,
			CollectionSettings collectionSettings,
			SendReceiver sendReceiver,
			EnhancedImageServer server)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_languageSettings = languageSettings;
			_deletePageCommand = deletePageCommand;
			_collectionSettings = collectionSettings;
			_sendReceiver = sendReceiver;
			_server = server;

			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
			templateInsertionCommand.InsertPage += new EventHandler(OnInsertTemplatePage);

			bookRefreshEvent.Subscribe((book) => OnBookSelectionChanged(null, null));
			selectedTabChangedEvent.Subscribe(OnTabChanged);
			selectedTabAboutToChangeEvent.Subscribe(OnTabAboutToChange);
			deletePageCommand.Implementer=OnDeletePage;
			pageListChangedEvent.Subscribe(x => _view.UpdatePageList(false));
			relocatePageEvent.Subscribe(OnRelocatePage);
			libraryClosingEvent.Subscribe(o=>SaveNow());
			localizationChangedEvent.Subscribe(o =>
			{
				RefreshDisplayOfCurrentPage();
				//_view.UpdateDisplay();
				_view.UpdatePageList(false);
				_view.UpdateTemplateList();
			});
			_contentLanguages = new List<ContentLanguage>();
		}


		/// <summary>
		/// we need to guarantee that we save *before* any other tabs try to update, hence this "about to change" event
		/// </summary>
		/// <param name="details"></param>
		private void OnTabAboutToChange(TabChangedDetails details)
		{
			if (details.From == _view)
			{
				SaveNow();
				//note: if they didn't actually change anything, Chorus is not going to actually do a checkin, so this
				//won't polute the history
				_sendReceiver.CheckInNow(string.Format("Edited '{0}'", _bookSelection.CurrentSelection.TitleBestForUserDisplay));

			}
		}

		private void OnTabChanged(TabChangedDetails details)
		{
			_previouslySelectedPage = null;
			Visible = details.To == _view;
			_view.OnVisibleChanged(Visible);
		}

		private void OnBookSelectionChanged(object sender, EventArgs e)
		{
			//prevent trying to save this page in whatever comes next
			var wasNull = _domForCurrentPage == null;
			_domForCurrentPage = null;
			_currentlyDisplayedBook = null;
			if (Visible)
			{
				_view.ClearOutDisplay();
				if (!wasNull)
					_view.UpdatePageList(false);
			}
		}

		private void OnDeletePage()
		{
			try
			{
				_inProcessOfDeleting = true;
				_domForCurrentPage = null; //prevent us trying to save it later, as the page selection changes
				_currentlyDisplayedBook.DeletePage(_pageSelection.CurrentSelection);
				_view.UpdatePageList(false);
				Logger.WriteEvent("Delete Page");
				Analytics.Track("Delete Page");
			}
			catch (Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error,
																 "Could not delete that page. Try quiting Bloom, run it again, and then attempt to delete the page again. And please click 'details' below and report this to us.");
			}
			finally
			{
				_inProcessOfDeleting = false;
			}
		}

		private void OnRelocatePage(RelocatePageInfo info)
		{
			info.Cancel = !_bookSelection.CurrentSelection.RelocatePage(info.Page, info.IndexOfPageAfterMove);
			if(!info.Cancel)
			{
				Analytics.Track("Relocate Page");
				Logger.WriteEvent("Relocate Page");
			}
		}

		private void OnInsertTemplatePage(object sender, EventArgs e)
		{
			_bookSelection.CurrentSelection.InsertPageAfter(DeterminePageWhichWouldPrecedeNextInsertion(), sender as Page);
			_view.UpdatePageList(false);
			//_pageSelection.SelectPage(newPage);
			try
			{
				Analytics.Track("Insert Template Page", new Dictionary<string, string>
					{
						{ "template-source", (sender as Page).Book.Title},
						{ "page", (sender as Page).Caption}
					});
			}
			catch (Exception)
			{
			}
			Logger.WriteEvent("InsertTemplatePage");
		}


		public bool HaveCurrentEditableBook
		{
			get { return _bookSelection.CurrentSelection != null; }
		}

		public Book.Book CurrentBook
		{
			get { return _bookSelection.CurrentSelection; }
		}

		public bool ShowTranslationPanel
		{
			get
			{
				return _bookSelection.CurrentSelection.HasSourceTranslations;
			}
		}

		public bool ShowTemplatePanel
		{
			get
			{
//                if (_librarySettings.IsSourceCollection)
//                {
//                    return true;
//                }
//                else
//                {

					return _bookSelection.CurrentSelection.UseSourceForTemplatePages;
//                }
			}
		}

		public bool CanDeletePage
		{
			get
			{
				return _pageSelection != null && _pageSelection.CurrentSelection != null &&
					   !_pageSelection.CurrentSelection.Required && _currentlyDisplayedBook!=null
					   && !_currentlyDisplayedBook.LockedDown;//this clause won't work when we start allowing custom front/backmatter pages
			}

		}

		/// <summary>
		/// These are the languages available for selecting for bilingual and trilingual
		/// </summary>
		public IEnumerable<ContentLanguage> ContentLanguages
		{
			get
			{
				//_contentLanguages.Clear();		CAREFUL... the tags in the dropdown are ContentLanguage's, so changing them breaks that binding
				if (_contentLanguages.Count() == 0)
				{
					_contentLanguages.Add(new ContentLanguage(_collectionSettings.Language1Iso639Code,
															  _collectionSettings.GetLanguage1Name("en"))
											{Locked = true, Selected = true});

					//NB: these won't *alway* be tied to teh national and regional languages, but they are for now. We would need more UI, without making for extra complexity
					var item2 = new ContentLanguage(_collectionSettings.Language2Iso639Code,
													_collectionSettings.GetLanguage2Name("en"))
									{
//					            		Selected =
//					            			_bookSelection.CurrentSelection.MultilingualContentLanguage2 ==
//					            			_librarySettings.Language2Iso639Code
									};
					_contentLanguages.Add(item2);
					if (!String.IsNullOrEmpty(_collectionSettings.Language3Iso639Code))
					{
						//NB: this could be the 2nd language (when the national 1 language is not selected)
//						bool selected = _bookSelection.CurrentSelection.MultilingualContentLanguage2 ==
//						                _librarySettings.Language3Iso639Code ||
//						                _bookSelection.CurrentSelection.MultilingualContentLanguage3 ==
//						                _librarySettings.Language3Iso639Code;
						var item3 = new ContentLanguage(_collectionSettings.Language3Iso639Code,
														_collectionSettings.GetLanguage3Name("en"));// {Selected = selected};
						_contentLanguages.Add(item3);
					}
				}
				//update the selections
				_contentLanguages.Where(l => l.Iso639Code == _collectionSettings.Language2Iso639Code).First().Selected =
					_bookSelection.CurrentSelection.MultilingualContentLanguage2 ==_collectionSettings.Language2Iso639Code;


				var contentLanguageMatchingNatLan2 =
					_contentLanguages.Where(l => l.Iso639Code == _collectionSettings.Language3Iso639Code).FirstOrDefault();

				if(contentLanguageMatchingNatLan2!=null)
				{
					contentLanguageMatchingNatLan2.Selected =
					_bookSelection.CurrentSelection.MultilingualContentLanguage2 ==_collectionSettings.Language3Iso639Code
					|| _bookSelection.CurrentSelection.MultilingualContentLanguage3 == _collectionSettings.Language3Iso639Code;
				}


				return _contentLanguages;
			}
		}

		public IEnumerable<Layout> GetLayoutChoices()
		{
			foreach(var layout in CurrentBook.GetLayoutChoices())
			{
				yield return layout;
			}
		}

		public void SetLayout(Layout layout)
		{
			SaveNow();
			CurrentBook.SetLayout(layout);
			CurrentBook.PrepareForEditing();
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);

			_view.UpdatePageList(true);//counting on this to redo the thumbnails
		}

		/// <summary>
		/// user has selected or de-selected a content language
		/// </summary>
		public void ContentLanguagesSelectionChanged()
		{
			Logger.WriteEvent("Changing Content Languages");
			string l2 = null;
			string l3 = null;
			foreach (var language in _contentLanguages)
			{
				if (language.Locked)
					continue; //that's the vernacular
				if(language.Selected && l2==null)
					l2 = language.Iso639Code;
				else if(language.Selected)
				{
					l3 = language.Iso639Code;
					break;
				}
			}

			//Reload to display these changes
			SaveNow();
			CurrentBook.SetMultilingualContentLanguages(l2, l3);
			CurrentBook.PrepareForEditing();
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
			_view.UpdatePageList(true);//counting on this to redo the thumbnails

			Logger.WriteEvent("ChangingContentLanguages");
			Analytics.Track("Change Content Languages");
		}

		public int NumberOfDisplayedLanguages
		{
			get { return ContentLanguages.Where(l => l.Selected).Count(); }
		}

		public bool CanEditCopyrightAndLicense
		{
			get { return CurrentBook.CanChangeLicense; }

		}

		public class ContentLanguage
		{
			public readonly string Iso639Code;
			public readonly string Name;

			public ContentLanguage(string iso639Code, string name)
			{
				Iso639Code = iso639Code;
				Name = name;
			}
			public override string ToString()
			{
				return Name;
			}

			public bool Selected;
			public bool Locked;
		}

		public bool GetBookHasChanged()
		{
			return _currentlyDisplayedBook != CurrentBook;
		}

		public void ViewVisibleNowDoSlowStuff()
		{
			if(_currentlyDisplayedBook != CurrentBook)
			{
				CurrentBook.PrepareForEditing();
			}

			_currentlyDisplayedBook = CurrentBook;

			var errors = _bookSelection.CurrentSelection.GetErrorsIfNotCheckedBefore();
			if (!string.IsNullOrEmpty(errors))
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(errors);
				return;
			}
			var page = _bookSelection.CurrentSelection.FirstPage;
			if (page != null)
				_pageSelection.SelectPage(page);

			if (_view != null)
			{
				if(ShowTemplatePanel)
				{
					_view.UpdateTemplateList();
				}
				_view.UpdatePageList(false);
			}
		}

		void OnPageSelectionChanged(object sender, EventArgs e)
		{
			Logger.WriteMinorEvent("changing page selection");
			Analytics.Track("Select Page");//not "edit page" because at the moment we don't have the capability of detecting that.

			if (_view != null)
			{
				if (_previouslySelectedPage != null && _domForCurrentPage != null)
				{
					if(!_inProcessOfDeleting)//this is a mess.. before if you did a delete and quickly selected another page, events transpired such that you're now trying to save a deleted page
						SaveNow();
					_view.UpdateThumbnailAsync(_previouslySelectedPage);
				}
				_previouslySelectedPage = _pageSelection.CurrentSelection;
				_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
				_deletePageCommand.Enabled = !_pageSelection.CurrentSelection.Required;
			}

			GC.Collect();//i put this in while looking for memory leaks, feel free to remove it.
		}

		public void RefreshDisplayOfCurrentPage()
		{
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
		}

		public void SetupServerWithCurrentPageIframeContents()
		{
			_domForCurrentPage = _bookSelection.CurrentSelection.GetEditableHtmlDomForPage(_pageSelection.CurrentSelection);
			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(_domForCurrentPage.RawDom);
			_server.CurrentPageContent = TempFileUtils.CreateHtml5StringFromXml(_domForCurrentPage.RawDom);
			_server.AccordionContent = MakeAccordionContent();
		}

		/// <summary>
		/// Return the DOM that represents the content of the current page.
		/// Note that this is typically not the top-level thing displayed by the browser; rather, it is embedded in an
		/// iframe.
		/// </summary>
		/// <returns></returns>
		public HtmlDom GetXmlDocumentForCurrentPage()
		{
			return _domForCurrentPage;
		}

		/// <summary>
		/// Return the top-level document that should be displayed in the browser for the current page.
		/// Enhance JohnT: Since EditViewFrame.htm does not change, it should be possible to modify
		/// the caller so that it just loads that file directly, rather than making a temp file out of
		/// the DOM we make out of the file. However, we probably soon want to make the accordion optional,
		/// at which point we may just return _domForCurrentPage when it is turned off, or more likely,
		/// return a modified DOM which hides it.
		/// </summary>
		/// <returns></returns>
		public HtmlDom GetXmlDocumentForEditScreenWebPage()
		{
			var path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI/bookEdit", "EditViewFrame.htm");
			var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(path));

			// only show the accordion when the template enables it
			var css_class = dom.Body.GetAttributeNode("class");

			if (css_class == null)
			{
				css_class = dom.Body.OwnerDocument.CreateAttribute("class");
				dom.Body.Attributes.Append(css_class);
			}

			if (_currentlyDisplayedBook.BookInfo.ReaderToolsAvailable)
				css_class.Value = "accordion";
			else
				css_class.Value = "no-accordion";

			return dom;
		}

		/// <summary>
		/// View calls this once the main document has completed loading
		/// </summary>
		internal void DocumentCompleted()
		{
			// listen for events raised by javascript
			_view.AddMessageEventListener("loadReaderToolSettingsEvent", LoadReaderToolSettings);
			_view.AddMessageEventListener("saveDecodableLevelSettingsEvent", SaveDecodableLevelSettings);
			_view.AddMessageEventListener("saveAccordionSettingsEvent", SaveAccordionSettings);
			_view.AddMessageEventListener("openTextsFolderEvent", OpenTextsFolder);
			_view.AddMessageEventListener("getTextsListEvent", GetTextsList);
			_view.AddMessageEventListener("getSampleFileContentsEvent", GetSampleFileContents);
			_view.AddMessageEventListener("setModalStateEvent", SetModalState);

			var tools = _currentlyDisplayedBook.BookInfo.Tools.Where(t => t.Enabled == true);
			var settings = new Dictionary<string, object>();

			settings.Add("showPE", tools.Any(t => t.Name == "pageElements").ToInt());
			settings.Add("showDRT", tools.Any(t => t.Name == "decodableReader").ToInt());
			settings.Add("showLRT", tools.Any(t => t.Name == "leveledReader").ToInt());
			settings.Add("current", AccordionToolNameToDirectoryName(_currentlyDisplayedBook.BookInfo.CurrentTool));

			var decodableTool = tools.FirstOrDefault(t => t.Name == "decodableReader");
			if (decodableTool != null && !string.IsNullOrEmpty(decodableTool.State))
				settings.Add("decodableState", decodableTool.State);
			var leveledTool = tools.FirstOrDefault(t => t.Name == "leveledReader");
			if (leveledTool != null && !string.IsNullOrEmpty(leveledTool.State))
				settings.Add("leveledState", leveledTool.State);

			var settingsStr = CleanUpJsonDataForJavascript(Newtonsoft.Json.JsonConvert.SerializeObject(settings));

			_view.RunJavaScript("if (calledByCSharp) { calledByCSharp.restoreAccordionSettings(\"" + settingsStr + "\"); }");
		}

		/// <summary>Gets reader tool settings from DecodableLevelData.json and send to javascript</summary>
		/// <param name="arg">Not Used, but required because it is being called by a javascrip MessageEvent</param>
		private void LoadReaderToolSettings(string arg)
		{
			// get saved reader settings
			var path = _collectionSettings.DecodableLevelPathName;
			var decodableLeveledSettings = "";
			if (File.Exists(path))
				decodableLeveledSettings = File.ReadAllText(path, Encoding.UTF8);

			var input = CleanUpJsonDataForJavascript(decodableLeveledSettings);

			var bookFontName = _currentlyDisplayedBook.CollectionSettings.DefaultLanguage1FontName;
			if (bookFontName.Length > 0)
				bookFontName = CleanUpDataForJavascript("\"" + bookFontName + "\", sans-serif");
			else
				bookFontName = "sans-serif";

			_view.RunJavaScript("if (calledByCSharp) { calledByCSharp.loadReaderToolSettings(\"" + input + "\", \"" + bookFontName + "\"); }");
		}

		private void SaveAccordionSettings(string data)
		{
			var args = data.Split(new[] { '\t' });

			switch (args[0])
			{
				case "showPE":
					UpdateActiveToolSetting("pageElements", args[1] == "1");
					return;

				case "showDRT":
					UpdateActiveToolSetting("decodableReader", args[1] == "1");
					return;

				case "showLRT":
					UpdateActiveToolSetting("leveledReader", args[1] == "1");
					return;

				case "current":
					_currentlyDisplayedBook.BookInfo.CurrentTool = AccordionDirectoryNameToToolName(args[1]);
					return;

				case "state":
					UpdateToolState(args[1], args[2]);
					return;
			}
		}

		private void UpdateToolState(string toolName, string state)
		{
			var tools = _currentlyDisplayedBook.BookInfo.Tools;
			var item = tools.FirstOrDefault(t => t.Name == toolName);

			if (item != null)
				item.State = state;
		}

		private void UpdateActiveToolSetting(string toolName, bool enabled)
		{
			var tools = _currentlyDisplayedBook.BookInfo.Tools;
			var item = tools.FirstOrDefault(t => t.Name == toolName);

			if (item == null)
				tools.Add(new AccordionTool() { Name = toolName, Enabled = enabled });
			else
				item.Enabled = enabled;
		}

		private static string AccordionToolNameToDirectoryName(string toolName)
		{
			switch (toolName)
			{
				case "pageElements":
					return "PageElements";

				case "decodableReader":
					return "DecodableRT";

				case "leveledReader":
					return "LeveledRT";
			}

			return string.Empty;
		}

		private static string AccordionDirectoryNameToToolName(string directoryName)
		{
			switch (directoryName)
			{
				case "PageElements":
					return "pageElements";

				case "DecodableRT":
					return "decodableReader";

				case "LeveledRT":
					return "leveledReader";
			}

			return string.Empty;
		}

		private void SetModalState(string isModal)
		{
			_view.SetModalState(isModal == "true");
		}

		private static string CleanUpDataForJavascript(string data)
		{
			// We need to escape backslashes and quotes so the whole content arrives intact.
			// Backslash first so the ones we insert for quotes don't get further escaped.
			// Since the input is going to be processed as a string literal in JavaScript, it also can't contain real newlines.
			return data.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
		}

		/// <summary>
		/// Remove line ends, otherwise javascript chokes during JSON.parse().
		/// Checks for both Windows and Unix line ends.
		/// </summary>
		/// <param name="jsonData"></param>
		/// <returns></returns>
		private static string CleanUpJsonDataForJavascript(string jsonData)
		{
			jsonData = jsonData.Replace("\r", "").Replace("\n", "");
			return CleanUpDataForJavascript(jsonData);
		}

		/// <summary>Receives data from javascript, saves it, then closes the dialog</summary>
		/// <param name="content"></param>
		private void SaveDecodableLevelSettings(string content)
		{
			var path = _collectionSettings.DecodableLevelPathName;
			File.WriteAllText(path, content, Encoding.UTF8);

			_view.RunJavaScript("if (typeof(closeSetupDialog) === \"function\") {closeSetupDialog();}");
		}

		/// <summary>Opens Explorer (or Linux equivalent) displaying the contents of the Sample Texts directory</summary>
		/// <param name="arg">Not Used, but required because it is being called by a javascrip MessageEvent</param>
		private void OpenTextsFolder(string arg)
		{
			if (_collectionSettings.SettingsFilePath == null) return;
			var path = Path.Combine(Path.GetDirectoryName(_collectionSettings.SettingsFilePath), "Sample Texts");
			if (!Directory.Exists(path)) Directory.CreateDirectory(path);
			Process.Start(path);
		}

		/// <summary>Gets a list of the files in the Sample Texts folder</summary>
		/// <param name="arg">Not Used, but required because it is being called by a javascrip MessageEvent</param>
		private void GetTextsList(string arg)
		{
			var path = Path.Combine(Path.GetDirectoryName(_collectionSettings.SettingsFilePath), "Sample Texts");
			var fileList = "";

			if (Directory.Exists(path)) {
				foreach (var file in Directory.GetFiles(path))
				{
					if (fileList.Length == 0) fileList = Path.GetFileName(file);
					else fileList += "\\r" + Path.GetFileName(file);
				}
			}

			_view.RunJavaScript("if (calledByCSharp) { calledByCSharp.setSampleTextsList(\"" + fileList + "\"); }");
		}

		/// <summary>Gets the contents of a Sample Text file</summary>
		/// <param name="fileName"></param>
		private void GetSampleFileContents(string fileName)
		{
			var path = Path.Combine(Path.GetDirectoryName(_collectionSettings.SettingsFilePath), "Sample Texts");
			path = Path.Combine(path, fileName);

			// first try utf-8/ascii encoding (the .Net default)
			var text = File.ReadAllText(path);

			// If the "unknown" character (65533) is present, C# did not sucessfully decode the file. Try the system default encoding and codepage.
			if (text.Contains((char)65533))
				text = File.ReadAllText(path, Encoding.Default);

			text = CleanUpDataForJavascript(text);

			_view.RunJavaScript("if (calledByCSharp) { calledByCSharp.setSampleFileContents(\"" + text + "\"); }");
		}

		private string MakeAccordionContent()
		{
			var path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI/bookEdit/accordion", "Accordion.htm");
			_accordionFolder = Path.GetDirectoryName(path);

			var domForAccordion = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(path));

			// Load settings into the accordion panel
			AppendAccordionSettingsPanel(domForAccordion);
			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(domForAccordion.RawDom);
			return TempFileUtils.CreateHtml5StringFromXml(domForAccordion.RawDom);
		}

		/// <summary>Loads the initial panel into the accordion</summary>
		private void AppendAccordionSettingsPanel(HtmlDom domForAccordion)
		{
			var accordion = domForAccordion.Body.SelectSingleNode("//div[@id='accordion']");
			var subFolder = Path.Combine(_accordionFolder, "settings");
			var filePath = FileLocator.GetFileDistributedWithApplication(subFolder, "Settings.htm");
			var subPanelDom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(filePath));
			AppendAllChildren(subPanelDom.Body, accordion);
		}

		void AppendAllChildren(XmlNode source, XmlNode dest)
		{
			// Not sure, but the ToArray MIGHT be needed because AppendChild MIGHT remove the node from the source
			// which MIGHT interfere with iterating over them.
			foreach (var node in source.ChildNodes.Cast<XmlNode>().ToArray())
			{
				// It's nice if the independent HMTL file we are copying can have its own title, but we don't want to duplicate that into
				// our page document, which already has its own.
				if (node.Name == "title")
					continue;
				// It's no good copying file references; they may be useful for independent testing of the control source,
				// but the relative paths won't work. Any needed scripts must be re-included.
				if (node.Name == "script" && node.Attributes != null && node.Attributes["src"] != null)
					continue;
				if (node.Name == "link" && node.Attributes != null && node.Attributes["rel"] != null)
					continue; // likewise stylesheets must be inserted
				dest.AppendChild(dest.OwnerDocument.ImportNode(node,true));
			}
		}

		public void SaveNow()
		{
			if (_domForCurrentPage != null)
			{
				_view.CleanHtmlAndCopyToPageDom();
				_bookSelection.CurrentSelection.SavePage(_domForCurrentPage);
			}
		}

		public void ChangePicture(GeckoHtmlElement img, PalasoImage imageInfo, IProgress progress)
		{
			try
			{
				Logger.WriteMinorEvent("Starting ChangePicture {0}...", imageInfo.FileName);
				var editor = new PageEditingModel();
				editor.ChangePicture(_bookSelection.CurrentSelection.FolderPath, img, imageInfo, progress);

				//we have to save so that when asked by the thumbnailer, the book will give the proper image
				SaveNow();
				//but then, we need the non-cleaned version back there
				_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);

				_view.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
				Logger.WriteMinorEvent("Finished ChangePicture {0} (except for async thumbnail) ...", imageInfo.FileName);
				Analytics.Track("Change Picture");
				Logger.WriteEvent("ChangePicture {0}...", imageInfo.FileName);

			}
			catch (Exception e)
			{
				ErrorReport.NotifyUserOfProblem(e, "Could not change the picture");
			}
		}

//        private void InvokeUpdatePageList()
//        {
//            if (UpdatePageList != null)
//            {
//                UpdatePageList(this, null);
//            }
//        }

		public void SetView(EditingView view)
		{
			_view = view;
		}


		public IPage DeterminePageWhichWouldPrecedeNextInsertion()
		{
			if (_view != null)
			{
				var pagesStartingWithCurrentSelection =
					_bookSelection.CurrentSelection.GetPages().SkipWhile(p => p.Id != _pageSelection.CurrentSelection.Id);
				var candidates = pagesStartingWithCurrentSelection.ToArray();
				for (int i = 0; i < candidates.Length - 1; i++)
				{
					if (!candidates[i + 1].Required)
					{
						return candidates[i];
					}
				}
				var pages = _bookSelection.CurrentSelection.GetPages();
				// ReSharper disable PossibleMultipleEnumeration
				if (!pages.Any())
				{
					var exception = new ApplicationException(
						string.Format(
							@"_bookSelection.CurrentSelection.GetPages() gave no pages (BL-262 repro).
									  Book is '{0}'\r\nErrors known to book=[{1}]\r\n{2}\r\n{3}",
							_bookSelection.CurrentSelection.TitleBestForUserDisplay,
							_bookSelection.CurrentSelection.CheckForErrors(),
							_bookSelection.CurrentSelection.RawDom.OuterXml,
							new StackTrace().ToString()));

					ErrorReport.NotifyUserOfProblem(exception,
													"There was a problem looking through the pages of this book. If you can send emails, please click 'details' and send this report to the developers.");
					return null;
				}
				IPage lastGuyWHoCanHaveAnInsertionAfterHim = pages.Last(p => !p.IsBackMatter);
				// ReSharper restore PossibleMultipleEnumeration
				return lastGuyWHoCanHaveAnInsertionAfterHim;
			}
			return null;
		}

		public bool CanChangeImages()
		{
			return _currentlyDisplayedBook.CanChangeImages;
		}


		public Layout GetCurrentLayout()
		{
			return CurrentBook.GetLayout();
		}

#if TooExpensive
		public void BrowserFocusChanged()
		{
			//review: will this be too slow on some machines? It's just a luxury to update the thumbnail even when you tab to a different field
			SaveNow();
			_view.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
		}
#endif

		public void CopyImageMetadataToWholeBook(Metadata metadata)
		{
			using (var dlg = new ProgressDialogForeground())//REVIEW: this foreground dialog has known problems in other contexts... it was used here because of its ability to handle exceptions well. TODO: make the background one handle exceptions well
			{
				dlg.ShowAndDoWork(progress => CurrentBook.CopyImageMetadataToWholeBookAndSave(metadata, progress));
			}
		}

		public string GetFontAvailabilityMessage()
		{
			var name = _collectionSettings.DefaultLanguage1FontName.ToLower();

			if(null == FontFamily.Families.FirstOrDefault(f => f.Name.ToLower() == name))
			{
				var s = L10NSharp.LocalizationManager.GetString("EditTab.FontMissing",
														   "The current selected " +
														   "font is '{0}', but it is not installed on this computer. Some other font will be used.");
				return string.Format(s, _collectionSettings.DefaultLanguage1FontName);
			}
			return null;
		}

	  /*  Later I found a different explanation for why i wasn't getting the data back... the new classes were at the pag div
	   *  level, and the c# code was only looking at the innerhtml of that div when saving (still is).
	   *  /// <summary>
		/// Although browsers are happy to let you manipulate the DOM, in most cases gecko/xulrunner does not expect that we,
		/// the host process, are going to need access to those changes. For example, if we have a control that adds a class
		/// to some element based on a user choice, the user will see the choice take effect, but then when they come back to the
		/// page later, their choice will be lost. This is because that new class just isn't in the html that gets returned to us,
		/// if we do, for example, _browser.Document.GetElementsByTagName("body").outerHtml. (Other things changes *are* returned, like
		/// the new contents of an editable div).
		///
		/// Anyhow this method, triggered by javascript that knows it did something that will be lost, is here in order to work
		/// around this. The Javascript does something like:
		/// var origin = window.location.protocol + '//' + window.location.host;
		/// event.initMessageEvent ('PreserveClassAttributeOfElement', true, true, theHTML, origin, 1234, window, null);
		/// document.dispatchEvent (event);
		///
		/// The hard part here is knowing which element gets this html
		/// </summary>
		/// <param name="?"></param>
		public void PreserveHtmlOfElement(string elementHtml)
		{
			try
			{
				var editor = new PageEditingModel();

				//todo if anyone ever needs it: preserve more than just the class
				editor.PreserveClassAttributeOfElement(_pageSelection.CurrentSelection.GetDivNodeForThisPage(), elementHtml);

				//we have to save so that when asked by the thumbnailer, the book will give the proper image
  //              SaveNow();
				//but then, we need the non-cleaned version back there
//                _view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);

  //              _view.UpdateThumbnailAsync(_pageSelection.CurrentSelection);

			}
			catch (Exception e)
			{
				ErrorReport.NotifyUserOfProblem(e, "Could not PreserveClassAttributeOfElement");
			}
		}
	   */
	}

	public class TemplateInsertionCommand
	{
		public event EventHandler InsertPage;

		public void Insert(Page page)
		{
			if (InsertPage != null)
			{
				InsertPage.Invoke(page, null);
			}
		}
	}
}
