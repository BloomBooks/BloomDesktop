using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.MiscUI;
//using Bloom.SendReceive;
using Bloom.Api;
using Gecko;
using Gecko.DOM;

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
			NavigationIsolator isolator)
		{
			InitializeComponent();
			_previewBrowser.Isolator = isolator;
			_readmeBrowser.Isolator = isolator;
			_bookSelection = bookSelection;
			//_sendReceiver = sendReceiver;
			_createFromSourceBookCommand = createFromSourceBookCommand;
			_editBookCommand = editBookCommand;
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);

			selectedTabChangedEvent.Subscribe(c =>
												{
													_visible = c.To is LibraryView;
													if(_reshowPending)
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

		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			try
			{
				LoadBook();
				UpdateTitleBar();
			}
			catch (Exception error)
			{
				var msg = L10NSharp.LocalizationManager.GetString("Errors.ErrorSelecting",
					"There was a problem selecting the book.  Restarting Bloom may fix the problem.  If not, please click the 'Details' button and report the problem to the Bloom Developers.");
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, msg);
			}
		}

		private void LoadBook()
		{
			_editBookButton.Visible = _addToCollectionButton.Visible =  _addToCollectionButton.Enabled = _bookSelection.CurrentSelection != null;
			ShowBook();
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

		private void ShowBook()
		{
			if (_bookSelection.CurrentSelection == null)
			{
				Debug.WriteLine("LibraryBookView.ShowBook() currentselection is null");
				_previewBrowser.Navigate("about:blank", false);
				//_previewBrowser.Visible = false;
				_splitContainerForPreviewAndAboutBrowsers.Visible = false;
				BackColor = Color.FromArgb(64,64,64);
			}
			else
			{
				Debug.WriteLine("LibraryBookView.ShowBook() currentselection ok");

				_addToCollectionButton.Visible = _bookSelection.CurrentSelection.IsShellOrTemplate && !_bookSelection.CurrentSelection.HasFatalError;
				_editBookButton.Visible = _bookSelection.CurrentSelection.IsEditable && !_bookSelection.CurrentSelection.HasFatalError;
				_readmeBrowser.Visible = false;
				//_previewBrowser.Visible = true;
				_splitContainerForPreviewAndAboutBrowsers.Visible = true;
				_previewBrowser.Navigate(_bookSelection.CurrentSelection.GetPreviewHtmlFileForWholeBook());
				_splitContainerForPreviewAndAboutBrowsers.Panel2Collapsed = true;
				if (_bookSelection.CurrentSelection.HasAboutBookInformationToShow)
				{
					_splitContainerForPreviewAndAboutBrowsers.Panel2Collapsed = false;
					_readmeBrowser.NavigateRawHtml(_bookSelection.CurrentSelection.GetAboutBookHtml);
					_readmeBrowser.Visible = true;
				}
				_reshowPending = false;
			}
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
				using (var dlg = new ProblemReporterDialog(null,_bookSelection))
				{
					dlg.SetDefaultIncludeBookSetting(true);
					dlg.Description =
						"This book had a problem. Please tell us anything that might be helpful in diagnosing the problem here:" +
						Environment.NewLine;
                   
					try
					{
						dlg.Description += Environment.NewLine + Environment.NewLine + Environment.NewLine;
						if(_bookSelection.CurrentSelection.Storage != null)
						{
							dlg.Description += _bookSelection.CurrentSelection.Storage.ErrorMessagesHtml;
						}
					}
					catch (Exception)
					{
						//no use chasing errors generated getting error info
					}
					dlg.ShowInTaskbar = true;
                    dlg.ShowDialog();
				}
			}
		}

		private static string GetAnchorHref(EventArgs e)
		{
			var element = (GeckoHtmlElement) (e as DomEventArgs).Target.CastToGeckoElement();
			//nb: it might not be an actual anchor; could be an input-button that we've stuck href on
			return element == null ? "" : 
					element.GetAttribute("href") ?? "";
		}
	}
}
