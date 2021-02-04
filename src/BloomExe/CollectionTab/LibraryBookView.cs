using System;
using System.Diagnostics;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.TeamCollection;
using Bloom.MiscUI;
using Bloom.web.controllers;
using Gecko;
using MarkdownDeep;
using SIL.IO;

namespace Bloom.CollectionTab
{
	/// <summary>
	/// this is an un-editable preview of a book in the library; either vernacular or template
	/// </summary>
	public partial class LibraryBookView : UserControl
	{
		private readonly BookSelection _bookSelection;
		//private readonly SendReceiver _sendReceiver;
		private readonly CreateFromSourceBookCommand _createFromSourceBookCommand;
		private readonly EditBookCommand _editBookCommand;
		private Shell _shell;
		private bool _reshowPending = false;
		private bool _visible;

		public delegate LibraryBookView Factory();//autofac uses this

		public LibraryBookView(BookSelection bookSelection,
			//SendReceiver sendReceiver,
			CreateFromSourceBookCommand createFromSourceBookCommand,
			EditBookCommand editBookCommand,
			SelectedTabChangedEvent selectedTabChangedEvent,
			SelectedTabAboutToChangeEvent selectedTabAboutToChangeEvent)
		{
			InitializeComponent();
			_bookSelection = bookSelection;
			//_sendReceiver = sendReceiver;
			_createFromSourceBookCommand = createFromSourceBookCommand;
			_editBookCommand = editBookCommand;
			if (!Bloom.CLI.UploadCommand.IsUploading)
				bookSelection.SelectionChanged += OnBookSelectionChanged;

			selectedTabAboutToChangeEvent.Subscribe(c =>
			{
				if (!(c.To is LibraryView))
				{
					// We're becoming invisible. Stop any work in progress to generate a preview
					// (thus allowing other browsers, like the ones in the Edit view, to navigate
					// to their destinations.)
					HidePreview();
				}
			});

			selectedTabChangedEvent.Subscribe(c =>
			{
				var wasVisible = _visible;
				_visible = c.To is LibraryView;
				if (_reshowPending || wasVisible != _visible)
				{
					ShowBook();
				}
			});

			_editBookButton.Visible = false;
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			LoadBook();
		}

		private void UpdateTitleBar()
		{
			if (this.ParentForm != null)
			{
				_shell = (Shell)this.ParentForm;
			}
			if (_shell != null)
			{
				_shell.SetWindowText((_bookSelection.CurrentSelection == null) ? null : _bookSelection.CurrentSelection.TitleBestForUserDisplay);
			}
		}

		void OnBookSelectionChanged(object sender, BookSelectionChangedEventArgs args)
		{
			try
			{
				// If we just created this book and are right about to switch to edit mode,
				// we don't need to update the preview. Not doing so prevents situations where
				// the page expires before we display it properly (somehow...BL-4856) and
				// also just saves time.
				LoadBook(!args.AboutToEdit);
				UpdateTitleBar();
			}
			catch (Exception error)
			{
				var msg = L10NSharp.LocalizationManager.GetString("Errors.ErrorSelecting",
					"There was a problem selecting the book.  Restarting Bloom may fix the problem.  If not, please click the 'Details' button and report the problem to the Bloom Developers.");
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, msg);
			}
		}

		private void LoadBook(bool updatePreview = true)
		{
			_addToCollectionButton.Visible =  _addToCollectionButton.Enabled = _bookSelection.CurrentSelection != null;
			_editBookButton.Visible = TeamCollectionApi.TheOneInstance.CanEditBook();
			ShowBook(updatePreview);
			if (_bookSelection.CurrentSelection != null)
			{
				_bookSelection.CurrentSelection.ContentsChanged += new EventHandler(CurrentSelection_ContentsChanged);
			}
		}

		void CurrentSelection_ContentsChanged(object sender, EventArgs e)
		{
			if(_visible)
				ShowBook();
			else
			{
				_reshowPending = true;
			}
			UpdateTitleBar();
		}

		private void ShowBook(bool updatePreview = true)
		{
			if (_bookSelection.CurrentSelection == null || !_visible)
			{
				HidePreview();
			}
			else
			{
				Debug.WriteLine("LibraryBookView.ShowBook() currentselection ok");

				_addToCollectionButton.Visible = _bookSelection.CurrentSelection.IsShellOrTemplate && !_bookSelection.CurrentSelection.HasFatalError;
				_editBookButton.Visible = TeamCollectionApi.TheOneInstance.CanEditBook();
				_readmeBrowser.Visible = false;
				//_previewBrowser.Visible = true;
				_splitContainerForPreviewAndAboutBrowsers.Visible = true;
				if (updatePreview && !TroubleShooterDialog.SuppressBookPreview)
					_previewBrowser.Navigate(_bookSelection.CurrentSelection.GetPreviewHtmlFileForWholeBook(), source:BloomServer.SimulatedPageFileSource.Preview);
				_splitContainerForPreviewAndAboutBrowsers.Panel2Collapsed = true;
				if (_bookSelection.CurrentSelection.HasAboutBookInformationToShow)
				{
					if (RobustFile.Exists(_bookSelection.CurrentSelection.AboutBookHtmlPath))
					{
						_splitContainerForPreviewAndAboutBrowsers.Panel2Collapsed = false;
						_readmeBrowser.Navigate(_bookSelection.CurrentSelection.AboutBookHtmlPath, false);
						_readmeBrowser.Visible = true;
					}
					else if (RobustFile.Exists(_bookSelection.CurrentSelection.AboutBookMdPath))
					{
						_splitContainerForPreviewAndAboutBrowsers.Panel2Collapsed = false;
						var md = new Markdown();
						var contents = RobustFile.ReadAllText(_bookSelection.CurrentSelection.AboutBookMdPath);
						_readmeBrowser.NavigateRawHtml( string.Format("<html><head><meta charset=\"utf-8\"/></head><body>{0}</body></html>", md.Transform(contents)));
						_readmeBrowser.Visible = true;
					}
				}
				_reshowPending = false;
			}
		}

		private void HidePreview()
		{
			// When the user clicks the Edit button, the preview browser may still be busy loading images and otherwise
			// working on the display of a long book. If we allow it to continue, our NavigationIsolator will
			// not allow the newly visible browser in the EditView (or the other useful one in the WebThumbNailList)
			// to start navigating to their pages. Thus, painting of the two current views is held up until we've
			// generated not only the previously-visible part of the preview, but all the rest of it as well.
			// So, we abort whatever is going on in the preview browser by pointing it at an empty page.
			_previewBrowser.Navigate("about:blank", false);
			//_previewBrowser.Visible = false;
			_splitContainerForPreviewAndAboutBrowsers.Visible = false;
			BackColor = Palette.GeneralBackground; // NB: this color is only seen in a flash before browser loads
		}

		private void OnAddToLibraryClick(object sender, EventArgs e)
		{
			if (_bookSelection.CurrentSelection != null)
			{
				try
				{
					//nb: don't move this to after the raise command, as the selection changes
					var checkinNotice = string.Format("Created book from '{0}'", _bookSelection.CurrentSelection.TitleBestForUserDisplay);

					_createFromSourceBookCommand.Raise(_bookSelection.CurrentSelection);
					//_sendReceiver.CheckInNow(checkinNotice);
				}
				catch(Exception error)
				{
					SIL.Reporting.ErrorReport.NotifyUserOfProblem(error,"Bloom could not add that book to the library.");
				}
			}
		}

		private void _addToLibraryButton_MouseEnter(object sender, EventArgs e)
		{
//            _addToLibraryButton.Text = string.Format("Add this book to {0}", _librarySettings.VernacularCollectionNamePhrase);
//            _addToLibraryButton.Width = 250;
		}

		private void _addToLibraryButton_MouseLeave(object sender, EventArgs e)
		{
//            _addToLibraryButton.Text="";
//            _addToLibraryButton.Width = 50;
		}

		private void _editBookButton_Click(object sender, EventArgs e)
		{
			_editBookCommand.Raise(_bookSelection.CurrentSelection);
		}

		private void LibraryBookView_Resize(object sender, EventArgs e)
		{

		}

		private void _readmeBrowser_OnBrowserClick(object sender, EventArgs e)
		{
			var ge = e as DomEventArgs;
			var target = (GeckoHtmlElement)ge.Target.CastToGeckoElement();
			var anchor = target as Gecko.DOM.GeckoAnchorElement;
			if (GetAnchorHref(e) != "" && GetAnchorHref(e) != "#")
			{
				_readmeBrowser.HandleLinkClick(anchor, ge, _bookSelection.CurrentSelection.FolderPath);
			}
		}

		/// <summary>
		/// Support the "Report a Problem" button when it shows up in the preview window as part of
		/// a page reporting that we can't open the book for some reason.
		/// </summary>
		private void _previewBrowser_OnBrowserClick(object sender, EventArgs e)
		{
			if (GetAnchorHref(e).EndsWith("ReportProblem"))
			{
				ProblemReportApi.ShowProblemDialog(_previewBrowser, null, "", "nonfatal");
			}
		}

		private static string GetAnchorHref(EventArgs e)
		{
			var element = (GeckoHtmlElement) (e as DomEventArgs).Target.CastToGeckoElement();
			//nb: it might not be an actual anchor; could be an input-button that we've stuck href on
			return element == null ? "" :
					element.GetAttribute("href") ?? "";
		}

		private void _splitContainerForPreviewAndAboutBrowsers_SplitterMoved(object sender, SplitterEventArgs e)
		{
			_readmeBrowser.Refresh();
		}
	}
}
