using System;
using System.Windows.Forms;
using Bloom.Properties;
using Bloom.Workspace;
using L10NSharp;
using SIL.Reporting;
using System.Drawing;
using System.IO;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using Bloom.web;
using SIL.Windows.Forms.SettingProtection;
using System.Diagnostics;

namespace Bloom.CollectionTab
{
	public partial class CollectionTabView : UserControl, IBloomTabArea
	{
		private readonly CollectionModel _model;
		private WorkspaceTabSelection _tabSelection;
		private BookSelection _bookSelection;
		private BloomWebSocketServer _webSocketServer;
		private TeamCollectionManager _tcManager;
		private bool _bookChangesPending = false; // bookchanged event while tab not visible

		public delegate CollectionTabView Factory();//autofac uses this

		public CollectionTabView(CollectionModel model,
			SelectedTabChangedEvent selectedTabChangedEvent,
			TeamCollectionManager tcManager, BookSelection bookSelection,
			WorkspaceTabSelection tabSelection, BloomWebSocketServer webSocketServer, LocalizationChangedEvent localizationChangedEvent)
		{
			_model = model;
			_tabSelection = tabSelection;
			_bookSelection = bookSelection;
			_webSocketServer = webSocketServer;
			_tcManager = tcManager;

			BookCollection.CollectionCreated += OnBookCollectionCreated;

			InitializeComponent();
			_reactControl.SetLocalizationChangedEvent(localizationChangedEvent); // after InitializeComponent, which creates it.
			BackColor = _reactControl.BackColor = Palette.GeneralBackground;
			_toolStrip.Renderer = new NoBorderToolStripRenderer();
			_toolStripLeft.Renderer = new NoBorderToolStripRenderer();

			// When going down to Shrink Stage 3 (see WorkspaceView), we want the right-side toolstrip to take precedence
			// (Settings, Other Collection).
			// This essentially makes the TC Status button's zIndex less than the buttons on the right side.
			_toolStripLeft.SendToBack();

			//TODO splitContainer1.SplitterDistance = _collectionListView.PreferredWidth;

			if (SIL.PlatformUtilities.Platform.IsMono)
			{
				BackgroundColorsForLinux();
			}

			selectedTabChangedEvent.Subscribe(c =>
			{
				if (c.To == this)
				{
					Logger.WriteEvent("Entered Collections Tab");
					if (_bookChangesPending && _bookSelection.CurrentSelection != null)
						UpdateForBookChanges(_bookSelection.CurrentSelection);
				}
			});
			SetTeamCollectionStatus(tcManager);
			TeamCollectionManager.TeamCollectionStatusChanged += (sender, args) =>
			{
				if (IsHandleCreated && !IsDisposed)
				{
					SafeInvoke.InvokeIfPossible("update TC status", this, false,
						() => SetTeamCollectionStatus(tcManager));
				}
			};
			_tcStatusButton.Click += (sender, args) =>
			{
				// Reinstate this to see messages from before we started up.
				// We think it might be too expensive to show a list as long as this might get.
				// Instead, in the short term we may add a button to show the file.
				// Later we may implement some efficient way to scroll through them.
				// tcManager.CurrentCollection?.MessageLog?.LoadSavedMessages();

				dynamic messageBundle = new DynamicJson();
				messageBundle.showReloadButton = tcManager.MessageLog.ShouldShowReloadButton;
				_webSocketServer.LaunchDialog("TeamCollectionDialog", messageBundle);
				tcManager.CurrentCollectionEvenIfDisconnected?.MessageLog.WriteMilestone(MessageAndMilestoneType.LogDisplayed);
			};

			// We don't want this control initializing until team collections sync (if any) is done.
			// That could change, but for now we're not trying to handle async changes arriving from
			// the TC to the local collection, and as part of that, the collection tab doesn't expect
			// the local collection to change because of TC stuff once it starts loading.
			Controls.Remove(_reactControl);
			bookSelection.SelectionChanged += (sender, e) => BookSelectionChanged(bookSelection.CurrentSelection);
		}

		private bool _minimized;

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			// To correct a weird SplitPane behavior in CollectionsTabPane, we need
			// a notification when our window changes state from minimized to something else.
			bool minimized = ParentForm?.WindowState == FormWindowState.Minimized;
			if (!minimized && _minimized)
			{
				_webSocketServer.SendEvent("window","restored");
			}

			_minimized = minimized;
		}

		private void BookSelectionChanged(Book.Book book)
		{
			if (book == null)
				return;
			if (book.IsEditable)
				_model.UpdateThumbnailAsync(book);
			book.ContentsChanged += (sender, args) =>
			{
				if (_tabSelection.ActiveTab == WorkspaceTab.collection)
				{
					UpdateForBookChanges(book);
				}
				else
				{
					_bookChangesPending = true;
				}
			};
		}

		private void UpdateForBookChanges(Book.Book book)
		{
			_model.UpdateThumbnailAsync(book);
			_model.UpdateLabelOfBookInEditableCollection(book);
			// This message causes the preview to update.
			_webSocketServer.SendEvent("bookContent", "reload");
			_bookChangesPending = false;
		}

		private void OnBookCollectionCreated(object collection, EventArgs args)
		{
			var c = collection as BookCollection;
			if (c.ContainsDownloadedBooks)
			{
				c.FolderContentChanged += (sender, eventArgs) =>
				{
					if (IsDisposed)
					{
						Debug.Fail("FolderContentChanged handler invoked from a CollectionTabView that has already been disposed. Did the collection have cleanup such as StopWatchingDirectory() occur?");
						return;
					}
					if (_tabSelection.ActiveTab == WorkspaceTab.collection)
					{
						// We got a new or modified book in the downloaded books collection.
						// If this (collection) tab is active, we want to select it.
						// (If we're in the middle of editing or publishing some book, we
						// don't want to change that.)
						// One day we may enhance it so that we switch tabs and show it,
						// but there are states where that would be dangerous.
						var newBook = new BookInfo(eventArgs.Path, false);
						var book = _model.GetBookFromBookInfo(newBook, true);
						if (string.IsNullOrEmpty(book.Storage.ErrorMessagesHtml))
						{
							// Happy path. Usually we can make a book object out of a downloaded book folder.
							_bookSelection.SelectBook(book, false);
						}
						else
						{
							// We failed to load it. In BL-12034, this took the form of not finding an HTM
							// file in the folder. This might be because debouncing didn't work
							// and we haven't finished copying the folder to the final destination.
							// Try again in a couple of seconds.
							var t = new Timer();
							var retries = 1;
							t.Tick += (o, args1) =>
							{
								retries++;
								book = _model.GetBookFromBookInfo(newBook, true);
								if (string.IsNullOrEmpty(book.Storage.ErrorMessagesHtml))
								{
									t.Stop();
									t.Dispose();
									_bookSelection.SelectBook(book, false);
								}
								else if (retries > 3)
								{
									t.Stop();
									t.Dispose();
									// Locks Bloom up. Come back to this in 5.6 (BL-12034)
									//ErrorReport.NotifyUserOfProblem(book.Storage.ErrorMessagesHtml);

									// So instead, go ahead and select it, even though it won't get added to the list
									// of books if it has no HTML file. This at least allows the user to see the message once.
									_bookSelection.SelectBook(book, false);
								}
							};
							t.Interval = 2000;
							t.Start();
							return;
						}
					}
				};
			}
		}

		public void ReadyToShowCollections()
		{
			Invoke((Action) (()=>
			{
				// I'm not sure this is the best place to do this. The old LibraryListView had a comment:
				// "If we repair duplicates and there is a reason to toast (e.g. locked meta.json file),
				// The ongoing UI activity focuses Bloom over top of the toast after a brief flash.
				// For that reason, we add a new stage for tasks that need to happen after the UI is updated."
				// I don't fully understand that. In that view, it was done after we created the collection buttons.
				// My current inclination is that it's not a view responsibility at all.
				// However, until we get rid of the old collection tab, it's tricky to move it, so I've just
				// duplicated it here.
				// Doing it at this point seems to work fine.
				RepairDuplicates();
				Controls.Add(_reactControl);
			}));
		}

		private void RepairDuplicates()
		{
			var collectionPath = _model.TheOneEditableCollection.PathToDirectory;
			// A book's ID may not be changed if we have a TC and the book is actually in the shared folder.
			// Eventually we may allow it if the book is checked out.
			BookInfo.RepairDuplicateInstanceIds(collectionPath, (bookPath) =>
			{
				if (_tcManager?.CurrentCollection == null)
				{
					return true; // OK to change, not a TC.
				}
				// Only OK if not present in the TC repo.
				return !_tcManager.CurrentCollection.IsBookPresentInRepo(Path.GetFileName(bookPath));
			});
		}

		internal void ManageSettings(SettingsProtectionHelper settingsLauncherHelper)
		{
			//we have a couple of buttons which don't make sense for the remote (therefore vulnerable) low-end user
			settingsLauncherHelper.ManageComponent(_settingsButton);

			//NB: this isn't really a setting, but we're using that feature to simplify this menu down to what makes sense for the easily-confused user
			settingsLauncherHelper.ManageComponent(_openCreateCollectionButton);
		}

		private void BackgroundColorsForLinux()
		{

			// Set the background image for Mono because the background color does not paint,
			// and if we override the background paint handler, the default styling of the child
			// controls is changed.

			// We are getting an exception if none of the buttons are visible. The tabstrip is set
			// to Dock.Top which results in the height being zero if no buttons are visible.
			if ((_toolStrip.Height == 0) || (_toolStrip.Width == 0)) return;

			var bmp = new Bitmap(_toolStrip.Width, _toolStrip.Height);
			using (var g = Graphics.FromImage(bmp))
			{
				using (var b = new SolidBrush(_toolStrip.BackColor))
				{
					g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
				}
			}
			_toolStrip.BackgroundImage = bmp;
		}

		public string CollectionTabLabel
		{
			get { return LocalizationManager.GetString("CollectionTab.CollectionTabLabel", "Collections"); }//_model.IsSourceCollection ? "Shell Collection" : "Collection"; }

		}

		public string HelpTopicUrl
		{
			get { return "/Tasks/Collections_tab_tasks/Collections_tab_tasks_overview.htm"; }
		}

		public Control TopBarControl
		{
			get { return _topBarControl; }
		}

		/// <summary>
		/// TopBarControl.Width is not right here, because (a) the Send/Receive button currently never shows, and
		/// (b) the Make Bloompack button only shows in source collections.
		/// </summary>
		public int WidthToReserveForTopBarControl => _openCreateCollectionButton.Width + _settingsButton.Width +
			(_tcStatusButton.Visible ? _tcStatusButton.Width : 0);

		public void PlaceTopBarControl()
		{
			_topBarControl.Dock = DockStyle.Right;
		}

		public Bitmap ToolStripBackground { get; set; }

		private WorkspaceView GetWorkspaceView()
		{
			Control ancestor = Parent;
			while (ancestor != null && !(ancestor is WorkspaceView))
				ancestor = ancestor.Parent;
			return ancestor as WorkspaceView;
		}

		private void _settingsButton_Click(object sender, EventArgs e)
		{
			GetWorkspaceView().OnSettingsButton_Click(sender, e);
		}

		private void _openCreateCollectionButton_Click(object sender, EventArgs e)
		{
			GetWorkspaceView().OpenCreateCollection();
		}

		/// <summary>
		/// Set a new TC status image. Called at Idle time or startup, on the UI thread.
		/// N.B.: It also gets called if the user tries to do something and the TeamCollection suddenly
		/// recognizes it is in a disconnected state.
		/// </summary>
		public void SetTeamCollectionStatus(TeamCollectionManager tcManager)
		{
			_tcStatusButton.Update(tcManager.CollectionStatus);
			// This will cause the CollectionsTabBookPane to reload the status of the book
			// (and the collection itself), which will trickle down to the status panel.
			if (tcManager.CollectionStatus == TeamCollectionStatus.Disconnected)
				_webSocketServer.SendEvent("bookStatus", "reload");
		}

		private void _tcStatusButton_Click(object sender, EventArgs e)
		{
			// probably will do GetWorkspaceView().OpenTCStatus();
		}
	}
}
