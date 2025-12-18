using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Publish;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web;
using Bloom.web.controllers;
using L10NSharp;
using L10NSharp.Windows.Forms;
using Newtonsoft.Json;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;
using SIL.Unicode;
using SIL.Windows.Forms.ReleaseNotes;
using SIL.WritingSystems;

namespace Bloom.Workspace
{
    public partial class WorkspaceView : UserControl
    {
        private readonly WorkspaceModel _model;
        private readonly CollectionSettingsDialog.Factory _settingsDialogFactory;
        private readonly SelectedTabAboutToChangeEvent _selectedTabAboutToChangeEvent;
        private readonly SelectedTabChangedEvent _selectedTabChangedEvent;
        private readonly LocalizationChangedEvent _localizationChangedEvent;
        private readonly CollectionSettings _collectionSettings;
        private EditingView _editingView;
        private PublishView _publishView;
        private CollectionTabView _collectionTabView;
        private Control _previouslySelectedControl;
        public event EventHandler ReopenCurrentProject;
        public static float DPIOfThisAccount;
        private ZoomModel _zoomModel;
        private bool _tabsEnabled = true;
        private readonly ContextMenuStrip _uiLanguageContextMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip _helpContextMenu = new ContextMenuStrip();

        public delegate WorkspaceView Factory();

        private TeamCollectionManager _tcManager;
        private BookSelection _bookSelection;
        private ToastNotifier _returnToCollectionTabNotifier;
        private BloomWebSocketServer _webSocketServer;
        private BookServer _bookServer;
        private WorkspaceTabSelection _tabSelection;
        private CollectionApi _collectionApi;
        private AudioRecording _audioRecording;
        private CollectionSettingsApi _collectionSettingsApi;

        private NewCollectionWizardApi _newCollectionWizardApi;

        internal ReactControl TopBarReactControl => _topBarReactControl;

        //autofac uses this

        public WorkspaceView(
            WorkspaceModel model,
            CollectionTabView.Factory collectionsTabViewFactory,
            EditingView.Factory editingViewFactory,
            PublishView.Factory publishViewFactory,
            CollectionSettingsDialog.Factory settingsDialogFactory,
            EditBookCommand editBookCommand,
            SelectedTabAboutToChangeEvent selectedTabAboutToChangeEvent,
            SelectedTabChangedEvent selectedTabChangedEvent,
            LocalizationChangedEvent localizationChangedEvent,
            CollectionSettings collectionSettings,
            CommonApi commonApi,
            BookSelection bookSelection,
            BookStatusChangeEvent bookStatusChangeEvent,
            TeamCollectionManager tcManager,
            BloomWebSocketServer webSocketServer,
            AppApi appApi,
            BookServer bookServer,
            CollectionApi collectionApi,
            WorkspaceApi workspaceApi,
            WorkspaceTabSelection tabSelection,
            AudioRecording audioRecording,
            CollectionSettingsApi collectionSettingsApi,
            NewCollectionWizardApi newCollectionWizardApi,
            TeamCollectionApi teamCollectionApi
        )
        {
            _model = model;
            _settingsDialogFactory = settingsDialogFactory;
            _selectedTabAboutToChangeEvent = selectedTabAboutToChangeEvent;
            _selectedTabChangedEvent = selectedTabChangedEvent;
            _bookSelection = bookSelection;
            _localizationChangedEvent = localizationChangedEvent;
            _tcManager = tcManager;
            _webSocketServer = webSocketServer;
            _bookServer = bookServer;
            _tabSelection = tabSelection;
            _audioRecording = audioRecording;
            _collectionApi = collectionApi;
            appApi.WorkspaceView = this; // it needs to know, and there's some circularity involved in having factory pass it in
            workspaceApi.WorkspaceView = this; // and yet one more
            teamCollectionApi.WorkspaceView = this;
            _collectionSettingsApi = collectionSettingsApi;
            _newCollectionWizardApi = newCollectionWizardApi;

            _collectionSettings = collectionSettings;
            // This provides the common API with a hook it can use to reload
            // the project. Another option would be to make Autofac pass a WorkspaceView
            // to the CommonApi constructor so it could raise the event more
            // directly. But I'm concerned about circularity: something that needs
            // the CommonApi may be needed itself by the WorkspaceView.
            commonApi.ReloadProjectAction = () =>
            {
                Invoke(ReopenCurrentProject);
            };

            //_chorusSystem = chorusSystem;
            _model.UpdateDisplay += new EventHandler(OnUpdateDisplay);

            // By this point, BloomServer is up and listening and our web controllers are registered,
            // so our new ProblemReportApi will function. These next two lines activate it.
            ErrorReport.OnShowDetails = ProblemReportApi.ShowProblemDialogForNonFatalException;
            FatalExceptionHandler.UseFallback = false;

            InitializeComponent();

            // Counter the scaling that WinForms does under .Net 8 based on default font size.
            // We've retained the old default font and size to avoid changing the dialog sizes
            // inappropriately, but need this scaling to keep the top pane of the workspace
            // from being too small.  (BL-15518)
            float scaleFactor = 1.1f; // determined experimentally
            this.Scale(new SizeF(scaleFactor, scaleFactor));

            _checkForNewVersionMenuItem.Visible = Platform.IsWindows;

            editBookCommand.Subscribe(OnEditBook);

            Application.Idle += new EventHandler(Application_Idle);
            Text = _model.ProjectName;

            //
            // _editingView needs to be created before we select a tab, since the model
            // gets notified about tab selection, and expects its view to be non-null,
            // and that is done by the EditingView constructor.
            //
            this._editingView = editingViewFactory();
            this._editingView.Dock = DockStyle.Fill;
            this._editingView.Model.EnableSwitchingTabs = (enabled) =>
            {
                _tabsEnabled = enabled;
                SendTopBarState();
            };

            _collectionTabView = collectionsTabViewFactory();
            _collectionTabView.Dock = DockStyle.Fill;
            _collectionTabView.BackColor = System.Drawing.Color.FromArgb(
                ((int)(((byte)(87)))),
                ((int)(((byte)(87)))),
                ((int)(((byte)(87))))
            );
            _tabSelection.ActiveTab = WorkspaceTab.collection;

            //
            // _pdfView
            //
            this._publishView = publishViewFactory();
            this._publishView.Dock = DockStyle.Fill;

            // Temporary: while Help/UI language menus are WinForms menus and tabs run in separate browsers,
            // listen for browser clicks from each main browser so those WinForms menus can close.
            // Remove once menus are in the single browser UI.
            _editingView.Browser.OnBrowserClick += HandleAnyBrowserClick;
            _collectionTabView.BrowserClick += HandleAnyBrowserClick;
            _publishView.BrowserClick += HandleAnyBrowserClick;

            SelectTab(_collectionTabView);

            SetupZoomModel();
            SetupTopBarReactControl();
            SendZoomInfo();
            CommonApi.WorkspaceView = this;

            // We put this on the high priority list because the notification it sends
            // updates the highlighting of the selected button. Other subscribers include
            // the code that updates the preview, which is quite slow. We need the button
            // to respond quickly.
            // We'll need to do something even trickier if there start to be slow things that
            // happen in response to the book selection changed websocket message.
            bookSelection.SelectionChangedHighPriority += HandleBookSelectionChanged;
            bookStatusChangeEvent.Subscribe(args =>
            {
                HandleBookStatusChange(args);
            });
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // If we're loading a team collection, we need to do that...with its progress dialog...
            // before anything else, and we'll need to close the splash screen to make room for
            // that dialog.
            // Note, this not put into _startupActions...it should never be disabled.
            if (_tcManager?.CurrentCollectionEvenIfDisconnected == null)
            {
                ReadyToShowCollections();
            }
            else
            {
                StartupScreenManager.AddStartupAction(
                    () =>
                    {
                        // Don't do anything else after this as part of this idle task.
                        // See the comment near the end of HandleTeamStuffBeforeGetBookCollections.
                        _model.HandleTeamStuffBeforeGetBookCollections(ReadyToShowCollections);
                    },
                    shouldHideSplashScreen: true
                );
            }

            // Must not do this until we've done TC sync. Among various potential confusions,
            // if the book has been renamed remotely but not yet here, we may not be able to tell that it
            // needs to be checked out before BringBookUpToDate renames it here.
            StartupScreenManager.AddStartupAction(
                () => SelectBookAtStartup(),
                // We want to delay this until the buttons get drawn,
                // since it ties up the UI thread for a while.
                // Enhance: the code in CollectionsApi that raises this event is crude; it just
                // looks for the first two button thumbnails to be requested. It would be better if
                // we had some way of knowing when the collection panes were fully rendered.
                // It would be better still if most of the work of SelectPreviouslySelectedBook could
                // be done on a background thread so it could make progress as quickly as possible
                // without holding up drawing the collection panes.
                waitForMilestone: "collectionButtonsDrawn",
                shouldHideSplashScreen: true
            ); // possibility of error message boxes (BL-12155)
        }

        private void ReadyToShowCollections()
        {
            _collectionTabView.ReadyToShowCollections();
        }

        /// <summary>
        /// Check whether the selected book's path is obsolete (not in the current
        /// collection or a source collection) or invalid (null or empty).
        /// </summary>
        private bool IsSelectedBookObsoleteOrInvalid(string selBookPath)
        {
            if (string.IsNullOrEmpty(selBookPath) || !Directory.Exists(selBookPath))
                return true;
            var selBookCollectionFolder = Path.GetDirectoryName(selBookPath);
            var inCurrentCollection = selBookCollectionFolder == _collectionSettings.FolderPath;
            var inSourceFolder =
                !inCurrentCollection
                && _model
                    .GetSourceCollectionFolders()
                    .ToList()
                    .Exists(folder => selBookCollectionFolder == folder);
            return !inCurrentCollection && !inSourceFolder;
        }

        /// <summary>
        /// Restore the the selection of the previously selected book, but only if the book is in either
        /// the same collection (waiting to be edited) or in a source collection (waiting to be created
        /// in the current collection).
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-10225.
        /// </remarks>
        private void SelectBookAtStartup()
        {
            try
            {
                // Now that _bookSelection is an application-level object, it's possible that it retains a
                // value from a previous collection when we switch collections while Bloom is running.
                // In such situations, we're restarting almost everything else, so we don't need notifications
                // that the book selection has changed. But we do need it to be cleared. For one thing,
                // it might be a book that isn't in any current collection and shouldn't be visible. For another,
                // the collection that it's part of might have been theOneEditableCollection last time, but
                // not now, or vice versa. In that case, we could end up retaining an obsolete BookInfo object
                // with stale data about editability, because when we go to create BookInfos for a collection,
                // we use the one from the book selection if it matches one of the folder paths.
                _bookSelection.ClearSelectionWithoutNotifications();

                var selBookPath =
                    Program.PathToBookDownloadedAtStartup ?? Settings.Default.CurrentBookPath;
                if (IsSelectedBookObsoleteOrInvalid(selBookPath))
                    return;
                // We used to just create a BookInfo here. But there was a race condition where
                // an API call on another thread looking for the books of the collection would cause
                // the collection to independently create a bookInfo for the same book while
                // the book object was being created but before it is registered as the selected book.
                // Then the book and the collection go on having independent bookInfo objects, and
                // the bookInfo in the collection doesn't get some important initialization that
                // we do to (at least) the AppearanceSettings of the selected book.
                // So now we wait, if necessary, until we can get the bookInfo from the
                // appropriate collection.
                // Note: I think the checks above for inCurrentCollection and inSourceFolder can now
                // be dropped in favor of just checking that we got a bookInfo here. If it's not in
                // one of the current collections, we won't get one.
                var info = _collectionTabView.GetBookInfoByFolderPath(selBookPath);
                //var info = new BookInfo(selBookPath, inCurrentCollection, _tcManager.CurrentCollectionEvenIfDisconnected ?? new AlwaysEditSaveContext() as ISaveContext);
                // Fully updating book files ensures that the proper branding files are found for
                // previewing when the collection settings change but the book selection does not.
                var book = _bookServer.GetBookFromBookInfo(info);
                _bookSelection.SelectBook(book);
            }
            // I think we would ideally catch ApplicationException here, but, at least for BL-11678,
            // what we actually get is an Autofac.Core.DependencyResolutionException.
            catch (Exception e)
            {
                // All we are trying to do here is select a book.
                // We certainly don't want to crash because we had a problem doing so.
                // One scenario we know of which causes this is if the book at
                // Settings.Default.CurrentBookPath gets corrupted, such as having no .htm file.
                // See BL-11678.
                Settings.Default.CurrentBookPath = null;

                MiscUtils.SuppressUnusedExceptionVarWarning(e);
            }
        }

        private void HandleBookSelectionChanged(object sender, BookSelectionChangedEventArgs e)
        {
            SendBookSelectionChanged(false);
        }

        private void SendBookSelectionChanged(bool forceNotSaveable)
        {
            var result = GetCurrentSelectedBookInfo(forceNotSaveable);
            // Important for at least the TeamCollectionBookStatusPanel and the CollectionsTabBookPanel.
            _webSocketServer.SendString("book-selection", "changed", result);
        }

        string _tempBookInfoHtmlPath;

        public string GetCurrentSelectedBookInfo(bool forceNotSaveable = false)
        {
            var book = _bookSelection.CurrentSelection;
            if (book != null && IsSelectedBookObsoleteOrInvalid(book.FolderPath))
            {
                // This can happen when changing collections, and the book is in the prior collection.
                // See BL-14313.
                book = null;
            }
            var collectionKind = Book.Book.CollectionKind(book);
            string aboutBookInfoUrl = null;
            if (book != null && book.HasAboutBookInformationToShow)
            {
                if (RobustFile.Exists(book.AboutBookHtmlPath))
                {
                    aboutBookInfoUrl = book.AboutBookHtmlPath.ToLocalhost();
                }
                else if (RobustFile.Exists(book.AboutBookMdPath))
                {
                    var mdContent = RobustFile.ReadAllText(book.AboutBookMdPath);
                    var htmlContent = string.Format(
                        "<html><head><meta charset=\"utf-8\"/></head><body>{0}</body></html>",
                        Markdig.Markdown.ToHtml(mdContent)
                    );
                    if (_tempBookInfoHtmlPath != null && RobustFile.Exists(_tempBookInfoHtmlPath))
                        RobustFile.Delete(_tempBookInfoHtmlPath);
                    _tempBookInfoHtmlPath = Path.Combine(
                        Path.GetTempPath(),
                        Path.GetFileNameWithoutExtension(book.AboutBookMdPath) + ".html"
                    );
                    RobustFile.WriteAllText(_tempBookInfoHtmlPath, htmlContent);
                    aboutBookInfoUrl = _tempBookInfoHtmlPath.ToLocalhost();
                }
            }

            var saveable = _bookSelection.CurrentSelection?.IsSaveable ?? false;
            if (forceNotSaveable)
                saveable = false;
            var deletable = _bookSelection.CurrentSelection?.IsDeletable ?? false;
            // notify browser components that are listening to this event
            var result = JsonConvert.SerializeObject(
                new
                {
                    id = book?.ID,
                    saveable,
                    deletable,
                    collectionKind,
                    aboutBookInfoUrl,
                    isTemplate = book?.IsTemplateBook,
                }
            );
            return result;
        }

        public static string MustBeAdminMessage(CollectionSettings collectionSettings)
        {
            return LocalizationManager.GetString(
                    "TeamCollection.MustBeAdmin",
                    "You must be an administrator to change collection settings"
                )
                + "<br><br>"
                + LocalizationManager.GetString(
                    "TeamCollection.AdministratorEmails",
                    "Administrator Emails:"
                )
                + " "
                + collectionSettings.AdministratorsDisplayString;
        }

        private void HandleBookStatusChange(BookStatusChangeEventArgs args)
        {
            var bookName = args.BookName;
            if (_bookSelection.CurrentSelection == null)
                return;
            if (this.IsDisposed)
                return; // We can't need the notification, and Invoke will fail.
            // Notify anything on the Javascript side that might care about the status change.
            // (This includes buttons that are not selected.)
            SafeInvoke.Invoke(
                "sending reload status",
                this,
                false,
                true,
                () =>
                {
                    _webSocketServer.SendEvent("bookTeamCollectionStatus", "reload");
                }
            );
            if (bookName != Path.GetFileName(_bookSelection.CurrentSelection?.FolderPath))
                return; // change is not to the book we're interested in.
            if (_tabSelection.ActiveTab == WorkspaceTab.collection)
                return; // this toast is all about returning to the collection tab
            if (_returnToCollectionTabNotifier != null)
                return; // notification already up
            if (_tcManager.CurrentCollection == null)
                return;
            if (_tcManager.CurrentCollection.HasClobberProblem(bookName))
            {
                SafeInvoke.Invoke(
                    "sending reload status",
                    this,
                    false,
                    true,
                    () =>
                    {
                        var msg = LocalizationManager.GetString(
                            "TeamCollection.ClobberProblem",
                            "The Team Collection has a newer version of this book. Return to the Collection Tab for more information."
                        );
                        _returnToCollectionTabNotifier = new ToastNotifier();
                        _returnToCollectionTabNotifier.Image.Image = Resources.Error32x32;
                        _returnToCollectionTabNotifier.ToastClicked += (sender, _) =>
                        {
                            _returnToCollectionTabNotifier.CloseSafely();
                            ChangeTab(WorkspaceTab.collection);
                        };
                        _returnToCollectionTabNotifier.Show(msg, "", -1);
                    }
                );
            }
        }

        private void SetupZoomModel()
        {
            _zoomModel = new ZoomModel();
            _zoomModel.ZoomChanged += OnZoomChanged;
            // Provide access for javascript to adjust this control via the EditingView and EditingModel.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-5584.
            _editingView.SetZoomModel(_zoomModel);
        }

        public void SetZoom(int zoom)
        {
            if (_zoomModel == null)
                return;

            _zoomModel.Zoom = zoom;
        }

        private void OnZoomChanged(object sender, EventArgs e)
        {
            if (CurrentTabView is IZoomManager zoomManager)
                zoomManager.SetZoom(_zoomModel.Zoom);
            SendZoomInfo();
        }

        private void SetupTopBarReactControl()
        {
            _topBarReactControl.SetLocalizationChangedEvent(_localizationChangedEvent);
            _topBarReactControl.ReplaceContextMenu = () =>
            {
                Shell.GetShellOrNull()?.ShowContextMenuAt(MousePosition);
            };
            // Temporary: top bar is currently hosted as a separate browser from other tabs.
            // Remove once menus and top bar run in one browser UI.
            _topBarReactControl.OnBrowserClick += HandleAnyBrowserClick;
        }

        // Temporary helper used to close WinForms menus from browser click notifications.
        // Remove once menus are rendered in the single browser UI.
        private void HandleAnyBrowserClick(object sender, EventArgs e)
        {
            if (_uiLanguageContextMenu.Visible)
                _uiLanguageContextMenu.Close();
            if (_helpContextMenu.Visible)
                _helpContextMenu.Close();
        }

        public dynamic GetTabInfoForClient()
        {
            dynamic tabInfo = new DynamicJson();

            var activeTabId = TabToId(_tabSelection.ActiveTab);

            tabInfo.tabStates = new DynamicJson();
            tabInfo.tabStates.collection = GetTabStateForUi("collection", activeTabId);
            tabInfo.tabStates.edit = GetTabStateForUi("edit", activeTabId);
            tabInfo.tabStates.publish = GetTabStateForUi("publish", activeTabId);
            return tabInfo;
        }

        private string GetTabStateForUi(string tabId, string activeTabId)
        {
            // Even if !_tabsEnabled, we want the current tab to look active.
            // Clicking on the active tab doesn't do anything anyway.
            if (tabId == activeTabId)
                return "active";

            if (tabId == "edit" && !_model.ShowEditTab)
                return "hidden";

            if (tabId == "publish" && !_model.ShowPublishTab)
                return "hidden";

            var disabled = !_tabsEnabled || (tabId == "edit" && _model.EditTabLocked);

            return disabled ? "disabled" : "enabled";
        }

        private static string TabToId(WorkspaceTab tab)
        {
            switch (tab)
            {
                case WorkspaceTab.collection:
                    return "collection";
                case WorkspaceTab.edit:
                    return "edit";
                case WorkspaceTab.publish:
                    return "publish";
                default:
                    return "collection";
            }
        }

        private void SendTopBarState()
        {
            _webSocketServer?.SendBundle("workspace", "tabs", GetTabInfoForClient());
        }

        public dynamic GetZoomInfo()
        {
            var zoomManager = CurrentTabView as IZoomManager;
            var zoomEnabled = zoomManager != null;
            var zoomValue = zoomEnabled ? zoomManager.Zoom : (_zoomModel?.Zoom ?? 100);
            dynamic zoomInfo = new DynamicJson();
            zoomInfo.zoom = zoomValue;
            zoomInfo.zoomEnabled = zoomEnabled;
            zoomInfo.minZoom = ZoomModel.kMinimumZoom;
            zoomInfo.maxZoom = ZoomModel.kMaximumZoom;
            return zoomInfo;
        }

        public string GetCurrentUiLanguageLabel()
        {
            var lang = Settings.Default.UserInterfaceLanguage;
            if (String.IsNullOrEmpty(lang))
                lang = "en";
            var item = CreateLanguageItem(lang);
            return GetShortenedLanguageName(item.MenuText);
        }

        private static List<LanguageItem> GetLanguageItems(bool onlyActiveItem)
        {
            var items = new List<LanguageItem>();
            if (onlyActiveItem)
            {
                if (String.IsNullOrEmpty(Settings.Default.UserInterfaceLanguage))
                    Settings.Default.UserInterfaceLanguage = "en"; // See BL-13545.
                items.Add(CreateLanguageItem(Settings.Default.UserInterfaceLanguage));
            }
            else
            {
                foreach (var lang in LocalizationManager.GetAvailableLocalizedLanguages())
                {
                    var approved = FractionApproved(lang);
                    if (Settings.Default.ShowUnapprovedLocalizations)
                        approved = FractionTranslated(lang);
                    var alpha = ApplicationUpdateSupport.IsDevOrAlpha;
                    if ((alpha && approved < 0.01F) || (!alpha && approved < 0.25F))
                        continue;
                    items.Add(CreateLanguageItem(lang));
                }
            }

            items.Sort(compareLangItems);
            return items;
        }

        public void ShowUiLanguageMenu()
        {
            SetupUiLanguageMenu();
            ShowContextMenu(_uiLanguageContextMenu);
        }

        public void ShowHelpMenu()
        {
            BuildHelpContextMenu();
            ShowContextMenu(_helpContextMenu);
        }

        private void BuildHelpContextMenu()
        {
            _helpContextMenu.Items.Clear();
            _helpContextMenu.Items.AddRange(
                new ToolStripItem[]
                {
                    _documentationMenuItem,
                    _bloomDocsMenuItem,
                    _trainingVideosMenuItem,
                    _buildingReaderTemplatesMenuItem,
                    _usingReaderTemplatesMenuItem,
                    _toolStripSeparator1,
                    _askAQuestionMenuItem,
                    _requestAFeatureMenuItem,
                    _reportAProblemMenuItem,
                    _divider1,
                    _releaseNotesMenuItem,
                    _checkForNewVersionMenuItem,
                    _registrationMenuItem,
                    _divider2,
                    _webSiteMenuItem,
                    _aboutBloomMenuItem,
                }
            );
        }

        private void ShowContextMenu(ContextMenuStrip menu)
        {
            // Align the menu's right edge with the window's right edge.
            // Ensures it stays on the same monitor.
            // But also, it provides more consistency than having it shift left/right
            // depending on where the mouse is.
            var host = FindForm();
            var windowRight = host?.Bounds.Right ?? MousePosition.X;
            var menuWidth = menu.Width > 0 ? menu.Width : menu.GetPreferredSize(Size.Empty).Width;
            var x = windowRight - menuWidth;
            var y = MousePosition.Y + 8;

            var timer = new System.Windows.Forms.Timer { Interval = 10 };
            timer.Tick += (s, a) =>
            {
                menu.Left = x;
                menu.Top = y;
                menu.Show(x, y);
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        }

        private void SendZoomInfo()
        {
            _webSocketServer?.SendBundle("workspaceTopRightControls", "zoom", GetZoomInfo());
        }

        private void _applicationUpdateCheckTimer_Tick(object sender, EventArgs e)
        {
            _applicationUpdateCheckTimer.Enabled = false;
            if (
                !Debugger.IsAttached
                && Platform.IsWindows
                && !InstallerSupport.SharedByAllUsers()
                && !ApplicationUpdateSupport.IsDev
            )
            {
                ApplicationUpdateSupport.CheckForAVelopackUpdate(
                    ApplicationUpdateSupport.BloomUpdateMessageVerbosity.Quiet,
                    () => RestartBloom()
                );
            }
        }

        ToolStripMenuItem _showAllTranslationsItem;

        private void SetupUiLanguageMenu()
        {
            var items = GetLanguageItems(onlyActiveItem: false);
            var tooltipFormat = GetUiLanguageTooltipFormat();
            var current = GetAndNormalizeCurrentUiLanguage();
            _uiLanguageContextMenu.Items.Clear();
            AddUiLanguageMenuItems(
                _uiLanguageContextMenu.Items,
                items,
                current,
                tooltipFormat,
                checkCurrentItem: true,
                (langItem) => SetUiLanguage(langItem.LangTag),
                onCurrentItemAdded: null
            );

            _uiLanguageContextMenu.Items.Add(new ToolStripSeparator());
            _showAllTranslationsItem = new ToolStripMenuItem(
                GetShowUnapprovedTranslationsMenuText()
            )
            {
                Checked = Settings.Default.ShowUnapprovedLocalizations,
            };
            _showAllTranslationsItem.Click += (sender, args) =>
                ToggleShowingOnlyApprovedTranslations();
            _uiLanguageContextMenu.Items.Add(_showAllTranslationsItem);

            AddHelpTranslateMenuItem(_uiLanguageContextMenu.Items);
        }

        private static string GetUiLanguageTooltipFormat()
        {
            return LocalizationManager.GetString(
                "CollectionTab.UILanguageMenu.ItemTooltip",
                "{0}% translated",
                "Shown when hovering over an item in the UI Language menu.  The {0} marker is filled in by a number between 1 and 100."
            );
        }

        private static string GetAndNormalizeCurrentUiLanguage()
        {
            var current = Settings.Default.UserInterfaceLanguage;
            if (String.IsNullOrEmpty(current))
                current = "en";
            Settings.Default.UserInterfaceLanguage = current;
            return current;
        }

        private static void AddUiLanguageMenuItems(
            ToolStripItemCollection target,
            IEnumerable<LanguageItem> items,
            string current,
            string tooltipFormat,
            bool checkCurrentItem,
            Action<LanguageItem> onSelect,
            Action<LanguageItem> onCurrentItemAdded
        )
        {
            foreach (var langItem in items)
            {
                var translationFraction = Settings.Default.ShowUnapprovedLocalizations
                    ? langItem.FractionTranslated
                    : langItem.FractionApproved;
                var toolTip = String.Format(tooltipFormat, (int)(translationFraction * 100.0F));
                var item = new ToolStripMenuItem(langItem.MenuText)
                {
                    Checked = checkCurrentItem && langItem.LangTag == current,
                    Tag = langItem,
                    ToolTipText = toolTip,
                };
                item.Click += (sender, args) => onSelect(langItem);
                target.Add(item);
                if (langItem.LangTag == current)
                    onCurrentItemAdded?.Invoke(langItem);
            }
        }

        private static void AddHelpTranslateMenuItem(ToolStripItemCollection target)
        {
            target.Add(new ToolStripSeparator());
            var message = LocalizationManager.GetString(
                "CollectionTab.UILanguageMenu.HelpTranslate",
                "Help us translate Bloom (web)",
                "The final item in the UI Language menu. When clicked, it opens Bloom's page in the Crowdin web-based translation system."
            );
            var helpItem = target.Add(message);
            helpItem.Image = Resources.weblink;
            helpItem.Click += (sender, args) =>
                ProcessExtra.SafeStartInFront(UrlLookup.LookupUrl(UrlType.LocalizingSystem, null));
        }

        private void ToggleShowingOnlyApprovedTranslations()
        {
            Settings.Default.ShowUnapprovedLocalizations = !Settings
                .Default
                .ShowUnapprovedLocalizations;
            LocalizationManager.ReturnOnlyApprovedStrings = !Settings
                .Default
                .ShowUnapprovedLocalizations;
            SetupUiLanguageMenu();
            FinishUiLanguageMenuItemClick(); // apply newly revealed/hidden localizations
            // until L10nSharp changes to allow dynamic response to setting change
            Settings.Default.Save();
            Program.RestartBloom(false);
        }

        /// <summary>
        /// This is also called by CollectionChoosing.OpenCreateCloneControl
        /// </summary>
        public static void SetupUiLanguageMenuCommon(
            ToolStripDropDownButton uiMenuControl,
            Action finishClickAction = null,
            bool onlyActiveItem = false
        )
        {
            var items = GetLanguageItems(onlyActiveItem);
            var tooltipFormat = GetUiLanguageTooltipFormat();
            var current = GetAndNormalizeCurrentUiLanguage();
            uiMenuControl.DropDownItems.Clear();
            AddUiLanguageMenuItems(
                uiMenuControl.DropDownItems,
                items,
                current,
                tooltipFormat,
                checkCurrentItem: false,
                (langItem) =>
                    UiLanguageMenuItemClickHandler(
                        uiMenuControl,
                        langItem.LangTag,
                        finishClickAction
                    ),
                onCurrentItemAdded: (langItem) =>
                    uiMenuControl.Text = GetShortenedLanguageName(langItem.MenuText)
            );
            AddHelpTranslateMenuItem(uiMenuControl.DropDownItems);
        }

        private static int compareLangItems(LanguageItem a, LanguageItem b)
        {
            var aText = a.MenuText;
            if (!CharacterUtils.IsLatinChar(aText[0]))
                aText = a.EnglishName;
            var bText = b.MenuText;
            if (!CharacterUtils.IsLatinChar(bText[0]))
                bText = b.EnglishName;
            return String.Compare(
                aText.ToLowerInvariant(),
                bText.ToLowerInvariant(),
                StringComparison.Ordinal
            );
        }

        private static void ApplyUiLanguageChange(string langTag)
        {
            try
            {
                LocalizationManagerWinforms.SetUILanguage(langTag, true);
            }
            catch (Exception e)
            {
                // When reapplying localizations, L10NSharp can sometimes try to update controls that have
                // already been disposed (e.g. controls from dialogs that have been closed).
                // Changing the UI language should not crash Bloom; fall back to setting the language
                // without reapplying localizations.
                Logger.WriteError(
                    "Ignoring Exception while reapplying localizations after changing UI language.",
                    e
                );
                try
                {
                    LocalizationManagerWinforms.SetUILanguage(langTag, false);
                }
                catch (Exception e2)
                {
                    Logger.WriteError("Failed to set UI language after Exception.", e2);
                    return; // Don't save since we didn't successfully change the language.
                }
            }
            // TODO-WV2: Can we set the browser language in WV2?  Do we need to?
            Settings.Default.UserInterfaceLanguage = langTag;
            Settings.Default.UserInterfaceLanguageSetExplicitly = true;
            Settings.Default.Save();

            // Currently needed for the language chooser to update its localization
            // BloomWebSocketServer.Instance is set only while loading a collection.
            // If we don't have a collection yet, then we need to create a temporary
            // server just to send this one message.  (BL-15230)
            if (BloomWebSocketServer.Instance == null)
            {
                using (var server = new BloomWebSocketServer())
                {
                    server.Init(
                        (BloomServer.portForHttp + 1).ToString(CultureInfo.InvariantCulture)
                    );
                    server.SendString("app", "uiLanguageChanged", langTag);
                }
            }
            else
            {
                BloomWebSocketServer.Instance.SendString("app", "uiLanguageChanged", langTag);
            }
        }

        private static void UiLanguageMenuItemClickHandler(
            ToolStripDropDownButton toolStripButton,
            string langTag,
            Action finishClickAction
        )
        {
            ApplyUiLanguageChange(langTag);
            if (toolStripButton != null)
            {
                var menuText = CreateLanguageItem(langTag).MenuText;
                toolStripButton.Text = GetShortenedLanguageName(menuText);
            }
            finishClickAction?.Invoke();
        }

        public void SetUiLanguage(string langTag)
        {
            ApplyUiLanguageChange(langTag);
            FinishUiLanguageMenuItemClick();
        }

        private void FinishUiLanguageMenuItemClick()
        {
            _showAllTranslationsItem.Text = GetShowUnapprovedTranslationsMenuText();
            _localizationChangedEvent.Raise(null);
        }

        private string GetShowUnapprovedTranslationsMenuText()
        {
            return LocalizationManager.GetString(
                "CollectionTab.LanguageMenu.ShowUnapprovedTranslations",
                "Show translations which have not been approved yet"
            );
        }

        public static LanguageItem CreateLanguageItem(string code)
        {
            // Get the language name in its own language if at all possible.
            // Add an English name suffix if it's not in a Latin script.
            var menuText = IetfLanguageTag.GetNativeLanguageNameWithEnglishSubtitle(code);
            var englishName = IetfLanguageTag.GetManuallyOverriddenEnglishNameIfNeeded(
                code,
                () => IetfLanguageTag.GetLocalizedLanguageName(code, "en")
            );
            return new LanguageItem
            {
                EnglishName = englishName,
                LangTag = code,
                MenuText = menuText,
                FractionApproved = FractionApproved(code),
                FractionTranslated = FractionTranslated(code),
            };
        }

        /// <summary>
        /// LocalizationManager.FractionApproved(code) divides by the number of English strings
        /// which is always larger because it includes strings from outside Bloom and Palaso
        /// that have been dynamically discovered.  There are some other things going on in
        /// the dynamic scanning that result in strings being duplicated in multiple .xlf files.
        /// So we calculate the number based solely on the particular language's counts.  Since
        /// Crowdin is supposed to pass through all strings to all localizations, this should be
        /// fairly accurate and give numbers similar to what Crowdin reports.  (Crowdin counts by
        /// word instead of by string.)
        /// </summary>
        public static float FractionApproved(string code)
        {
            var totalCount = LocalizationManager.StringCount(code);
            var approvedCount = LocalizationManager.NumberApproved(code);
            return (float)approvedCount / (float)totalCount;
        }

        /// <summary>
        /// LocalizationManager.FractionTranslated(code) divides by the number of English strings
        /// which is always larger because it includes strings from outside Bloom and Palaso
        /// that have been dynamically discovered.  So we use the language's counts directly to
        /// compute this fraction.
        /// </summary>
        public static float FractionTranslated(string code)
        {
            var totalCount = LocalizationManager.StringCount(code);
            var translatedCount = LocalizationManager.NumberTranslated(code);
            return (float)translatedCount / (float)totalCount;
        }

        public static string GetShortenedLanguageName(string itemText)
        {
            var idxChinese = itemText.IndexOf(" (Chinese");
            if (idxChinese > 0)
            {
                return itemText.Substring(0, idxChinese);
            }
            else
            {
                var idxCountry = itemText.IndexOf(" (");
                if (idxCountry > 0)
                    return itemText.Substring(0, idxCountry);
                else
                {
                    return itemText;
                }
            }
        }

        private void OnEditBook(Book.Book book)
        {
            ChangeTab(WorkspaceTab.edit);
        }

        public bool InEditMode => _tabSelection.ActiveTab == WorkspaceTab.edit;

        public bool InCollectionTab => _tabSelection.ActiveTab == WorkspaceTab.collection;

        private void Application_Idle(object sender, EventArgs e)
        {
            Application.Idle -= Application_Idle;
        }

        private void OnUpdateDisplay(object sender, EventArgs e)
        {
            SendTopBarState();
        }

        // Called early in checkin, will be re-updated by OnUpdateDisplay later in checkin,
        // but we need to disable it right away when losing permission to edit.
        public void DisableEditTab()
        {
            SendTopBarState();
            // This disables the Edit button.
            SendBookSelectionChanged(forceNotSaveable: true);
        }

        public void OpenCreateCollection()
        {
            _selectedTabAboutToChangeEvent.Raise(
                new TabChangedDetails() { From = _previouslySelectedControl, To = null }
            );

            _selectedTabChangedEvent.Raise(
                new TabChangedDetails() { From = _previouslySelectedControl, To = null }
            );

            var oldSelectedControl = _previouslySelectedControl;
            _previouslySelectedControl = null;

            Invoke(
                (Action)(
                    () =>
                    {
                        var didOpen = Program.ChooseACollection(
                            Shell.GetShellOrOtherOpenForm() as Shell
                        );
                        if (!didOpen)
                        {
                            // We want to resume whatever tab we were in.
                            // There is some overkill here...the old tab can only be the collection tab,
                            // and currently it doesn't care about these events. The critical thing is to
                            // restore _previouslySelectedControl, which is required so we can remove it
                            // if we subsequently switch to another tab. But it seemed best to be consistent.
                            // If we're not shutting down, we're switching the previously selected tab back on.
                            _selectedTabAboutToChangeEvent.Raise(
                                new TabChangedDetails() { From = null, To = oldSelectedControl }
                            );
                            _selectedTabChangedEvent.Raise(
                                new TabChangedDetails() { From = null, To = oldSelectedControl }
                            );
                            _previouslySelectedControl = oldSelectedControl;
                        }
                    }
                )
            );
        }

        private CollectionSettingsDialog _currentlyOpenSettingsDialog;

        public void OpenLegacySettingsDialog(
            string tab = null,
            bool forFixingEnterpriseSubscription = false
        )
        {
            if (InvokeRequired)
            {
                SafeInvoke.Invoke(
                    "OpenSettingsDialog",
                    this,
                    true,
                    false,
                    () => OpenLegacySettingsDialog(tab, forFixingEnterpriseSubscription)
                );
            }
            else
            {
                if (_currentlyOpenSettingsDialog != null)
                {
                    _currentlyOpenSettingsDialog.SetDesiredTab(tab);
                    return;
                }

                DialogResult result = DialogResult.Cancel;
                if (!_tcManager.OkToEditCollectionSettings)
                {
                    BloomMessageBox.ShowInfo(MustBeAdminMessage(_collectionSettings));
                }
                else
                {
                    _collectionSettingsApi.PrepareToShowDialog();
                    using (var dlg = _settingsDialogFactory())
                    {
                        dlg.FixingEnterpriseSubscriptionCode = forFixingEnterpriseSubscription;
                        _currentlyOpenSettingsDialog = dlg;
                        dlg.SetDesiredTab(tab);
                        result = dlg.ShowDialog(this);
                        _currentlyOpenSettingsDialog = null;
                        CollectionSettingsApi.DialogClosed();
                    }
                }
                if (result == DialogResult.Yes)
                {
                    Invoke(ReopenCurrentProject);
                }
            }
        }

        public void CheckForInvalidBranding()
        {
            if (_collectionSettings.InvalidBranding == null || _collectionSettings.IgnoreExpiration)
                return;
            // I'm not very happy with this, but the only place I could find to detect that we're opening a new project
            // is too soon to bring up a dialog; it comes up before the main window is fully initialized, which can
            // leave the main window in the wrong place. Waiting until idle gives a much better effect.
            StartupScreenManager.AddStartupAction(
                () =>
                {
                    OpenLegacySettingsDialog("subscription", forFixingEnterpriseSubscription: true);
                },
                shouldHideSplashScreen: true,
                lowPriority: true
            );
        }

        private void SelectTab(Control view)
        {
            // Already on the desired tab: nothing to do.  And possible problems if we do do something.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-8382.
            if (view == _previouslySelectedControl)
                return;

            CurrentTabView = view as IBloomTabArea;
            // Warn the user if we're starting to use too much memory.
            //MemoryManagement.CheckMemory(false, "switched tab in workspace", true);

            if (_previouslySelectedControl != null)
            {
                _containerPanel.Controls.Remove(_previouslySelectedControl);
                if (_previouslySelectedControl is EditingView)
                {
                    // I wish this was unnecessary; ideally, we'd get the notification to
                    // stop monitoring from the stopMonitoring function in audioRecording.ts.
                    // We should be able to achieve that when the tabs are embedded in a single
                    // Browser control. For now, the shutdown of the EditingView seems to
                    // preempt it, so we handle it here.
                    _audioRecording.PauseMonitoringAudio(false);
                }
            }

            view.Dock = DockStyle.Fill;
            _containerPanel.Controls.Add(view);

            _selectedTabAboutToChangeEvent.Raise(
                new TabChangedDetails()
                {
                    From = _previouslySelectedControl,
                    To = view,
                    PostponedWork = () =>
                    {
                        _selectedTabChangedEvent.Raise(
                            new TabChangedDetails() { From = _previouslySelectedControl, To = view }
                        );

                        _previouslySelectedControl = view;
                        _collectionApi.ResetUpdatingList();

                        var zoomManager = CurrentTabView as IZoomManager;
                        if (zoomManager != null)
                        {
                            _zoomModel.Zoom = zoomManager.Zoom;
                        }
                        SendZoomInfo();
                        SendTopBarState();
                        // TODO-WV2: Can we clear the cache in WV2?  Do we need to?
                    },
                }
            );
        }

        protected IBloomTabArea CurrentTabView { get; set; }

        public void ChangeTab(WorkspaceTab newTab)
        {
            _tabSelection.ActiveTab = newTab;
            switch (newTab)
            {
                case WorkspaceTab.edit:
                    SelectTab(_editingView);
                    break;
                case WorkspaceTab.collection:
                    SelectTab(_collectionTabView);
                    if (_returnToCollectionTabNotifier != null)
                    {
                        _returnToCollectionTabNotifier.CloseSafely();
                        _returnToCollectionTabNotifier = null;
                    }
                    if (_collectionTabView != null)
                    {
                        if (Publish.BloomLibrary.BloomLibraryPublishModel.BookUploaded)
                        {
                            _collectionTabView.UpdateBloomLibraryStatus(
                                Publish.BloomLibrary.BloomLibraryPublishModel.BookUploadedId
                            );
                            Publish.BloomLibrary.BloomLibraryPublishModel.BookUploaded = false;
                            Publish.BloomLibrary.BloomLibraryPublishModel.BookUploadedId = null;
                        }
                    }
                    break;
                case WorkspaceTab.publish:
                    SelectTab(_publishView);
                    break;
            }
        }

        private void OnAboutBoxClick(object sender, EventArgs e)
        {
            if (InEditMode)
                _editingView.ShowAboutDialog();
            else
                _webSocketServer.LaunchDialog("AboutDialog");
        }

        private void _documentationMenuItem_Click(object sender, EventArgs e)
        {
            HelpLauncher.Show(this, CurrentTabView.HelpTopicUrl);
        }

        private void _bloom_docs_Click(object sender, EventArgs e)
        {
            SIL.Program.Process.SafeStart("https://docs.bloomlibrary.org");
        }

        private void _webSiteMenuItem_Click(object sender, EventArgs e)
        {
            ProcessExtra.SafeStartInFront(UrlLookup.LookupUrl(UrlType.LibrarySite, null));
        }

        private void _releaseNotesMenuItem_Click(object sender, EventArgs e)
        {
            SIL.Program.Process.SafeStart("https://docs.bloomlibrary.org/Release-Notes");
        }

        private void _requestAFeatureMenuItem_Click(object sender, EventArgs e)
        {
            ProcessExtra.SafeStartInFront(UrlLookup.LookupUrl(UrlType.UserSuggestions, null));
        }

        private void _askAQuestionMenuItem_Click(object sender, EventArgs e)
        {
            ProcessExtra.SafeStartInFront(UrlLookup.LookupUrl(UrlType.Support, null));
        }

        // Currently not used, but I'm leaving the method in case we want to put it
        // back in for debug or alpha builds, etc.
        private void _showLogMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                Logger.ShowUserTheLogFile();
            }
            catch (Exception) { }
        }

        private void WorkspaceView_Load(object sender, EventArgs e)
        {
            CheckDPISettings();
            ShowAutoUpdateDialogIfNeeded();
            ShowForumInvitationDialogIfNeeded();
            // Whether we showed the dialog or not we'll check for a new version in 1 minute.
            _applicationUpdateCheckTimer.Enabled = true;
            SendTopBarState();
        }

        private const int kCurrentAutoUpdateVersion = 1;

        private void ShowAutoUpdateDialogIfNeeded()
        {
            if (Platform.IsLinux)
                return;
            // If Bloom is newly installed or we only had old versions before, this should be 0.
            var isShown = Settings.Default.AutoUpdateDialogShown;
            if (isShown < kCurrentAutoUpdateVersion)
            {
                // It's tempting to make the whole process of calling this function a startup action,
                // but until we actually decide whether to show it, we don't know whether we need to
                // hide the splash screen. This is as much as we can postpone.
                StartupScreenManager.AddStartupAction(
                    () =>
                    {
                        using (
                            var dlg = new ReactDialog("autoUpdateSoftwareDlgBundle", "Auto Update")
                        )
                        {
                            dlg.Height = 250;
                            dlg.Width = 500;
                            dlg.ShowDialog(this);
                        }
                    },
                    shouldHideSplashScreen: true,
                    lowPriority: false
                );
            }
        }

        private void ShowForumInvitationDialogIfNeeded()
        {
            if (Settings.Default.ForumInvitationAcknowledged)
                return;
            var lastShown = Settings.Default.ForumInvitationLastShown;
            var today = DateTime.Now;
            if (lastShown == DateTime.MinValue)
            {
                // If Bloom is newly installed or we only had old versions before,
                // wait 2 weeks before showing this nagging dialog.
                Settings.Default.ForumInvitationLastShown = today;
                Settings.Default.Save();
                return;
            }
            // Show once every two weeks until the user gives up and acknowledges it.
            if (today.Subtract(lastShown).TotalDays > 13)
            {
                // It's tempting to make the whole process of calling this function a startup action,
                // but until we actually decide whether to show it, we don't know whether we need to
                // hide the splash screen. This is as much as we can postpone.
                AppApi.OpenDialogs.AddOrUpdate("ForumInvitationDialog", 0, (key, val) => val);
                StartupScreenManager.AddStartupAction(
                    () =>
                    {
                        AppApi.OpenDialogs.AddOrUpdate(
                            "ForumInvitationDialog",
                            1,
                            (key, val) => val + 1
                        );
                        if (
                            AppApi.OpenDialogs.TryGetValue("ForumInvitationDialog", out var count)
                            && count > 1
                        )
                            return;
                        Settings.Default.ForumInvitationLastShown = today;
                        Settings.Default.Save();
                        _webSocketServer.LaunchDialog("ForumInvitationDialog");
                    },
                    shouldHideSplashScreen: true,
                    lowPriority: true,
                    needsToRun: () =>
                    {
                        // The startup task is called repeatedly until this function returns false.
                        var shouldShow = AppApi.OpenDialogs.TryGetValue(
                            "ForumInvitationDialog",
                            out var count
                        );
                        return shouldShow && count < 2;
                    },
                    // We need to wait for the "collectionButtonsDrawn" milestone to ensure that the collection
                    // tab has rendered sufficiently to hook up the socket event handler for the dialog.
                    // See September 18-19 comments in https://issues.bloomlibrary.org/youtrack/issue/BL-12410.
                    // But we need to allow a number of ticks for the milestone to show up.  A delay of 100
                    // ticks seems to work well.  A debug build on an older development machine recorded 4654
                    // ticks in about a minute.
                    waitForMilestone: "collectionButtonsDrawn",
                    maxTickWaitForMilestone: 100
                );
            }
        }

        private void OnRegistrationMenuItem_Click(object sender, EventArgs e)
        {
            ShowRegistrationDialog();
        }

        public void ShowRegistrationDialog()
        {
            if (InEditMode)
                _editingView.ShowRegistrationDialog();
            else
            {
                dynamic messageBundle = new DynamicJson();
                _webSocketServer.LaunchDialog("RegistrationDialog", messageBundle);
            }
        }

        private void CheckDPISettings()
        {
            Graphics g = this.CreateGraphics();
            try
            {
                var dx = g.DpiX;
                DPIOfThisAccount = dx;
                var dy = g.DpiY;
                if (dx != 96 || dy != 96)
                {
                    ErrorReport.NotifyUserOfProblem(
                        new ShowOncePerSessionBasedOnExactMessagePolicy(),
                        "The \"text size (DPI)\" or \"Screen Magnification\" of the display on this computer is set to a special value, {0}. With that setting, some thing won't look right in Bloom. Possibly books won't lay out correctly. If this is a problem, change the DPI back to 96 (the default on most computers), using the 'Display' Control Panel.",
                        dx
                    );
                }
            }
            finally
            {
                g.Dispose();
            }
        }

        public void CheckForCollectionUpdates()
        {
            _collectionApi.CheckForCollectionUpdates();
        }

        public void CheckForUpdates()
        {
            Invoke((Action)(() => _checkForNewVersionMenuItem_Click(this, new EventArgs())));
        }

        private void _checkForNewVersionMenuItem_Click(object sender, EventArgs e)
        {
            if (Debugger.IsAttached)
            {
                MessageBox.Show(this, "Sorry, you cannot check for updates from the debugger.");
            }
            else if (InstallerSupport.SharedByAllUsers())
            {
                MessageBox.Show(
                    this,
                    LocalizationManager.GetString(
                        "CollectionTab.AdminManagesUpdates",
                        "Your system administrator manages Bloom updates for this computer."
                    )
                );
            }
            else if (ApplicationUpdateSupport.IsDev)
            {
                MessageBox.Show(
                    this,
                    "Checking for updates is disabled on developer builds. No relevant channel."
                );
            }
            else
            {
                ApplicationUpdateSupport.CheckForAVelopackUpdate(
                    ApplicationUpdateSupport.BloomUpdateMessageVerbosity.Verbose,
                    () => RestartBloom()
                );
            }
        }

        private void RestartBloom()
        {
            Control ancestor = Parent;
            while (ancestor != null && !(ancestor is Shell))
                ancestor = ancestor.Parent;
            if (ancestor == null)
                return;
            var shell = (Shell)ancestor;
            shell.QuitForVersionUpdate = true;
            shell.Close();
        }

        private static void OpenInfoFile(string fileName)
        {
            // These are PDF files, but stored under browser/infoPages.
            ProcessExtra.SafeStartInFront(
                BloomFileLocator.GetBrowserFile(false, "infoPages", fileName)
            );
        }

        private void buildingReaderTemplatesMenuItem_Click(object sender, EventArgs e)
        {
            OpenInfoFile("Building and Distributing Reader Templates in Bloom.pdf");
        }

        private void usingReaderTemplatesMenuItem_Click(object sender, EventArgs e)
        {
            OpenInfoFile("Using Bloom Reader Templates.pdf");
        }

        private void _reportAProblemMenuItem_Click(object sender, EventArgs e)
        {
            // Screen shots were showing the menu still open on Linux, so delay a bit by starting the
            // dialog on the next idle loop.  Also allow one repaint event to be handled immediately.
            // (This method has to return for the menu to fully hide itself on Linux.)
            // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3792.
            Application.DoEvents();
            Application.Idle += StartProblemReport;
        }

        private void StartProblemReport(object sender, EventArgs e)
        {
            Application.Idle -= StartProblemReport;

            // Try to ensure latest changes in book are included in report.  (BL-10480)
            try
            {
                if (InEditMode)
                {
                    _editingView.Model.SaveThen(
                        () =>
                        {
                            // To test the Problem Dialog with a fatal error, uncomment this next line.
                            // throw new ApplicationException("I just felt like an error!");

                            // To test the Problem Dialog with a nonfatal error, uncomment this next line.
                            // NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "My test 'yellow screen' error", "Any more details here?");
                            // To test clicking 'Report' in a toast, uncomment the line above, but use ModalIf.None.

                            // To test the old ErrorReport.NotifyUserOfProblem, uncomment this next line.
                            // ErrorReport.NotifyUserOfProblem(new ApplicationException("internal exception message"), "My main message");
                            ReportAndLogProblem();
                            return _editingView.Model.CurrentPage.Id;
                        },
                        () => { } // wrong state, do nothing
                    );
                }
                else
                {
                    ReportAndLogProblem();
                }
            }
            catch
            {
                // Ignore errors saving. (But this may miss problems while responding to getting the page content.)
                ReportAndLogProblem();
            }
        }

        private void ReportAndLogProblem()
        {
            Logger.WriteEvent("User clicked the 'Report a Problem' menu item");
            ProblemReportApi.ShowProblemDialog(this, null);
        }

        public void SetTabsEnabled(bool enable)
        {
            _tabsEnabled = enable;
            SendTopBarState();
        }

        private void _trainingVideosMenuItem_Click(object sender, EventArgs e)
        {
            //note: markdown processors pass raw html through unchanged.  Bloom's localization process
            // is designed to produce HTML files, not Markdown files.
            var path = BloomFileLocator.GetBestLocalizableFileDistributedWithApplication(
                false,
                "infoPages",
                "TrainingVideos-en.htm"
            );
            using (var dlg = new ShowReleaseNotesDialog(Resources.BloomIcon, path))
            {
                dlg.ApplyMarkdown = false;
                dlg.Text = LocalizationManager.GetString(
                    "HelpMenu.trainingVideos",
                    "Training Videos"
                );
                dlg.ShowDialog();
            }
        }
    }

    public class NoBorderToolStripRenderer : ToolStripProfessionalRenderer
    {
        public NoBorderToolStripRenderer()
            : base(new NoBorderToolStripColorTable()) { }

        public Color DisabledColor { get; set; }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // this is needed, especially on Linux
            e.SizeTextRectangleToText();
            AdjustToolStripLocationIfNecessary(e);
            if (e.Item.Enabled)
            {
                base.OnRenderItemText(e);
                return;
            }

            // We have to actually take over drawing it, because when disabled, e.TextColor
            // is ignored. There doesn't seem to be a property for disabled text color.
            TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                e.TextRectangle,
                DisabledColor,
                e.TextFormat
            );
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (!e.Item.Enabled)
                e.ArrowColor = DisabledColor;

            base.OnRenderArrow(e);
        }

        /// <summary>
        /// A toolstrip with one item embedded in a panel embedded in a TableLayoutPanel does not display well
        /// on Linux/Mono.  The text display can be truncated and moves around the panel horizontally.  The
        /// sizing calculation is carried out properly, but the ensuing horizontal location seems almost random.
        /// Rather than try to fix possibly several layers of Mono libary code, we calculate the desired location
        /// here to prevent the text from being truncated if possible.
        /// </summary>
        /// <remarks>
        /// See http://issues.bloomlibrary.org/youtrack/issue/BL-4409.
        /// </remarks>
        private void AdjustToolStripLocationIfNecessary(ToolStripItemTextRenderEventArgs e)
        {
            if (
                SIL.PlatformUtilities.Platform.IsUnix
                && e.ToolStrip != null
                && e.ToolStrip.Items.Count == 1
                && e.ToolStrip.Parent != null
                && e.ToolStrip.Parent.Parent is TableLayoutPanel
            )
            {
                var delta = (e.ToolStrip.Location.X + e.ToolStrip.Width) - e.ToolStrip.Parent.Width;
                // Try to leave a pixel of margin.
                if (delta >= 0)
                {
                    e.ToolStrip.Location = new Point(
                        Math.Max(e.ToolStrip.Location.X - (delta + 1), 1),
                        e.ToolStrip.Location.Y
                    );
                }
            }
        }
    }

    public class NoBorderToolStripColorTable : ProfessionalColorTable
    {
        // BL-5071 Make the border the same color as the button when selected/hovered
        public override Color ButtonSelectedBorder => SystemColors.GradientActiveCaption;
    }

    /// <summary>
    /// Mono refuses to create CultureInfo items for languages it doesn't recognize.  And I'm not
    /// sure that .Net allows you to modify the dummy objects it creates.  So this class serves
    /// to store the language code and the most useful names associated with it, at least for the
    /// purposes of the drop-down menus that display the localization languages available.
    /// </summary>
    public class LanguageItem
    {
        public string LangTag;
        public string EnglishName;
        public string MenuText;
        public float FractionApproved;
        public float FractionTranslated;
    }
}
